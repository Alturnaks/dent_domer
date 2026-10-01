using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Inventory;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Dental.EntityFrameworkCore;

/// <summary>Блокировки строк для складских операций: PostgreSQL — SELECT … FOR UPDATE в текущей транзакции UoW.</summary>
public class EfCoreStockLockProvider : IStockLockProvider, ITransientDependency
{
    private readonly IDbContextProvider<DentalDbContext> _dbContextProvider;

    public EfCoreStockLockProvider(IDbContextProvider<DentalDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task LockBalancesAsync(Guid warehouseId, Guid itemId)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null) return;
        await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"AppStockBalances\" WHERE \"WarehouseId\" = {warehouseId} AND \"ItemId\" = {itemId} ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync();
    }

    public async Task LockDocumentAsync(Guid documentId)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null) return;
        await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"AppStockDocuments\" WHERE \"Id\" = {documentId} FOR UPDATE")
            .ToListAsync();
    }
}

/// <summary>
/// Нумерация документов (AppDocumentCounters). PostgreSQL — атомарный upsert … RETURNING в транзакции документа
/// (строка счётчика заблокирована до коммита, откат — без дыр). Другие провайдеры — через EF.
/// </summary>
public class EfCoreDocumentNumberGenerator : IDocumentNumberGenerator, ITransientDependency
{
    private readonly IDbContextProvider<DentalDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;

    public EfCoreDocumentNumberGenerator(IDbContextProvider<DentalDbContext> dbContextProvider, ICurrentTenant currentTenant, IGuidGenerator guidGenerator)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
    }

    public async Task<long> NextAsync(string key)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        if (db.Database.IsNpgsql())
        {
            var id = _guidGenerator.Create();
            var values = await db.Database.SqlQuery<long>($"""
                INSERT INTO "AppDocumentCounters" ("Id", "TenantId", "Key", "Value") VALUES ({id}, {tenantId}, {key}, 1)
                ON CONFLICT ("TenantId", "Key") DO UPDATE SET "Value" = "AppDocumentCounters"."Value" + 1
                RETURNING "Value"
                """).ToListAsync();
            return values[0];
        }

        var counter = await db.DocumentCounters.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Key == key);
        if (counter is null)
        {
            counter = new DocumentCounter(_guidGenerator.Create(), tenantId, key);
            db.DocumentCounters.Add(counter);
        }
        var next = counter.Next();
        await db.SaveChangesAsync();
        return next;
    }
}
