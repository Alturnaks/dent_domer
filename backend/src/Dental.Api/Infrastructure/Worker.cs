using Dental.Infrastructure;
using Dental.Infrastructure.Jobs;
using Hangfire;
using Hangfire.PostgreSql;

namespace Dental.Api.Infrastructure;

/// <summary>Воркер: тот же образ, запускается командой "worker" — Hangfire-сервер, outbox, cron-задачи.</summary>
public static class Worker
{
    public static WebApplicationBuilder AddDentalWorker(this WebApplicationBuilder builder)
    {
        var ownerCs = builder.Configuration[$"{DatabaseOptions.Section}:{nameof(DatabaseOptions.OwnerConnectionString)}"] ?? "";
        builder.Services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(ownerCs), new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
        builder.Services.AddHostedService<OutboxDispatcher>();
        builder.Services.AddHostedService<RecurringJobsRegistrar>();
        return builder;
    }

    /// <summary>В API без воркера фоновые задачи не запускаются.</summary>
    public static WebApplicationBuilder AddDentalBackground(this WebApplicationBuilder builder) => builder;

    public static WebApplication UseDentalWorkerDashboard(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/jobs", new DashboardOptions { Authorization = [new LocalOnlyDashboardFilter()] });
        }
        return app;
    }
}

internal sealed class LocalOnlyDashboardFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context) => true;
}

/// <summary>Регистрирует повторяющиеся задачи при старте воркера.</summary>
internal sealed class RecurringJobsRegistrar(IRecurringJobManager jobs, IEnumerable<IRecurringJobDefinition> definitions, ILogger<RecurringJobsRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var d in definitions)
        {
            d.Register(jobs);
            logger.LogInformation("Recurring job {Id} registered ({Cron})", d.Id, d.Cron);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
