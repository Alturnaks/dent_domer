using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Dental.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
namespace Dental.Audit;
public class SqlAuditStore(IDbContextProvider<DentalDbContext> provider) : IAuditStore,ITransientDependency
{
    public async Task<AuditPage> GetAsync(AuditQuery q)
    {
        var db=await provider.GetDbContextAsync();if(db.Database.GetDbConnection().State!=ConnectionState.Open)await db.Database.OpenConnectionAsync();await using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText="""
            WITH scoped AS (
              SELECT "Id"::text id FROM "AppVisits" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppPayments" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppCashShifts" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppExpenses" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppAppointments" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppStockDocuments" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppPurchaseOrders" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
              UNION SELECT "Id"::text FROM "AppPayrollPeriods" WHERE "TenantId"=@tenant AND "BranchId"=ANY(@branches)
            )
            SELECT c."Id",c."ChangeTime",l."UserName",c."EntityTypeFullName",c."EntityId",c."ChangeType",count(*) OVER(),
              coalesce((SELECT jsonb_agg(jsonb_build_object('name',p."PropertyName",'before',p."OriginalValue",'after',p."NewValue")) FROM "AbpEntityPropertyChanges" p WHERE p."EntityChangeId"=c."Id" AND p."PropertyName" NOT IN('Iin','Phone','PhoneExtra','Email','Address','Notes','ExtraProperties','ConcurrencyStamp','LastName','FirstName','MiddleName','PasswordHash','SecurityStamp','Details')), '[]'::jsonb)::text
            FROM "AbpEntityChanges" c JOIN "AbpAuditLogs" l ON l."Id"=c."AuditLogId"
            WHERE l."TenantId"=@tenant AND c."ChangeTime">=@from AND c."ChangeTime"<@to AND (@network OR c."EntityId" IN(SELECT id FROM scoped))
            ORDER BY c."ChangeTime" DESC,c."Id" OFFSET @offset LIMIT @limit
            """;
        void P(string name,object value){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}P("tenant",q.TenantId);P("branches",q.BranchIds);P("network",q.Network);P("from",q.From);P("to",q.To);P("offset",q.Offset);P("limit",q.Limit);
        var rows=new List<AuditRow>();long total=0;await using var reader=await cmd.ExecuteReaderAsync();while(await reader.ReadAsync()){total=reader.GetInt64(6);rows.Add(new(reader.GetGuid(0),reader.GetDateTime(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt16(5) switch{0=>"Создано",1=>"Изменено",2=>"Удалено",_=>"Операция"},JsonSerializer.Deserialize<List<AuditProperty>>(reader.GetString(7),new JsonSerializerOptions(JsonSerializerDefaults.Web))??[]));}return new(rows,total);
    }
}
