using Dental.Application.Notifications;

namespace Dental.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
        g.MapGet("", async (int? limit, bool? unread, NotificationQueryService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(limit ?? 30, unread ?? false, ct)))
            .WithName("ListNotifications");
        g.MapPost("/read-all", async (NotificationQueryService s, CancellationToken ct) => { await s.ReadAllAsync(ct); return TypedResults.NoContent(); })
            .WithName("ReadAllNotifications");
        g.MapPost("/{id:guid}/read", async (Guid id, NotificationQueryService s, CancellationToken ct) => { await s.ReadAsync(id, ct); return TypedResults.NoContent(); })
            .WithName("ReadNotification");
        return app;
    }
}
