using Dental.Domain.Common;

namespace Dental.Domain.Purchasing;

[Audited]
public class Supplier : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public string? Bin { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class SupplierItem : TenantEntity
{
    public Guid SupplierId { get; set; }
    public Guid ItemId { get; set; }
    public string? SupplierSku { get; set; }
    public long LastPrice { get; set; }
    public DateTimeOffset? LastPriceAt { get; set; }
}

public enum PurchaseRequestStatus { Draft, Submitted, Processed, Rejected }
public enum PurchaseRequestSource { Auto, Manual }

[Audited]
public class PurchaseRequest : TenantEntity
{
    public Guid? BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public PurchaseRequestSource Source { get; set; }
    public string? Comment { get; set; }
    public List<PurchaseRequestLine> Lines { get; set; } = [];
}

public class PurchaseRequestLine : TenantEntity
{
    public Guid RequestId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Qty { get; set; }
    public decimal CurrentQty { get; set; }
    public decimal MinQty { get; set; }
    public decimal OptimalQty { get; set; }
}

public enum PurchaseOrderStatus { Draft, PendingApproval, Sent, PartiallyReceived, Received, Cancelled }

[Audited]
public class PurchaseOrder : TenantEntity, IVersioned
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateOnly? ExpectedAt { get; set; }
    public long Total { get; set; }
    public string? SentVia { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? Comment { get; set; }
    public int Version { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];

    public void RecalculateStatus()
    {
        if (Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval) return;
        if (Lines.Count > 0 && Lines.All(l => l.ReceivedQty >= l.Qty)) Status = PurchaseOrderStatus.Received;
        else if (Lines.Any(l => l.ReceivedQty > 0)) Status = PurchaseOrderStatus.PartiallyReceived;
    }
}

public class PurchaseOrderLine : TenantEntity
{
    public Guid OrderId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Qty { get; set; }
    public long UnitPrice { get; set; }
    public decimal ReceivedQty { get; set; }
}

public enum SupplierInvoiceStatus { Unpaid, PartiallyPaid, Paid }

[Audited]
public class SupplierInvoice : TenantEntity
{
    public Guid SupplierId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? StockDocumentId { get; set; }
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }
    public long Amount { get; set; }
    public DateOnly? DueDate { get; set; }
    public long PaidAmount { get; set; }
    public long ReturnedAmount { get; set; }
    public SupplierInvoiceStatus Status { get; set; }

    public void RecalculateStatus() =>
        Status = PaidAmount + ReturnedAmount >= Amount ? SupplierInvoiceStatus.Paid
            : PaidAmount > 0 ? SupplierInvoiceStatus.PartiallyPaid : SupplierInvoiceStatus.Unpaid;
}

public static class Replenishment
{
    /// <summary>Предлагаемое количество = optimal − остаток − в пути − уже заказано (не меньше 0).</summary>
    public static decimal SuggestedQty(decimal qty, decimal minQty, decimal optimalQty, decimal inTransit, decimal ordered)
    {
        if (qty >= minQty) return 0;
        return Math.Max(0, optimalQty - qty - inTransit - ordered);
    }
}
