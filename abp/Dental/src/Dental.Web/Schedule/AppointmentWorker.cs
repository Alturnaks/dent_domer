using System;
using System.Threading;
using System.Threading.Tasks;
using Dental.Schedule;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Dental.Web.Schedule;

public class AppointmentWorker(IServiceScopeFactory scopeFactory, ILogger<AppointmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        // Let the application finish starting before resolving tenant repositories.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var uows = sp.GetRequiredService<IUnitOfWorkManager>();
                using var hostUow = uows.Begin(requiresNew: true, isTransactional: false);
                var tenants = await sp.GetRequiredService<ITenantRepository>().GetListAsync(cancellationToken: stoppingToken);
                await hostUow.CompleteAsync(stoppingToken);
                foreach (var tenant in tenants)
                {
                    try
                    {
                        using var tenantScope = scopeFactory.CreateScope();
                        var services = tenantScope.ServiceProvider;
                        using var current = services.GetRequiredService<ICurrentTenant>().Change(tenant.Id);
                        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
                        if (await services.GetRequiredService<IAppointmentStore>().TryLockTasksAsync(tenant.Id))
                            await services.GetRequiredService<AppointmentReminderManager>().RunAsync();
                        await uow.CompleteAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Appointment tasks failed for tenant {TenantId}", tenant.Id); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Appointment worker failed"); }
        }
    }
}
