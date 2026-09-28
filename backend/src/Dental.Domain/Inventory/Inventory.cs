using Dental.Domain.Common;

namespace Dental.Domain.Inventory;

public enum WarehouseType { Central, Branch, Cabinet }

[Audited]
public class Warehouse : TenantEntity, ISoftDeletable
{
    /// <summary>NULL = центральный склад сети.</summary>
    public Guid? BranchId { get; set; }
    public WarehouseType Type { get; set; }
    public Guid? ParentWarehouseId { get; set; }
    public string Name { get; set; } = "";
    public Guid? ResponsibleId { get; set; }
    /// <summary>Склад заблокирован для списаний и перемещений на время инвентаризации.</summary>
    public bool Locked { get; set; }
    public Guid? LockedByDocumentId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class ItemCategory : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum BaseUnit { Pcs, G, Ml, Pack }

[Audited]
public class Item : TenantEntity, ISoftDeletable
{
    public Guid? CategoryId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }
    public BaseUnit BaseUnit { get; set; } = BaseUnit.Pcs;
    public bool TrackBatches { get; set; }
    public bool TrackSerials { get; set; }
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Barcode { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<ItemUnit> Units { get; set; } = [];

    /// <summary>Перевод количества из единицы ввода в базовую.</summary>
    public decimal ToBase(decimal qty, Guid? unitId)
    {
        if (unitId is null) return qty;
        var unit = Units.FirstOrDefault(u => u.Id == unitId)
                   ?? throw new DomainException("UNIT_NOT_FOUND", "Единица измерения не найдена у товара", status: 400);
        return qty * unit.FactorToBase;
    }
}

public class ItemUnit : TenantEntity
{
    public Guid ItemId { get; set; }
    public string UnitName { get; set; } = "";
    public decimal FactorToBase { get; set; } = 1;
}

[Audited]
public class ItemStockLevel : TenantEntity
{
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal MinQty { get; set; }
    public decimal OptimalQty { get; set; }
}

public class Batch : TenantEntity
{
    public Guid ItemId { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    /// <summary>Себестоимость единицы (тиыны за базовую единицу, может быть дробной).</summary>
    public decimal UnitCost { get; set; }
    public Guid? SupplierId { get; set; }
}

public enum StockDocumentType { Receipt, Transfer, Writeoff, Inventory, ReturnToSupplier, VisitConsumption }
public enum StockDocumentStatus { Draft, PendingApproval, Posted, InTransit, Received, Cancelled }

[Audited]
public class StockDocument : TenantEntity, IVersioned
{
    public StockDocumentType Type { get; set; }
    public string Number { get; set; } = "";
    public StockDocumentStatus Status { get; set; } = StockDocumentStatus.Draft;
    public Guid? BranchId { get; set; }
    public Guid? WarehouseFromId { get; set; }
    public Guid? WarehouseToId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? VisitId { get; set; }
    /// <summary>Для документа списания недостачи при приёмке перемещения — исходное перемещение.</summary>
    public Guid? SourceDocumentId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public Guid? ReasonId { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedBy { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedBy { get; set; }
    /// <summary>Для инвентаризации — момент фиксации ожидаемых остатков.</summary>
    public DateTimeOffset? SnapshotAt { get; set; }
    public long TotalCost { get; set; }
    public int Version { get; set; }
    public List<StockDocumentLine> Lines { get; set; } = [];

    public static string Prefix(StockDocumentType type) => type switch
    {
        StockDocumentType.Receipt => "ПРХ",
        StockDocumentType.Transfer => "ПРМ",
        StockDocumentType.Writeoff => "СПС",
        StockDocumentType.Inventory => "ИНВ",
        StockDocumentType.ReturnToSupplier => "ВЗВ",
        StockDocumentType.VisitConsumption => "РСХ",
        _ => "ДОК",
    };

    public void EnsureEditable()
    {
        if (Status is not (StockDocumentStatus.Draft or StockDocumentStatus.PendingApproval))
            throw new DomainException("DOCUMENT_NOT_EDITABLE", "Проведённый документ нельзя изменить", status: 409);
    }
}

public class StockDocumentLine : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? BatchId { get; set; }
    /// <summary>Количество в базовых единицах.</summary>
    public decimal Qty { get; set; }
    public decimal QtyInput { get; set; }
    public Guid? UnitId { get; set; }
    public decimal UnitCost { get; set; }
    public long TotalCost { get; set; }
    public decimal? ExpectedQty { get; set; }
    public decimal? ActualQty { get; set; }
    /// <summary>Для прихода: данные новой партии.</summary>
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
}

/// <summary>Неизменяемый журнал движений. Остаток = сумма движений.</summary>
public class StockMovement : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Guid? LineId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset MovedAt { get; set; }
    public Guid? PatientId { get; set; }
    public StockDocumentType DocumentType { get; set; }
}

/// <summary>Кэш остатков: UNIQUE(warehouse_id, item_id, batch_id) NULLS NOT DISTINCT.</summary>
public class StockBalance : TenantEntity
{
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Qty { get; set; }
    /// <summary>Средневзвешенная себестоимость (для товаров без партий).</summary>
    public decimal AvgCost { get; set; }
}

public enum WriteoffReasonType { Expired, Damaged, Defect, Lost, Other }

[Audited]
public class WriteoffReason : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public WriteoffReasonType Type { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Доступная партия для подбора FEFO.</summary>
public sealed record BatchStock(Guid? BatchId, DateOnly? ExpiresAt, decimal Qty, decimal UnitCost, DateTimeOffset? ReceivedAt = null);

/// <summary>Результат подбора: сколько списать из какой партии.</summary>
public sealed record BatchAllocation(Guid? BatchId, decimal Qty, decimal UnitCost);

public static class Fefo
{
    /// <summary>
    /// Подбор партий FEFO: сначала ближайший срок годности, партии без срока — в конце, затем по дате поступления.
    /// Если не хватает и allowNegative — остаток списывается «в минус» с последней известной партии (или без партии).
    /// </summary>
    public static IReadOnlyList<BatchAllocation> Allocate(IEnumerable<BatchStock> stock, decimal qty, bool allowNegative, decimal fallbackCost = 0)
    {
        if (qty <= 0) return [];
        var ordered = stock.Where(s => s.Qty > 0)
            .OrderBy(s => s.ExpiresAt is null ? 1 : 0)
            .ThenBy(s => s.ExpiresAt)
            .ThenBy(s => s.ReceivedAt)
            .ToList();

        var result = new List<BatchAllocation>();
        var left = qty;
        foreach (var s in ordered)
        {
            if (left <= 0) break;
            var take = Math.Min(left, s.Qty);
            result.Add(new BatchAllocation(s.BatchId, take, s.UnitCost));
            left -= take;
        }

        if (left > 0)
        {
            if (!allowNegative)
                throw new DomainException("STOCK_INSUFFICIENT", "Недостаточно остатка на складе",
                    new Dictionary<string, object?> { ["missing"] = left }, 409);
            var last = ordered.LastOrDefault() ?? stock.LastOrDefault();
            var batchId = last?.BatchId;
            var cost = last?.UnitCost ?? fallbackCost;
            var existing = result.FindIndex(r => r.BatchId == batchId);
            if (existing >= 0) result[existing] = result[existing] with { Qty = result[existing].Qty + left };
            else result.Add(new BatchAllocation(batchId, left, cost));
        }
        return result;
    }

    /// <summary>Новая средневзвешенная себестоимость после прихода.</summary>
    public static decimal WeightedAverage(decimal currentQty, decimal currentCost, decimal inQty, decimal inCost)
    {
        var baseQty = Math.Max(currentQty, 0);
        var total = baseQty + inQty;
        if (total <= 0) return inCost;
        return decimal.Round((baseQty * currentCost + inQty * inCost) / total, 4);
    }
}
