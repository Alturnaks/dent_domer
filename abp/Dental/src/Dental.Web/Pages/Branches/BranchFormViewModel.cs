using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Dental.Branches;

namespace Dental.Web.Pages.Branches;

public class BranchFormViewModel
{
    [Required]
    [StringLength(BranchConsts.MaxNameLength)]
    public string Name { get; set; } = "";

    [StringLength(BranchConsts.MaxAddressLength)]
    public string? Address { get; set; }

    [StringLength(BranchConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Порядок отображения: пн … вс.</summary>
    public List<WorkingDayViewModel> Days { get; set; } = [];

    public static BranchFormViewModel From(BranchDto? dto)
    {
        var hours = dto?.WorkingHours is { Count: 7 } h ? h : Branch_DefaultHours();
        return new BranchFormViewModel
        {
            Name = dto?.Name ?? "",
            Address = dto?.Address,
            Phone = dto?.Phone,
            IsActive = dto?.IsActive ?? true,
            Days = hours.OrderBy(d => (d.DayOfWeek + 6) % 7).Select(d => new WorkingDayViewModel
            {
                DayOfWeek = d.DayOfWeek,
                IsWorking = d.IsWorking,
                Open = d.Open.ToString("HH:mm", CultureInfo.InvariantCulture),
                Close = d.Close.ToString("HH:mm", CultureInfo.InvariantCulture),
            }).ToList(),
        };
    }

    public CreateUpdateBranchDto ToDto() => new()
    {
        Name = Name,
        Address = Address,
        Phone = Phone,
        IsActive = IsActive,
        WorkingHours = Days.Select(d => new WorkingDay
        {
            DayOfWeek = d.DayOfWeek,
            IsWorking = d.IsWorking,
            Open = ParseTime(d.Open, new TimeOnly(9, 0)),
            Close = ParseTime(d.Close, new TimeOnly(20, 0)),
        }).ToList(),
    };

    private static TimeOnly ParseTime(string? s, TimeOnly fallback) =>
        TimeOnly.TryParse(s, CultureInfo.InvariantCulture, out var t) ? t : fallback;

    private static List<WorkingDay> Branch_DefaultHours() => Enumerable.Range(0, 7).Select(d => new WorkingDay
    {
        DayOfWeek = d,
        IsWorking = d != 0,
        Open = new TimeOnly(9, 0),
        Close = d == 6 ? new TimeOnly(15, 0) : new TimeOnly(20, 0),
    }).ToList();
}

public class WorkingDayViewModel
{
    public int DayOfWeek { get; set; }
    public bool IsWorking { get; set; }
    public string? Open { get; set; }
    public string? Close { get; set; }
}
