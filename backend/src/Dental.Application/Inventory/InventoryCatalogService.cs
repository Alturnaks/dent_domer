using Dental.Application.Common;
using Dental.Domain.Inventory;
using Dental.Domain.Purchasing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Inventory;

public sealed record WarehouseDto(Guid Id, Guid? BranchId, WarehouseType Type, Guid? ParentWarehouseId, string Name, Guid? ResponsibleId, bool Locked);
public sealed record WarehouseRequest(Guid? BranchId, WarehouseType Type, Guid? ParentWarehouseId, string Name, Guid? ResponsibleId);

public sealed record ItemCategoryDto(Guid Id, string Name, Guid? ParentId);
public sealed record ItemCategoryRequest(string Name, Guid? ParentId);

public sealed record ItemUnitDto(Guid Id, string UnitName, decimal FactorToBase);
public sealed record ItemUnitInput(Guid? Id, string UnitName, decimal FactorToBase);
public sealed record ItemDto(
    Guid Id, Guid? CategoryId, string Sku, string Name, string? Manufacturer, BaseUnit BaseUnit, bool TrackBatches, bool TrackSerials,
    bool TrackExpiry, bool IsActive, string? Barcode, IReadOnlyList<ItemUnitDto> Units);
public sealed record ItemRequest(
    Guid? CategoryId, string Sku, string Name, string? Manufacturer, BaseUnit BaseUnit, bool TrackBatches, bool TrackSerials,
    bool TrackExpiry, bool? IsActive, string? Barcode, IReadOnlyList<ItemUnitInput>? Units);

public sealed record StockLevelDto(Guid WarehouseId, string WarehouseName, decimal MinQty, decimal OptimalQty);
public sealed record StockLevelInput(Guid WarehouseId, decimal MinQty, decimal OptimalQty);
public sealed record BulkStockLevelInput(Guid ItemId, Guid WarehouseId, decimal MinQty, decimal OptimalQty);

public sealed record SupplierDto(Guid Id, string Name, string? Bin, string? ContactPerson, string? Phone, string? Email, string? Whatsapp, int PaymentTermsDays, string? Notes);
public sealed record SupplierRequest(string Name, string? Bin, string? ContactPerson, string? Phone, string? Email, string? Whatsapp, int? PaymentTermsDays, string? Notes);
public sealed record SupplierItemDto(Guid ItemId, string ItemName, string ItemSku, string? SupplierSku, long LastPrice, DateTimeOffset? LastPriceAt);
public sealed record SupplierItemInput(Guid ItemId, string? SupplierSku, long LastPrice);

public sealed class ItemRequestValidator : AbstractValidator<ItemRequest>
{
    public ItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(60);
        RuleFor(x => x).Must(x => !x.TrackSerials || x.BaseUnit == BaseUnit.Pcs).WithMessage("Серийный учёт возможен только для штучных товаров").WithName("trackSerials");
        RuleForEach(x => x.Units).ChildRules(u =>
        {
            u.RuleFor(x => x.UnitName).NotEmpty().MaximumLength(40);
            u.RuleFor(x => x.FactorToBase).GreaterThan(0);
        });
    }
}

public sealed class WarehouseRequestValidator : AbstractValidator<WarehouseRequest>
{
    public WarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BranchId).NotNull().When(x => x.Type != WarehouseType.Central).WithMessage("Укажите филиал");
    }
}

public sealed class SupplierRequestValidator : AbstractValidator<SupplierRequest>
{
    public SupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Bin).Matches("^\\d{12}$").When(x => !string.IsNullOrWhiteSpace(x.Bin)).WithMessage("БИН — 12 цифр");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class InventoryCatalogService(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    // Склады: пользователь видит центральные склады и склады своих филиалов.
    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid? branchId, CancellationToken ct)
    {
        var q = db.Warehouses.AsNoTracking().NotDeleted();
        if (branchId is { } b) q = q.Where(w => w.BranchId == b);
        else if (!user.AllBranches) q = q.Where(w => w.BranchId == null || user.BranchIds.Contains(w.BranchId.Value));
        return await q.OrderBy(w => w.Type).ThenBy(w => w.Name).Select(w => ToDto(w)).ToListAsync(ct);
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(WarehouseRequest r, CancellationToken ct)
    {
        if (r.BranchId is { } b) user.EnsureBranchAccess(b);
        var w = new Warehouse { BranchId = r.Type == WarehouseType.Central ? null : r.BranchId, Type = r.Type, ParentWarehouseId = r.ParentWarehouseId, Name = r.Name.Trim(), ResponsibleId = r.ResponsibleId };
        db.Warehouses.Add(w);
        await db.SaveChangesAsync(ct);
        return ToDto(w);
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid id, WarehouseRequest r, CancellationToken ct)
    {
        var w = await db.Warehouses.GetOrThrowAsync(id, "Склад", ct);
        if (w.BranchId is { } b) user.EnsureBranchAccess(b);
        w.Name = r.Name.Trim();
        w.ResponsibleId = r.ResponsibleId;
        w.ParentWarehouseId = r.ParentWarehouseId;
        await db.SaveChangesAsync(ct);
        return ToDto(w);
    }

    /// <summary>Доступ к складу: центральный — всем с правом, филиальный — при доступе к филиалу.</summary>
    public async Task<Warehouse> GetAccessibleWarehouseAsync(Guid id, CancellationToken ct)
    {
        var w = await db.Warehouses.GetOrThrowAsync(id, "Склад", ct);
        if (w.BranchId is { } b) user.EnsureBranchAccess(b);
        return w;
    }

    // Категории номенклатуры
    public async Task<IReadOnlyList<ItemCategoryDto>> ListItemCategoriesAsync(CancellationToken ct) =>
        await db.ItemCategories.AsNoTracking().NotDeleted().OrderBy(c => c.Name).Select(c => new ItemCategoryDto(c.Id, c.Name, c.ParentId)).ToListAsync(ct);

    public async Task<ItemCategoryDto> CreateItemCategoryAsync(ItemCategoryRequest r, CancellationToken ct)
    {
        var c = new ItemCategory { Name = r.Name.Trim(), ParentId = r.ParentId };
        db.ItemCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new(c.Id, c.Name, c.ParentId);
    }

    public async Task<ItemCategoryDto> UpdateItemCategoryAsync(Guid id, ItemCategoryRequest r, CancellationToken ct)
    {
        var c = await db.ItemCategories.GetOrThrowAsync(id, "Категория", ct);
        c.Name = r.Name.Trim();
        c.ParentId = r.ParentId == id ? null : r.ParentId;
        await db.SaveChangesAsync(ct);
        return new(c.Id, c.Name, c.ParentId);
    }

    // Номенклатура
    public async Task<PagedResult<ItemDto>> ListItemsAsync(string? q, Guid? categoryId, bool includeInactive, PageQuery page, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking().Include(i => i.Units).NotDeleted();
        if (!includeInactive) query = query.Where(i => i.IsActive);
        if (categoryId is { } c) query = query.Where(i => i.CategoryId == c);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(i => EF.Functions.ILike(i.Name, term) || EF.Functions.ILike(i.Sku, term) || i.Barcode == q.Trim());
        }
        return await query.OrderBy(i => i.Name).ToPagedAsync(page, ToDto, ct);
    }

    public async Task<ItemDto> GetItemAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Items.AsNoTracking().Include(i => i.Units).NotDeleted().GetOrThrowAsync(id, "Товар", ct));

    public async Task<ItemDto> CreateItemAsync(ItemRequest r, CancellationToken ct)
    {
        var item = new Item();
        Apply(item, r);
        item.Units = (r.Units ?? []).Select(u => new ItemUnit { ItemId = item.Id, UnitName = u.UnitName.Trim(), FactorToBase = u.FactorToBase }).ToList();
        db.Items.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ItemDto> UpdateItemAsync(Guid id, ItemRequest r, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Units).NotDeleted().GetOrThrowAsync(id, "Товар", ct);
        var hasMovements = await db.StockMovements.AnyAsync(m => m.ItemId == id, ct);
        if (hasMovements && item.BaseUnit != r.BaseUnit)
            throw AppException.Conflict(ErrorCodes.ValidationFailed, "Нельзя менять базовую единицу товара, по которому уже были движения");
        Apply(item, r);
        if (r.Units is not null)
        {
            var keep = r.Units.Where(u => u.Id is not null).Select(u => u.Id!.Value).ToHashSet();
            foreach (var removed in item.Units.Where(u => !keep.Contains(u.Id)).ToList()) item.Units.Remove(removed);
            foreach (var u in r.Units)
            {
                var existing = u.Id is { } uid ? item.Units.FirstOrDefault(x => x.Id == uid) : null;
                if (existing is null) item.Units.Add(new ItemUnit { ItemId = item.Id, UnitName = u.UnitName.Trim(), FactorToBase = u.FactorToBase });
                else { existing.UnitName = u.UnitName.Trim(); existing.FactorToBase = u.FactorToBase; }
            }
        }
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    private static void Apply(Item item, ItemRequest r)
    {
        item.CategoryId = r.CategoryId;
        item.Sku = r.Sku.Trim();
        item.Name = r.Name.Trim();
        item.Manufacturer = r.Manufacturer.NullIfEmpty();
        item.BaseUnit = r.BaseUnit;
        item.TrackBatches = r.TrackBatches || r.TrackSerials || r.TrackExpiry;
        item.TrackSerials = r.TrackSerials;
        item.TrackExpiry = r.TrackExpiry;
        if (r.IsActive is not null) item.IsActive = r.IsActive.Value;
        item.Barcode = r.Barcode.NullIfEmpty();
    }

    // Нормы остатков (мин/опт по складам)
    public async Task<IReadOnlyList<StockLevelDto>> GetStockLevelsAsync(Guid itemId, CancellationToken ct) =>
        await (from l in db.ItemStockLevels.AsNoTracking()
               join w in db.Warehouses.AsNoTracking() on l.WarehouseId equals w.Id
               where l.ItemId == itemId
               orderby w.Name
               select new StockLevelDto(w.Id, w.Name, l.MinQty, l.OptimalQty)).ToListAsync(ct);

    public async Task<IReadOnlyList<StockLevelDto>> SetStockLevelsAsync(Guid itemId, IReadOnlyList<StockLevelInput> levels, CancellationToken ct)
    {
        await BulkSetStockLevelsAsync(levels.Select(l => new BulkStockLevelInput(itemId, l.WarehouseId, l.MinQty, l.OptimalQty)).ToList(), ct);
        return await GetStockLevelsAsync(itemId, ct);
    }

    public async Task<int> BulkSetStockLevelsAsync(IReadOnlyList<BulkStockLevelInput> levels, CancellationToken ct)
    {
        if (levels.Any(l => l.MinQty < 0 || l.OptimalQty < l.MinQty))
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Оптимальный остаток должен быть не меньше минимального");
        var itemIds = levels.Select(l => l.ItemId).Distinct().ToList();
        var existing = await db.ItemStockLevels.Where(l => itemIds.Contains(l.ItemId)).ToListAsync(ct);
        foreach (var input in levels)
        {
            var row = existing.FirstOrDefault(e => e.ItemId == input.ItemId && e.WarehouseId == input.WarehouseId);
            if (row is null) db.ItemStockLevels.Add(new ItemStockLevel { ItemId = input.ItemId, WarehouseId = input.WarehouseId, MinQty = input.MinQty, OptimalQty = input.OptimalQty });
            else { row.MinQty = input.MinQty; row.OptimalQty = input.OptimalQty; }
        }
        await db.SaveChangesAsync(ct);
        return levels.Count;
    }

    // Поставщики
    public async Task<PagedResult<SupplierDto>> ListSuppliersAsync(string? q, PageQuery page, CancellationToken ct)
    {
        var query = db.Suppliers.AsNoTracking().NotDeleted();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(s => EF.Functions.ILike(s.Name, $"%{q.Trim()}%") || s.Bin == q.Trim());
        return await query.OrderBy(s => s.Name).ToPagedAsync(page, ToDto, ct);
    }

    public async Task<SupplierDto> GetSupplierAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Suppliers.AsNoTracking().NotDeleted().GetOrThrowAsync(id, "Поставщик", ct));

    public async Task<SupplierDto> CreateSupplierAsync(SupplierRequest r, CancellationToken ct)
    {
        var s = new Supplier();
        Apply(s, r);
        db.Suppliers.Add(s);
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task<SupplierDto> UpdateSupplierAsync(Guid id, SupplierRequest r, CancellationToken ct)
    {
        var s = await db.Suppliers.NotDeleted().GetOrThrowAsync(id, "Поставщик", ct);
        Apply(s, r);
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private static void Apply(Supplier s, SupplierRequest r)
    {
        s.Name = r.Name.Trim();
        s.Bin = r.Bin.NullIfEmpty();
        s.ContactPerson = r.ContactPerson.NullIfEmpty();
        s.Phone = r.Phone.NullIfEmpty();
        s.Email = r.Email.NullIfEmpty();
        s.Whatsapp = r.Whatsapp.NullIfEmpty();
        s.PaymentTermsDays = r.PaymentTermsDays ?? 0;
        s.Notes = r.Notes.NullIfEmpty();
    }

    public async Task<IReadOnlyList<SupplierItemDto>> GetSupplierItemsAsync(Guid supplierId, CancellationToken ct) =>
        await (from si in db.SupplierItems.AsNoTracking()
               join i in db.Items.AsNoTracking() on si.ItemId equals i.Id
               where si.SupplierId == supplierId
               orderby i.Name
               select new SupplierItemDto(i.Id, i.Name, i.Sku, si.SupplierSku, si.LastPrice, si.LastPriceAt)).ToListAsync(ct);

    public async Task<IReadOnlyList<SupplierItemDto>> SetSupplierItemsAsync(Guid supplierId, IReadOnlyList<SupplierItemInput> items, CancellationToken ct)
    {
        _ = await db.Suppliers.GetOrThrowAsync(supplierId, "Поставщик", ct);
        var existing = await db.SupplierItems.Where(s => s.SupplierId == supplierId).ToDictionaryAsync(s => s.ItemId, ct);
        var now = clock.GetUtcNow();
        foreach (var input in items)
        {
            if (existing.TryGetValue(input.ItemId, out var row))
            {
                if (row.LastPrice != input.LastPrice) row.LastPriceAt = now;
                row.LastPrice = input.LastPrice;
                row.SupplierSku = input.SupplierSku.NullIfEmpty();
            }
            else db.SupplierItems.Add(new SupplierItem { SupplierId = supplierId, ItemId = input.ItemId, SupplierSku = input.SupplierSku.NullIfEmpty(), LastPrice = input.LastPrice, LastPriceAt = now });
        }
        await db.SaveChangesAsync(ct);
        return await GetSupplierItemsAsync(supplierId, ct);
    }

    public static WarehouseDto ToDto(Warehouse w) => new(w.Id, w.BranchId, w.Type, w.ParentWarehouseId, w.Name, w.ResponsibleId, w.Locked);

    public static ItemDto ToDto(Item i) => new(i.Id, i.CategoryId, i.Sku, i.Name, i.Manufacturer, i.BaseUnit, i.TrackBatches, i.TrackSerials, i.TrackExpiry,
        i.IsActive, i.Barcode, i.Units.OrderBy(u => u.FactorToBase).Select(u => new ItemUnitDto(u.Id, u.UnitName, u.FactorToBase)).ToList());

    public static SupplierDto ToDto(Supplier s) => new(s.Id, s.Name, s.Bin, s.ContactPerson, s.Phone, s.Email, s.Whatsapp, s.PaymentTermsDays, s.Notes);
}
