using Dental.Domain.Cash;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Payroll;
using Dental.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dental.Infrastructure.Persistence.Configurations;

internal sealed class CashRegisterConfiguration : IEntityTypeConfiguration<CashRegister>
{
    public void Configure(EntityTypeBuilder<CashRegister> b)
    {
        b.ToTable("cash_registers");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> b)
    {
        b.ToTable("cash_shifts");
        b.HasIndex(x => new { x.CashRegisterId, x.Status });
        // Одна открытая смена на кассу.
        b.HasIndex(x => x.CashRegisterId).IsUnique().HasFilter("status = 'Open'").HasDatabaseName("ux_cash_shifts_one_open");
        b.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CashRegisterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.Ignore(x => x.SignedAmount);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.HasIndex(x => new { x.BranchId, x.CreatedAt });
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.VisitId);
        b.HasIndex(x => x.CashShiftId);
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses");
        b.Ignore(x => x.PaidFromCash);
        b.HasIndex(x => new { x.BranchId, x.PaidAt });
        b.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b) => b.ToTable("expense_categories");
}

internal sealed class CashOperationConfiguration : IEntityTypeConfiguration<CashOperation>
{
    public void Configure(EntityTypeBuilder<CashOperation> b)
    {
        b.ToTable("cash_operations");
        b.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b) => b.ToTable("warehouses");
}

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b) => b.ToTable("item_categories");
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.Property(x => x.Sku).HasMaxLength(60);
        b.HasIndex(x => new { x.OrganizationId, x.Sku });
        b.HasMany(x => x.Units).WithOne().HasForeignKey(u => u.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ItemCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItemUnitConfiguration : IEntityTypeConfiguration<ItemUnit>
{
    public void Configure(EntityTypeBuilder<ItemUnit> b)
    {
        b.ToTable("item_units");
        b.Property(x => x.FactorToBase).HasPrecision(14, 3);
    }
}

internal sealed class ItemStockLevelConfiguration : IEntityTypeConfiguration<ItemStockLevel>
{
    public void Configure(EntityTypeBuilder<ItemStockLevel> b)
    {
        b.ToTable("item_stock_levels");
        b.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
        b.Property(x => x.MinQty).HasPrecision(14, 3);
        b.Property(x => x.OptimalQty).HasPrecision(14, 3);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.ToTable("batches");
        b.HasIndex(x => new { x.ItemId, x.ExpiresAt });
        b.HasIndex(x => x.SerialNumber);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> b)
    {
        b.ToTable("stock_documents");
        b.Property(x => x.Number).HasMaxLength(40);
        b.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.Type, x.Status });
        b.HasIndex(x => x.VisitId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StockDocumentLineConfiguration : IEntityTypeConfiguration<StockDocumentLine>
{
    public void Configure(EntityTypeBuilder<StockDocumentLine> b)
    {
        b.ToTable("stock_document_lines");
        b.Property(x => x.Qty).HasPrecision(14, 3);
        b.Property(x => x.QtyInput).HasPrecision(14, 3);
        b.Property(x => x.ExpectedQty).HasPrecision(14, 3);
        b.Property(x => x.ActualQty).HasPrecision(14, 3);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements");
        b.Property(x => x.Qty).HasPrecision(14, 3);
        b.HasIndex(x => new { x.WarehouseId, x.ItemId, x.MovedAt });
        b.HasIndex(x => new { x.ItemId, x.MovedAt });
        b.HasIndex(x => x.DocumentId);
        b.HasOne<StockDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("stock_balances");
        b.Property(x => x.Qty).HasPrecision(14, 3);
        b.HasIndex(x => new { x.WarehouseId, x.ItemId, x.BatchId }).IsUnique().AreNullsDistinct(false);
        b.HasIndex(x => x.ItemId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WriteoffReasonConfiguration : IEntityTypeConfiguration<WriteoffReason>
{
    public void Configure(EntityTypeBuilder<WriteoffReason> b) => b.ToTable("writeoff_reasons");
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.Property(x => x.Bin).HasMaxLength(12);
    }
}

internal sealed class SupplierItemConfiguration : IEntityTypeConfiguration<SupplierItem>
{
    public void Configure(EntityTypeBuilder<SupplierItem> b)
    {
        b.ToTable("supplier_items");
        b.HasIndex(x => new { x.SupplierId, x.ItemId }).IsUnique();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> b)
    {
        b.ToTable("purchase_requests");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PurchaseRequestLineConfiguration : IEntityTypeConfiguration<PurchaseRequestLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestLine> b)
    {
        b.ToTable("purchase_request_lines");
        b.Property(x => x.Qty).HasPrecision(14, 3);
        b.Property(x => x.CurrentQty).HasPrecision(14, 3);
        b.Property(x => x.MinQty).HasPrecision(14, 3);
        b.Property(x => x.OptimalQty).HasPrecision(14, 3);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders");
        b.Property(x => x.Number).HasMaxLength(40);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines");
        b.Property(x => x.Qty).HasPrecision(14, 3);
        b.Property(x => x.ReceivedQty).HasPrecision(14, 3);
    }
}

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> b)
    {
        b.ToTable("supplier_invoices");
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayrollSchemeConfiguration : IEntityTypeConfiguration<PayrollScheme>
{
    public void Configure(EntityTypeBuilder<PayrollScheme> b)
    {
        b.ToTable("payroll_schemes");
        b.Property(x => x.Percent).HasPrecision(5, 2);
        b.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> b) => b.ToTable("payroll_periods");
}

internal sealed class PayrollEntryConfiguration : IEntityTypeConfiguration<PayrollEntry>
{
    public void Configure(EntityTypeBuilder<PayrollEntry> b)
    {
        b.ToTable("payroll_entries");
        b.HasIndex(x => new { x.PeriodId, x.MembershipId }).IsUnique();
        b.HasOne<PayrollPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
    }
}
