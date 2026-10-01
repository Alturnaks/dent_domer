using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Dental.Patients;

public class GetPatientListInput : PagedAndSortedResultRequestDto
{
    /// <summary>ФИО (по словам, без учёта регистра), телефон (от 4 цифр) или ИИН.</summary>
    public string? Filter { get; set; }
    public string? Tag { get; set; }
    public Guid? SourceId { get; set; }
    /// <summary>Только должники (баланс &lt; 0).</summary>
    public bool? Debtors { get; set; }
    /// <summary>Не было закрытых визитов N месяцев (работает, когда подключён модуль визитов).</summary>
    public int? NotVisitedMonths { get; set; }
    public bool? IsVip { get; set; }
}

public class PatientListItemDto : EntityDto<Guid>
{
    public string FullName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Gender Gender { get; set; }
    public string? Phone { get; set; }
    /// <summary>Маскируется для ролей без права ViewMedical/Edit.</summary>
    public string? Iin { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsVip { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceName { get; set; }
    /// <summary>+ аванс / − долг, тиыны.</summary>
    public long Balance { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public DateTime? NextAppointmentAt { get; set; }
    public DateTime CreationTime { get; set; }
}

public class PatientDto : PatientListItemDto
{
    public string? PhoneExtra { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public Guid? MergedIntoId { get; set; }
    public int VisitsCount { get; set; }
    public long TotalPaid { get; set; }
    public bool CanSeeIin { get; set; }
}

public class CreateUpdatePatientDto
{
    [Required, StringLength(PatientConsts.MaxNameLength)]
    public string LastName { get; set; } = "";

    [Required, StringLength(PatientConsts.MaxNameLength)]
    public string FirstName { get; set; } = "";

    [StringLength(PatientConsts.MaxNameLength)]
    public string? MiddleName { get; set; }

    public DateOnly? BirthDate { get; set; }
    public Gender Gender { get; set; }

    /// <summary>12 цифр с контрольной суммой. Маска (со звёздочками) при обновлении — оставить как было.</summary>
    [StringLength(PatientConsts.MaxIinLength)]
    public string? Iin { get; set; }

    [StringLength(PatientConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    [StringLength(PatientConsts.MaxPhoneLength)]
    public string? PhoneExtra { get; set; }

    [EmailAddress, StringLength(PatientConsts.MaxEmailLength)]
    public string? Email { get; set; }

    [StringLength(PatientConsts.MaxAddressLength)]
    public string? Address { get; set; }

    public Guid? SourceId { get; set; }

    [StringLength(PatientConsts.MaxNotesLength)]
    public string? Notes { get; set; }

    public List<string>? Tags { get; set; }
    public bool IsVip { get; set; }

    /// <summary>Создать, несмотря на совпадение телефона (совпадение ИИН всё равно запрещено).</summary>
    public bool IgnoreDuplicates { get; set; }
}

public class DuplicateCandidateDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Iin { get; set; }
    public DateOnly? BirthDate { get; set; }
    /// <summary>phone | iin | name_birthdate.</summary>
    public string Reason { get; set; } = "";
}

public class CheckDuplicatesInput
{
    public string? Phone { get; set; }
    public string? Iin { get; set; }
    public Guid? ExcludeId { get; set; }
}

public class DuplicatePairDto
{
    public DuplicateCandidateDto A { get; set; } = new();
    public DuplicateCandidateDto B { get; set; } = new();
    public string Reason { get; set; } = "";
}

public class MergePatientsInput
{
    [Required]
    public Guid MainId { get; set; }

    [Required]
    public Guid DuplicateId { get; set; }
}

public class PatientBalanceDto
{
    public long Balance { get; set; }
    public long TotalBilled { get; set; }
    public long TotalPaid { get; set; }
    public long Debt { get; set; }
    public long Advance { get; set; }
}

public class PatientConsentDto : EntityDto<Guid>
{
    public Guid PatientId { get; set; }
    public string Type { get; set; } = "";
    public DateTime SignedAt { get; set; }
    public string? FileUrl { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreatePatientConsentDto
{
    [Required, StringLength(PatientConsts.MaxConsentTypeLength)]
    public string Type { get; set; } = "";

    public DateTime? SignedAt { get; set; }

    [StringLength(PatientConsts.MaxFileUrlLength)]
    public string? FileUrl { get; set; }
}

/// <summary>Короткая запись для глобального поиска и выпадающих списков других модулей.</summary>
public class PatientLookupDto : EntityDto<Guid>
{
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool IsVip { get; set; }
    public long Balance { get; set; }
}

public class LeadSourceDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
}

public class CreateUpdateLeadSourceDto
{
    [Required, StringLength(PatientConsts.MaxLeadSourceNameLength)]
    public string Name { get; set; } = "";
}
