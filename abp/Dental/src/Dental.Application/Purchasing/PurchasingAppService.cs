using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Finance;
using Dental.Inventory;
using Dental.Permissions;
using Dental.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Dental.Reports;
using Volo.Abp.Content;
using System.IO;
namespace Dental.Purchasing;
[Authorize]
public class PurchasingAppService(IFinanceLock financeLock, StockManager stock, ApprovalManager approvals, IDocumentNumberGenerator numbers, ISettingProvider settings) : DentalAppService, IPurchasingAppService
{
    private IRepository<T,Guid> R<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T,Guid>>();
    private async Task Read() { if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.RequestCreate) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.OrderCreate) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.OrderApprove)) throw new AbpAuthorizationException(); }
    private async Task Write(string permission) { await AuthorizationService.CheckAsync(permission); await financeLock.AcquireAsync(); }
    private async Task Ensure(Guid? branch) { if (branch != null) await BranchScope.EnsureCanAccessAsync(branch.Value); else if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.OrderCreate)) throw new AbpAuthorizationException(); }
    private async Task<Warehouse> Warehouse(Guid id) { var w = await R<Warehouse>().GetAsync(id); await Ensure(w.BranchId); w.EnsureNotLocked(); return w; }
    private static void Stamp(string a, string b) { if (a != b) throw new UserFriendlyException("Данные изменились. Обновите страницу."); }
    private async Task<List<Warehouse>> ScopedWarehouses()
    {
        var all = await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.OrderCreate);
        var branches = await AsyncExecuter.ToListAsync(await BranchScope.ApplyAsync(await R<Dental.Branches.Branch>().GetQueryableAsync(),b => b.Id)); var ids = branches.Select(b => b.Id).ToList();
        return await R<Warehouse>().GetListAsync(w => w.BranchId == null ? all : ids.Contains(w.BranchId.Value));
    }
    public async Task<List<PurchaseLookupDto>> GetWarehousesAsync() { await Read(); return (await ScopedWarehouses()).Select(w => new PurchaseLookupDto(w.Id,w.Name)).ToList(); }
    public async Task<List<PurchaseLookupDto>> GetSuppliersAsync() { await Read(); return (await R<Supplier>().GetListAsync()).Select(s => new PurchaseLookupDto(s.Id,s.Name)).ToList(); }
    public async Task<List<PurchaseLookupDto>> GetItemsAsync() { await Read(); return (await R<Item>().GetListAsync(i => i.IsActive)).Select(i => new PurchaseLookupDto(i.Id,i.Name)).ToList(); }
    public async Task<List<PurchasePriceDto>> GetPricesAsync()
    { await Read(); var items = await R<Item>().GetListAsync(); var suppliers = await R<Supplier>().GetListAsync(); return (await R<SupplierItem>().GetListAsync()).Select(p => new PurchasePriceDto(p.ItemId,items.FirstOrDefault(i => i.Id == p.ItemId)?.Name ?? "—",p.SupplierId,suppliers.FirstOrDefault(s => s.Id == p.SupplierId)?.Name ?? "—",p.LastPrice,p.LastPriceAt)).OrderBy(p => p.ItemName).ThenBy(p => p.Price).ToList(); }
    private async Task ValidateLines(List<PurchaseLineInput> lines)
    { if (lines.Count is < 1 or > 200 || lines.Any(l => l.Qty <= 0 || l.Qty > 100000000 || l.UnitPrice < 0) || lines.Select(l => l.ItemId).Distinct().Count() != lines.Count) throw new UserFriendlyException("Укажите от 1 до 200 разных товаров с положительным количеством."); var ids = lines.Select(l => l.ItemId).ToList(); if (await R<Item>().CountAsync(i => ids.Contains(i.Id) && i.IsActive) != ids.Count) throw new UserFriendlyException("Один из товаров недоступен."); }
    private async Task<PurchaseRequest> Request(Guid id) => await AsyncExecuter.FirstAsync((await R<PurchaseRequest>().WithDetailsAsync(r => r.Lines)).Where(r => r.Id == id));
    private async Task<PurchaseOrder> Order(Guid id) => await AsyncExecuter.FirstAsync((await R<PurchaseOrder>().WithDetailsAsync(o => o.Lines)).Where(o => o.Id == id));
    private async Task<PurchaseRequestDto> Map(PurchaseRequest r)
    { var w = await R<Warehouse>().GetAsync(r.WarehouseId); var ids = r.Lines.Select(l => l.ItemId).ToList(); var items = await R<Item>().GetListAsync(i => ids.Contains(i.Id)); return new(r.Id,r.WarehouseId,w.Name,r.Status,r.Source,r.ConcurrencyStamp,r.Comment,r.Lines.Select(l => new PurchaseLineDto(l.ItemId,items.FirstOrDefault(i => i.Id == l.ItemId)?.Name ?? "—",l.Qty,l.ProcessedQty,0,0)).ToList()); }
    private async Task<PurchaseOrderDto> Map(PurchaseOrder o)
    { var w = await R<Warehouse>().GetAsync(o.WarehouseId); var supplier = await R<Supplier>().GetAsync(o.SupplierId); var ids = o.Lines.Select(l => l.ItemId).ToList(); var items = await R<Item>().GetListAsync(i => ids.Contains(i.Id)); return new(o.Id,o.Number,o.SupplierId,supplier.Name,o.WarehouseId,w.Name,o.Status,o.ExpectedAt,o.Total,o.ConcurrencyStamp,o.SentVia,o.Comment,o.Lines.Where(l=>!l.IsDeleted).Select(l => new PurchaseLineDto(l.ItemId,items.FirstOrDefault(i => i.Id == l.ItemId)?.Name ?? "—",l.Qty,0,l.ReceivedQty,l.UnitPrice)).ToList()); }
    public async Task<List<PurchaseRequestDto>> GetRequestsAsync()
    { await Read(); var ids = (await ScopedWarehouses()).Select(w => w.Id).ToList(); var rows = await AsyncExecuter.ToListAsync((await R<PurchaseRequest>().WithDetailsAsync(r => r.Lines)).Where(r => ids.Contains(r.WarehouseId)).OrderByDescending(r => r.CreationTime).Take(200)); var result = new List<PurchaseRequestDto>(); foreach (var r in rows) result.Add(await Map(r)); return result; }
    public async Task<PurchaseRequestDto> CreateRequestAsync(PurchaseRequestInput input)
    { await Write(DentalPermissions.Purchase.RequestCreate); var w = await Warehouse(input.WarehouseId); await ValidateLines(input.Lines); var r = new PurchaseRequest(GuidGenerator.Create(),CurrentTenant.Id,w.Id,w.BranchId,PurchaseRequestSource.Manual,input.Comment); foreach(var line in input.Lines) r.Lines.Add(new PurchaseRequestLine(GuidGenerator.Create(),CurrentTenant.Id,r.Id,line.ItemId,line.Qty)); await R<PurchaseRequest>().InsertAsync(r,true); return await Map(r); }
    public async Task<List<PurchaseRequestDto>> GenerateAsync()
    {
        await Write(DentalPermissions.Purchase.RequestCreate); var ws = await ScopedWarehouses(); var wids = ws.Select(w => w.Id).ToList(); var levels = await R<ItemStockLevel>().GetListAsync(l => wids.Contains(l.WarehouseId)); var balances = await R<StockBalance>().GetListAsync(b => wids.Contains(b.WarehouseId));
        var orders = await AsyncExecuter.ToListAsync((await R<PurchaseOrder>().WithDetailsAsync(o => o.Lines)).Where(o => wids.Contains(o.WarehouseId) && (o.Status == PurchaseOrderStatus.Sent || o.Status == PurchaseOrderStatus.PartiallyReceived || o.Status == PurchaseOrderStatus.PendingApproval || o.Status == PurchaseOrderStatus.Draft)));
        var existing = await AsyncExecuter.ToListAsync((await R<PurchaseRequest>().WithDetailsAsync(r => r.Lines)).Where(r => wids.Contains(r.WarehouseId) && (r.Status == PurchaseRequestStatus.Draft || r.Status == PurchaseRequestStatus.Submitted)));
        var transfers = await AsyncExecuter.ToListAsync((await R<StockDocument>().WithDetailsAsync(d => d.Lines)).Where(d => d.Type == StockDocumentType.Transfer && d.Status == StockDocumentStatus.InTransit && d.WarehouseToId != null && wids.Contains(d.WarehouseToId.Value)));
        var result = new List<PurchaseRequestDto>();
        foreach (var w in ws.Where(w => !w.IsLocked))
        {
            var r = new PurchaseRequest(GuidGenerator.Create(),CurrentTenant.Id,w.Id,w.BranchId,PurchaseRequestSource.Auto,"Автоматическое пополнение");
            foreach (var l in levels.Where(l => l.WarehouseId == w.Id))
            {
                var qty = balances.Where(b => b.WarehouseId == w.Id && b.ItemId == l.ItemId).Sum(b => b.Qty); var ordered = orders.Where(o => o.WarehouseId == w.Id).SelectMany(o => o.Lines).Where(x => x.ItemId == l.ItemId).Sum(x => Math.Max(0,x.Qty-x.ReceivedQty));
                var transit = transfers.Where(d => d.WarehouseToId == w.Id).SelectMany(d => d.Lines).Where(x => x.ItemId == l.ItemId).Sum(x => x.Qty);
                var requested = existing.Where(x => x.WarehouseId == w.Id).SelectMany(x => x.Lines).Where(x => x.ItemId == l.ItemId).Sum(x => x.Qty-x.ProcessedQty);
                var need = Math.Max(0,Replenishment.SuggestedQty(qty,l.MinQty,l.OptimalQty,transit,ordered)-requested); if (need > 0) r.Lines.Add(new PurchaseRequestLine(GuidGenerator.Create(),CurrentTenant.Id,r.Id,l.ItemId,need,qty,l.MinQty,l.OptimalQty));
            }
            if (r.Lines.Count == 0) continue; r.Status = PurchaseRequestStatus.Submitted; await R<PurchaseRequest>().InsertAsync(r,true); result.Add(await Map(r));
        }
        return result;
    }
    public async Task<PurchaseRequestDto> SubmitAsync(Guid id, PurchaseStampInput input)
    { await Write(DentalPermissions.Purchase.RequestCreate); var r = await Request(id); await Ensure(r.BranchId); Stamp(input.ConcurrencyStamp,r.ConcurrencyStamp); if (r.Status != PurchaseRequestStatus.Draft) throw new UserFriendlyException("Отправить можно только черновик."); r.Status=PurchaseRequestStatus.Submitted; await R<PurchaseRequest>().UpdateAsync(r,true); return await Map(r); }
    public async Task<PurchaseRequestDto> ProcessAsync(Guid id, PurchaseProcessInput input)
    {
        await Write(DentalPermissions.Purchase.OrderCreate); var r = await Request(id); await Ensure(r.BranchId); Stamp(input.ConcurrencyStamp,r.ConcurrencyStamp); if (r.Status != PurchaseRequestStatus.Submitted) throw new UserFriendlyException("Заявка должна быть отправлена.");
        if (input.Reject) { r.Status=PurchaseRequestStatus.Rejected; await R<PurchaseRequest>().UpdateAsync(r,true); return await Map(r); }
        await ValidateLines(input.Lines); foreach(var l in input.Lines) { var source = r.Lines.SingleOrDefault(x => x.ItemId == l.ItemId); if (source == null || l.Qty > source.Qty-source.ProcessedQty) throw new UserFriendlyException("Количество превышает необработанную потребность."); }
        if (input.SourceWarehouseId != null)
        {
            await AuthorizationService.CheckAsync(DentalPermissions.Inventory.TransferCreate); await Warehouse(input.SourceWarehouseId.Value); if (input.SourceWarehouseId == r.WarehouseId) throw new UserFriendlyException("Выберите другой склад.");
            await stock.CreateAsync(StockDocumentType.Transfer,new(input.SourceWarehouseId,r.WarehouseId,Comment:"По заявке " + r.Id),input.Lines.Select(l => new StockLineData(l.ItemId,l.Qty)).ToList());
        }
        else
        {
            if (input.SupplierId == null) throw new UserFriendlyException("Выберите поставщика или склад-источник.");
            await NewOrder(new PurchaseOrderInput {WarehouseId=r.WarehouseId,SupplierId=input.SupplierId.Value,Comment="По заявке " + r.Id,Lines=input.Lines});
        }
        foreach(var l in input.Lines) r.Lines.Single(x => x.ItemId == l.ItemId).ProcessedQty += l.Qty;
        if (r.Lines.All(l => l.ProcessedQty >= l.Qty)) r.Status=PurchaseRequestStatus.Processed;
        await R<PurchaseRequest>().UpdateAsync(r,true); return await Map(r);
    }
    public async Task<List<PurchaseOrderDto>> GetOrdersAsync()
    { await Read(); var ids=(await ScopedWarehouses()).Select(w => w.Id).ToList(); var rows=await AsyncExecuter.ToListAsync((await R<PurchaseOrder>().WithDetailsAsync(o => o.Lines)).Where(o => ids.Contains(o.WarehouseId)).OrderByDescending(o => o.CreationTime).Take(200)); var result=new List<PurchaseOrderDto>(); foreach(var o in rows) result.Add(await Map(o)); return result; }
    private async Task<PurchaseOrder> NewOrder(PurchaseOrderInput input)
    { var w=await Warehouse(input.WarehouseId); await R<Supplier>().GetAsync(input.SupplierId); await ValidateLines(input.Lines); var o=new PurchaseOrder(GuidGenerator.Create(),CurrentTenant.Id,"ЗАК-"+(await numbers.NextAsync("po")).ToString("D6"),input.SupplierId,w.Id,w.BranchId,input.ExpectedAt,input.Comment); foreach(var l in input.Lines) o.Lines.Add(new PurchaseOrderLine(GuidGenerator.Create(),CurrentTenant.Id,o.Id,l.ItemId,l.Qty,l.UnitPrice)); o.Recalculate(); return await R<PurchaseOrder>().InsertAsync(o,true); }
    public async Task<PurchaseOrderDto> CreateOrderAsync(PurchaseOrderInput input) { await Write(DentalPermissions.Purchase.OrderCreate); return await Map(await NewOrder(input)); }
    public async Task<PurchaseOrderDto> UpdateOrderAsync(Guid id,PurchaseOrderUpdateInput input)
    {
        await Write(DentalPermissions.Purchase.OrderCreate);var o=await Order(id);await Ensure(o.BranchId);Stamp(input.ConcurrencyStamp,o.ConcurrencyStamp);var w=await Warehouse(input.WarehouseId);await R<Supplier>().GetAsync(input.SupplierId);await ValidateLines(input.Lines);o.Revise(input.SupplierId,w.Id,w.BranchId,input.ExpectedAt,input.Comment);
        foreach(var l in o.Lines)l.IsDeleted=true;await R<PurchaseOrderLine>().UpdateManyAsync(o.Lines,true);
        foreach(var l in input.Lines)o.Lines.Add(new(GuidGenerator.Create(),CurrentTenant.Id,o.Id,l.ItemId,l.Qty,l.UnitPrice));o.Recalculate();await R<PurchaseOrder>().UpdateAsync(o,true);return await Map(o);
    }
    public async Task<IRemoteStreamContent> ExportOrderAsync(Guid id,PurchaseExportInput input)
    {
        await Read();var order=await Order(id);await Ensure(order.BranchId);if(input.Format is not ("xlsx" or "pdf"))throw new UserFriendlyException("Выберите Excel или PDF.");var dto=await Map(order);
        var table=new ReportTable{Columns=[new("Товар","String"),new("Количество","Decimal"),new("Цена, ₸","Decimal"),new("Сумма, ₸","Decimal")]};foreach(var l in dto.Lines)table.Rows.Add([l.ItemName,l.Qty,l.UnitPrice/100m,Math.Round(l.Qty*l.UnitPrice,MidpointRounding.AwayFromZero)/100m]);
        var bytes=LazyServiceProvider.LazyGetRequiredService<IReportDocumentRenderer>().Render(table,order.Number+" · "+dto.SupplierName+" · "+dto.WarehouseName,input.Format);
        return new RemoteStreamContent(new MemoryStream(bytes),order.Number+"."+input.Format,input.Format=="pdf"?"application/pdf":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    public async Task<PurchaseOrderDto> SendAsync(Guid id, PurchaseSendInput input)
    {
        await Write(DentalPermissions.Purchase.OrderCreate); var o=await Order(id); await Ensure(o.BranchId); Stamp(input.ConcurrencyStamp,o.ConcurrencyStamp); if (o.Status != PurchaseOrderStatus.Draft) throw new UserFriendlyException("Отправить можно только черновик заказа.");
        var threshold=await settings.GetAsync<long>(DentalSettings.PurchaseOrderApprovalThreshold); o.SentVia=input.Via;
        if (o.Total > threshold && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Purchase.OrderApprove)) { o.Status=PurchaseOrderStatus.PendingApproval; await approvals.RequestAsync(ApprovalTypes.PurchaseOrder,nameof(PurchaseOrder),o.Id,o.Total,o.Number,new {orderId=o.Id},o.BranchId); }
        else { o.Status=PurchaseOrderStatus.Sent; o.SentAt=Clock.Now; }
        await R<PurchaseOrder>().UpdateAsync(o,true); return await Map(o);
    }
    public async Task<PurchaseOrderDto> CancelAsync(Guid id, PurchaseStampInput input)
    { await Write(DentalPermissions.Purchase.OrderCreate); var o=await Order(id); await Ensure(o.BranchId); Stamp(input.ConcurrencyStamp,o.ConcurrencyStamp); if (o.Lines.Any(l => l.ReceivedQty > 0)) throw new UserFriendlyException("Заказ с приёмками нельзя отменить. Оформите возврат поставщику."); o.Status=PurchaseOrderStatus.Cancelled; await approvals.CancelPendingAsync(nameof(PurchaseOrder),o.Id,"Заказ отменён"); await R<PurchaseOrder>().UpdateAsync(o,true); return await Map(o); }
    public async Task<Guid> ReceiveAsync(Guid id, PurchaseReceiveInput input)
    {
        await Write(DentalPermissions.Inventory.Receive); await Read(); var o=await Order(id); await Ensure(o.BranchId); Stamp(input.ConcurrencyStamp,o.ConcurrencyStamp); if (o.Status is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived)) throw new UserFriendlyException("Заказ не ожидает приёмку."); await ValidateLines(input.Lines);
        foreach(var l in input.Lines) { var expected=o.Lines.SingleOrDefault(x => x.ItemId == l.ItemId); if (expected == null || l.Qty > expected.Qty-expected.ReceivedQty) throw new UserFriendlyException("Количество превышает остаток заказа."); }
        var doc=await stock.CreateAsync(StockDocumentType.Receipt,new(null,o.WarehouseId,o.SupplierId,o.Id,input.InvoiceNumber,input.InvoiceDate,Comment:"Приёмка " + o.Number),input.Lines.Select(l => new StockLineData(l.ItemId,l.Qty,UnitCost:o.Lines.Single(x => x.ItemId == l.ItemId).UnitPrice)).ToList());
        await stock.PostAsync(doc.Id); return doc.Id;
    }
    private async Task<SupplierInvoiceDto> Map(SupplierInvoice i) => new(i.Id,i.Number,i.SupplierId,(await R<Supplier>().GetAsync(i.SupplierId)).Name,i.Date,i.DueDate,i.Amount,i.PaidAmount,i.ReturnedAmount,i.Status,i.ConcurrencyStamp);
    public async Task<List<SupplierInvoiceDto>> GetInvoicesAsync()
    { await AuthorizationService.CheckAsync(DentalPermissions.Purchase.OrderCreate); var result=new List<SupplierInvoiceDto>(); var ws=await ScopedWarehouses(); var branches=ws.Where(w => w.BranchId != null).Select(w => w.BranchId!.Value).Distinct().ToList(); var rows=await R<SupplierInvoice>().GetListAsync(i => i.BranchId == null || branches.Contains(i.BranchId.Value)); foreach(var i in rows.OrderByDescending(i => i.Date).Take(200)) result.Add(await Map(i)); return result; }
    public async Task<SupplierInvoiceDto> PayInvoiceAsync(Guid id, SupplierPayInput input)
    {
        await Write(DentalPermissions.Purchase.OrderCreate); var i=await R<SupplierInvoice>().GetAsync(id); await Ensure(i.BranchId);
        var previous=await R<SupplierInvoicePayment>().FirstOrDefaultAsync(p => p.IdempotencyKey == input.IdempotencyKey);
        if (previous != null) { if (previous.InvoiceId != id || previous.Amount != input.Amount || previous.Reference != input.Reference) throw new UserFriendlyException("Ключ операции уже использован."); return await Map(i); }
        Stamp(input.ConcurrencyStamp,i.ConcurrencyStamp); if (input.Amount <= 0 || input.Amount > i.Amount-i.PaidAmount-i.ReturnedAmount) throw new UserFriendlyException("Сумма превышает задолженность по счёту.");
        await R<SupplierInvoicePayment>().InsertAsync(new(GuidGenerator.Create(),CurrentTenant.Id,id,input.Amount,input.Reference,input.IdempotencyKey),true); i.PaidAmount=checked(i.PaidAmount+input.Amount); i.Recalculate(); await R<SupplierInvoice>().UpdateAsync(i,true); return await Map(i);
    }
    public async Task<List<PurchaseLineDto>> GetReturnLinesAsync(Guid id)
    {
        await AuthorizationService.CheckAsync(DentalPermissions.Purchase.OrderCreate); var invoice=await R<SupplierInvoice>().GetAsync(id);await Ensure(invoice.BranchId);var receipt=await stock.GetWithLinesAsync(invoice.StockDocumentId);
        var returns=await AsyncExecuter.ToListAsync((await R<StockDocument>().WithDetailsAsync(d=>d.Lines)).Where(d=>d.SourceDocumentId==receipt.Id&&d.Type==StockDocumentType.ReturnToSupplier&&d.Status==StockDocumentStatus.Posted));var items=await R<Item>().GetListAsync();
        return receipt.Lines.GroupBy(l=>l.ItemId).Select(g=>new PurchaseLineDto(g.Key,items.FirstOrDefault(i=>i.Id==g.Key)?.Name??"—",Math.Max(0,g.Sum(l=>l.Qty)-returns.SelectMany(d=>d.Lines).Where(l=>l.ItemId==g.Key).Sum(l=>l.Qty)),0,0,checked((long)Math.Round(g.Sum(l=>l.TotalCost)/g.Sum(l=>l.Qty),MidpointRounding.AwayFromZero)))).Where(l=>l.Qty>0).ToList();
    }
    public async Task<Guid> ReturnInvoiceAsync(Guid id,SupplierReturnInput input)
    {
        await Write(DentalPermissions.Purchase.OrderCreate);await AuthorizationService.CheckAsync(DentalPermissions.Inventory.Writeoff);var invoice=await R<SupplierInvoice>().GetAsync(id);await Ensure(invoice.BranchId);Stamp(input.ConcurrencyStamp,invoice.ConcurrencyStamp);await ValidateLines(input.Lines);
        var available=await GetReturnLinesAsync(id);foreach(var line in input.Lines)if(!available.Any(l=>l.ItemId==line.ItemId&&l.Qty>=line.Qty))throw new UserFriendlyException("Количество возврата превышает исходную приёмку.");
        var receipt=await stock.GetWithLinesAsync(invoice.StockDocumentId);if(receipt.Status!=StockDocumentStatus.Posted)throw new UserFriendlyException("Приход уже отменён.");
        var doc=await stock.CreateAsync(StockDocumentType.ReturnToSupplier,new(receipt.WarehouseToId,null,invoice.SupplierId,invoice.PurchaseOrderId,Comment:input.Reason),input.Lines.Select(l=>new StockLineData(l.ItemId,l.Qty,UnitCost:available.Single(x=>x.ItemId==l.ItemId).UnitPrice)).ToList());doc.SourceDocumentId=receipt.Id;await R<StockDocument>().UpdateAsync(doc,true);await stock.PostAsync(doc.Id);return doc.Id;
    }
}
