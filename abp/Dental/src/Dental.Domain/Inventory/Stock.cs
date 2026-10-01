using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.MultiTenancy;

namespace Dental.Inventory;

/// <summary>Бизнес-ошибка склада с HTTP-статусом (по умолчанию 409, как в старом стеке).</summary>
public class StockException : BusinessException, IHasHttpStatusCode
{
    public int HttpStatusCode { get; }

    public StockException(string code, int httpStatusCode = 409) : base(code)
    {
        HttpStatusCode = httpStatusCode;
    }

    public new StockException WithData(string name, object value)
    {
        base.WithData(name, value);
        return this;
    }

    public static StockException BadRequest(string code) => new(code, 400);
}

/// <summary>Складской документ: приход, перемещение, списание, инвентаризация, возврат поставщику, расход по визиту.</summary>
[Audited]
public class StockDocument : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public StockDocumentType Type { get; private set; }
    public string Number { get; private set; } = null!;
    public StockDocumentStatus Status { get; set; } = StockDocumentStatus.Draft;
    public Guid? BranchId { get; set; }
    public Guid? WarehouseFromId { get; set; }
    public Guid? WarehouseToId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? VisitId { get; set; }
    /// <summary>Для списания недостачи при приёмке перемещения — исходное перемещение.</summary>
    public Guid? SourceDocumentId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public Guid? ReasonId { get; set; }
    public string? Comment { get; set; }
    public DateTime? PostedAt { get; private set; }
    public Guid? PostedBy { get; private set; }
    public DateTime? ReceivedAt { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    /// <summary>Для инвентаризации — момент фиксации ожидаемых остатков.</summary>
    public DateTime? SnapshotAt { get; set; }
    /// <summary>Сумма документа, тиыны.</summary>
    public long TotalCost { get; private set; }
    public List<StockDocumentLine> Lines { get; private set; } = [];

    protected StockDocument() { }

    public StockDocument(Guid id, Guid? tenantId, StockDocumentType type, string number) : base(id)
    {
        TenantId = tenantId;
        Type = type;
        Number = number;
    }

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

    /// <summary>Редактировать можно только черновик.</summary>
    public void EnsureDraft()
    {
        if (Status != StockDocumentStatus.Draft)
        {
            throw new StockException(DentalDomainErrorCodes.StockDocumentNotEditable);
        }
    }

    public void RecalculateTotal() => TotalCost = Lines.Sum(l => l.TotalCost);

    public void MarkPosted(DateTime now, Guid? userId)
    {
        Status = StockDocumentStatus.Posted;
        PostedAt = now;
        PostedBy = userId;
    }

    public void MarkInTransit(DateTime now, Guid? userId)
    {
        Status = StockDocumentStatus.InTransit;
        PostedAt = now;
        PostedBy = userId;
    }

    public void MarkReceived(DateTime now, Guid? userId)
    {
        Status = StockDocumentStatus.Received;
        ReceivedAt = now;
        ReceivedBy = userId;
    }

    public void AppendComment(string? text, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var line = (prefix ?? "") + text.Trim();
        Comment = string.IsNullOrEmpty(Comment) ? line : Comment + "\n" + line;
        if (Comment.Length > InventoryConsts.MaxCommentLength) Comment = Comment[..InventoryConsts.MaxCommentLength];
    }

    /// <summary>Локальное событие (для закупок/отчётов): документ проведён, получен или отменён.</summary>
    public void RaiseChanged(string action) =>
        AddLocalEvent(new StockDocumentChangedEto
        {
            DocumentId = Id, Type = Type, Status = Status, Action = action, Number = Number, SupplierId = SupplierId,
            PurchaseOrderId = PurchaseOrderId, InvoiceNumber = InvoiceNumber, InvoiceDate = InvoiceDate, TotalCost = TotalCost, BranchId = BranchId,
            Lines = Lines.Select(l => new StockDocumentChangedEto.LineInfo(l.ItemId, l.Qty, l.TotalCost)).ToList(),
        });
}

/// <summary>Строка документа. Qty — в базовых единицах, QtyInput — в единице ввода.</summary>
public class StockDocumentLine : Entity<Guid>
{
    public Guid DocumentId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Qty { get; set; }
    public decimal QtyInput { get; set; }
    public Guid? UnitId { get; set; }
    /// <summary>Себестоимость базовой единицы, тиыны.</summary>
    public decimal UnitCost { get; set; }
    public long TotalCost { get; set; }
    public decimal? ExpectedQty { get; set; }
    public decimal? ActualQty { get; set; }
    /// <summary>Для прихода: данные новой партии.</summary>
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }

    protected StockDocumentLine() { }

    public StockDocumentLine(Guid id, Guid documentId, Guid itemId) : base(id)
    {
        DocumentId = documentId;
        ItemId = itemId;
    }

    public void RecalculateTotal() => TotalCost = MoneyMath.Round(Qty * UnitCost);
}

/// <summary>Неизменяемый журнал движений. Остаток = сумма движений.</summary>
public class StockMovement : BasicAggregateRoot<Guid>, IMultiTenant, IHasCreationTime
{
    public Guid? TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid? LineId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? BatchId { get; private set; }
    public decimal Qty { get; private set; }
    public decimal UnitCost { get; private set; }
    public DateTime MovedAt { get; private set; }
    public Guid? PatientId { get; private set; }
    public StockDocumentType DocumentType { get; private set; }
    public DateTime CreationTime { get; set; }

    protected StockMovement() { }

    public StockMovement(Guid id, Guid? tenantId, Guid documentId, Guid? lineId, Guid warehouseId, Guid itemId, Guid? batchId, decimal qty,
        decimal unitCost, DateTime movedAt, Guid? patientId, StockDocumentType documentType) : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        LineId = lineId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        BatchId = batchId;
        Qty = qty;
        UnitCost = unitCost;
        MovedAt = movedAt;
        PatientId = patientId;
        DocumentType = documentType;
    }
}

/// <summary>Кэш остатков: UNIQUE(TenantId, WarehouseId, ItemId, BatchId) NULLS NOT DISTINCT. Меняется только вместе с журналом.</summary>
public class StockBalance : BasicAggregateRoot<Guid>, IMultiTenant, IHasCreationTime
{
    public Guid? TenantId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? BatchId { get; private set; }
    public decimal Qty { get; private set; }
    /// <summary>Средневзвешенная себестоимость (для товаров без партий).</summary>
    public decimal AvgCost { get; private set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastMovedAt { get; private set; }

    protected StockBalance() { }

    public StockBalance(Guid id, Guid? tenantId, Guid warehouseId, Guid itemId, Guid? batchId) : base(id)
    {
        TenantId = tenantId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        BatchId = batchId;
    }

    /// <summary>Применение движения: приход пересчитывает средневзвешенную себестоимость.</summary>
    public void Apply(decimal qty, decimal unitCost, DateTime now)
    {
        if (qty > 0)
        {
            AvgCost = Fefo.WeightedAverage(Qty, AvgCost, qty, unitCost);
        }
        Qty += qty;
        LastMovedAt = now;
    }

    public void Reset()
    {
        Qty = 0;
        AvgCost = 0;
    }
}

/// <summary>Счётчик нумерации документов (инкремент в той же транзакции, что и документ).</summary>
public class DocumentCounter : BasicAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public long Value { get; private set; }

    protected DocumentCounter() { }

    public DocumentCounter(Guid id, Guid? tenantId, string key) : base(id)
    {
        TenantId = tenantId;
        Key = key;
    }

    public long Next() => ++Value;
}

public static class MoneyMath
{
    /// <summary>Округление суммы до тиына (от нуля).</summary>
    public static long Round(decimal amount) => (long)Math.Round(amount, MidpointRounding.AwayFromZero);
}

/// <summary>Доступная партия для подбора FEFO.</summary>
public sealed record BatchStock(Guid? BatchId, DateOnly? ExpiresAt, decimal Qty, decimal UnitCost, DateTime? ReceivedAt = null);

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
        var all = stock.ToList();
        var ordered = all.Where(s => s.Qty > 0)
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
            {
                throw new StockException(DentalDomainErrorCodes.StockInsufficient)
                    .WithData("missing", left.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).WithData("name", "");
            }
            var last = ordered.LastOrDefault() ?? all.LastOrDefault();
            var batchId = last?.BatchId;
            var cost = last?.UnitCost ?? fallbackCost;
            var existing = result.FindIndex(r => r.BatchId == batchId);
            if (existing >= 0) result[existing] = result[existing] with { Qty = result[existing].Qty + left };
            else result.Add(new BatchAllocation(batchId, left, cost));
        }
        return result;
    }

    /// <summary>Новая средневзвешенная себестоимость после прихода (отрицательный остаток считается нулём).</summary>
    public static decimal WeightedAverage(decimal currentQty, decimal currentCost, decimal inQty, decimal inCost)
    {
        var baseQty = Math.Max(currentQty, 0);
        var total = baseQty + inQty;
        if (total <= 0) return inCost;
        return decimal.Round((baseQty * currentCost + inQty * inCost) / total, 4);
    }
}
