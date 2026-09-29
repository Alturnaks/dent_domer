using Dental.Domain.Inventory;
using Dental.Domain.Purchasing;
using FluentValidation;

namespace Dental.Application.Purchasing;

// ================= Заявки на пополнение =================

public sealed record PurchaseRequestLineInput(Guid ItemId, decimal Qty);

public sealed record CreatePurchaseRequestRequest(Guid WarehouseId, string? Comment, IReadOnlyList<PurchaseRequestLineInput> Lines, bool Submit = false);

public sealed record UpdatePurchaseRequestRequest(string? Comment, IReadOnlyList<PurchaseRequestLineInput>? Lines);

public sealed record PurchaseRequestCommentRequest(string? Comment);

public sealed record PurchaseRequestLineDto(
    Guid Id, Guid ItemId, string ItemName, string ItemSku, string BaseUnit, decimal Qty, decimal CurrentQty, decimal MinQty, decimal OptimalQty);

public sealed record PurchaseRequestDto(
    Guid Id, Guid? BranchId, string? BranchName, Guid WarehouseId, string WarehouseName, PurchaseRequestStatus Status, PurchaseRequestSource Source,
    string? Comment, DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset UpdatedAt, IReadOnlyList<PurchaseRequestLineDto> Lines);

public sealed record PurchaseRequestListItem(
    Guid Id, Guid? BranchId, string? BranchName, Guid WarehouseId, string WarehouseName, PurchaseRequestStatus Status, PurchaseRequestSource Source,
    string? Comment, DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset UpdatedAt, int LinesCount, decimal TotalQty);

/// <summary>Итог автоформирования: сколько заявок создано/обновлено/закрыто.</summary>
public sealed record GenerateRequestsResult(int Created, int Updated, int Closed, int Lines, IReadOnlyList<Guid> RequestIds);

// ================= Сводная потребность сети =================

public sealed record SupplierPriceDto(Guid SupplierId, string SupplierName, long LastPrice, DateTimeOffset? LastPriceAt);

public sealed record DemandRow(
    Guid ItemId, string ItemName, string ItemSku, string BaseUnit, Guid WarehouseId, string WarehouseName, Guid? BranchId, string? BranchName,
    decimal Qty, decimal CurrentQty, decimal MinQty, decimal OptimalQty, IReadOnlyList<Guid> RequestIds, IReadOnlyList<Guid> LineIds);

public sealed record DemandItem(
    Guid ItemId, string ItemName, string ItemSku, string BaseUnit, decimal TotalQty, decimal CentralQty, IReadOnlyList<SupplierPriceDto> Prices, Guid? BestSupplierId);

public sealed record DemandWarehouseRef(Guid Id, string Name);

public sealed record NetworkDemandDto(
    IReadOnlyList<DemandRow> Rows, IReadOnlyList<DemandItem> Items, IReadOnlyList<DemandWarehouseRef> CentralWarehouses, Guid? CentralWarehouseId,
    int RequestsCount, long EstimatedTotal);

public enum DemandAction { Transfer, Order, Reject }

/// <summary>Строка сводной потребности (товар × склад). Qty — сколько переместить/заказать (по умолчанию — вся потребность); SupplierId — поставщик строки для заказа.</summary>
public sealed record DemandSelection(Guid ItemId, Guid WarehouseId, decimal? Qty, Guid? SupplierId, long? UnitPrice);

/// <summary>
/// Обработка сводной потребности. Выбор — либо строки (товар × склад), либо целые заявки (RequestIds).
/// transfer: черновики перемещений с центрального склада (по одному на склад-получатель);
/// order: черновики заказов поставщикам (группировка по поставщику и складу доставки);
/// reject: строки/заявки отклоняются с комментарием.
/// </summary>
public sealed record ProcessDemandRequest(
    DemandAction Action, IReadOnlyList<DemandSelection>? Rows, IReadOnlyList<Guid>? RequestIds, Guid? SupplierId, Guid? FromWarehouseId,
    Guid? DeliverToWarehouseId, string? Comment);

public sealed record ProcessDemandResult(DemandAction Action, IReadOnlyList<Guid> TransferIds, IReadOnlyList<Guid> OrderIds, int ProcessedLines, IReadOnlyList<string> Numbers);

// ================= Заказы поставщикам =================

public sealed record PurchaseOrderLineInput(Guid ItemId, decimal Qty, long? UnitPrice);

public sealed record CreatePurchaseOrderRequest(Guid SupplierId, Guid WarehouseId, DateOnly? ExpectedAt, string? Comment, IReadOnlyList<PurchaseOrderLineInput> Lines);

public sealed record UpdatePurchaseOrderRequest(
    Guid? SupplierId, Guid? WarehouseId, DateOnly? ExpectedAt, string? Comment, IReadOnlyList<PurchaseOrderLineInput>? Lines, int? Version);

public sealed record SendPurchaseOrderRequest(string SentVia, int? Version);

public sealed record PurchaseOrderActionRequest(string? Comment, int? Version);

public sealed record PurchaseOrderLineDto(
    Guid Id, Guid ItemId, string ItemName, string ItemSku, string BaseUnit, string? SupplierSku, decimal Qty, long UnitPrice, long Total, decimal ReceivedQty,
    decimal RemainingQty);

public sealed record PurchaseOrderDocRef(Guid Id, string Number, StockDocumentStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? PostedAt, long TotalCost);

public sealed record PurchaseOrderInvoiceRef(Guid Id, string Number, DateOnly Date, long Amount, long PaidAmount, SupplierInvoiceStatus Status);

public sealed record PurchaseOrderDto(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid SupplierId, string SupplierName, string? SupplierPhone, string? SupplierWhatsapp,
    string? SupplierEmail, Guid WarehouseId, string WarehouseName, Guid? BranchId, DateOnly? ExpectedAt, long Total, string? SentVia, DateTimeOffset? SentAt,
    string? Comment, DateTimeOffset CreatedAt, string? CreatedByName, int Version, bool NeedsApproval, long ApprovalThreshold,
    IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<PurchaseOrderDocRef> Receipts, IReadOnlyList<PurchaseOrderInvoiceRef> Invoices);

public sealed record PurchaseOrderListItem(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid SupplierId, string SupplierName, Guid WarehouseId, string WarehouseName, DateOnly? ExpectedAt,
    long Total, string? SentVia, DateTimeOffset? SentAt, DateTimeOffset CreatedAt, int LinesCount, decimal ReceivedPct);

public static class SentVia
{
    public const string Pdf = "pdf";
    public const string Excel = "excel";
    public const string Whatsapp = "whatsapp";
    public const string Email = "email";
    public const string Manual = "manual";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Pdf, Excel, Whatsapp, Email, Manual };
}

// ================= Счета поставщиков и долги =================

public sealed record SupplierInvoiceDto(
    Guid Id, Guid SupplierId, string SupplierName, Guid? PurchaseOrderId, string? PurchaseOrderNumber, Guid? StockDocumentId, string? StockDocumentNumber,
    string Number, DateOnly Date, long Amount, DateOnly? DueDate, long PaidAmount, long ReturnedAmount, long Remaining, SupplierInvoiceStatus Status,
    bool Overdue, int? DaysOverdue);

public sealed record CreateSupplierInvoiceRequest(Guid SupplierId, Guid? PurchaseOrderId, string Number, DateOnly Date, long Amount, DateOnly? DueDate);

public sealed record UpdateSupplierInvoiceRequest(string? Number, DateOnly? Date, long? Amount, DateOnly? DueDate);

public sealed record SupplierPaymentRequest(long Amount, DateOnly? Date, string? Comment);

public sealed record SupplierDebtRow(
    Guid SupplierId, string SupplierName, int InvoicesCount, long Amount, long Paid, long Returned, long Debt, long OverdueDebt, DateOnly? NextDueDate);

public sealed record SupplierDebtsDto(IReadOnlyList<SupplierDebtRow> Rows, long TotalDebt, long TotalOverdue);

// ================= Экспорт =================

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

// ================= Валидация =================

public sealed class CreatePurchaseRequestRequestValidator : AbstractValidator<CreatePurchaseRequestRequest>
{
    public CreatePurchaseRequestRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Добавьте хотя бы одну позицию");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).NotEmpty();
            l.RuleFor(x => x.Qty).GreaterThan(0);
        });
    }
}

public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).NotEmpty();
            l.RuleFor(x => x.Qty).GreaterThan(0);
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice is not null);
        });
    }
}

public sealed class SendPurchaseOrderRequestValidator : AbstractValidator<SendPurchaseOrderRequest>
{
    public SendPurchaseOrderRequestValidator() =>
        RuleFor(x => x.SentVia).Must(v => SentVia.All.Contains(v)).WithMessage("Способ отправки: pdf, excel, whatsapp, email или manual");
}

public sealed class CreateSupplierInvoiceRequestValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{
    public CreateSupplierInvoiceRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Number).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class SupplierPaymentRequestValidator : AbstractValidator<SupplierPaymentRequest>
{
    public SupplierPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}
