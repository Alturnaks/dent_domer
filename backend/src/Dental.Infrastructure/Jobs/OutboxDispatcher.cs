using Dapper;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dental.Infrastructure.Jobs;

/// <summary>Обработчик события из outbox.</summary>
public interface IOutboxHandler
{
    string Type { get; }
    Task HandleAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// Диспетчер outbox: события пишутся в outbox_messages в транзакции use-case и обрабатываются здесь после коммита.
/// Каждое событие — в контексте своей организации; конкурентные воркеры не мешают друг другу (SKIP LOCKED).
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<DatabaseOptions> db, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(db.Value.OwnerConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var batch = (await conn.QueryAsync<(Guid Id, Guid OrgId)>(new CommandDefinition("""
            SELECT id, organization_id FROM outbox_messages
            WHERE processed_at IS NULL AND attempts < 5
            ORDER BY created_at LIMIT 50 FOR UPDATE SKIP LOCKED
            """, transaction: tx, cancellationToken: ct))).ToList();

        foreach (var (id, orgId) in batch)
        {
            string? error = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(orgId);
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var message = await ctx.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id, ct);
                var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Type == message.Type);
                if (handler is not null) await handler.HandleAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox message {Id} failed", id);
                error = ex.Message;
            }

            await conn.ExecuteAsync(new CommandDefinition(error is null
                    ? "UPDATE outbox_messages SET processed_at = now(), attempts = attempts + 1, error = NULL WHERE id = @id"
                    : "UPDATE outbox_messages SET attempts = attempts + 1, error = @error WHERE id = @id",
                new { id, error }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
