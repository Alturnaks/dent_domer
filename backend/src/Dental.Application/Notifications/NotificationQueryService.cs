using Dental.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Notifications;

public sealed record NotificationDto(Guid Id, string Type, string Title, string? Body, string? EntityType, Guid? EntityId, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);
public sealed record NotificationList(IReadOnlyList<NotificationDto> Items, int Unread);

public sealed class NotificationQueryService(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<NotificationList> ListAsync(int limit, bool unreadOnly, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == user.UserId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var items = await q.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(limit, 1, 200))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.EntityType, n.EntityId, n.ReadAt, n.CreatedAt)).ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == user.UserId && n.ReadAt == null, ct);
        return new NotificationList(items, unread);
    }

    public async Task ReadAllAsync(CancellationToken ct) =>
        await db.Notifications.Where(n => n.UserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);

    public async Task ReadAsync(Guid id, CancellationToken ct) =>
        await db.Notifications.Where(n => n.Id == id && n.UserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
}
