using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Audit;
using Dental.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dental.Infrastructure.Services;

public sealed class NotificationService(AppDbContext db, ITenantContext tenant) : INotificationService
{
    public async Task NotifyByPermissionAsync(string permission, string type, string title, string? body, string? entityType, Guid? entityId,
        Guid? branchId, CancellationToken ct)
    {
        var recipients = await (from m in db.Memberships
                                join r in db.Roles on m.RoleId equals r.Id
                                where m.IsActive && m.FiredAt == null && (r.Permissions.Contains(permission) || r.Code == RolePresets.Owner)
                                      && (branchId == null || m.AllBranches || m.BranchIds.Contains(branchId.Value))
                                select m.UserId).Distinct().ToListAsync(ct);
        foreach (var userId in recipients) Notify(userId, type, title, body, entityType, entityId);
    }

    public async Task NotifyOwnersAsync(string type, string title, string? body, string? entityType, Guid? entityId, CancellationToken ct)
    {
        var owners = await (from m in db.Memberships
                            join r in db.Roles on m.RoleId equals r.Id
                            where m.IsActive && m.FiredAt == null && r.Code == RolePresets.Owner
                            select m.UserId).Distinct().ToListAsync(ct);
        foreach (var userId in owners) Notify(userId, type, title, body, entityType, entityId);
    }

    public void Notify(Guid userId, string type, string title, string? body, string? entityType, Guid? entityId) =>
        db.Notifications.Add(new Notification
        {
            OrganizationId = tenant.RequiredOrganizationId,
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            EntityType = entityType,
            EntityId = entityId,
        });
}
