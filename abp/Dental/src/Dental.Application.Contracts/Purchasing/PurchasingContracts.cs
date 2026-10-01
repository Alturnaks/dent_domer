using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;
namespace Dental.Purchasing;
public record PurchaseLookupDto(Guid Id, string Name);
public record PurchaseLineDto(Guid ItemId, string ItemName, decimal Qty, decimal ProcessedQty, decimal ReceivedQty, long UnitPrice);
public record PurchaseRequestDto(Guid Id, Guid WarehouseId, string WarehouseName, PurchaseRequestStatus Status, PurchaseRequestSource Source, string ConcurrencyStamp, string? Comment, List<PurchaseLineDto> Lines);
public record PurchaseOrderDto(Guid Id, string Number, Guid SupplierId, string SupplierName, Guid WarehouseId, string WarehouseName, PurchaseOrderStatus Status, DateOnly? ExpectedAt, long Total, string ConcurrencyStamp, string? SentVia, string? Comment, List<PurchaseLineDto> Lines);
public record SupplierInvoiceDto(Guid Id, string Number, Guid SupplierId, string SupplierName, DateOnly Date, DateOnly DueDate, long Amount, long PaidAmount, long ReturnedAmount, SupplierInvoiceStatus Status, string ConcurrencyStamp);
public record PurchasePriceDto(Guid ItemId, string ItemName, Guid SupplierId, string SupplierName, long Price, DateTime? PriceAt);
public class PurchaseLineInput { public Guid ItemId { get; set; } public decimal Qty { get; set; } public long UnitPrice { get; set; } }
public class PurchaseRequestInput { public Guid WarehouseId { get; set; } [StringLength(2000)] public string? Comment { get; set; } public List<PurchaseLineInput> Lines { get; set; } = []; }
public class PurchaseOrderInput : PurchaseRequestInput { public Guid SupplierId { get; set; } public DateOnly? ExpectedAt { get; set; } }
public class PurchaseOrderUpdateInput : PurchaseOrderInput { [Required] public string ConcurrencyStamp { get; set; } = ""; }
public class PurchaseExportInput { public string Format { get; set; } = "xlsx"; }
public class PurchaseStampInput { [Required] public string ConcurrencyStamp { get; set; } = ""; }
public class PurchaseSendInput : PurchaseStampInput { [Required, StringLength(100)] public string Via { get; set; } = ""; }
public class PurchaseReceiveInput : PurchaseStampInput { [Required, StringLength(100)] public string InvoiceNumber { get; set; } = ""; public DateOnly InvoiceDate { get; set; } public List<PurchaseLineInput> Lines { get; set; } = []; }
public class PurchaseProcessInput : PurchaseStampInput { public Guid? SupplierId { get; set; } public Guid? SourceWarehouseId { get; set; } public bool Reject { get; set; } public List<PurchaseLineInput> Lines { get; set; } = []; }
public class SupplierPayInput : PurchaseStampInput { public long Amount { get; set; } [Required, StringLength(200)] public string Reference { get; set; } = ""; [Required, StringLength(100)] public string IdempotencyKey { get; set; } = ""; }
public class SupplierReturnInput : PurchaseStampInput { [Required,StringLength(2000)] public string Reason { get; set; } = ""; public List<PurchaseLineInput> Lines { get; set; } = []; }
public interface IPurchasingAppService : IApplicationService
{
    Task<List<PurchaseLookupDto>> GetWarehousesAsync();
    Task<List<PurchaseLookupDto>> GetSuppliersAsync();
    Task<List<PurchaseLookupDto>> GetItemsAsync();
    Task<List<PurchasePriceDto>> GetPricesAsync();
    Task<List<PurchaseRequestDto>> GetRequestsAsync();
    Task<PurchaseRequestDto> CreateRequestAsync(PurchaseRequestInput input);
    Task<List<PurchaseRequestDto>> GenerateAsync();
    Task<PurchaseRequestDto> SubmitAsync(Guid id, PurchaseStampInput input);
    Task<PurchaseRequestDto> ProcessAsync(Guid id, PurchaseProcessInput input);
    Task<List<PurchaseOrderDto>> GetOrdersAsync();
    Task<PurchaseOrderDto> CreateOrderAsync(PurchaseOrderInput input);
    Task<PurchaseOrderDto> UpdateOrderAsync(Guid id,PurchaseOrderUpdateInput input);
    Task<IRemoteStreamContent> ExportOrderAsync(Guid id,PurchaseExportInput input);
    Task<PurchaseOrderDto> SendAsync(Guid id, PurchaseSendInput input);
    Task<PurchaseOrderDto> CancelAsync(Guid id, PurchaseStampInput input);
    Task<Guid> ReceiveAsync(Guid id, PurchaseReceiveInput input);
    Task<List<SupplierInvoiceDto>> GetInvoicesAsync();
    Task<SupplierInvoiceDto> PayInvoiceAsync(Guid id, SupplierPayInput input);
    Task<List<PurchaseLineDto>> GetReturnLinesAsync(Guid id);
    Task<Guid> ReturnInvoiceAsync(Guid id,SupplierReturnInput input);
}
