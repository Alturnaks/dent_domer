using Dental.Api.Auth;
using Dental.Application.Audit;
using Dental.Application.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Audit").RequireAuthorization();
        g.MapGet("/audit", async ([FromQuery(Name = "entity_type")] string? entityType, [FromQuery(Name = "entity_id")] Guid? entityId,
                [FromQuery(Name = "user_id")] Guid? userId, bool? suspicious, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int? limit,
                AuditQueryService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(new AuditQuery(entityType, entityId, userId, suspicious, from, to, cursor, limit ?? 50), ct)))
            .RequirePermission(Perm.Audit.View).WithName("ListAudit");

        // История изменений карточки пациента — для всех, кто видит пациентов (без права на весь журнал).
        g.MapGet("/patients/{id:guid}/history", async (Guid id, string? cursor, AuditQueryService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(new AuditQuery("Patient", id, null, null, null, null, cursor, 100), ct)))
            .RequirePermission(Perm.Patients.View).WithName("PatientHistory");
        return app;
    }
}
