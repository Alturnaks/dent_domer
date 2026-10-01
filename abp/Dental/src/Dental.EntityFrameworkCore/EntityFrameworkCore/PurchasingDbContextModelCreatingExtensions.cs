using System;
using Dental.Purchasing;
using Dental.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;
namespace Dental.EntityFrameworkCore;
public static class PurchasingDbContextModelCreatingExtensions
{
    private static void Root<T>(EntityTypeBuilder<T> e, string name) where T : FinanceEntity { e.HasBaseType((Type?)null); e.ToTable("App"+name); e.ConfigureByConvention(); }
    public static void ConfigurePurchasing(this ModelBuilder b)
    {
        b.Entity<PurchaseRequest>(e => { Root(e,"PurchaseRequests"); e.Property(x => x.Comment).HasMaxLength(2000); e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new {x.TenantId,x.WarehouseId,x.Status}); });
        b.Entity<PurchaseRequestLine>(e => { Root(e,"PurchaseRequestLines"); e.Property(x => x.Qty).HasPrecision(18,6); e.Property(x => x.ProcessedQty).HasPrecision(18,6); e.Property(x => x.CurrentQty).HasPrecision(18,6); e.Property(x => x.MinQty).HasPrecision(18,6); e.Property(x => x.OptimalQty).HasPrecision(18,6); });
        b.Entity<PurchaseOrder>(e => { Root(e,"PurchaseOrders"); e.Property(x => x.Comment).HasMaxLength(2000); e.Property(x => x.Number).HasMaxLength(100); e.Property(x => x.SentVia).HasMaxLength(100); e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new {x.TenantId,x.Number}).IsUnique(); });
        b.Entity<PurchaseOrderLine>(e => { Root(e,"PurchaseOrderLines"); e.Property(x => x.Qty).HasPrecision(18,6); e.Property(x => x.ReceivedQty).HasPrecision(18,6); });
        b.Entity<SupplierInvoice>(e => { Root(e,"SupplierInvoices"); e.Property(x => x.Number).HasMaxLength(100); e.HasIndex(x => new {x.TenantId,x.StockDocumentId}).IsUnique(); });
        b.Entity<SupplierInvoicePayment>(e => { Root(e,"SupplierInvoicePayments"); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.Reference).HasMaxLength(200); e.HasIndex(x => new {x.TenantId,x.IdempotencyKey}).IsUnique(); e.HasOne<SupplierInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict); });
    }
}
