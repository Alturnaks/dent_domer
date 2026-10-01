namespace Dental.Purchasing;
public enum PurchaseRequestStatus { Draft, Submitted, Processed, Rejected }
public enum PurchaseRequestSource { Auto, Manual }
public enum PurchaseOrderStatus { Draft, PendingApproval, Sent, PartiallyReceived, Received, Cancelled }
public enum SupplierInvoiceStatus { Unpaid, PartiallyPaid, Paid }
