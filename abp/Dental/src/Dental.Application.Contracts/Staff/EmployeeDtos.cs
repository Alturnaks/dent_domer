using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dental.Roles;
using Volo.Abp.Application.Dtos;

namespace Dental.Staff;

public class EmployeeDto : FullAuditedEntityDto<Guid>
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public StaffPosition Position { get; set; }
    public string? Specialty { get; set; }
    public string? Color { get; set; }
    public bool AllBranches { get; set; }
    public List<Guid> BranchIds { get; set; } = [];
    public List<string> BranchNames { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public bool IsActive { get; set; }
    public DateTime? FiredAt { get; set; }
}

public abstract class EmployeeInputBase
{
    [Required]
    [StringLength(EmployeeConsts.MaxFullNameLength)]
    public string FullName { get; set; } = null!;

    [StringLength(EmployeeConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    public StaffPosition Position { get; set; } = StaffPosition.Other;

    /// <summary>Имя IdentityRole (например «Врач»).</summary>
    [Required]
    public string RoleName { get; set; } = null!;

    [StringLength(EmployeeConsts.MaxSpecialtyLength)]
    public string? Specialty { get; set; }

    [StringLength(EmployeeConsts.MaxColorLength)]
    public string? Color { get; set; }

    public bool AllBranches { get; set; }
    public List<Guid> BranchIds { get; set; } = [];
}

public class CreateEmployeeDto : EmployeeInputBase
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = null!;

    [Required]
    [StringLength(128, MinimumLength = 6)]
    public string Password { get; set; } = null!;
}

public class UpdateEmployeeDto : EmployeeInputBase
{
}

public class GetEmployeeListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public StaffPosition? Position { get; set; }
    public Guid? BranchId { get; set; }
    public bool? IsActive { get; set; }
}

public class GetEmployeeLookupInput
{
    public Guid? BranchId { get; set; }

    /// <summary>По умолчанию — врачи.</summary>
    public StaffPosition? Position { get; set; }
}

public class RoleLookupDto : EntityDto<Guid>
{
    public string Name { get; set; } = null!;
}

public class EmployeeLookupDto : EntityDto<Guid>
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = null!;
    public StaffPosition Position { get; set; }
    public string? Specialty { get; set; }
    public string? Color { get; set; }
}

/// <summary>Профиль текущего пользователя: сотрудник, доступные филиалы, лимиты.</summary>
public class CurrentEmployeeDto
{
    public Guid? EmployeeId { get; set; }
    public string? FullName { get; set; }
    public StaffPosition? Position { get; set; }
    public bool AllBranches { get; set; }
    public List<Guid> BranchIds { get; set; } = [];
    public RoleLimitsData Limits { get; set; } = new();
}
