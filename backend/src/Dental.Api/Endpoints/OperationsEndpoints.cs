using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Cashdesk;
using Dental.Application.Common;
using Dental.Application.Inventory;
using Dental.Application.Permissions;
using Dental.Application.Visits;
using Dental.Domain.Cash;
using Dental.Domain.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapVisitEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Visits").RequireAuthorization();
        string[] edit = [Perm.Visits.EditOpen, Perm.Visits.Complete];

        g.MapPost("/appointments/{id:guid}/arrive", async (Guid id, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.ArriveAsync(id, ct)))
            .RequirePermission(Perm.Schedule.Manage, Perm.Visits.EditOpen).WithName("ArriveAppointment");
        g.MapGet("/visits/{id:guid}", async (Guid id, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission([.. edit, Perm.Cash.PaymentCreate, Perm.Visits.EditClosed, Perm.Schedule.ViewAll]).WithName("GetVisit");
        g.MapPatch("/visits/{id:guid}", async (Guid id, UpdateVisitRequest r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(edit).WithName("UpdateVisit");
        g.MapPost("/visits/{id:guid}/items", async (Guid id, VisitItemRequest r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.AddItemAsync(id, r, ct)))
            .RequirePermission(edit).Validate<VisitItemRequest>().WithName("AddVisitItem");
        g.MapPatch("/visits/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, VisitItemRequest r, VisitService s, CancellationToken ct) =>
                TypedResults.Ok(await s.UpdateItemAsync(id, itemId, r, ct)))
            .RequirePermission(edit).WithName("UpdateVisitItem");
        g.MapDelete("/visits/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.RemoveItemAsync(id, itemId, ct)))
            .RequirePermission(edit).WithName("RemoveVisitItem");
        g.MapGet("/visits/{id:guid}/materials", async (Guid id, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.GetMaterialsAsync(id, ct)))
            .RequirePermission([.. edit, Perm.Inventory.View]).WithName("GetVisitMaterials");
        g.MapPut("/visits/{id:guid}/materials", async (Guid id, List<VisitMaterialInput> r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.SetMaterialsAsync(id, r, ct)))
            .RequirePermission(edit).WithName("SetVisitMaterials");
        g.MapPost("/visits/{id:guid}/close", async (Guid id, CloseVisitRequest r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.CloseAsync(id, r, ct)))
            .RequirePermission(Perm.Visits.Complete).WithName("CloseVisit");
        g.MapPost("/visits/{id:guid}/corrections", async (Guid id, VisitCorrectionRequest r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.CorrectAsync(id, r, ct)))
            .RequirePermission(Perm.Visits.EditClosed).Validate<VisitCorrectionRequest>().WithName("CorrectVisit");
        g.MapPost("/visits/{id:guid}/cancel", async (Guid id, CancelVisitRequest r, VisitService s, CancellationToken ct) => TypedResults.Ok(await s.CancelAsync(id, r, ct)))
            .RequirePermission(Perm.Visits.Cancel).Validate<CancelVisitRequest>().WithName("CancelVisit");
        return app;
    }

    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Cashdesk").RequireAuthorization();
        string[] cashView = [Perm.Cash.ShiftOpenClose, Perm.Cash.PaymentCreate, Perm.Cash.PaymentRefund, Perm.Cash.ExpenseCreate, Perm.Reports.Finance];

        g.MapGet("/cash-shifts", async ([FromQuery(Name = "branch_id")] Guid? branchId, CashShiftStatus? status, int? limit, CashService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListShiftsAsync(branchId, status, limit ?? 50, ct)))
            .RequirePermission(cashView).WithName("ListCashShifts");
        g.MapGet("/cash-shifts/{id:guid}", async (Guid id, CashService s, CancellationToken ct) => TypedResults.Ok(await s.GetShiftAsync(id, ct)))
            .RequirePermission(cashView).WithName("GetCashShift");
        g.MapPost("/cash-shifts/open", async (OpenShiftRequest r, CashService s, CancellationToken ct) => TypedResults.Ok(await s.OpenShiftAsync(r, ct)))
            .RequirePermission(Perm.Cash.ShiftOpenClose).WithName("OpenCashShift");
        g.MapPost("/cash-shifts/{id:guid}/close", async (Guid id, CloseShiftRequest r, CashService s, CancellationToken ct) => TypedResults.Ok(await s.CloseShiftAsync(id, r, ct)))
            .RequirePermission(Perm.Cash.ShiftOpenClose).Validate<CloseShiftRequest>().WithName("CloseCashShift");
        g.MapGet("/cash-shifts/{id:guid}/operations", async (Guid id, CashService s, CancellationToken ct) => TypedResults.Ok(await s.ListOperationsAsync(id, ct)))
            .RequirePermission(cashView).WithName("ListCashOperations");
        g.MapPost("/cash-shifts/{id:guid}/operations", async (Guid id, CashOperationRequest r, CashService s, CancellationToken ct) => TypedResults.Ok(await s.CreateOperationAsync(id, r, ct)))
            .RequirePermission(Perm.Cash.ShiftOpenClose).WithName("CreateCashOperation");

        g.MapGet("/payments", async ([FromQuery(Name = "branch_id")] Guid? branchId, [FromQuery(Name = "shift_id")] Guid? shiftId, [FromQuery(Name = "patient_id")] Guid? patientId,
                DateTimeOffset? from, DateTimeOffset? to, int? limit, CashService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListPaymentsAsync(branchId, shiftId, patientId, from, to, limit ?? 100, ct)))
            .RequirePermission(cashView).WithName("ListPayments");
        g.MapPost("/payments", async (CreatePaymentRequest r, CashService s, HttpContext http, CancellationToken ct) =>
                TypedResults.Ok(await s.CreatePaymentAsync(r, http.Request.Headers[IdempotencyFilter.Header].ToString(), ct)))
            .RequirePermission(Perm.Cash.PaymentCreate).Validate<CreatePaymentRequest>().Idempotent().WithName("CreatePayment");
        g.MapPost("/payments/{id:guid}/refund", async (Guid id, RefundRequest r, CashService s, HttpContext http, CancellationToken ct) =>
                TypedResults.Ok(await s.RefundAsync(id, r, http.Request.Headers[IdempotencyFilter.Header].ToString().NullIfEmpty(), ct)))
            .RequirePermission(Perm.Cash.PaymentRefund).Validate<RefundRequest>().Idempotent(required: false).WithName("RefundPayment");

        g.MapGet("/expenses", async ([FromQuery(Name = "branch_id")] Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, CashService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListExpensesAsync(branchId, from, to, ct)))
            .RequirePermission(Perm.Cash.ExpenseCreate, Perm.Reports.Finance).WithName("ListExpenses");
        g.MapPost("/expenses", async (ExpenseRequest r, CashService s, CancellationToken ct) => TypedResults.Ok(await s.CreateExpenseAsync(r, ct)))
            .RequirePermission(Perm.Cash.ExpenseCreate).Validate<ExpenseRequest>().WithName("CreateExpense");
        return app;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Inventory").RequireAuthorization();
        string[] view = [Perm.Inventory.View];

        g.MapGet("/stock/balances", async ([FromQuery(Name = "warehouse_id")] Guid? warehouseId, [FromQuery(Name = "category_id")] Guid? categoryId,
                [FromQuery(Name = "below_min")] bool? belowMin, [FromQuery(Name = "expiring_days")] int? expiringDays, string? q, StockService s, CancellationToken ct) =>
                TypedResults.Ok(await s.BalancesAsync(warehouseId, categoryId, belowMin ?? false, expiringDays, q, ct)))
            .RequirePermission(view).WithName("StockBalances");
        g.MapGet("/stock/movements", async ([FromQuery(Name = "item_id")] Guid? itemId, [FromQuery(Name = "warehouse_id")] Guid? warehouseId,
                DateTimeOffset? from, DateTimeOffset? to, string? cursor, int? limit, StockService s, CancellationToken ct) =>
                TypedResults.Ok(await s.MovementsAsync(itemId, warehouseId, from, to, cursor, limit ?? 100, ct)))
            .RequirePermission(view).WithName("StockMovements");
        g.MapGet("/stock/items/{id:guid}/card", async (Guid id, StockService s, CancellationToken ct) => TypedResults.Ok(await s.ItemCardAsync(id, ct)))
            .RequirePermission(view).WithName("StockItemCard");

        g.MapGet("/stock-documents", async (StockDocumentType? type, StockDocumentStatus? status, [FromQuery(Name = "warehouse_id")] Guid? warehouseId,
                DateOnly? from, DateOnly? to, int? page, [FromQuery(Name = "page_size")] int? pageSize, StockService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(type, status, warehouseId, from, to, new PageQuery(page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(view).WithName("ListStockDocuments");
        g.MapPost("/stock-documents", async (CreateStockDocumentRequest r, StockService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct)))
            .RequirePermission(Perm.Inventory.Receive, Perm.Inventory.Writeoff, Perm.Inventory.TransferCreate, Perm.Inventory.Count)
            .Validate<CreateStockDocumentRequest>().WithName("CreateStockDocument");
        g.MapGet("/stock-documents/{id:guid}", async (Guid id, StockService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(view).WithName("GetStockDocument");
        g.MapPatch("/stock-documents/{id:guid}", async (Guid id, UpdateStockDocumentRequest r, StockService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.Receive, Perm.Inventory.Writeoff, Perm.Inventory.TransferCreate, Perm.Inventory.Count).WithName("UpdateStockDocument");
        g.MapPost("/stock-documents/{id:guid}/post", async (Guid id, StockActionRequest r, StockService s, CancellationToken ct) => TypedResults.Ok(await s.PostAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.Receive, Perm.Inventory.Writeoff, Perm.Inventory.TransferCreate, Perm.Inventory.CountApprove)
            .Idempotent(required: false).WithName("PostStockDocument");
        g.MapPost("/stock-documents/{id:guid}/receive", async (Guid id, ReceiveTransferRequest r, StockService s, CancellationToken ct) => TypedResults.Ok(await s.ReceiveAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.TransferReceive).Idempotent(required: false).WithName("ReceiveStockTransfer");
        g.MapPost("/stock-documents/{id:guid}/cancel", async (Guid id, StockActionRequest r, StockService s, CancellationToken ct) => TypedResults.Ok(await s.CancelAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.Receive, Perm.Inventory.Writeoff, Perm.Inventory.TransferCreate, Perm.Inventory.Count).WithName("CancelStockDocument");
        return app;
    }
}
