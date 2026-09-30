using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Branches;

/// <summary>Стоматологическое кресло (ресурс записи). Может быть привязано к кабинету.</summary>
public class Chair : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? RoomId { get; set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; set; } = true;

    protected Chair() { }

    public Chair(Guid id, Guid? tenantId, Guid branchId, string name, Guid? roomId = null) : base(id)
    {
        TenantId = tenantId;
        BranchId = branchId;
        RoomId = roomId;
        SetName(name);
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), BranchConsts.MaxChairNameLength).Trim();
}
