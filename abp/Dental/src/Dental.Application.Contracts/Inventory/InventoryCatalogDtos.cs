using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Inventory;

public class WarehouseDto : EntityDto<Guid>
{
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public WarehouseType Type { get; set; }
    public Guid? ParentWarehouseId { get; set; }
    public string Name { get; set; } = "";
    public Guid? ResponsibleId { get; set; }
    public bool IsLocked { get; set; }
}

public class GetWarehouseListInput
{
    /// <summary>Только склады филиала (иначе — все доступные, включая центральные).</summary>
    public Guid? BranchId { get; set; }
}

public class CreateUpdateWarehouseDto
{
    public Guid? BranchId { get; set; }
    public WarehouseType Type { get; set; } = WarehouseType.Branch;
    public Guid? ParentWarehouseId { get; set; }
    [Required, StringLength(InventoryConsts.MaxWarehouseNameLength)]
    public string Name { get; set; } = "";
    public Guid? ResponsibleId { get; set; }
}

public class ItemCategoryDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public int ItemsCount { get; set; }
}

public class CreateUpdateItemCategoryDto
{
    [Required, StringLength(InventoryConsts.MaxCategoryNameLength)]
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
}

public class ItemUnitDto
{
    public Guid Id { get; set; }
    public string UnitName { get; set; } = "";
    public decimal FactorToBase { get; set; }
}

public class ItemUnitInput
{
    public Guid? Id { get; set; }
    [Required, StringLength(InventoryConsts.MaxUnitNameLength)]
    public string UnitName { get; set; } = "";
    [Range(0.0001, 1_000_000)]
    public decimal FactorToBase { get; set; } = 1;
}

public class ItemDto : EntityDto<Guid>
{
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }
    public BaseUnit BaseUnit { get; set; }
    public bool TrackBatches { get; set; }
    public bool TrackSerials { get; set; }
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; }
    public string? Barcode { get; set; }
    public List<ItemUnitDto> Units { get; set; } = [];
}

public class CreateUpdateItemDto
{
    public Guid? CategoryId { get; set; }
    [Required, StringLength(InventoryConsts.MaxSkuLength)]
    public string Sku { get; set; } = "";
    [Required, StringLength(InventoryConsts.MaxNameLength)]
    public string Name { get; set; } = "";
    [StringLength(InventoryConsts.MaxManufacturerLength)]
    public string? Manufacturer { get; set; }
    public BaseUnit BaseUnit { get; set; }
    public bool TrackBatches { get; set; }
    public bool TrackSerials { get; set; }
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(InventoryConsts.MaxBarcodeLength)]
    public string? Barcode { get; set; }
    public List<ItemUnitInput>? Units { get; set; }
}

public class GetItemListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IncludeInactive { get; set; }
}

/// <summary>Товар для выпадающих списков (с единицами и признаками учёта).</summary>
public class ItemLookupDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public BaseUnit BaseUnit { get; set; }
    public bool TrackBatches { get; set; }
    public bool TrackSerials { get; set; }
    public bool TrackExpiry { get; set; }
    public List<ItemUnitDto> Units { get; set; } = [];
}

public class StockLevelDto
{
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal MinQty { get; set; }
    public decimal OptimalQty { get; set; }
}

public class StockLevelInput
{
    public Guid WarehouseId { get; set; }
    [Range(0, 100_000_000)]
    public decimal MinQty { get; set; }
    [Range(0, 100_000_000)]
    public decimal OptimalQty { get; set; }
}

public class SupplierDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public string? Bin { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }
}

public class CreateUpdateSupplierDto
{
    [Required, StringLength(InventoryConsts.MaxNameLength)]
    public string Name { get; set; } = "";
    [StringLength(InventoryConsts.MaxBinLength)]
    public string? Bin { get; set; }
    [StringLength(InventoryConsts.MaxContactLength)]
    public string? ContactPerson { get; set; }
    [StringLength(InventoryConsts.MaxPhoneLength)]
    public string? Phone { get; set; }
    [EmailAddress, StringLength(InventoryConsts.MaxEmailLength)]
    public string? Email { get; set; }
    [StringLength(InventoryConsts.MaxPhoneLength)]
    public string? Whatsapp { get; set; }
    [Range(0, 365)]
    public int PaymentTermsDays { get; set; }
    [StringLength(InventoryConsts.MaxNotesLength)]
    public string? Notes { get; set; }
}

public class GetSupplierListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
}

public class SupplierItemDto
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemSku { get; set; } = "";
    public string? SupplierSku { get; set; }
    /// <summary>Тиыны за базовую единицу.</summary>
    public long LastPrice { get; set; }
    public DateTime? LastPriceAt { get; set; }
}

public class SupplierItemInput
{
    public Guid ItemId { get; set; }
    [StringLength(InventoryConsts.MaxSkuLength)]
    public string? SupplierSku { get; set; }
    [Range(0, long.MaxValue)]
    public long LastPrice { get; set; }
}

public class LookupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class WriteoffReasonDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public WriteoffReasonType Type { get; set; }
}

public class CreateUpdateWriteoffReasonDto
{
    [Required, StringLength(InventoryConsts.MaxNameLength)]
    public string Name { get; set; } = "";
    public WriteoffReasonType Type { get; set; } = WriteoffReasonType.Other;
}

/// <summary>Справочники склада: склады, категории, номенклатура, нормы остатков, поставщики, причины списания.</summary>
public interface IInventoryCatalogAppService : IApplicationService
{
    Task<ListResultDto<WarehouseDto>> GetWarehousesAsync(GetWarehouseListInput input);
    Task<WarehouseDto> CreateWarehouseAsync(CreateUpdateWarehouseDto input);
    Task<WarehouseDto> UpdateWarehouseAsync(Guid id, CreateUpdateWarehouseDto input);
    Task DeleteWarehouseAsync(Guid id);

    Task<ListResultDto<ItemCategoryDto>> GetCategoriesAsync();
    Task<ItemCategoryDto> CreateCategoryAsync(CreateUpdateItemCategoryDto input);
    Task<ItemCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateItemCategoryDto input);
    Task DeleteCategoryAsync(Guid id);

    Task<PagedResultDto<ItemDto>> GetItemsAsync(GetItemListInput input);
    Task<ItemDto> GetItemAsync(Guid id);
    Task<ItemDto> CreateItemAsync(CreateUpdateItemDto input);
    Task<ItemDto> UpdateItemAsync(Guid id, CreateUpdateItemDto input);
    Task<ListResultDto<ItemLookupDto>> GetItemLookupAsync(string? filter, int maxCount = 30);

    Task<ListResultDto<StockLevelDto>> GetStockLevelsAsync(Guid itemId);
    Task<ListResultDto<StockLevelDto>> SetStockLevelsAsync(Guid itemId, List<StockLevelInput> levels);

    Task<PagedResultDto<SupplierDto>> GetSuppliersAsync(GetSupplierListInput input);
    Task<SupplierDto> GetSupplierAsync(Guid id);
    Task<SupplierDto> CreateSupplierAsync(CreateUpdateSupplierDto input);
    Task<SupplierDto> UpdateSupplierAsync(Guid id, CreateUpdateSupplierDto input);
    Task DeleteSupplierAsync(Guid id);
    Task<ListResultDto<LookupDto>> GetSupplierLookupAsync();
    Task<ListResultDto<SupplierItemDto>> GetSupplierItemsAsync(Guid supplierId);
    Task<ListResultDto<SupplierItemDto>> SetSupplierItemsAsync(Guid supplierId, List<SupplierItemInput> items);

    Task<ListResultDto<WriteoffReasonDto>> GetWriteoffReasonsAsync();
    Task<WriteoffReasonDto> CreateWriteoffReasonAsync(CreateUpdateWriteoffReasonDto input);
    Task<WriteoffReasonDto> UpdateWriteoffReasonAsync(Guid id, CreateUpdateWriteoffReasonDto input);
    Task DeleteWriteoffReasonAsync(Guid id);
}
