using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Dental.Patients;

public record DuplicateCandidate(Guid Id, string FullName, string? Phone, string? Iin, DateOnly? BirthDate, string Reason);

/// <summary>Правила карточки пациента: поиск дублей, создание с балансом, слияние.</summary>
public class PatientManager : DomainService
{
    private readonly IRepository<Patient, Guid> _patients;
    private readonly IRepository<PatientBalance, Guid> _balances;
    private readonly IRepository<PatientConsent, Guid> _consents;
    private readonly IEnumerable<IPatientMergeContributor> _mergeContributors;

    public PatientManager(
        IRepository<Patient, Guid> patients,
        IRepository<PatientBalance, Guid> balances,
        IRepository<PatientConsent, Guid> consents,
        IEnumerable<IPatientMergeContributor> mergeContributors)
    {
        _patients = patients;
        _balances = balances;
        _consents = consents;
        _mergeContributors = mergeContributors;
    }

    /// <summary>Совпадения по нормализованному телефону (основному/доп.) или ИИН среди активных карточек.</summary>
    public async Task<List<DuplicateCandidate>> FindDuplicatesAsync(string? phone, string? iin, Guid? excludeId)
    {
        var normalized = Patient.NormalizePhone(phone);
        var iinValue = string.IsNullOrWhiteSpace(iin) || iin.Contains('*') ? null : iin.Trim();
        if (normalized is null && iinValue is null)
        {
            return [];
        }
        var query = (await _patients.GetQueryableAsync())
            .Where(p => p.MergedIntoId == null && p.Id != excludeId &&
                        ((normalized != null && (p.Phone == normalized || p.PhoneExtra == normalized)) || (iinValue != null && p.Iin == iinValue)))
            .Take(10);
        var rows = await AsyncExecuter.ToListAsync(query);
        return rows.Select(p => new DuplicateCandidate(p.Id, p.FullName, p.Phone, p.Iin, p.BirthDate,
            iinValue != null && p.Iin == iinValue ? PatientConsts.DuplicateReasonIin : PatientConsts.DuplicateReasonPhone)).ToList();
    }

    /// <summary>
    /// Проверка перед созданием: совпадение ИИН — всегда ошибка (Dental:IinTaken);
    /// совпадение телефона — Dental:PatientDuplicate, если не подтверждено ignoreDuplicates.
    /// </summary>
    public async Task<List<DuplicateCandidate>> EnsureNoDuplicatesAsync(string? phone, string? iin, Guid? excludeId, bool ignorePhoneDuplicates)
    {
        var duplicates = await FindDuplicatesAsync(phone, iin, excludeId);
        if (duplicates.Any(d => d.Reason == PatientConsts.DuplicateReasonIin))
        {
            throw new BusinessException(DentalDomainErrorCodes.IinTaken).WithData("duplicates", duplicates);
        }
        if (duplicates.Count > 0 && !ignorePhoneDuplicates)
        {
            throw new BusinessException(DentalDomainErrorCodes.PatientDuplicate).WithData("duplicates", duplicates);
        }
        return duplicates;
    }

    /// <summary>Сохранить нового пациента вместе со строкой баланса (0).</summary>
    public async Task<Patient> InsertAsync(Patient patient)
    {
        await _patients.InsertAsync(patient, autoSave: true);
        await _balances.InsertAsync(new PatientBalance(GuidGenerator.Create(), patient.TenantId, patient.Id), autoSave: true);
        return patient;
    }

    public async Task<PatientBalance> GetOrCreateBalanceAsync(Guid patientId)
    {
        var balance = await _balances.FirstOrDefaultAsync(b => b.PatientId == patientId);
        if (balance is null)
        {
            balance = await _balances.InsertAsync(new PatientBalance(GuidGenerator.Create(), CurrentTenant.Id, patientId), autoSave: true);
        }
        return balance;
    }

    /// <summary>
    /// Слияние: связанные данные модулей (IPatientMergeContributor), согласия и баланс переносятся на основную карточку,
    /// пустые поля основной дополняются данными дубля, дубль получает MergedIntoId. Возвращает число перенесённых строк по модулям.
    /// </summary>
    public async Task<Dictionary<string, int>> MergeAsync(Patient main, Patient dup)
    {
        if (main.Id == dup.Id || main.MergedIntoId != null || dup.MergedIntoId != null)
        {
            throw new BusinessException(DentalDomainErrorCodes.PatientMergeInvalid);
        }

        var moved = new Dictionary<string, int>();
        foreach (var contributor in _mergeContributors)
        {
            moved[contributor.Name] = await contributor.MoveAsync(dup.Id, main.Id);
        }

        var consents = await _consents.GetListAsync(c => c.PatientId == dup.Id);
        foreach (var c in consents)
        {
            c.MoveTo(main.Id);
        }
        await _consents.UpdateManyAsync(consents);
        moved["consents"] = consents.Count;

        var mainBalance = await GetOrCreateBalanceAsync(main.Id);
        var dupBalance = await _balances.FirstOrDefaultAsync(b => b.PatientId == dup.Id);
        if (dupBalance is not null)
        {
            mainBalance.Add(dupBalance.TakeAll());
            await _balances.UpdateAsync(dupBalance);
            await _balances.UpdateAsync(mainBalance);
        }

        main.AbsorbFrom(dup);
        // Сначала дубль (освобождает ИИН), затем основная карточка.
        await _patients.UpdateAsync(dup, autoSave: true);
        await _patients.UpdateAsync(main, autoSave: true);
        return moved;
    }
}
