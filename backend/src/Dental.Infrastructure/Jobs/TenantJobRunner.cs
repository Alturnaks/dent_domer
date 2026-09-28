using Dental.Application.Auth;
using Dental.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dental.Infrastructure.Jobs;

/// <summary>Запуск действия в контексте каждой организации (отдельный scope, tenant задан явно).</summary>
public sealed class TenantJobRunner(IServiceScopeFactory scopes, ISystemDb systemDb, ILogger<TenantJobRunner> logger)
{
    public async Task ForEachOrganizationAsync(string jobName, Func<IServiceProvider, Guid, TimeZoneInfo, CancellationToken, Task> action, CancellationToken ct)
    {
        var orgs = await systemDb.ListOrganizationsAsync(ct);
        foreach (var (orgId, tzId) in orgs)
        {
            await RunForOrganizationAsync(jobName, orgId, tzId, action, ct);
        }
    }

    public async Task RunForOrganizationAsync(string jobName, Guid orgId, string tzId, Func<IServiceProvider, Guid, TimeZoneInfo, CancellationToken, Task> action, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(orgId);
        var tz = TimeZones.Find(tzId);
        try
        {
            await action(scope.ServiceProvider, orgId, tz, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Job {Job} failed for organization {OrgId}", jobName, orgId);
            throw;
        }
    }
}

public static class TimeZones
{
    /// <summary>IANA-идентификатор → TimeZoneInfo (на Windows .NET сам конвертирует через ICU).</summary>
    public static TimeZoneInfo Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) id = "Asia/Almaty";
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("UTC+05", TimeSpan.FromHours(5), "UTC+05", "UTC+05"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.CreateCustomTimeZone("UTC+05", TimeSpan.FromHours(5), "UTC+05", "UTC+05"); }
    }
}
