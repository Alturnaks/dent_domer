using Dental.Domain.Common;

namespace Dental.Domain.Patients;

public enum Gender { Unknown, Male, Female }

[Audited]
public class Patient : TenantEntity, ISoftDeletable
{
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Gender Gender { get; set; }
    [Sensitive] public string? Iin { get; set; }
    [Sensitive] public string? Phone { get; set; }
    [Sensitive] public string? PhoneExtra { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? SourceId { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsVip { get; set; }
    public Guid? MergedIntoId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Нормализация телефона к виду 7XXXXXXXXXX для поиска дублей.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        if (digits.Length == 10) digits = "7" + digits;
        return digits.Length == 0 ? null : digits;
    }
}

public class PatientConsent : TenantEntity
{
    public Guid PatientId { get; set; }
    public string Type { get; set; } = "";
    public DateTimeOffset SignedAt { get; set; }
    public string? FileUrl { get; set; }
}

/// <summary>Кэш баланса пациента: + аванс / − долг (тиыны).</summary>
public class PatientBalance : TenantEntity
{
    public Guid PatientId { get; set; }
    public long Balance { get; set; }
}

[Audited]
public class LeadSource : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public DateTimeOffset? DeletedAt { get; set; }
}
