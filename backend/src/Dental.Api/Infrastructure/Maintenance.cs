using Dapper;
using Dental.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dental.Api.Infrastructure;

public static class Maintenance
{
    /// <summary>Сравнивает кэш stock_balances с суммой журнала движений и пересобирает его.</summary>
    public const string DiffSql = """
        WITH m AS (
          SELECT organization_id, warehouse_id, item_id, batch_id, SUM(qty) AS qty
          FROM stock_movements GROUP BY organization_id, warehouse_id, item_id, batch_id
        )
        SELECT COUNT(*) FROM m
        FULL JOIN stock_balances b
          ON b.warehouse_id = m.warehouse_id AND b.item_id = m.item_id AND b.batch_id IS NOT DISTINCT FROM m.batch_id
        WHERE COALESCE(m.qty, 0) <> COALESCE(b.qty, 0)
        """;

    public const string RebuildSql = """
        DELETE FROM stock_balances;
        INSERT INTO stock_balances (id, organization_id, warehouse_id, item_id, batch_id, qty, avg_cost, created_at, updated_at)
        SELECT gen_random_uuid(), organization_id, warehouse_id, item_id, batch_id,
               SUM(qty),
               COALESCE(SUM(CASE WHEN qty > 0 THEN qty * unit_cost END) / NULLIF(SUM(CASE WHEN qty > 0 THEN qty END), 0), 0),
               now(), now()
        FROM stock_movements
        GROUP BY organization_id, warehouse_id, item_id, batch_id;
        """;

    public static async Task<int> RebuildBalancesAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("rebuild-balances");
        await using var conn = new NpgsqlConnection(db.OwnerConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var diff = await conn.ExecuteScalarAsync<long>(new CommandDefinition(DiffSql, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(RebuildSql, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        logger.LogInformation("Stock balances rebuilt from movements. Mismatched rows before rebuild: {Diff}", diff);
        return (int)diff;
    }
}
