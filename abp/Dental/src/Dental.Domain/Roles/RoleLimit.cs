using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Roles;

/// <summary>Лимиты роли (1:1 с IdentityRole по RoleId). Отсутствие записи = всё запрещено.</summary>
public class RoleLimit : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RoleId { get; private set; }
    public decimal? MaxDiscountPct { get; private set; }
    public long? MaxWriteoffAmount { get; private set; }
    public long? MaxRefundAmount { get; private set; }
    public bool CanEditClosedShiftVisits { get; private set; }

    protected RoleLimit() { }

    public RoleLimit(Guid id, Guid? tenantId, Guid roleId, RoleLimitsData limits) : base(id)
    {
        TenantId = tenantId;
        RoleId = roleId;
        Set(limits);
    }

    public void Set(RoleLimitsData limits)
    {
        MaxDiscountPct = limits.MaxDiscountPct is { } p ? Math.Clamp(p, 0, 100) : null;
        MaxWriteoffAmount = limits.MaxWriteoffAmount is { } w ? Math.Max(0, w) : null;
        MaxRefundAmount = limits.MaxRefundAmount is { } r ? Math.Max(0, r) : null;
        CanEditClosedShiftVisits = limits.CanEditClosedShiftVisits;
    }

    public RoleLimitsData ToData() => new()
    {
        MaxDiscountPct = MaxDiscountPct,
        MaxWriteoffAmount = MaxWriteoffAmount,
        MaxRefundAmount = MaxRefundAmount,
        CanEditClosedShiftVisits = CanEditClosedShiftVisits,
    };
}
