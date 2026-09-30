using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Branches;

/// <summary>Филиал сети клиник. Часы работы хранятся в jsonb.</summary>
public class Branch : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public List<WorkingDay> WorkingHours { get; private set; } = [];

    protected Branch() { }

    public Branch(Guid id, Guid? tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        WorkingHours = DefaultWorkingHours();
    }

    public void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), BranchConsts.MaxNameLength).Trim();
    }

    public void SetWorkingHours(IEnumerable<WorkingDay> days)
    {
        var list = days.OrderBy(d => d.DayOfWeek).ToList();
        if (list.Count != 7 || list.Select(d => d.DayOfWeek).Distinct().Count() != 7 || list.Any(d => d.DayOfWeek is < 0 or > 6))
        {
            throw new BusinessException(DentalDomainErrorCodes.InvalidWorkingHours);
        }
        if (list.Any(d => d.IsWorking && d.Close <= d.Open))
        {
            throw new BusinessException(DentalDomainErrorCodes.InvalidWorkingHours);
        }
        WorkingHours = list;
    }

    public WorkingDay? For(DayOfWeek day) => WorkingHours.FirstOrDefault(d => d.DayOfWeek == (int)day);

    /// <summary>Пн–Пт 9–20, Сб 9–15, Вс выходной.</summary>
    public static List<WorkingDay> DefaultWorkingHours() => Enumerable.Range(0, 7).Select(d => new WorkingDay
    {
        DayOfWeek = d,
        IsWorking = d != 0,
        Open = new TimeOnly(9, 0),
        Close = d == 6 ? new TimeOnly(15, 0) : new TimeOnly(20, 0),
    }).ToList();
}
