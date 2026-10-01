using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Finance;
using Dental.Inventory;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Linq;
namespace Dental.Purchasing;
public class PurchaseOrderApprovalHandler(IRepository<PurchaseOrder,Guid> orders, IFinanceLock financeLock) : IApprovalHandler, ITransientDependency
{
    public string Type => ApprovalTypes.PurchaseOrder;
    public async Task OnApprovedAsync(ApprovalRequest request)
    { await financeLock.AcquireAsync(); var o=await orders.GetAsync(request.EntityId); if (o.Status != PurchaseOrderStatus.PendingApproval) throw new UserFriendlyException("Заказ больше не ожидает подтверждения."); o.Status=PurchaseOrderStatus.Sent; o.SentAt=DateTime.UtcNow; await orders.UpdateAsync(o,true); }
    public async Task OnRejectedAsync(ApprovalRequest request)
    { await financeLock.AcquireAsync(); var o=await orders.GetAsync(request.EntityId); if (o.Status == PurchaseOrderStatus.PendingApproval) { o.Status=PurchaseOrderStatus.Draft; await orders.UpdateAsync(o,true); } }
}
public class PurchasingStockHandler(IRepository<PurchaseOrder,Guid> orders, IRepository<StockDocument,Guid> documents, IRepository<SupplierInvoice,Guid> invoices, IRepository<Supplier,Guid> suppliers, IFinanceLock financeLock, IAsyncQueryableExecuter executer) : ILocalEventHandler<StockDocumentChangedEto>, ITransientDependency
{
    public async Task HandleEventAsync(StockDocumentChangedEto e)
    {
        if (e.Type is not (StockDocumentType.Receipt or StockDocumentType.ReturnToSupplier)) return;
        await financeLock.AcquireAsync();
        var document=await documents.GetAsync(e.DocumentId);
        if (e.PurchaseOrderId != null)
        {
            var order=await executer.FirstAsync((await orders.WithDetailsAsync(o => o.Lines)).Where(o => o.Id == e.PurchaseOrderId));
            if (order.SupplierId != e.SupplierId || (e.Type == StockDocumentType.Receipt && document.WarehouseToId != order.WarehouseId)) throw new UserFriendlyException("Склад или поставщик не соответствует заказу.");
            if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Cancelled) throw new UserFriendlyException("Нельзя проводить приёмку по неподтверждённому или отменённому заказу.");
            var docs=await executer.ToListAsync((await documents.WithDetailsAsync(d => d.Lines)).Where(d => d.PurchaseOrderId == order.Id && d.Status == StockDocumentStatus.Posted && (d.Type == StockDocumentType.Receipt || d.Type == StockDocumentType.ReturnToSupplier)));
            foreach(var line in order.Lines)
            {
                line.ReceivedQty=Math.Max(0,docs.Sum(d => d.Lines.Where(l => l.ItemId == line.ItemId).Sum(l => l.Qty) * (d.Type == StockDocumentType.ReturnToSupplier ? -1 : 1)));
                if (line.ReceivedQty > line.Qty) throw new UserFriendlyException("Приёмка превышает заказанное количество.");
            }
            if (docs.SelectMany(d => d.Lines).Any(l => !order.Lines.Any(x => x.ItemId == l.ItemId))) throw new UserFriendlyException("В накладной есть товар, которого нет в заказе.");
            order.RecalculateStatus(); await orders.UpdateAsync(order,true);
        }
        if (e.SupplierId == null) return;
        if (e.Type == StockDocumentType.Receipt)
        {
            var invoice=await invoices.FirstOrDefaultAsync(i => i.StockDocumentId == e.DocumentId);
            if (invoice == null && e.Action == "posted")
            {
                var supplier=await suppliers.GetAsync(e.SupplierId.Value); var date=e.InvoiceDate ?? DateOnly.FromDateTime(document.PostedAt ?? DateTime.UtcNow);
                invoice=new SupplierInvoice(Guid.NewGuid(),document.TenantId,supplier.Id,document.Id,e.PurchaseOrderId,document.BranchId,e.InvoiceNumber ?? e.Number,date,e.TotalCost,date.AddDays(supplier.PaymentTermsDays));
                await invoices.InsertAsync(invoice,true);
            }
            if (invoice != null && e.Action == "storno") { invoice.ReturnedAmount=invoice.Amount; invoice.Recalculate(); await invoices.UpdateAsync(invoice,true); }
        }
        else if (e.Action is "posted" or "storno")
        {
            // Returns reference the original receipt via SourceDocumentId; allocation is deterministic.
            if (document.SourceDocumentId == null) return;
            var invoice=await invoices.FirstOrDefaultAsync(i => i.StockDocumentId == document.SourceDocumentId);
            if (invoice == null || invoice.SupplierId != e.SupplierId) throw new UserFriendlyException("Не найден исходный счёт для возврата.");
            var returns=await executer.ToListAsync((await documents.WithDetailsAsync(d=>d.Lines)).Where(d => d.SourceDocumentId == invoice.StockDocumentId && d.Type == StockDocumentType.ReturnToSupplier && d.Status == StockDocumentStatus.Posted));
            var receipt=await executer.FirstAsync((await documents.WithDetailsAsync(d=>d.Lines)).Where(d=>d.Id==invoice.StockDocumentId));
            long credit=0;foreach(var group in returns.SelectMany(d=>d.Lines).GroupBy(l=>l.ItemId))
            {
                var original=receipt.Lines.Where(l=>l.ItemId==group.Key).ToList();var received=original.Sum(l=>l.Qty);var returned=group.Sum(l=>l.Qty);
                if(received<=0||returned>received)throw new UserFriendlyException("Возврат превышает исходную накладную.");
                credit=checked(credit+(long)Math.Round(returned*original.Sum(l=>l.TotalCost)/received,MidpointRounding.AwayFromZero));
            }
            invoice.ReturnedAmount=Math.Min(invoice.Amount,credit); invoice.Recalculate(); await invoices.UpdateAsync(invoice,true);
        }
    }
}
