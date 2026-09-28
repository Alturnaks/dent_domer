using Dental.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Dental.Api.Endpoints;

public sealed record HealthResponse(string Status, string Database, string Cache, DateTimeOffset Time);

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (AppDbContext db, IDistributedCache cache, TimeProvider clock, CancellationToken ct) =>
            {
                string dbStatus, cacheStatus;
                try
                {
                    await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                    dbStatus = "ok";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    dbStatus = "error: " + ex.GetType().Name;
                }
                try
                {
                    await cache.SetStringAsync("health", "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, ct);
                    cacheStatus = "ok";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    cacheStatus = "error: " + ex.GetType().Name;
                }
                var ok = dbStatus == "ok" && cacheStatus == "ok";
                var body = new HealthResponse(ok ? "ok" : "degraded", dbStatus, cacheStatus, clock.GetUtcNow());
                return ok ? Results.Ok(body) : Results.Json(body, statusCode: 503);
            })
            .AllowAnonymous()
            .WithTags("System")
            .WithName("Health")
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(503);
        return app;
    }
}
