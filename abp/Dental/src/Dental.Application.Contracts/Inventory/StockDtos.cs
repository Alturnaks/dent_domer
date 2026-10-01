using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Inventory;

/// <summary>Строка документа на вводе: Qty — в единице UnitId (null — базовая), UnitCost — тиыны за единицу ввода.</summary>
public class StockLineInputDto
{
    public Guid ItemId { get; set; }
    [Range(0, 100_000_000)]
    public decimal Qty { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? BatchId { get; set; }
    [Range(0, long.MaxValue)]
    public long? UnitCost { get; set; }
    [StringLength(InventoryConsts.MaxBatchNumberLength)]
    public string? BatchNumber { get; set; }
    [StringLength(InventoryConsts.MaxSerialNumberLength)]
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public decimal? ActualQty { get; set; }
}

public class StockDocumentHeaderDto
{
    public Guid? WarehouseFromId { get; set; }
    public Guid? WarehouseToId { get; set; }
    public Guid? SupplierId { get; set; }
    [StringLength(InventoryConsts.MaxInvoiceNumberLength)]
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public Guid? ReasonId { get; set; }
    [StringLength(InventoryConsts.MaxCommentLength)]
    public string? Comment { get; set; }
    public List<StockLineInputDto>? Lines { get; set; }
}

public class CreateStockDocumentDto : StockDocumentHeaderDto
{
    public StockDocumentType Type { get; set; }
    public Guid? PurchaseOrderId { get; set; }
}

public class UpdateStockDocumentDto : StockDocumentHeaderDto
{
    public string? ConcurrencyStamp { get; set; }
}

public class CountLineInput
{
    public Guid LineId { get; set; }
    [Range(0, 100_000_000)]
    public decimal ActualQty { get; set; }
}

/// <summary>Проведение/отмена: комментарий (обязателен для сторно), факт инвентаризации.</summary>
public class StockActionDto
{
    [StringLength(InventoryConsts.MaxCommentLength)]
    public string? Comment { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public List<CountLineInput>? Counts { get; set; }
}

public class ReceiveTransferDto
{
    public List<CountLineInput>? Lines { get; set; }
    [StringLength(InventoryConsts.MaxCommentLength)]
    public string? Comment { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

public class StockDocumentLineDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemSku { get; set; } = "";
    public BaseUnit BaseUnit { get; set; }
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public decimal Qty { get; set; }
    public decimal QtyInput { get; set; }
    public Guid? UnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal UnitCost { get; set; }
    public long TotalCost { get; set; }
    public decimal? ExpectedQty { get; set; }
    public decimal? ActualQty { get; set; }
}

public class StockDocumentDto : EntityDto<Guid>
{
    public StockDocumentType Type { get; set; }
    public string Number { get; set; } = "";
    public StockDocumentStatus Status { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? WarehouseFromId { get; set; }
    public string? WarehouseFromName { get; set; }
    public Guid? WarehouseToId { get; set; }
    public string? WarehouseToName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? VisitId { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public Guid? ReasonId { get; set; }
    public string? ReasonName { get; set; }
    public string? Comment { get; set; }
    public DateTime CreationTime { get; set; }
    public string? CreatorName { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedByName { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? SnapshotAt { get; set; }
    public long TotalCost { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public List<StockDocumentLineDto> Lines { get; set; } = [];
}

public class StockDocumentListItemDto : EntityDto<Guid>
{
    public StockDocumentType Type { get; set; }
    public string Number { get; set; } = "";
    public StockDocumentStatus Status { get; set; }
    public string? WarehouseFromName { get; set; }
    public string? WarehouseToName { get; set; }
    public string? SupplierName { get; set; }
    public string? ReasonName { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? PostedAt { get; set; }
    public long TotalCost { get; set; }
    public int LinesCount { get; set; }
    public string? Comment { get; set; }
}

public class GetStockDocumentListInput : PagedResultRequestDto
{
    public StockDocumentType? Type { get; set; }
    public StockDocumentStatus? Status { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Filter { get; set; }
}

public class GetBalancesInput
{
    public Guid? WarehouseId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool BelowMin { get; set; }
    /// <summary>Только истекающие в течение N дней (и просроченные).</summary>
    public int? ExpiringDays { get; set; }
    public string? Filter { get; set; }
}

public class StockBalanceRowDto
{
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemSku { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public BaseUnit BaseUnit { get; set; }
    public decimal Qty { get; set; }
    public decimal AvgCost { get; set; }
    /// <summary>Сумма остатка, тиыны.</summary>
    public long Amount { get; set; }
    public decimal? MinQty { get; set; }
    public decimal? OptimalQty { get; set; }
    public DateOnly? NearestExpiry { get; set; }
    public bool BelowMin { get; set; }
    public bool Expiring { get; set; }
    public bool Expired { get; set; }
}

public class GetMovementsInput : PagedResultRequestDto
{
    public Guid? ItemId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class StockMovementRowDto
{
    public Guid Id { get; set; }
    public DateTime MovedAt { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public StockDocumentType DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = "";
}

public class ItemCardBatchDto
{
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
}

public class ItemCardDto
{
    public ItemDto Item { get; set; } = new();
    public decimal TotalQty { get; set; }
    public long TotalAmount { get; set; }
    public List<ItemCardBatchDto> Balances { get; set; } = [];
    public List<StockLevelDto> Levels { get; set; } = [];
    public List<StockMovementRowDto> Movements { get; set; } = [];
}

/// <summary>Партия, доступная для списания/перемещения на складе.</summary>
public class AvailableBatchDto
{
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>Складские документы, остатки, движения, карточка товара.</summary>
public interface IStockAppService : IApplicationService
{
    Task<PagedResultDto<StockDocumentListItemDto>> GetListAsync(GetStockDocumentListInput input);
    Task<StockDocumentDto> GetAsync(Guid id);
    Task<StockDocumentDto> CreateAsync(CreateStockDocumentDto input);
    Task<StockDocumentDto> UpdateAsync(Guid id, UpdateStockDocumentDto input);
    Task<StockDocumentDto> PostAsync(Guid id, StockActionDto input);
    Task<StockDocumentDto> ReceiveAsync(Guid id, ReceiveTransferDto input);
    Task<StockDocumentDto> CancelAsync(Guid id, StockActionDto input);

    Task<ListResultDto<StockBalanceRowDto>> GetBalancesAsync(GetBalancesInput input);
    Task<PagedResultDto<StockMovementRowDto>> GetMovementsAsync(GetMovementsInput input);
    Task<ItemCardDto> GetItemCardAsync(Guid itemId);
    Task<ListResultDto<AvailableBatchDto>> GetAvailableBatchesAsync(Guid warehouseId, Guid itemId);

    /// <summary>Пересборка кэша остатков из журнала движений. Возвращает число изменённых строк.</summary>
    Task<int> RebuildBalancesAsync();
}
