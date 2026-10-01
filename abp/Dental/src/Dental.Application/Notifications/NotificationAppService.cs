using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Dental.Notifications;

[Authorize]
public class NotificationAppService : DentalAppService, INotificationAppService
{
    private readonly IRepository<Notification, Guid> _notifications;

    public NotificationAppService(IRepository<Notification, Guid> notifications) => _notifications = notifications;

    public async Task<NotificationListDto> GetListAsync(bool unreadOnly = false, int maxResultCount = 20)
    {
        var userId = CurrentUser.GetId();
        var query = (await _notifications.GetQueryableAsync()).Where(n => n.UserId == userId);
        var unread = await AsyncExecuter.CountAsync(query.Where(n => n.ReadAt == null));
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }
        var items = await AsyncExecuter.ToListAsync(query.OrderByDescending(n => n.CreationTime).Take(Math.Clamp(maxResultCount, 1, 200)));
        return new NotificationListDto
        {
            Unread = unread,
            Items = items.Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                EntityType = n.EntityType,
                EntityId = n.EntityId,
                ReadAt = n.ReadAt,
                CreationTime = n.CreationTime,
                Url = UrlFor(n.EntityType, n.EntityId),
            }).ToList(),
        };
    }

    /// <summary>Куда вести по клику. Модули добавляют свои типы сюда.</summary>
    public static string? UrlFor(string? entityType, Guid? entityId) => entityType switch
    {
        nameof(ApprovalRequest) => "/Approvals",
        "Appointment" when entityId is { } appointmentId => "/Schedule?appointmentId=" + appointmentId,
        "Patient" when entityId is { } id => "/Patients/Detail?id=" + id,
        "StockDocument" when entityId is { } id => "/Inventory/Documents/Edit?id=" + id,
        _ => null,
    };

    public async Task MarkReadAsync(Guid id)
    {
        var n = await _notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUser.GetId());
        if (n is not null)
        {
            n.MarkRead(Clock.Now);
            await _notifications.UpdateAsync(n);
        }
    }

    public async Task MarkAllReadAsync()
    {
        var userId = CurrentUser.GetId();
        var list = await _notifications.GetListAsync(x => x.UserId == userId && x.ReadAt == null);
        foreach (var n in list)
        {
            n.MarkRead(Clock.Now);
        }
        await _notifications.UpdateManyAsync(list);
    }
}
