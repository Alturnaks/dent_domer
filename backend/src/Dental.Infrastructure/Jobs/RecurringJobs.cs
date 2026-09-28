using Dapper;
using Hangfire;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dental.Infrastructure.Jobs;

/// <summary>Описание повторяющейся задачи Hangfire.</summary>
public interface IRecurringJobDefinition
{
    string Id { get; }
    string Cron { get; }
    void Register(IRecurringJobManager manager);
}

public sealed class RecurringJob<TJob>(string id, string cron) : IRecurringJobDefinition where TJob : IDentalJob
{
    public string Id => id;
    public string Cron => cron;

    public void Register(IRecurringJobManager manager) =>
        manager.AddOrUpdate<TJob>(id, j => j.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

/// <summary>Задача воркера. Реализации идемпотентны; при сбое Hangfire повторяет автоматически.</summary>
public interface IDentalJob
{
    Task RunAsync(CancellationToken ct);
}

/// <summary>audit_partition_maintenance: ежемесячно создаёт партиции audit_log на год вперёд.</summary>
public sealed class AuditPartitionMaintenanceJob(IOptions<DatabaseOptions> db) : IDentalJob
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(db.Value.OwnerConnectionString);
        await conn.ExecuteAsync(new CommandDefinition("SELECT ensure_audit_partitions(date_trunc('month', now())::date, 13)", cancellationToken: ct));
    }
}
