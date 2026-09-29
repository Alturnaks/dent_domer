using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Dental.Infrastructure.Persistence;

/// <summary>
/// Применение миграций под ролью-владельцем и идемпотентная пост-настройка:
/// права роли приложения, RLS-политики для всех таблиц с organization_id, партиции audit_log.
/// </summary>
public static class DatabaseBootstrap
{
    /// <summary>
    /// Для каждой таблицы public с колонкой organization_id включается RLS с политикой по app.org_id.
    /// Роль приложения не имеет BYPASSRLS; владелец таблиц (миграции, системные выборки) политикам не подчиняется.
    /// </summary>
    public const string RlsSql = """
        DO $$
        DECLARE t record;
        BEGIN
          FOR t IN
            SELECT c.table_name FROM information_schema.columns c
            JOIN information_schema.tables tb ON tb.table_schema = c.table_schema AND tb.table_name = c.table_name
            WHERE c.table_schema = 'public' AND c.column_name = 'organization_id' AND tb.table_type = 'BASE TABLE'
              AND c.table_name NOT IN ('refresh_tokens')
          LOOP
            EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t.table_name);
            EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t.table_name);
            EXECUTE format($p$CREATE POLICY tenant_isolation ON %I
                USING (organization_id = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.org_id', true), '')::uuid)$p$, t.table_name);
          END LOOP;
        END $$;

        -- refresh_tokens — глобальная таблица аутентификации (обновление сессии идёт до выбора организации).
        DROP POLICY IF EXISTS tenant_isolation ON refresh_tokens;
        ALTER TABLE refresh_tokens DISABLE ROW LEVEL SECURITY;

        ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_isolation ON organizations;
        CREATE POLICY tenant_isolation ON organizations
          USING (id = NULLIF(current_setting('app.org_id', true), '')::uuid)
          WITH CHECK (id = NULLIF(current_setting('app.org_id', true), '')::uuid);
        """;

    public static string GrantsSql(string appRole) => $"""
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{appRole}') THEN
            EXECUTE 'GRANT USAGE ON SCHEMA public TO {appRole}';
            EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {appRole}';
            EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {appRole}';
            EXECUTE 'GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO {appRole}';
            -- Журнал движений и аудит неизменяемы для приложения.
            EXECUTE 'REVOKE UPDATE, DELETE ON stock_movements FROM {appRole}';
            EXECUTE 'REVOKE UPDATE, DELETE ON audit_log FROM {appRole}';
            IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'hangfire') THEN
              EXECUTE 'GRANT USAGE, CREATE ON SCHEMA hangfire TO {appRole}';
              EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO {appRole}';
              EXECUTE 'GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO {appRole}';
            END IF;
          END IF;
        END $$;
        """;

    public const string AuditPartitionsSql = """
        SELECT ensure_audit_partitions((date_trunc('month', now()) - interval '3 months')::date, 15);
        """;

    public static async Task MigrateAsync(string ownerConnectionString, string appConnectionString, ILogger logger, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ownerConnectionString, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new AppDbContext(options, new Tenancy.TenantContext()))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            logger.LogInformation("Pending migrations: {Count} {Names}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(ct);
        }

        var appRole = new NpgsqlConnectionStringBuilder(appConnectionString).Username ?? "dental_app";
        await using var conn = new NpgsqlConnection(ownerConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(RlsSql, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(AuditPartitionsSql, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(GrantsSql(appRole), cancellationToken: ct));
        logger.LogInformation("RLS policies, audit partitions and grants for role {Role} applied", appRole);
    }
}
