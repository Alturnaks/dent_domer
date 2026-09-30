using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Branches;

/// <summary>Кабинет филиала.</summary>
public class Room : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = null!;

    protected Room() { }

    public Room(Guid id, Guid? tenantId, Guid branchId, string name) : base(id)
    {
        TenantId = tenantId;
        BranchId = branchId;
        SetName(name);
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), BranchConsts.MaxRoomNameLength).Trim();
}
