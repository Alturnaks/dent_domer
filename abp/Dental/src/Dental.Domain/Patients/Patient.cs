using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Patients;

/// <summary>
/// Пациент. Телефоны хранятся нормализованными (7XXXXXXXXXX). Теги — строка вида "|тег1|тег2|"
/// (портируемый фильтр по тегу без массивов). Слитая карточка получает MergedIntoId и скрывается из списков.
/// </summary>
[Audited]
public class Patient : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string LastName { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string? MiddleName { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public Gender Gender { get; set; }
    public string? Iin { get; private set; }
    public string? Phone { get; private set; }
    public string? PhoneExtra { get; private set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? SourceId { get; set; }
    public string? Notes { get; set; }
    public string TagsRaw { get; private set; } = "";
    public bool IsVip { get; set; }
    public Guid? MergedIntoId { get; private set; }

    public string FullName => BuildFullName(LastName, FirstName, MiddleName);

    public IReadOnlyList<string> Tags => SplitTags(TagsRaw);

    protected Patient() { }

    public Patient(Guid id, Guid? tenantId, string lastName, string firstName, string? middleName = null) : base(id)
    {
        TenantId = tenantId;
        SetName(lastName, firstName, middleName);
    }

    public void SetName(string lastName, string firstName, string? middleName)
    {
        LastName = Check.NotNullOrWhiteSpace(lastName, nameof(lastName), PatientConsts.MaxNameLength).Trim();
        FirstName = Check.NotNullOrWhiteSpace(firstName, nameof(firstName), PatientConsts.MaxNameLength).Trim();
        MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : Check.Length(middleName.Trim(), nameof(middleName), PatientConsts.MaxNameLength);
    }

    public void SetBirthDate(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is { } d && (d.Year <= 1900 || d > today))
        {
            throw new BusinessException(DentalDomainErrorCodes.InvalidBirthDate);
        }
        BirthDate = birthDate;
    }

    /// <summary>ИИН: пусто или 12 цифр с корректной контрольной суммой.</summary>
    public void SetIin(string? iin)
    {
        if (string.IsNullOrWhiteSpace(iin))
        {
            Iin = null;
            return;
        }
        iin = iin.Trim();
        if (!IinValidator.IsValid(iin))
        {
            throw new BusinessException(DentalDomainErrorCodes.InvalidIin).WithData("iin", iin);
        }
        Iin = iin;
    }

    public void SetPhones(string? phone, string? phoneExtra)
    {
        Phone = NormalizeValidPhone(phone);
        PhoneExtra = NormalizeValidPhone(phoneExtra);
    }

    public void SetTags(IEnumerable<string>? tags)
    {
        var list = (tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)
            .Select(t => t.Replace("|", "", StringComparison.Ordinal))
            .Select(t => t.Length > PatientConsts.MaxTagLength ? t[..PatientConsts.MaxTagLength] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TagsRaw = list.Count == 0 ? "" : "|" + string.Join('|', list) + "|";
    }

    /// <summary>Отметить карточку как дубль, слитый в основную.</summary>
    internal void MarkMergedInto(Guid mainId)
    {
        MergedIntoId = mainId;
        Iin = null;
    }

    /// <summary>Дополнить пустые поля данными дубля (при слиянии).</summary>
    internal void AbsorbFrom(Patient dup)
    {
        var dupIin = dup.Iin;
        Phone ??= dup.Phone;
        if (PhoneExtra is null && dup.Phone != Phone)
        {
            PhoneExtra = dup.Phone;
        }
        BirthDate ??= dup.BirthDate;
        Email ??= dup.Email;
        Address ??= dup.Address;
        SourceId ??= dup.SourceId;
        IsVip = IsVip || dup.IsVip;
        SetTags(Tags.Union(dup.Tags));
        if (!string.IsNullOrWhiteSpace(dup.Notes))
        {
            Notes = string.Join("\n", new[] { Notes, dup.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        dup.MarkMergedInto(Id);
        Iin ??= dupIin;
    }

    public static string BuildFullName(string lastName, string firstName, string? middleName) =>
        string.Join(' ', new[] { lastName, firstName, middleName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static IReadOnlyList<string> SplitTags(string? raw) =>
        string.IsNullOrEmpty(raw) ? [] : raw.Split('|', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Нормализация телефона к виду 7XXXXXXXXXX для поиска дублей.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '8')
        {
            digits = "7" + digits[1..];
        }
        if (digits.Length == 10)
        {
            digits = "7" + digits;
        }
        return digits.Length == 0 ? null : digits;
    }

    private static string? NormalizeValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }
        var normalized = NormalizePhone(phone);
        if (normalized is null || normalized.Length != 11)
        {
            throw new BusinessException(DentalDomainErrorCodes.InvalidPhone).WithData("phone", phone);
        }
        return normalized;
    }
}
