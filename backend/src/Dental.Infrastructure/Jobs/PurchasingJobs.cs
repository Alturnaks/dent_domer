using Dental.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dental.Infrastructure.Jobs;

/// <summary>
/// generate_replenishment_requests: ежедневно в 03:00 по часовому поясу организации формирует автозаявки
/// (склады с остатком ниже минимума). Задача запускается ежечасно; идемпотентна — открытая автозаявка склада обновляется.
/// </summary>
public sealed class GenerateReplenishmentRequestsJob(TenantJobRunner runner, TimeProvider clock, ILogger<GenerateReplenishmentRequestsJob> logger) : IDentalJob
{
    public const int LocalHour = 3;

    public Task RunAsync(CancellationToken ct) => runner.ForEachOrganizationAsync("generate_replenishment_requests", async (sp, orgId, tz, token) =>
    {
        if (TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).Hour != LocalHour) return;
        var result = await sp.GetRequiredService<PurchaseRequestService>().GenerateAllAsync(token);
        logger.LogInformation("Replenishment requests for {OrgId}: created {Created}, updated {Updated}, closed {Closed}", orgId, result.Created, result.Updated, result.Closed);
    }, ct);
}
