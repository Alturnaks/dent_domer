using System;
using System.Collections.Generic;
using System.Linq;
using Dental.Finance;
using Volo.Abp;
namespace Dental.Purchasing;
public class PurchaseRequest : FinanceEntity
{
    protected PurchaseRequest() { }
    public PurchaseRequest(Guid id, Guid? tenantId, Guid warehouseId, Guid? branchId, PurchaseRequestSource source, string? comment) : base(id,tenantId) { WarehouseId = warehouseId; BranchId = branchId; Source = source; Comment = comment; }
    public Guid? BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public PurchaseRequestStatus Status { get; set; }
    public PurchaseRequestSource Source { get; private set; }
    public string? Comment { get; private set; }
    public List<PurchaseRequestLine> Lines { get; private set; } = [];
}
public class PurchaseRequestLine : FinanceEntity
{
    protected PurchaseRequestLine() { }
    public PurchaseRequestLine(Guid id, Guid? tenant, Guid request, Guid item, decimal qty, decimal current = 0, decimal min = 0, decimal optimal = 0) : base(id,tenant) { if (qty <= 0) throw new UserFriendlyException("Количество должно быть положительным."); RequestId = request; ItemId = item; Qty = qty; CurrentQty = current; MinQty = min; OptimalQty = optimal; }
    public Guid RequestId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Qty { get; private set; }
    public decimal ProcessedQty { get; set; }
    public decimal CurrentQty { get; private set; }
    public decimal MinQty { get; private set; }
    public decimal OptimalQty { get; private set; }
}
public class PurchaseOrder : FinanceEntity
{
    protected PurchaseOrder() { }
    public PurchaseOrder(Guid id, Guid? tenant, string number, Guid supplier, Guid warehouse, Guid? branch, DateOnly? expected, string? comment) : base(id,tenant) { Number = number; SupplierId = supplier; WarehouseId = warehouse; BranchId = branch; ExpectedAt = expected; Comment = comment; }
    public string Number { get; private set; } = "";
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid? BranchId { get; private set; }
    public PurchaseOrderStatus Status { get; set; }
    public DateOnly? ExpectedAt { get; private set; }
    public long Total { get; private set; }
    public string? SentVia { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Comment { get; private set; }
    public List<PurchaseOrderLine> Lines { get; private set; } = [];
    public void Revise(Guid supplier,Guid warehouse,Guid? branch,DateOnly? expected,string? comment)
    { if(Status!=PurchaseOrderStatus.Draft)throw new UserFriendlyException("Редактировать можно только черновик заказа.");SupplierId=supplier;WarehouseId=warehouse;BranchId=branch;ExpectedAt=expected;Comment=comment; }
    public void Recalculate() => Total = Lines.Where(l => !l.IsDeleted).Sum(l => checked((long)Math.Round(l.Qty*l.UnitPrice,MidpointRounding.AwayFromZero)));
    public void RecalculateStatus() { if (Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Cancelled) return; var active=Lines.Where(l=>!l.IsDeleted).ToList();Status = active.All(l => l.ReceivedQty >= l.Qty) ? PurchaseOrderStatus.Received : active.Any(l => l.ReceivedQty > 0) ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.Sent; }
}
public class PurchaseOrderLine : FinanceEntity
{
    protected PurchaseOrderLine() { }
    public PurchaseOrderLine(Guid id, Guid? tenant, Guid order, Guid item, decimal qty, long price) : base(id,tenant) { if (qty <= 0 || price < 0) throw new UserFriendlyException("Некорректная строка заказа."); OrderId = order; ItemId = item; Qty = qty; UnitPrice = price; }
    public Guid OrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Qty { get; private set; }
    public long UnitPrice { get; private set; }
    public decimal ReceivedQty { get; set; }
}
public class SupplierInvoice : FinanceEntity
{
    protected SupplierInvoice() { }
    public SupplierInvoice(Guid id, Guid? tenant, Guid supplier, Guid document, Guid? order, Guid? branch, string number, DateOnly date, long amount, DateOnly due) : base(id,tenant) { SupplierId=supplier; StockDocumentId=document; PurchaseOrderId=order; BranchId=branch; Number=number; Date=date; Amount=amount; DueDate=due; }
    public Guid SupplierId { get; private set; }
    public Guid StockDocumentId { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Number { get; private set; } = "";
    public DateOnly Date { get; private set; }
    public DateOnly DueDate { get; private set; }
    public long Amount { get; private set; }
    public long PaidAmount { get; set; }
    public long ReturnedAmount { get; set; }
    public SupplierInvoiceStatus Status { get; private set; }
    public void Recalculate() => Status = PaidAmount+ReturnedAmount >= Amount ? SupplierInvoiceStatus.Paid : PaidAmount > 0 ? SupplierInvoiceStatus.PartiallyPaid : SupplierInvoiceStatus.Unpaid;
}
public class SupplierInvoicePayment : FinanceEntity
{
    protected SupplierInvoicePayment() { }
    public SupplierInvoicePayment(Guid id, Guid? tenant, Guid invoice, long amount, string reference, string key) : base(id,tenant) { InvoiceId=invoice; Amount=amount; Reference=reference; IdempotencyKey=key; }
    public Guid InvoiceId { get; private set; }
    public long Amount { get; private set; }
    public string Reference { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
}
public static class Replenishment
{
    public static decimal SuggestedQty(decimal qty, decimal min, decimal optimal, decimal transit, decimal ordered) => qty >= min ? 0 : Math.Max(0,optimal-qty-transit-ordered);
}
