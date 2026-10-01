using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.References;

/// <summary>Причина отмены/переноса записи (справочник, используется расписанием).</summary>
[Audited]
public class CancelReason : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public CancelReasonType Type { get; set; }

    protected CancelReason() { }

    public CancelReason(Guid id, Guid? tenantId, string name, CancelReasonType type) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        Type = type;
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), ReferenceConsts.MaxNameLength).Trim();
}
