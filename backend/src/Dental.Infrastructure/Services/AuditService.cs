using System.Text.Json;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Infrastructure.Persistence;
using Dental.Infrastructure.Persistence.Interceptors;

namespace Dental.Infrastructure.Services;

public sealed class AuditService(AppDbContext db, ITenantContext tenant, IAuditActor actor, TimeProvider clock) : IAuditService
{
    public void Log(string entityType, Guid? entityId, string action, object? diff = null, string? reason = null, bool suspicious = false, Guid? branchId = null)
    {
        var orgId = tenant.RequiredOrganizationId;
        var log = new AuditLog
        {
            OrganizationId = orgId,
            UserId = actor.UserId,
            BranchId = branchId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Diff = diff is null ? null : JsonSerializer.Serialize(diff, AuditingInterceptor.JsonOptions),
            Reason = reason ?? actor.Reason,
            Ip = actor.Ip,
            UserAgent = actor.UserAgent,
            IsSuspicious = suspicious,
            CreatedAt = clock.GetUtcNow(),
        };
        db.AuditLogs.Add(log);

        if (suspicious)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                OrganizationId = orgId,
                Type = OutboxEvents.SuspiciousEvent,
                Payload = JsonSerializer.Serialize(new SuspiciousEventPayload(log.Id, entityType, entityId, action, reason, branchId), AuditingInterceptor.JsonOptions),
                CreatedAt = log.CreatedAt,
            });
        }
    }
}

public static class OutboxEvents
{
    public const string SuspiciousEvent = "audit.suspicious";
}

public sealed record SuspiciousEventPayload(Guid AuditId, string EntityType, Guid? EntityId, string Action, string? Reason, Guid? BranchId);

public sealed class Outbox(AppDbContext db, ITenantContext tenant, TimeProvider clock) : IOutbox
{
    public void Enqueue(string type, object payload) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            OrganizationId = tenant.RequiredOrganizationId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, AuditingInterceptor.JsonOptions),
            CreatedAt = clock.GetUtcNow(),
        });
}
