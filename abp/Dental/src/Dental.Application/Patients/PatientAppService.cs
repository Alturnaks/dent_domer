using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Dental.Patients;

[Authorize(DentalPermissions.Patients.View)]
public class PatientAppService : DentalAppService, IPatientAppService
{
    private readonly IRepository<Patient, Guid> _patients;
    private readonly IRepository<PatientBalance, Guid> _balances;
    private readonly IRepository<PatientConsent, Guid> _consents;
    private readonly IRepository<LeadSource, Guid> _sources;
    private readonly PatientManager _manager;
    private readonly IPatientActivityProvider _activity;

    public PatientAppService(
        IRepository<Patient, Guid> patients,
        IRepository<PatientBalance, Guid> balances,
        IRepository<PatientConsent, Guid> consents,
        IRepository<LeadSource, Guid> sources,
        PatientManager manager,
        IPatientActivityProvider activity)
    {
        _patients = patients;
        _balances = balances;
        _consents = consents;
        _sources = sources;
        _manager = manager;
        _activity = activity;
    }

    public async Task<PagedResultDto<PatientListItemDto>> GetListAsync(GetPatientListInput input)
    {
        var query = (await _patients.GetQueryableAsync()).Where(p => p.MergedIntoId == null);
        if (!input.Filter.IsNullOrWhiteSpace())
        {
            query = ApplySearch(query, input.Filter!);
        }
        if (!input.Tag.IsNullOrWhiteSpace())
        {
            var tagToken = "|" + input.Tag!.Trim() + "|";
            query = query.Where(p => p.TagsRaw.Contains(tagToken));
        }
        query = query
            .WhereIf(input.SourceId.HasValue, p => p.SourceId == input.SourceId)
            .WhereIf(input.IsVip.HasValue, p => p.IsVip == input.IsVip);
        if (input.Debtors == true)
        {
            var balances = await _balances.GetQueryableAsync();
            query = query.Where(p => balances.Any(b => b.PatientId == p.Id && b.Balance < 0));
        }
        if (input.NotVisitedMonths is > 0 and var months)
        {
            var visited = await _activity.GetVisitedSinceQueryAsync(Clock.Now.AddMonths(-months));
            if (visited is not null)
            {
                query = query.Where(p => !visited.Contains(p.Id));
            }
        }

        var total = await AsyncExecuter.CountAsync(query);
        query = (input.Sorting ?? "").Trim().ToLowerInvariant() switch
        {
            "fullname" or "lastname" or "fullname asc" or "lastname asc" => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName),
            "fullname desc" or "lastname desc" => query.OrderByDescending(p => p.LastName).ThenByDescending(p => p.FirstName),
            "birthdate" or "birthdate asc" => query.OrderBy(p => p.BirthDate),
            "birthdate desc" => query.OrderByDescending(p => p.BirthDate),
            "creationtime" or "creationtime asc" => query.OrderBy(p => p.CreationTime),
            _ => query.OrderByDescending(p => p.CreationTime).ThenByDescending(p => p.Id),
        };
        var page = await AsyncExecuter.ToListAsync(query.PageBy(input));
        return new PagedResultDto<PatientListItemDto>(total, await EnrichAsync(page));
    }

    /// <summary>Поиск по ФИО (каждое слово — подстрока фамилии/имени/отчества), телефону (от 4 цифр) или ИИН.</summary>
    private static IQueryable<Patient> ApplySearch(IQueryable<Patient> query, string q)
    {
        var term = q.Trim();
        var digits = new string(term.Where(char.IsDigit).ToArray());
        var stripped = term.Replace(" ", "").Replace("+", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (digits.Length >= 4 && digits.Length == stripped.Length)
        {
            var phoneDigits = digits.Length >= 10 ? Patient.NormalizePhone(digits) ?? digits : digits;
            return query.Where(p => (p.Phone != null && p.Phone.Contains(phoneDigits)) || (p.PhoneExtra != null && p.PhoneExtra.Contains(phoneDigits)) || p.Iin == digits);
        }
        foreach (var part in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var like = part.ToLower();
            query = query.Where(p => p.LastName.ToLower().Contains(like) || p.FirstName.ToLower().Contains(like) ||
                                     (p.MiddleName != null && p.MiddleName.ToLower().Contains(like)));
        }
        return query;
    }

    private async Task<bool> CanSeeIinAsync() =>
        await AuthorizationService.IsGrantedAsync(DentalPermissions.Patients.ViewMedical) ||
        await AuthorizationService.IsGrantedAsync(DentalPermissions.Patients.Edit);

    private static string? Mask(string? s) => s is null ? null : s.Length <= 4 ? "****" : new string('*', s.Length - 4) + s[^4..];

    private async Task<List<PatientListItemDto>> EnrichAsync(List<Patient> page)
    {
        var ids = page.Select(p => p.Id).ToList();
        var balances = (await _balances.GetListAsync(b => ids.Contains(b.PatientId))).ToDictionary(b => b.PatientId, b => b.Balance);
        var sourceIds = page.Where(p => p.SourceId != null).Select(p => p.SourceId!.Value).Distinct().ToList();
        var sources = (await _sources.GetListAsync(s => sourceIds.Contains(s.Id))).ToDictionary(s => s.Id, s => s.Name);
        var activity = await _activity.GetActivityAsync(ids);
        var canSeeIin = await CanSeeIinAsync();
        return page.Select(p =>
        {
            var dto = new PatientListItemDto();
            Fill(dto, p, canSeeIin);
            dto.Balance = balances.GetValueOrDefault(p.Id);
            dto.SourceName = p.SourceId is { } sid ? sources.GetValueOrDefault(sid) : null;
            if (activity.TryGetValue(p.Id, out var a))
            {
                dto.LastVisitAt = a.LastVisitAt;
                dto.NextAppointmentAt = a.NextAppointmentAt;
            }
            return dto;
        }).ToList();
    }

    private static void Fill(PatientListItemDto dto, Patient p, bool canSeeIin)
    {
        dto.Id = p.Id;
        dto.FullName = p.FullName;
        dto.LastName = p.LastName;
        dto.FirstName = p.FirstName;
        dto.MiddleName = p.MiddleName;
        dto.BirthDate = p.BirthDate;
        dto.Gender = p.Gender;
        dto.Phone = p.Phone;
        dto.Iin = canSeeIin ? p.Iin : Mask(p.Iin);
        dto.Tags = p.Tags.ToList();
        dto.IsVip = p.IsVip;
        dto.SourceId = p.SourceId;
        dto.CreationTime = p.CreationTime;
    }

    public async Task<PatientDto> GetAsync(Guid id)
    {
        var p = await _patients.GetAsync(id);
        var canSeeIin = await CanSeeIinAsync();
        var dto = new PatientDto
        {
            PhoneExtra = p.PhoneExtra,
            Email = p.Email,
            Address = p.Address,
            Notes = p.Notes,
            MergedIntoId = p.MergedIntoId,
            CanSeeIin = canSeeIin,
        };
        Fill(dto, p, canSeeIin);
        dto.Balance = (await _balances.FirstOrDefaultAsync(b => b.PatientId == id))?.Balance ?? 0;
        dto.SourceName = p.SourceId is { } sid ? (await _sources.FindAsync(sid))?.Name : null;
        if ((await _activity.GetActivityAsync([id])).TryGetValue(id, out var a))
        {
            dto.LastVisitAt = a.LastVisitAt;
            dto.NextAppointmentAt = a.NextAppointmentAt;
            dto.VisitsCount = a.VisitsCount;
            dto.TotalPaid = a.TotalPaid;
        }
        return dto;
    }

    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task<PatientDto> CreateAsync(CreateUpdatePatientDto input)
    {
        await _manager.EnsureNoDuplicatesAsync(input.Phone, input.Iin, null, input.IgnoreDuplicates);
        var p = new Patient(GuidGenerator.Create(), CurrentTenant.Id, input.LastName, input.FirstName, input.MiddleName);
        await ApplyAsync(p, input, keepIin: false);
        await _manager.InsertAsync(p);
        return await GetAsync(p.Id);
    }

    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task<PatientDto> UpdateAsync(Guid id, CreateUpdatePatientDto input)
    {
        var p = await _patients.GetAsync(id);
        // ИИН скрыт от ролей без права — маску не сохраняем.
        var keepIin = input.Iin is not null && input.Iin.Contains('*');
        if (!keepIin && !input.Iin.IsNullOrWhiteSpace() && input.Iin!.Trim() != p.Iin &&
            await _patients.AnyAsync(x => x.Iin == input.Iin.Trim() && x.Id != id && x.MergedIntoId == null))
        {
            throw new BusinessException(DentalDomainErrorCodes.IinTaken);
        }
        p.SetName(input.LastName, input.FirstName, input.MiddleName);
        await ApplyAsync(p, input, keepIin);
        await _patients.UpdateAsync(p, autoSave: true);
        return await GetAsync(id);
    }

    private async Task ApplyAsync(Patient p, CreateUpdatePatientDto r, bool keepIin)
    {
        p.SetBirthDate(r.BirthDate, DateOnly.FromDateTime(Clock.Now));
        p.Gender = r.Gender;
        if (!keepIin)
        {
            p.SetIin(r.Iin);
        }
        p.SetPhones(r.Phone, r.PhoneExtra);
        p.Email = r.Email.IsNullOrWhiteSpace() ? null : r.Email!.Trim();
        p.Address = r.Address.IsNullOrWhiteSpace() ? null : r.Address!.Trim();
        if (r.SourceId is { } sid && !await _sources.AnyAsync(s => s.Id == sid))
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(LeadSource), sid);
        }
        p.SourceId = r.SourceId;
        p.Notes = r.Notes.IsNullOrWhiteSpace() ? null : r.Notes!.Trim();
        p.SetTags(r.Tags);
        p.IsVip = r.IsVip;
    }

    public async Task<ListResultDto<PatientLookupDto>> GetLookupAsync(string? filter, int maxResultCount = 10)
    {
        if (filter.IsNullOrWhiteSpace() || filter!.Trim().Length < 2)
        {
            return new ListResultDto<PatientLookupDto>([]);
        }
        var query = ApplySearch((await _patients.GetQueryableAsync()).Where(p => p.MergedIntoId == null), filter)
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Take(Math.Clamp(maxResultCount, 1, 50));
        var rows = await AsyncExecuter.ToListAsync(query);
        var ids = rows.Select(r => r.Id).ToList();
        var balances = (await _balances.GetListAsync(b => ids.Contains(b.PatientId))).ToDictionary(b => b.PatientId, b => b.Balance);
        return new ListResultDto<PatientLookupDto>(rows.Select(p => new PatientLookupDto
        {
            Id = p.Id,
            FullName = p.FullName,
            Phone = p.Phone,
            BirthDate = p.BirthDate,
            IsVip = p.IsVip,
            Balance = balances.GetValueOrDefault(p.Id),
        }).ToList());
    }

    public async Task<ListResultDto<DuplicateCandidateDto>> CheckDuplicatesAsync(CheckDuplicatesInput input)
    {
        var list = await _manager.FindDuplicatesAsync(input.Phone, input.Iin, input.ExcludeId);
        var canSeeIin = await CanSeeIinAsync();
        return new ListResultDto<DuplicateCandidateDto>(list.Select(d => ToDto(d, canSeeIin)).ToList());
    }

    private static DuplicateCandidateDto ToDto(DuplicateCandidate d, bool canSeeIin) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        Phone = d.Phone,
        Iin = canSeeIin ? d.Iin : Mask(d.Iin),
        BirthDate = d.BirthDate,
        Reason = d.Reason,
    };

    /// <summary>Кандидаты на слияние: одинаковый телефон, ИИН, либо ФИО + дата рождения.</summary>
    [Authorize(DentalPermissions.Patients.Merge)]
    public async Task<ListResultDto<DuplicatePairDto>> GetDuplicatesAsync()
    {
        var patients = await AsyncExecuter.ToListAsync((await _patients.GetQueryableAsync()).Where(p => p.MergedIntoId == null));
        var canSeeIin = await CanSeeIinAsync();
        var result = new List<DuplicatePairDto>();
        var seen = new HashSet<(Guid, Guid)>();

        void Add(IEnumerable<IGrouping<string, Patient>> groups, string reason)
        {
            foreach (var g in groups)
            {
                var list = g.OrderBy(p => p.CreationTime).ToList();
                for (var i = 1; i < list.Count; i++)
                {
                    var (a, b) = (list[0].Id, list[i].Id);
                    if (!seen.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a)))
                    {
                        continue;
                    }
                    result.Add(new DuplicatePairDto
                    {
                        A = ToDto(new DuplicateCandidate(list[0].Id, list[0].FullName, list[0].Phone, list[0].Iin, list[0].BirthDate, reason), canSeeIin),
                        B = ToDto(new DuplicateCandidate(list[i].Id, list[i].FullName, list[i].Phone, list[i].Iin, list[i].BirthDate, reason), canSeeIin),
                        Reason = reason,
                    });
                }
            }
        }

        Add(patients.Where(p => p.Phone != null).GroupBy(p => p.Phone!).Where(g => g.Count() > 1), PatientConsts.DuplicateReasonPhone);
        Add(patients.Where(p => p.Iin != null).GroupBy(p => p.Iin!).Where(g => g.Count() > 1), PatientConsts.DuplicateReasonIin);
        Add(patients.Where(p => p.BirthDate != null)
            .GroupBy(p => $"{p.LastName.ToUpperInvariant()}|{p.FirstName.ToUpperInvariant()}|{p.BirthDate}")
            .Where(g => g.Count() > 1), PatientConsts.DuplicateReasonNameBirthDate);
        return new ListResultDto<DuplicatePairDto>(result.Take(200).ToList());
    }

    [Authorize(DentalPermissions.Patients.Merge)]
    [UnitOfWork(isTransactional: true)]
    public async Task<PatientDto> MergeAsync(MergePatientsInput input)
    {
        if (input.MainId == input.DuplicateId)
        {
            throw new BusinessException(DentalDomainErrorCodes.PatientMergeInvalid);
        }
        var main = await _patients.GetAsync(input.MainId);
        var dup = await _patients.GetAsync(input.DuplicateId);
        var moved = await _manager.MergeAsync(main, dup);
        Logger.LogInformation("Patient {Dup} merged into {Main}: {@Moved}", dup.Id, main.Id, moved);
        return await GetAsync(main.Id);
    }

    public async Task<PatientBalanceDto> GetBalanceAsync(Guid id)
    {
        await _patients.GetAsync(id);
        var balance = (await _balances.FirstOrDefaultAsync(b => b.PatientId == id))?.Balance ?? 0;
        var activity = (await _activity.GetActivityAsync([id])).GetValueOrDefault(id);
        return new PatientBalanceDto
        {
            Balance = balance,
            TotalBilled = activity?.TotalBilled ?? 0,
            TotalPaid = activity?.TotalPaid ?? 0,
            Debt = Math.Max(0, -balance),
            Advance = Math.Max(0, balance),
        };
    }

    public async Task<ListResultDto<PatientConsentDto>> GetConsentsAsync(Guid id)
    {
        var list = (await _consents.GetListAsync(c => c.PatientId == id)).OrderByDescending(c => c.SignedAt);
        return new ListResultDto<PatientConsentDto>(list.Select(ToDto).ToList());
    }

    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task<PatientConsentDto> AddConsentAsync(Guid id, CreatePatientConsentDto input)
    {
        await _patients.GetAsync(id);
        var c = new PatientConsent(GuidGenerator.Create(), CurrentTenant.Id, id, input.Type, input.SignedAt ?? Clock.Now, input.FileUrl);
        await _consents.InsertAsync(c, autoSave: true);
        return ToDto(c);
    }

    private static PatientConsentDto ToDto(PatientConsent c) => new()
    {
        Id = c.Id,
        PatientId = c.PatientId,
        Type = c.Type,
        SignedAt = c.SignedAt,
        FileUrl = c.FileUrl,
        CreationTime = c.CreationTime,
    };

    public async Task<List<string>> GetTagsAsync()
    {
        var raws = await AsyncExecuter.ToListAsync((await _patients.GetQueryableAsync())
            .Where(p => p.MergedIntoId == null && p.TagsRaw != "").Select(p => p.TagsRaw).Distinct());
        return raws.SelectMany(Patient.SplitTags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
    }
}
