using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dental.Staff;

namespace Dental.Web.Pages.Staff;

public class EmployeeFormViewModel
{
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [StringLength(128, MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    [StringLength(EmployeeConsts.MaxFullNameLength)]
    public string FullName { get; set; } = "";

    [StringLength(EmployeeConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    public StaffPosition Position { get; set; } = StaffPosition.Doctor;

    [Required]
    public string RoleName { get; set; } = "";

    [StringLength(EmployeeConsts.MaxSpecialtyLength)]
    public string? Specialty { get; set; }

    [StringLength(EmployeeConsts.MaxColorLength)]
    public string? Color { get; set; } = "#2563eb";

    public bool AllBranches { get; set; }

    public List<Guid> BranchIds { get; set; } = [];

    public static EmployeeFormViewModel From(EmployeeDto dto) => new()
    {
        Email = dto.Email,
        FullName = dto.FullName,
        Phone = dto.Phone,
        Position = dto.Position,
        RoleName = dto.RoleNames.Count > 0 ? dto.RoleNames[0] : "",
        Specialty = dto.Specialty,
        Color = dto.Color,
        AllBranches = dto.AllBranches,
        BranchIds = dto.BranchIds,
    };

    public void Fill(EmployeeInputBase input)
    {
        input.FullName = FullName;
        input.Phone = Phone;
        input.Position = Position;
        input.RoleName = RoleName;
        input.Specialty = Specialty;
        input.Color = Color;
        input.AllBranches = AllBranches;
        input.BranchIds = BranchIds;
    }
}
