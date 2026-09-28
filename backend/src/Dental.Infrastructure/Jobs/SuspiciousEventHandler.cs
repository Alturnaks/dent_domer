using System.Text.Json;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Infrastructure.Persistence;
using Dental.Infrastructure.Persistence.Interceptors;
using Dental.Infrastructure.Services;

namespace Dental.Infrastructure.Jobs;

/// <summary>По каждому подозрительному событию владелец получает уведомление (SPEC §8.8).</summary>
public sealed class SuspiciousEventHandler(INotificationService notifications, AppDbContext db) : IOutboxHandler
{
    public string Type => OutboxEvents.SuspiciousEvent;

    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SuspiciousEventPayload>(message.Payload, AuditingInterceptor.JsonOptions);
        if (payload is null) return;
        var title = SuspiciousTitles.For(payload.Action);
        await notifications.NotifyOwnersAsync("suspicious", title, payload.Reason, payload.EntityType, payload.EntityId, ct);
        await db.SaveChangesAsync(ct);
    }
}

public static class SuspiciousTitles
{
    public static string For(string action) => action switch
    {
        "closed_visit_edit" => "Изменён закрытый визит",
        "discount_over_limit" => "Скидка выше лимита роли",
        "payment_refund" => "Возврат платежа",
        "writeoff_large" => "Крупное ручное списание",
        "inventory_discrepancy" => "Крупное расхождение инвентаризации",
        "cash_difference" => "Расхождение кассы при закрытии смены",
        "login_new_device_off_hours" => "Вход с нового устройства вне рабочего времени",
        "visit_cancel_closed" => "Отменён закрытый визит",
        _ => "Подозрительное действие",
    };
}
