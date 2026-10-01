using System;
using System.Collections.Generic;

namespace Dental.Inventory;

/// <summary>
/// Локальное событие складского документа (ILocalEventBus). Action: "posted", "sent", "received", "cancelled", "storno".
/// Подписчики: закупки (счёт поставщика по накладной, received_qty заказа, сторно счёта), отчёты.
/// </summary>
[Serializable]
public class StockDocumentChangedEto
{
    public Guid DocumentId { get; set; }
    public StockDocumentType Type { get; set; }
    public StockDocumentStatus Status { get; set; }
    public string Action { get; set; } = "";
    public string Number { get; set; } = "";
    public Guid? SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public long TotalCost { get; set; }
    public Guid? BranchId { get; set; }
    public List<LineInfo> Lines { get; set; } = [];

    public record LineInfo(Guid ItemId, decimal Qty, long TotalCost);
}
