using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Notifications;

public class NotificationDto : EntityDto<Guid>
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreationTime { get; set; }
    /// <summary>Ссылка для перехода (по типу сущности).</summary>
    public string? Url { get; set; }
}

public class NotificationListDto
{
    public List<NotificationDto> Items { get; set; } = [];
    public int Unread { get; set; }
}

/// <summary>Уведомления текущего пользователя (колокольчик).</summary>
public interface INotificationAppService : IApplicationService
{
    Task<NotificationListDto> GetListAsync(bool unreadOnly = false, int maxResultCount = 20);
    Task MarkReadAsync(Guid id);
    Task MarkAllReadAsync();
}
