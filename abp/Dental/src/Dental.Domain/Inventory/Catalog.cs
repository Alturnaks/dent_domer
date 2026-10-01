using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Inventory;

/// <summary>Склад. BranchId = null — центральный склад сети.</summary>
[Audited]
public class Warehouse : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public WarehouseType Type { get; private set; }
    public Guid? ParentWarehouseId { get; set; }
    public string Name { get; private set; } = null!;
    /// <summary>Ответственный — Employee.Id.</summary>
    public Guid? ResponsibleId { get; set; }
    /// <summary>Склад заблокирован для списаний и перемещений на время инвентаризации.</summary>
    public bool IsLocked { get; private set; }
    public Guid? LockedByDocumentId { get; private set; }

    protected Warehouse() { }

    public Warehouse(Guid id, Guid? tenantId, WarehouseType type, Guid? branchId, string name) : base(id)
    {
        TenantId = tenantId;
        if (type != WarehouseType.Central && branchId is null)
        {
            throw new BusinessException(DentalDomainErrorCodes.WarehouseBranchRequired);
        }
        Type = type;
        BranchId = type == WarehouseType.Central ? null : branchId;
        SetName(name);
    }

    public void SetName(string name) =>
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), InventoryConsts.MaxWarehouseNameLength).Trim();

    public void EnsureNotLocked()
    {
        if (IsLocked)
        {
            throw new StockException(DentalDomainErrorCodes.StockWarehouseLocked).WithData("name", Name);
        }
    }

    public void Lock(Guid documentId)
    {
        EnsureNotLocked();
        IsLocked = true;
        LockedByDocumentId = documentId;
    }

    public void Unlock(Guid documentId)
    {
        if (LockedByDocumentId == documentId)
        {
            IsLocked = false;
            LockedByDocumentId = null;
        }
    }
}

/// <summary>Категория номенклатуры (дерево).</summary>
[Audited]
public class ItemCategory : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }

    protected ItemCategory() { }

    public ItemCategory(Guid id, Guid? tenantId, string name, Guid? parentId = null) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        ParentId = parentId;
    }

    public void SetName(string name) =>
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), InventoryConsts.MaxCategoryNameLength).Trim();

    /// <summary>Смена родителя; цикл проверяется по карте parent-ов всех категорий.</summary>
    public void SetParent(Guid? parentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var cur = parentId;
        var guard = 0;
        while (cur is { } c)
        {
            if (c == Id || ++guard > 100)
            {
                throw new BusinessException(DentalDomainErrorCodes.CategoryCycle);
            }
            cur = parents.TryGetValue(c, out var p) ? p : null;
        }
        ParentId = parentId;
    }
}

/// <summary>Номенклатура (материал/товар). Количества — в базовой единице, альтернативные единицы — Units.</summary>
[Audited]
public class Item : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid? CategoryId { get; set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Manufacturer { get; set; }
    public BaseUnit BaseUnit { get; private set; }
    public bool TrackBatches { get; private set; }
    public bool TrackSerials { get; private set; }
    public bool TrackExpiry { get; private set; }
    public bool IsActive { get; set; } = true;
    public string? Barcode { get; set; }
    public List<ItemUnit> Units { get; private set; } = [];

    /// <summary>Приход создаёт партию (номер/серия/срок).</summary>
    public bool UsesBatches => TrackBatches || TrackSerials || TrackExpiry;

    protected Item() { }

    public Item(Guid id, Guid? tenantId, string sku, string name, BaseUnit baseUnit) : base(id)
    {
        TenantId = tenantId;
        SetSku(sku);
        SetName(name);
        BaseUnit = baseUnit;
    }

    public void SetSku(string sku) => Sku = Check.NotNullOrWhiteSpace(sku, nameof(sku), InventoryConsts.MaxSkuLength).Trim();

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), InventoryConsts.MaxNameLength).Trim();

    /// <summary>Базовую единицу нельзя менять, если по товару уже были движения.</summary>
    public void SetBaseUnit(BaseUnit unit, bool hasMovements)
    {
        if (unit == BaseUnit) return;
        if (hasMovements)
        {
            throw new BusinessException(DentalDomainErrorCodes.ItemBaseUnitLocked);
        }
        BaseUnit = unit;
        if (TrackSerials && unit != BaseUnit.Pcs)
        {
            throw new BusinessException(DentalDomainErrorCodes.ItemSerialRequiresPcs);
        }
    }

    /// <summary>Серийный учёт — только для штучных; любой вид учёта включает партионный.</summary>
    public void SetTracking(bool batches, bool serials, bool expiry)
    {
        if (serials && BaseUnit != BaseUnit.Pcs)
        {
            throw new BusinessException(DentalDomainErrorCodes.ItemSerialRequiresPcs);
        }
        TrackBatches = batches || serials || expiry;
        TrackSerials = serials;
        TrackExpiry = expiry;
    }

    /// <summary>Замена списка единиц: существующие (по Id) обновляются, новые добавляются, отсутствующие удаляются.</summary>
    public void SetUnits(IEnumerable<(Guid? Id, string Name, decimal Factor)> units, Func<Guid> newId)
    {
        var list = units.ToList();
        var keep = list.Where(u => u.Id is not null).Select(u => u.Id!.Value).ToHashSet();
        Units.RemoveAll(u => !keep.Contains(u.Id));
        foreach (var u in list)
        {
            var existing = u.Id is { } id ? Units.FirstOrDefault(x => x.Id == id) : null;
            if (existing is null)
            {
                Units.Add(new ItemUnit(newId(), Id, u.Name, u.Factor));
            }
            else
            {
                existing.Set(u.Name, u.Factor);
            }
        }
    }

    public decimal FactorOf(Guid? unitId)
    {
        if (unitId is null) return 1m;
        var unit = Units.FirstOrDefault(u => u.Id == unitId)
                   ?? throw new BusinessException(DentalDomainErrorCodes.ItemUnitNotFound);
        return unit.FactorToBase;
    }

    /// <summary>Перевод количества из единицы ввода в базовую.</summary>
    public decimal ToBase(decimal qty, Guid? unitId) => qty * FactorOf(unitId);
}

/// <summary>Альтернативная единица товара (упаковка и т.п.): 1 единица = FactorToBase базовых.</summary>
public class ItemUnit : Entity<Guid>
{
    public Guid ItemId { get; private set; }
    public string UnitName { get; private set; } = null!;
    public decimal FactorToBase { get; private set; }

    protected ItemUnit() { }

    public ItemUnit(Guid id, Guid itemId, string unitName, decimal factorToBase) : base(id)
    {
        ItemId = itemId;
        Set(unitName, factorToBase);
    }

    public void Set(string unitName, decimal factorToBase)
    {
        UnitName = Check.NotNullOrWhiteSpace(unitName, nameof(unitName), InventoryConsts.MaxUnitNameLength).Trim();
        if (factorToBase <= 0)
        {
            throw new ArgumentException("FactorToBase must be > 0", nameof(factorToBase));
        }
        FactorToBase = factorToBase;
    }
}

/// <summary>Нормы остатка товара на складе (минимум/оптимум, в базовых единицах).</summary>
[Audited]
public class ItemStockLevel : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal MinQty { get; private set; }
    public decimal OptimalQty { get; private set; }

    protected ItemStockLevel() { }

    public ItemStockLevel(Guid id, Guid? tenantId, Guid itemId, Guid warehouseId, decimal minQty, decimal optimalQty) : base(id)
    {
        TenantId = tenantId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Set(minQty, optimalQty);
    }

    public void Set(decimal minQty, decimal optimalQty)
    {
        if (minQty < 0 || optimalQty < minQty)
        {
            throw new BusinessException(DentalDomainErrorCodes.StockLevelInvalid);
        }
        MinQty = minQty;
        OptimalQty = optimalQty;
    }
}

/// <summary>Партия товара (номер, серия, срок годности, себестоимость базовой единицы в тиынах).</summary>
public class Batch : BasicAggregateRoot<Guid>, IMultiTenant, IHasCreationTime
{
    public Guid? TenantId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? BatchNumber { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? ExpiresAt { get; private set; }
    /// <summary>Себестоимость базовой единицы, тиыны (может быть дробной).</summary>
    public decimal UnitCost { get; private set; }
    public Guid? SupplierId { get; private set; }
    public DateTime CreationTime { get; set; }

    protected Batch() { }

    public Batch(Guid id, Guid? tenantId, Guid itemId, string? batchNumber, string? serialNumber, DateOnly? expiresAt, decimal unitCost, Guid? supplierId) : base(id)
    {
        TenantId = tenantId;
        ItemId = itemId;
        BatchNumber = batchNumber;
        SerialNumber = serialNumber;
        ExpiresAt = expiresAt;
        UnitCost = unitCost;
        SupplierId = supplierId;
    }
}

/// <summary>Поставщик.</summary>
[Audited]
public class Supplier : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Bin { get; private set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }

    protected Supplier() { }

    public Supplier(Guid id, Guid? tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), InventoryConsts.MaxNameLength).Trim();

    /// <summary>БИН — 12 цифр (или пусто).</summary>
    public void SetBin(string? bin)
    {
        bin = string.IsNullOrWhiteSpace(bin) ? null : bin.Trim();
        if (bin is not null && (bin.Length != 12 || !bin.All(char.IsDigit)))
        {
            throw new BusinessException(DentalDomainErrorCodes.SupplierBinInvalid);
        }
        Bin = bin;
    }
}

/// <summary>Товар у поставщика: артикул поставщика и последняя цена (тиыны за базовую единицу).</summary>
[Audited]
public class SupplierItem : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? SupplierSku { get; set; }
    public long LastPrice { get; private set; }
    public DateTime? LastPriceAt { get; private set; }

    protected SupplierItem() { }

    public SupplierItem(Guid id, Guid? tenantId, Guid supplierId, Guid itemId) : base(id)
    {
        TenantId = tenantId;
        SupplierId = supplierId;
        ItemId = itemId;
    }

    public void SetPrice(long price, DateTime now)
    {
        if (price != LastPrice || LastPriceAt is null)
        {
            LastPriceAt = now;
        }
        LastPrice = Math.Max(0, price);
    }
}

/// <summary>Причина списания.</summary>
[Audited]
public class WriteoffReason : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public WriteoffReasonType Type { get; set; }

    protected WriteoffReason() { }

    public WriteoffReason(Guid id, Guid? tenantId, string name, WriteoffReasonType type) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        Type = type;
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), InventoryConsts.MaxNameLength).Trim();
}
