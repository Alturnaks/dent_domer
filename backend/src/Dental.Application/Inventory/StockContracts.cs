using Dental.Domain.Inventory;
using FluentValidation;

namespace Dental.Application.Inventory;

public sealed record StockLineInput(
    Guid ItemId, decimal Qty, Guid? UnitId, Guid? BatchId, long? UnitCost, string? BatchNumber, string? SerialNumber, DateOnly? ExpiresAt, decimal? ActualQty);

public sealed record CreateStockDocumentRequest(
    StockDocumentType Type, Guid? WarehouseFromId, Guid? WarehouseToId, Guid? SupplierId, Guid? PurchaseOrderId, string? InvoiceNumber,
    DateOnly? InvoiceDate, Guid? ReasonId, string? Comment, IReadOnlyList<StockLineInput>? Lines);

public sealed record UpdateStockDocumentRequest(
    Guid? WarehouseFromId, Guid? WarehouseToId, Guid? SupplierId, string? InvoiceNumber, DateOnly? InvoiceDate, Guid? ReasonId, string? Comment,
    IReadOnlyList<StockLineInput>? Lines, int? Version);

public sealed record ReceiveTransferLine(Guid LineId, decimal ActualQty);
public sealed record ReceiveTransferRequest(IReadOnlyList<ReceiveTransferLine>? Lines, string? Comment, int? Version);
public sealed record CountLineInput(Guid LineId, decimal ActualQty);
public sealed record StockActionRequest(string? Comment, int? Version, IReadOnlyList<CountLineInput>? Counts);

public sealed record StockDocumentLineDto(
    Guid Id, Guid ItemId, string ItemName, string ItemSku, string BaseUnit, Guid? BatchId, string? BatchNumber, string? SerialNumber, DateOnly? ExpiresAt,
    decimal Qty, decimal QtyInput, Guid? UnitId, string? UnitName, decimal UnitCost, long TotalCost, decimal? ExpectedQty, decimal? ActualQty);

public sealed record StockDocumentDto(
    Guid Id, StockDocumentType Type, string Number, StockDocumentStatus Status, Guid? BranchId, Guid? WarehouseFromId, string? WarehouseFromName,
    Guid? WarehouseToId, string? WarehouseToName, Guid? SupplierId, string? SupplierName, Guid? PurchaseOrderId, Guid? VisitId, Guid? SourceDocumentId,
    string? InvoiceNumber, DateOnly? InvoiceDate, Guid? ReasonId, string? ReasonName, string? Comment, DateTimeOffset CreatedAt, Guid? CreatedBy,
    string? CreatedByName, DateTimeOffset? PostedAt, string? PostedByName, DateTimeOffset? ReceivedAt, long TotalCost, int Version,
    IReadOnlyList<StockDocumentLineDto> Lines);

public sealed record StockDocumentListItem(
    Guid Id, StockDocumentType Type, string Number, StockDocumentStatus Status, string? WarehouseFromName, string? WarehouseToName, string? SupplierName,
    string? ReasonName, DateTimeOffset CreatedAt, DateTimeOffset? PostedAt, long TotalCost, int LinesCount, string? Comment);

public sealed record StockBalanceRow(
    Guid WarehouseId, string WarehouseName, Guid ItemId, string ItemName, string ItemSku, Guid? CategoryId, string BaseUnit, decimal Qty, decimal AvgCost,
    long Amount, decimal? MinQty, decimal? OptimalQty, DateOnly? NearestExpiry, bool BelowMin, bool Expiring, bool Expired);

public sealed record StockMovementRow(
    Guid Id, DateTimeOffset MovedAt, Guid WarehouseId, string WarehouseName, Guid ItemId, string ItemName, Guid? BatchId, string? BatchNumber, decimal Qty,
    decimal UnitCost, StockDocumentType DocumentType, Guid DocumentId, string DocumentNumber);

public sealed record ItemCardBatch(Guid? BatchId, string? BatchNumber, string? SerialNumber, DateOnly? ExpiresAt, Guid WarehouseId, string WarehouseName, decimal Qty, decimal UnitCost);
public sealed record ItemCardDto(ItemDto Item, decimal TotalQty, long TotalAmount, IReadOnlyList<ItemCardBatch> Balances, IReadOnlyList<StockMovementRow> Movements);

public sealed class CreateStockDocumentRequestValidator : AbstractValidator<CreateStockDocumentRequest>
{
    public CreateStockDocumentRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t != StockDocumentType.VisitConsumption).WithMessage("Расход по визиту создаётся автоматически");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).NotEmpty();
            l.RuleFor(x => x.Qty).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost is not null);
        });
    }
}

/// <summary>Списание материалов по закрытому визиту (реализация — StockService).</summary>
public interface IVisitStockConsumer
{
    /// <summary>Проводит расход по визиту (FEFO), возвращает документ и себестоимость по каждой строке материалов.</summary>
    Task<(Guid DocumentId, IReadOnlyDictionary<Guid, (long Cost, Guid? BatchId)> Costs)> ConsumeAsync(
        Guid visitId, Guid branchId, Guid patientId, IReadOnlyList<(Guid UsageId, Guid ItemId, decimal Qty)> usages, CancellationToken ct);

    /// <summary>Возврат материалов визита на склад (сторно документа расхода).</summary>
    Task ReverseAsync(Guid documentId, string reason, CancellationToken ct);
}
