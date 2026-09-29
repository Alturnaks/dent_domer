using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Application.Purchasing;
using Dental.Domain.Purchasing;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class PurchasingEndpoints
{
    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Purchasing").RequireAuthorization();
        string[] requestView = [Perm.Purchase.RequestCreate, Perm.Purchase.OrderCreate, Perm.Purchase.OrderApprove];
        string[] network = [Perm.Purchase.OrderCreate, Perm.Purchase.OrderApprove];
        string[] orderView = [Perm.Purchase.OrderCreate, Perm.Purchase.OrderApprove, Perm.Inventory.Receive];
        string[] invoiceView = [Perm.Purchase.OrderCreate, Perm.Purchase.OrderApprove, Perm.Reports.Finance];

        // ---------- Заявки на пополнение ----------
        g.MapGet("/purchase-requests", async (PurchaseRequestStatus? status, [FromQuery(Name = "branch_id")] Guid? branchId, [FromQuery(Name = "warehouse_id")] Guid? warehouseId,
                PurchaseRequestSource? source, int? page, [FromQuery(Name = "page_size")] int? pageSize, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(status, branchId, warehouseId, source, new PageQuery(page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(requestView).WithName("ListPurchaseRequests");
        g.MapPost("/purchase-requests", async (CreatePurchaseRequestRequest r, PurchaseRequestService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct)))
            .RequirePermission(Perm.Purchase.RequestCreate).Validate<CreatePurchaseRequestRequest>().WithName("CreatePurchaseRequest");
        g.MapPost("/purchase-requests/generate", async ([FromQuery(Name = "warehouse_id")] Guid? warehouseId, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.GenerateAsync(warehouseId, ct)))
            .RequirePermission(Perm.Purchase.RequestCreate).WithName("GeneratePurchaseRequests");
        g.MapGet("/purchase-requests/network-demand", async ([FromQuery(Name = "central_warehouse_id")] Guid? centralId, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.DemandAsync(centralId, ct)))
            .RequirePermission(network).WithName("GetNetworkDemand");
        g.MapPost("/purchase-requests/process", async (ProcessDemandRequest r, PurchaseRequestService s, CancellationToken ct) => TypedResults.Ok(await s.ProcessAsync(r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).Idempotent(required: false).WithName("ProcessPurchaseDemand");
        g.MapGet("/purchase-requests/{id:guid}", async (Guid id, PurchaseRequestService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(requestView).WithName("GetPurchaseRequest");
        g.MapPatch("/purchase-requests/{id:guid}", async (Guid id, UpdatePurchaseRequestRequest r, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.RequestCreate).WithName("UpdatePurchaseRequest");
        g.MapPost("/purchase-requests/{id:guid}/submit", async (Guid id, PurchaseRequestService s, CancellationToken ct) => TypedResults.Ok(await s.SubmitAsync(id, ct)))
            .RequirePermission(Perm.Purchase.RequestCreate).WithName("SubmitPurchaseRequest");
        g.MapPost("/purchase-requests/{id:guid}/reject", async (Guid id, PurchaseRequestCommentRequest r, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.RejectAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.RequestCreate, Perm.Purchase.OrderCreate).WithName("RejectPurchaseRequest");
        g.MapPost("/purchase-requests/{id:guid}/mark-processed", async (Guid id, PurchaseRequestCommentRequest r, PurchaseRequestService s, CancellationToken ct) =>
                TypedResults.Ok(await s.MarkProcessedAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).WithName("MarkPurchaseRequestProcessed");

        // ---------- Заказы поставщикам ----------
        g.MapGet("/purchase-orders", async (PurchaseOrderStatus? status, [FromQuery(Name = "supplier_id")] Guid? supplierId, [FromQuery(Name = "warehouse_id")] Guid? warehouseId,
                string? q, int? page, [FromQuery(Name = "page_size")] int? pageSize, PurchaseOrderService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(status, supplierId, warehouseId, q, new PageQuery(page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(orderView).WithName("ListPurchaseOrders");
        g.MapPost("/purchase-orders", async (CreatePurchaseOrderRequest r, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).Validate<CreatePurchaseOrderRequest>().WithName("CreatePurchaseOrder");
        g.MapGet("/purchase-orders/{id:guid}", async (Guid id, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(orderView).WithName("GetPurchaseOrder");
        g.MapPatch("/purchase-orders/{id:guid}", async (Guid id, UpdatePurchaseOrderRequest r, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).WithName("UpdatePurchaseOrder");
        g.MapPost("/purchase-orders/{id:guid}/send", async (Guid id, SendPurchaseOrderRequest r, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.SendAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).Validate<SendPurchaseOrderRequest>().WithName("SendPurchaseOrder");
        g.MapPost("/purchase-orders/{id:guid}/cancel", async (Guid id, PurchaseOrderActionRequest r, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.CancelAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).WithName("CancelPurchaseOrder");
        g.MapPost("/purchase-orders/{id:guid}/receive", async (Guid id, PurchaseOrderService s, CancellationToken ct) => TypedResults.Ok(await s.ReceiveAsync(id, ct)))
            .RequirePermission(Perm.Inventory.Receive).WithName("ReceivePurchaseOrder");
        g.MapGet("/purchase-orders/{id:guid}/export", async (Guid id, string? format, PurchaseOrderService s, CancellationToken ct) =>
            {
                var file = await s.ExportAsync(id, format, ct);
                return TypedResults.File(file.Content, file.ContentType, file.FileName);
            })
            .RequirePermission(orderView).WithName("ExportPurchaseOrder");

        // ---------- Счета поставщиков и долги ----------
        g.MapGet("/supplier-invoices", async ([FromQuery(Name = "supplier_id")] Guid? supplierId, SupplierInvoiceStatus? status, bool? overdue,
                [FromQuery(Name = "purchase_order_id")] Guid? purchaseOrderId, int? page, [FromQuery(Name = "page_size")] int? pageSize, SupplierInvoiceService s,
                CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(supplierId, status, overdue, purchaseOrderId, new PageQuery(page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(invoiceView).WithName("ListSupplierInvoices");
        g.MapPost("/supplier-invoices", async (CreateSupplierInvoiceRequest r, SupplierInvoiceService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).Validate<CreateSupplierInvoiceRequest>().WithName("CreateSupplierInvoice");
        g.MapGet("/supplier-invoices/{id:guid}", async (Guid id, SupplierInvoiceService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(invoiceView).WithName("GetSupplierInvoice");
        g.MapPatch("/supplier-invoices/{id:guid}", async (Guid id, UpdateSupplierInvoiceRequest r, SupplierInvoiceService s, CancellationToken ct) =>
                TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).WithName("UpdateSupplierInvoice");
        g.MapPost("/supplier-invoices/{id:guid}/payments", async (Guid id, SupplierPaymentRequest r, SupplierInvoiceService s, CancellationToken ct) =>
                TypedResults.Ok(await s.PayAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate).Validate<SupplierPaymentRequest>().Idempotent(required: false).WithName("PaySupplierInvoice");
        g.MapGet("/suppliers/debts", async (SupplierInvoiceService s, CancellationToken ct) => TypedResults.Ok(await s.DebtsAsync(ct)))
            .RequirePermission(invoiceView).WithName("SupplierDebts");
        return app;
    }
}
