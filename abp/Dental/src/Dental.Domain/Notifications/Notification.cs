using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Notifications;

/// <summary>Внутреннее уведомление пользователю (колокольчик в шапке).</summary>
public class Notification : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Body { get; private set; }
    public string? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public DateTime? ReadAt { get; private set; }

    protected Notification() { }

    public Notification(Guid id, Guid? tenantId, Guid userId, string type, string title, string? body, string? entityType, Guid? entityId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Type = Check.NotNullOrWhiteSpace(type, nameof(type), NotificationConsts.MaxTypeLength);
        Title = Truncate(Check.NotNullOrWhiteSpace(title, nameof(title)), NotificationConsts.MaxTitleLength)!;
        Body = Truncate(body, NotificationConsts.MaxBodyLength);
        EntityType = entityType;
        EntityId = entityId;
    }

    public void MarkRead(DateTime at) => ReadAt ??= at;

    private static string? Truncate(string? s, int max) => s is { } v && v.Length > max ? v[..max] : s;
}
