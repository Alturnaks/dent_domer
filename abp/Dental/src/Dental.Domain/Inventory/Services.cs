using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Dental.Inventory;

/// <summary>
/// Блокировки строк на время транзакции (PostgreSQL: SELECT … FOR UPDATE). Реализация — в EntityFrameworkCore;
/// на других провайдерах (SQLite в тестах) — без блокировки.
/// </summary>
public interface IStockLockProvider
{
    /// <summary>Блокирует строки кэша остатков (склад, товар). Порядок блокировок задаёт вызывающий.</summary>
    Task LockBalancesAsync(Guid warehouseId, Guid itemId);

    /// <summary>Блокирует строку документа.</summary>
    Task LockDocumentAsync(Guid documentId);
}

/// <summary>Нумерация документов: следующий номер счётчика в текущей транзакции (откат транзакции — откат номера).</summary>
public interface IDocumentNumberGenerator
{
    Task<long> NextAsync(string key);
}

/// <summary>
/// Шлюз подтверждений (временный контракт склада до слияния с модулем Approvals).
/// Реализация по умолчанию — лимиты роли без очереди подтверждений.
/// </summary>
public interface IApprovalGateway
{
    /// <summary>
    /// true — сумма в пределах лимита роли, действие выполняется сразу; false — создан запрос подтверждения, документ ждёт.
    /// Реализация по умолчанию при превышении бросает Dental:RoleLimitExceeded.
    /// </summary>
    Task<bool> RequestIfOverWriteoffLimitAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId);

    /// <summary>Безусловный запрос подтверждения (недостача при перемещении).</summary>
    Task RequestAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId);

    /// <summary>Отклоняет ожидающие запросы по сущности (документ отменён).</summary>
    Task CancelPendingAsync(string entityType, Guid entityId, string reason);
}

/// <summary>Реализация по умолчанию: лимит роли (IRoleLimitsProvider), без очереди. Заменяется модулем Approvals.</summary>
[Dependency(TryRegister = true)]
public class RoleLimitApprovalGateway : IApprovalGateway, ITransientDependency
{
    private readonly IRoleLimitsProvider _limits;

    public ILogger<RoleLimitApprovalGateway> Logger { get; set; } = NullLogger<RoleLimitApprovalGateway>.Instance;

    public RoleLimitApprovalGateway(IRoleLimitsProvider limits)
    {
        _limits = limits;
    }

    public async Task<bool> RequestIfOverWriteoffLimitAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId)
    {
        await _limits.EnsureWriteoffAllowedAsync(amount);
        return true;
    }

    public Task RequestAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId)
    {
        Logger.LogWarning("Approval requested ({Type}) for {Entity} {Id}: {Title} — Approvals module is not connected, document stays pending", approvalType, entityType, entityId, title);
        return Task.CompletedTask;
    }

    public Task CancelPendingAsync(string entityType, Guid entityId, string reason) => Task.CompletedTask;
}

/// <summary>Краткие сведения о товаре для других модулей (техкарты, визиты, закупки).</summary>
public sealed record ItemLookupInfo(Guid Id, string Sku, string Name, BaseUnit BaseUnit, bool IsActive, Guid? CategoryId);

/// <summary>Справочник номенклатуры для других модулей.</summary>
public interface IItemLookup
{
    Task<IReadOnlyDictionary<Guid, ItemLookupInfo>> GetAsync(IEnumerable<Guid> ids);

    Task<List<ItemLookupInfo>> SearchAsync(string? filter, int maxCount = 20, bool onlyActive = true);
}

public class ItemLookup : IItemLookup, ITransientDependency
{
    private readonly IRepository<Item, Guid> _items;
    private readonly IAsyncQueryableExecuter _async;

    public ItemLookup(IRepository<Item, Guid> items, IAsyncQueryableExecuter async)
    {
        _items = items;
        _async = async;
    }

    public async Task<IReadOnlyDictionary<Guid, ItemLookupInfo>> GetAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, ItemLookupInfo>();
        var q = (await _items.GetQueryableAsync()).Where(i => list.Contains(i.Id))
            .Select(i => new ItemLookupInfo(i.Id, i.Sku, i.Name, i.BaseUnit, i.IsActive, i.CategoryId));
        return (await _async.ToListAsync(q)).ToDictionary(i => i.Id);
    }

    public async Task<List<ItemLookupInfo>> SearchAsync(string? filter, int maxCount = 20, bool onlyActive = true)
    {
        var q = (await _items.GetQueryableAsync()).WhereIf(onlyActive, i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim().ToLower();
            q = q.Where(i => i.Name.ToLower().Contains(f) || i.Sku.ToLower().Contains(f) || i.Barcode == filter.Trim());
        }
        return await _async.ToListAsync(q.OrderBy(i => i.Name).Take(Math.Clamp(maxCount, 1, 200))
            .Select(i => new ItemLookupInfo(i.Id, i.Sku, i.Name, i.BaseUnit, i.IsActive, i.CategoryId)));
    }
}

/// <summary>Материал, израсходованный в визите (UsageId — строка материалов визита).</summary>
public sealed record VisitMaterialUsage(Guid UsageId, Guid ItemId, decimal Qty);

/// <summary>Себестоимость строки материалов визита и первая списанная партия.</summary>
public sealed record VisitMaterialCost(long Cost, Guid? BatchId);

/// <summary>Списание материалов по закрытому визиту (реализация — StockManager).</summary>
public interface IVisitStockConsumer
{
    /// <summary>Проводит расход по визиту (FEFO) со склада филиала, возвращает документ и себестоимость по каждой строке материалов.</summary>
    Task<(Guid DocumentId, IReadOnlyDictionary<Guid, VisitMaterialCost> Costs)> ConsumeAsync(
        Guid visitId, Guid branchId, Guid patientId, IReadOnlyList<VisitMaterialUsage> usages);

    /// <summary>Возврат материалов визита на склад (сторно документа расхода).</summary>
    Task ReverseAsync(Guid documentId, string reason);
}

/// <summary>Строка документа на вводе (количество — в единице UnitId, цена UnitCost — тиыны за единицу ввода).</summary>
public sealed record StockLineData(
    Guid ItemId, decimal Qty, Guid? UnitId = null, Guid? BatchId = null, long? UnitCost = null, string? BatchNumber = null,
    string? SerialNumber = null, DateOnly? ExpiresAt = null, decimal? ActualQty = null);

/// <summary>Шапка документа на вводе.</summary>
public sealed record StockDocumentHeader(
    Guid? WarehouseFromId, Guid? WarehouseToId, Guid? SupplierId = null, Guid? PurchaseOrderId = null, string? InvoiceNumber = null,
    DateOnly? InvoiceDate = null, Guid? ReasonId = null, string? Comment = null);
