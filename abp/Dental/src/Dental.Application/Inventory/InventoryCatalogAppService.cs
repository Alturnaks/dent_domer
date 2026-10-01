using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Dental.Inventory;

[Authorize]
public class InventoryCatalogAppService : DentalAppService, IInventoryCatalogAppService
{
    private readonly IRepository<Warehouse, Guid> _warehouses;
    private readonly IRepository<ItemCategory, Guid> _categories;
    private readonly IRepository<Item, Guid> _items;
    private readonly IRepository<ItemStockLevel, Guid> _levels;
    private readonly IRepository<Supplier, Guid> _suppliers;
    private readonly IRepository<SupplierItem, Guid> _supplierItems;
    private readonly IRepository<WriteoffReason, Guid> _reasons;
    private readonly IRepository<StockMovement, Guid> _movements;
    private readonly IRepository<StockBalance, Guid> _balances;
    private readonly IRepository<Branch, Guid> _branches;

    public InventoryCatalogAppService(
        IRepository<Warehouse, Guid> warehouses,
        IRepository<ItemCategory, Guid> categories,
        IRepository<Item, Guid> items,
        IRepository<ItemStockLevel, Guid> levels,
        IRepository<Supplier, Guid> suppliers,
        IRepository<SupplierItem, Guid> supplierItems,
        IRepository<WriteoffReason, Guid> reasons,
        IRepository<StockMovement, Guid> movements,
        IRepository<StockBalance, Guid> balances,
        IRepository<Branch, Guid> branches)
    {
        _warehouses = warehouses;
        _categories = categories;
        _items = items;
        _levels = levels;
        _suppliers = suppliers;
        _supplierItems = supplierItems;
        _reasons = reasons;
        _movements = movements;
        _balances = balances;
        _branches = branches;
    }

    // ================= Склады =================

    /// <summary>Склады: центральные — всем, филиальные — при доступе к филиалу.</summary>
    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<WarehouseDto>> GetWarehousesAsync(GetWarehouseListInput input)
    {
        var branchId = input.BranchId;
        var scope = await BranchScope.GetAsync();
        var list = (await _warehouses.GetListAsync())
            .Where(w => branchId is null ? w.BranchId is null || scope.AllBranches || scope.Contains(w.BranchId.Value) : w.BranchId == branchId)
            .Where(w => w.BranchId is null || scope.AllBranches || scope.Contains(w.BranchId.Value))
            .OrderBy(w => w.Type).ThenBy(w => w.Name).ToList();
        var branchNames = (await _branches.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);
        return new ListResultDto<WarehouseDto>(list.Select(w => ToDto(w, branchNames)).ToList());
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<WarehouseDto> CreateWarehouseAsync(CreateUpdateWarehouseDto input)
    {
        if (input.Type != WarehouseType.Central && input.BranchId is { } b) await BranchScope.EnsureCanAccessAsync(b);
        if (input.Type == WarehouseType.Central && !(await BranchScope.GetAsync()).AllBranches)
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        }
        var w = new Warehouse(GuidGenerator.Create(), CurrentTenant.Id, input.Type, input.BranchId, input.Name)
        {
            ParentWarehouseId = input.ParentWarehouseId,
            ResponsibleId = input.ResponsibleId,
        };
        await _warehouses.InsertAsync(w, autoSave: true);
        return ToDto(w, await BranchNamesAsync());
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid id, CreateUpdateWarehouseDto input)
    {
        var w = await GetAccessibleWarehouseAsync(id);
        w.SetName(input.Name);
        w.ResponsibleId = input.ResponsibleId;
        w.ParentWarehouseId = input.ParentWarehouseId == id ? null : input.ParentWarehouseId;
        await _warehouses.UpdateAsync(w, autoSave: true);
        return ToDto(w, await BranchNamesAsync());
    }

    /// <summary>Мягкое удаление; склад с ненулевыми остатками удалить нельзя.</summary>
    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task DeleteWarehouseAsync(Guid id)
    {
        var w = await GetAccessibleWarehouseAsync(id);
        if (await _balances.AnyAsync(b => b.WarehouseId == id && b.Qty != 0))
        {
            throw new BusinessException(DentalDomainErrorCodes.WarehouseNotEmpty);
        }
        await _warehouses.DeleteAsync(w);
    }

    private async Task<Warehouse> GetAccessibleWarehouseAsync(Guid id)
    {
        var w = await _warehouses.GetAsync(id);
        if (w.BranchId is { } b) await BranchScope.EnsureCanAccessAsync(b);
        return w;
    }

    private async Task<Dictionary<Guid, string>> BranchNamesAsync() => (await _branches.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);

    private static WarehouseDto ToDto(Warehouse w, IReadOnlyDictionary<Guid, string> branches) => new()
    {
        Id = w.Id, BranchId = w.BranchId, BranchName = w.BranchId is { } b ? branches.GetValueOrDefault(b) : null, Type = w.Type,
        ParentWarehouseId = w.ParentWarehouseId, Name = w.Name, ResponsibleId = w.ResponsibleId, IsLocked = w.IsLocked,
    };

    // ================= Категории =================

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<ItemCategoryDto>> GetCategoriesAsync()
    {
        var counts = (await _items.GetListAsync()).Where(i => i.CategoryId != null).GroupBy(i => i.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var list = (await _categories.GetListAsync()).OrderBy(c => c.Name)
            .Select(c => new ItemCategoryDto { Id = c.Id, Name = c.Name, ParentId = c.ParentId, ItemsCount = counts.GetValueOrDefault(c.Id) }).ToList();
        return new ListResultDto<ItemCategoryDto>(list);
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ItemCategoryDto> CreateCategoryAsync(CreateUpdateItemCategoryDto input)
    {
        var c = new ItemCategory(GuidGenerator.Create(), CurrentTenant.Id, input.Name);
        c.SetParent(input.ParentId, await ParentsAsync());
        await _categories.InsertAsync(c, autoSave: true);
        return new ItemCategoryDto { Id = c.Id, Name = c.Name, ParentId = c.ParentId };
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ItemCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateItemCategoryDto input)
    {
        var c = await _categories.GetAsync(id);
        c.SetName(input.Name);
        c.SetParent(input.ParentId, await ParentsAsync());
        await _categories.UpdateAsync(c, autoSave: true);
        return new ItemCategoryDto { Id = c.Id, Name = c.Name, ParentId = c.ParentId };
    }

    /// <summary>Мягкое удаление: дочерние категории поднимаются к родителю, товары остаются без категории.</summary>
    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task DeleteCategoryAsync(Guid id)
    {
        var c = await _categories.GetAsync(id);
        var parents = await ParentsAsync();
        foreach (var child in await _categories.GetListAsync(x => x.ParentId == id))
        {
            parents[child.Id] = c.ParentId;
            child.SetParent(c.ParentId, parents);
            await _categories.UpdateAsync(child);
        }
        foreach (var item in await _items.GetListAsync(i => i.CategoryId == id))
        {
            item.CategoryId = null;
            await _items.UpdateAsync(item);
        }
        await _categories.DeleteAsync(c);
    }

    private async Task<Dictionary<Guid, Guid?>> ParentsAsync() => (await _categories.GetListAsync()).ToDictionary(c => c.Id, c => c.ParentId);

    // ================= Номенклатура =================

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<PagedResultDto<ItemDto>> GetItemsAsync(GetItemListInput input)
    {
        var q = (await _items.WithDetailsAsync(i => i.Units))
            .WhereIf(!input.IncludeInactive, i => i.IsActive)
            .WhereIf(input.CategoryId.HasValue, i => i.CategoryId == input.CategoryId);
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim().ToLower();
            var raw = input.Filter.Trim();
            q = q.Where(i => i.Name.ToLower().Contains(f) || i.Sku.ToLower().Contains(f) || i.Barcode == raw || (i.Manufacturer != null && i.Manufacturer.ToLower().Contains(f)));
        }
        var total = await AsyncExecuter.CountAsync(q);
        var sorted = (input.Sorting ?? "").Trim().ToLowerInvariant() switch
        {
            "sku" or "sku asc" => q.OrderBy(i => i.Sku),
            "sku desc" => q.OrderByDescending(i => i.Sku),
            "name desc" => q.OrderByDescending(i => i.Name),
            _ => q.OrderBy(i => i.Name),
        };
        var items = await AsyncExecuter.ToListAsync(sorted.Skip(input.SkipCount).Take(input.MaxResultCount));
        var categories = await CategoryNamesAsync();
        return new PagedResultDto<ItemDto>(total, items.Select(i => ToDto(i, categories)).ToList());
    }

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ItemDto> GetItemAsync(Guid id) => ToDto(await GetItemWithUnitsAsync(id), await CategoryNamesAsync());

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ItemDto> CreateItemAsync(CreateUpdateItemDto input)
    {
        await EnsureUniqueSkuAsync(input.Sku, null);
        var item = new Item(GuidGenerator.Create(), CurrentTenant.Id, input.Sku, input.Name, input.BaseUnit);
        Apply(item, input, hasMovements: false);
        await _items.InsertAsync(item, autoSave: true);
        return ToDto(item, await CategoryNamesAsync());
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ItemDto> UpdateItemAsync(Guid id, CreateUpdateItemDto input)
    {
        var item = await GetItemWithUnitsAsync(id);
        await EnsureUniqueSkuAsync(input.Sku, id);
        Apply(item, input, await _movements.AnyAsync(m => m.ItemId == id));
        await CurrentUnitOfWork!.SaveChangesAsync();
        return ToDto(item, await CategoryNamesAsync());
    }

    /// <summary>Поиск товаров для выпадающих списков (документы, техкарты, закупки).</summary>
    public async Task<ListResultDto<ItemLookupDto>> GetItemLookupAsync(string? filter, int maxCount = 30)
    {
        var q = (await _items.WithDetailsAsync(i => i.Units)).Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim().ToLower();
            var raw = filter.Trim();
            q = q.Where(i => i.Name.ToLower().Contains(f) || i.Sku.ToLower().Contains(f) || i.Barcode == raw);
        }
        var items = await AsyncExecuter.ToListAsync(q.OrderBy(i => i.Name).Take(Math.Clamp(maxCount, 1, 200)));
        return new ListResultDto<ItemLookupDto>(items.Select(i => new ItemLookupDto
        {
            Id = i.Id, Sku = i.Sku, Name = i.Name, BaseUnit = i.BaseUnit, TrackBatches = i.TrackBatches, TrackSerials = i.TrackSerials, TrackExpiry = i.TrackExpiry,
            Units = i.Units.OrderBy(u => u.FactorToBase).Select(u => new ItemUnitDto { Id = u.Id, UnitName = u.UnitName, FactorToBase = u.FactorToBase }).ToList(),
        }).ToList());
    }

    private void Apply(Item item, CreateUpdateItemDto input, bool hasMovements)
    {
        item.SetSku(input.Sku);
        item.SetName(input.Name);
        item.CategoryId = input.CategoryId;
        item.Manufacturer = string.IsNullOrWhiteSpace(input.Manufacturer) ? null : input.Manufacturer.Trim();
        item.SetBaseUnit(input.BaseUnit, hasMovements);
        item.SetTracking(input.TrackBatches, input.TrackSerials, input.TrackExpiry);
        item.IsActive = input.IsActive;
        item.Barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
        if (input.Units is not null)
        {
            item.SetUnits(input.Units.Select(u => (u.Id, u.UnitName, u.FactorToBase)), GuidGenerator.Create);
        }
    }

    private async Task EnsureUniqueSkuAsync(string sku, Guid? exceptId)
    {
        var s = sku.Trim();
        if (await _items.AnyAsync(i => i.Sku == s && i.Id != exceptId))
        {
            throw new BusinessException(DentalDomainErrorCodes.ItemSkuAlreadyExists).WithData("sku", s);
        }
    }

    private async Task<Item> GetItemWithUnitsAsync(Guid id) =>
        await AsyncExecuter.FirstOrDefaultAsync((await _items.WithDetailsAsync(i => i.Units)).Where(i => i.Id == id))
        ?? throw new EntityNotFoundException(typeof(Item), id);

    private async Task<Dictionary<Guid, string>> CategoryNamesAsync() => (await _categories.GetListAsync()).ToDictionary(c => c.Id, c => c.Name);

    internal static ItemDto ToDto(Item i, IReadOnlyDictionary<Guid, string> categories) => new()
    {
        Id = i.Id, CategoryId = i.CategoryId, CategoryName = i.CategoryId is { } c ? categories.GetValueOrDefault(c) : null, Sku = i.Sku, Name = i.Name,
        Manufacturer = i.Manufacturer, BaseUnit = i.BaseUnit, TrackBatches = i.TrackBatches, TrackSerials = i.TrackSerials, TrackExpiry = i.TrackExpiry,
        IsActive = i.IsActive, Barcode = i.Barcode,
        Units = i.Units.OrderBy(u => u.FactorToBase).Select(u => new ItemUnitDto { Id = u.Id, UnitName = u.UnitName, FactorToBase = u.FactorToBase }).ToList(),
    };

    // ================= Нормы остатков =================

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<StockLevelDto>> GetStockLevelsAsync(Guid itemId)
    {
        var warehouses = (await _warehouses.GetListAsync()).ToDictionary(w => w.Id, w => w.Name);
        var list = (await _levels.GetListAsync(l => l.ItemId == itemId)).Where(l => warehouses.ContainsKey(l.WarehouseId))
            .Select(l => new StockLevelDto { WarehouseId = l.WarehouseId, WarehouseName = warehouses[l.WarehouseId], MinQty = l.MinQty, OptimalQty = l.OptimalQty })
            .OrderBy(l => l.WarehouseName).ToList();
        return new ListResultDto<StockLevelDto>(list);
    }

    /// <summary>Нормы по складам (min = opt = 0 — норма не задана).</summary>
    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ListResultDto<StockLevelDto>> SetStockLevelsAsync(Guid itemId, List<StockLevelInput> levels)
    {
        await _items.GetAsync(itemId, includeDetails: false);
        var existing = (await _levels.GetListAsync(l => l.ItemId == itemId)).ToDictionary(l => l.WarehouseId);
        foreach (var input in levels)
        {
            if (existing.TryGetValue(input.WarehouseId, out var row))
            {
                row.Set(input.MinQty, input.OptimalQty);
            }
            else if (input.MinQty != 0 || input.OptimalQty != 0)
            {
                await _levels.InsertAsync(new ItemStockLevel(GuidGenerator.Create(), CurrentTenant.Id, itemId, input.WarehouseId, input.MinQty, input.OptimalQty));
            }
        }
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetStockLevelsAsync(itemId);
    }

    // ================= Поставщики =================

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<PagedResultDto<SupplierDto>> GetSuppliersAsync(GetSupplierListInput input)
    {
        var q = await _suppliers.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim().ToLower();
            var raw = input.Filter.Trim();
            q = q.Where(s => s.Name.ToLower().Contains(f) || s.Bin == raw || (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(f)));
        }
        var total = await AsyncExecuter.CountAsync(q);
        var list = await AsyncExecuter.ToListAsync(q.OrderBy(s => s.Name).Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<SupplierDto>(total, list.Select(ToDto).ToList());
    }

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<SupplierDto> GetSupplierAsync(Guid id) => ToDto(await _suppliers.GetAsync(id));

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<SupplierDto> CreateSupplierAsync(CreateUpdateSupplierDto input)
    {
        var s = new Supplier(GuidGenerator.Create(), CurrentTenant.Id, input.Name);
        Apply(s, input);
        await _suppliers.InsertAsync(s, autoSave: true);
        return ToDto(s);
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<SupplierDto> UpdateSupplierAsync(Guid id, CreateUpdateSupplierDto input)
    {
        var s = await _suppliers.GetAsync(id);
        Apply(s, input);
        await _suppliers.UpdateAsync(s, autoSave: true);
        return ToDto(s);
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task DeleteSupplierAsync(Guid id) => await _suppliers.DeleteAsync(id);

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<LookupDto>> GetSupplierLookupAsync() =>
        new((await _suppliers.GetListAsync()).OrderBy(s => s.Name).Select(s => new LookupDto { Id = s.Id, Name = s.Name }).ToList());

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<SupplierItemDto>> GetSupplierItemsAsync(Guid supplierId)
    {
        var rows = await _supplierItems.GetListAsync(s => s.SupplierId == supplierId);
        var ids = rows.Select(r => r.ItemId).ToList();
        var items = (await _items.GetListAsync(i => ids.Contains(i.Id))).ToDictionary(i => i.Id);
        return new ListResultDto<SupplierItemDto>(rows.Where(r => items.ContainsKey(r.ItemId)).Select(r => new SupplierItemDto
        {
            ItemId = r.ItemId, ItemName = items[r.ItemId].Name, ItemSku = items[r.ItemId].Sku, SupplierSku = r.SupplierSku, LastPrice = r.LastPrice, LastPriceAt = r.LastPriceAt,
        }).OrderBy(r => r.ItemName).ToList());
    }

    /// <summary>Добавляет/обновляет позиции поставщика (артикул, цена). Позиции, которых нет во входе, не удаляются.</summary>
    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<ListResultDto<SupplierItemDto>> SetSupplierItemsAsync(Guid supplierId, List<SupplierItemInput> items)
    {
        await _suppliers.GetAsync(supplierId);
        var existing = (await _supplierItems.GetListAsync(s => s.SupplierId == supplierId)).ToDictionary(s => s.ItemId);
        foreach (var input in items)
        {
            if (!existing.TryGetValue(input.ItemId, out var row))
            {
                await _items.GetAsync(input.ItemId, includeDetails: false);
                row = new SupplierItem(GuidGenerator.Create(), CurrentTenant.Id, supplierId, input.ItemId);
                await _supplierItems.InsertAsync(row);
                existing[input.ItemId] = row;
            }
            row.SupplierSku = string.IsNullOrWhiteSpace(input.SupplierSku) ? null : input.SupplierSku.Trim();
            row.SetPrice(input.LastPrice, Clock.Now);
        }
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetSupplierItemsAsync(supplierId);
    }

    private static void Apply(Supplier s, CreateUpdateSupplierDto r)
    {
        s.SetName(r.Name);
        s.SetBin(r.Bin);
        s.ContactPerson = Clean(r.ContactPerson);
        s.Phone = Clean(r.Phone);
        s.Email = Clean(r.Email);
        s.Whatsapp = Clean(r.Whatsapp);
        s.PaymentTermsDays = r.PaymentTermsDays;
        s.Notes = Clean(r.Notes);
    }

    private static SupplierDto ToDto(Supplier s) => new()
    {
        Id = s.Id, Name = s.Name, Bin = s.Bin, ContactPerson = s.ContactPerson, Phone = s.Phone, Email = s.Email, Whatsapp = s.Whatsapp,
        PaymentTermsDays = s.PaymentTermsDays, Notes = s.Notes,
    };

    // ================= Причины списания =================

    [Authorize(DentalPermissions.Inventory.View)]
    public async Task<ListResultDto<WriteoffReasonDto>> GetWriteoffReasonsAsync() =>
        new((await _reasons.GetListAsync()).OrderBy(r => r.Type).ThenBy(r => r.Name).Select(ToDto).ToList());

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<WriteoffReasonDto> CreateWriteoffReasonAsync(CreateUpdateWriteoffReasonDto input)
    {
        var r = new WriteoffReason(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Type);
        await _reasons.InsertAsync(r, autoSave: true);
        return ToDto(r);
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task<WriteoffReasonDto> UpdateWriteoffReasonAsync(Guid id, CreateUpdateWriteoffReasonDto input)
    {
        var r = await _reasons.GetAsync(id);
        r.SetName(input.Name);
        r.Type = input.Type;
        await _reasons.UpdateAsync(r, autoSave: true);
        return ToDto(r);
    }

    [Authorize(DentalPermissions.Inventory.ItemsManage)]
    public async Task DeleteWriteoffReasonAsync(Guid id) => await _reasons.DeleteAsync(id);

    private static WriteoffReasonDto ToDto(WriteoffReason r) => new() { Id = r.Id, Name = r.Name, Type = r.Type };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
