using Dental.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

/// <summary>Маппинг модуля «Склад». Таблицы App{Имя}s.</summary>
public static class InventoryDbContextModelCreatingExtensions
{
    private const string P = DentalConsts.DbTablePrefix;

    private static PropertyBuilder<decimal> Qty(this PropertyBuilder<decimal> p) => p.HasPrecision(InventoryConsts.QtyPrecision, InventoryConsts.QtyScale);
    private static PropertyBuilder<decimal?> Qty(this PropertyBuilder<decimal?> p) => p.HasPrecision(InventoryConsts.QtyPrecision, InventoryConsts.QtyScale);

    public static void ConfigureInventory(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
        var schema = DentalConsts.DbSchema;

        builder.Entity<Warehouse>(b =>
        {
            b.ToTable(P + "Warehouses", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(InventoryConsts.MaxWarehouseNameLength);
            b.HasIndex(x => new { x.TenantId, x.BranchId });
        });

        builder.Entity<ItemCategory>(b =>
        {
            b.ToTable(P + "ItemCategories", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(InventoryConsts.MaxCategoryNameLength);
            b.HasIndex(x => new { x.TenantId, x.ParentId });
        });

        builder.Entity<Item>(b =>
        {
            b.ToTable(P + "Items", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Sku).IsRequired().HasMaxLength(InventoryConsts.MaxSkuLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(InventoryConsts.MaxNameLength);
            b.Property(x => x.Manufacturer).HasMaxLength(InventoryConsts.MaxManufacturerLength);
            b.Property(x => x.Barcode).HasMaxLength(InventoryConsts.MaxBarcodeLength);
            b.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.ItemId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Sku });
            b.HasIndex(x => new { x.TenantId, x.CategoryId });
            b.HasIndex(x => new { x.TenantId, x.Barcode });
        });

        builder.Entity<ItemUnit>(b =>
        {
            b.ToTable(P + "ItemUnits", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UnitName).IsRequired().HasMaxLength(InventoryConsts.MaxUnitNameLength);
            b.Property(x => x.FactorToBase).Qty();
        });

        builder.Entity<ItemStockLevel>(b =>
        {
            b.ToTable(P + "ItemStockLevels", schema);
            b.ConfigureByConvention();
            b.Property(x => x.MinQty).Qty();
            b.Property(x => x.OptimalQty).Qty();
            b.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.WarehouseId });
        });

        builder.Entity<Batch>(b =>
        {
            b.ToTable(P + "Batches", schema);
            b.ConfigureByConvention();
            b.Property(x => x.BatchNumber).HasMaxLength(InventoryConsts.MaxBatchNumberLength);
            b.Property(x => x.SerialNumber).HasMaxLength(InventoryConsts.MaxSerialNumberLength);
            b.Property(x => x.UnitCost).Qty();
            b.HasIndex(x => new { x.TenantId, x.ItemId });
            b.HasIndex(x => new { x.TenantId, x.ExpiresAt });
        });

        builder.Entity<Supplier>(b =>
        {
            b.ToTable(P + "Suppliers", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(InventoryConsts.MaxNameLength);
            b.Property(x => x.Bin).HasMaxLength(InventoryConsts.MaxBinLength);
            b.Property(x => x.ContactPerson).HasMaxLength(InventoryConsts.MaxContactLength);
            b.Property(x => x.Phone).HasMaxLength(InventoryConsts.MaxPhoneLength);
            b.Property(x => x.Whatsapp).HasMaxLength(InventoryConsts.MaxPhoneLength);
            b.Property(x => x.Email).HasMaxLength(InventoryConsts.MaxEmailLength);
            b.Property(x => x.Notes).HasMaxLength(InventoryConsts.MaxNotesLength);
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        builder.Entity<SupplierItem>(b =>
        {
            b.ToTable(P + "SupplierItems", schema);
            b.ConfigureByConvention();
            b.Property(x => x.SupplierSku).HasMaxLength(InventoryConsts.MaxSkuLength);
            b.HasIndex(x => new { x.SupplierId, x.ItemId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.ItemId });
        });

        builder.Entity<WriteoffReason>(b =>
        {
            b.ToTable(P + "WriteoffReasons", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(InventoryConsts.MaxNameLength);
        });

        builder.Entity<StockDocument>(b =>
        {
            b.ToTable(P + "StockDocuments", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Number).IsRequired().HasMaxLength(InventoryConsts.MaxDocumentNumberLength);
            b.Property(x => x.InvoiceNumber).HasMaxLength(InventoryConsts.MaxInvoiceNumberLength);
            b.Property(x => x.Comment).HasMaxLength(InventoryConsts.MaxCommentLength);
            b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Number });
            b.HasIndex(x => new { x.TenantId, x.Type, x.Status });
            b.HasIndex(x => new { x.TenantId, x.CreationTime });
            b.HasIndex(x => x.VisitId);
            b.HasIndex(x => x.PurchaseOrderId);
        });

        builder.Entity<StockDocumentLine>(b =>
        {
            b.ToTable(P + "StockDocumentLines", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Qty).Qty();
            b.Property(x => x.QtyInput).Qty();
            b.Property(x => x.UnitCost).Qty();
            b.Property(x => x.ExpectedQty).Qty();
            b.Property(x => x.ActualQty).Qty();
            b.Property(x => x.BatchNumber).HasMaxLength(InventoryConsts.MaxBatchNumberLength);
            b.Property(x => x.SerialNumber).HasMaxLength(InventoryConsts.MaxSerialNumberLength);
        });

        builder.Entity<StockMovement>(b =>
        {
            b.ToTable(P + "StockMovements", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Qty).Qty();
            b.Property(x => x.UnitCost).Qty();
            b.HasIndex(x => x.DocumentId);
            b.HasIndex(x => new { x.TenantId, x.ItemId, x.MovedAt });
            b.HasIndex(x => new { x.TenantId, x.WarehouseId, x.MovedAt });
        });

        builder.Entity<StockBalance>(b =>
        {
            b.ToTable(P + "StockBalances", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Qty).Qty();
            b.Property(x => x.AvgCost).Qty();
            // Одна строка на (склад, товар, партия); партия NULL — тоже одна строка (PG 15+).
            b.HasIndex(x => new { x.WarehouseId, x.ItemId, x.BatchId }).IsUnique().AreNullsDistinct(false);
            b.HasIndex(x => new { x.TenantId, x.ItemId });
        });

        builder.Entity<DocumentCounter>(b =>
        {
            b.ToTable(P + "DocumentCounters", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Key).IsRequired().HasMaxLength(InventoryConsts.MaxCounterKeyLength);
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique().AreNullsDistinct(false);
        });
    }
}
