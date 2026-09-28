using System.Data.Common;
using Dental.Application.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dental.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Второй слой изоляции — PostgreSQL RLS. При каждом открытии соединения выставляет app.org_id
/// (сессионно; Npgsql выполняет DISCARD ALL при возврате соединения в пул, значение перезаписывается при каждом открытии).
/// Пустое значение → политики возвращают 0 строк.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = CreateCommand(connection, tenant.OrganizationId);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = CreateCommand(connection, tenant.OrganizationId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static DbCommand CreateCommand(DbConnection connection, Guid? organizationId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.org_id', @org, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "org";
        p.Value = organizationId?.ToString() ?? "";
        cmd.Parameters.Add(p);
        return cmd;
    }
}
