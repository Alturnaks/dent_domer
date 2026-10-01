using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Catalog;

/// <summary>Категория услуг (дерево через ParentId).</summary>
[Audited]
public class ServiceCategory : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public int Sort { get; set; }

    protected ServiceCategory() { }

    public ServiceCategory(Guid id, Guid? tenantId, string name, Guid? parentId = null, int sort = 0) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        ParentId = parentId;
        Sort = sort;
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CatalogConsts.MaxCategoryNameLength).Trim();

    /// <summary>Родитель не может быть самой категорией или её потомком (descendants — потомки этой категории).</summary>
    public void SetParent(Guid? parentId, IReadOnlyCollection<Guid> descendants)
    {
        if (parentId is { } p && (p == Id || descendants.Contains(p)))
        {
            throw new BusinessException(DentalDomainErrorCodes.CategoryParentInvalid);
        }
        ParentId = parentId;
    }
}

/// <summary>Услуга клиники. Цена — не здесь, а в прайс-листах (см. IPriceResolver).</summary>
[Audited]
public class ClinicService : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid CategoryId { get; set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int DurationMin { get; private set; } = 30;
    public bool IsActive { get; set; } = true;

    protected ClinicService() { }

    public ClinicService(Guid id, Guid? tenantId, Guid categoryId, string code, string name, int durationMin) : base(id)
    {
        TenantId = tenantId;
        CategoryId = categoryId;
        SetCode(code);
        SetName(name);
        SetDuration(durationMin);
    }

    public void SetCode(string code) => Code = Check.NotNullOrWhiteSpace(code, nameof(code), CatalogConsts.MaxServiceCodeLength).Trim();

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CatalogConsts.MaxServiceNameLength).Trim();

    public void SetDuration(int minutes) =>
        DurationMin = Check.Range(minutes, nameof(minutes), CatalogConsts.MinDurationMin, CatalogConsts.MaxDurationMin);
}

/// <summary>Прайс-лист: сетевой (BranchId = null) или филиала; действует с ValidFrom.</summary>
[Audited]
public class PriceList : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? BranchId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public List<PriceListItem> Items { get; private set; } = [];

    protected PriceList() { }

    public PriceList(Guid id, Guid? tenantId, string name, Guid? branchId, DateOnly validFrom) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        BranchId = branchId;
        ValidFrom = validFrom;
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CatalogConsts.MaxPriceListNameLength).Trim();

    /// <summary>Установить цену услуги (upsert). Цена в тиынах, не отрицательная.</summary>
    public void SetPrice(Guid itemId, Guid serviceId, long price)
    {
        if (price < 0)
        {
            throw new BusinessException(DentalDomainErrorCodes.PriceInvalid);
        }
        var row = Items.FirstOrDefault(i => i.ServiceId == serviceId);
        if (row is null)
        {
            Items.Add(new PriceListItem(itemId, Id, serviceId, price));
        }
        else
        {
            row.Price = price;
        }
    }

    public void RemovePrice(Guid serviceId) => Items.RemoveAll(i => i.ServiceId == serviceId);

    /// <summary>Изменить цены выбранных услуг на процент с округлением до roundTo тиынов. Возвращает число изменённых строк.</summary>
    public int BulkChange(Func<Guid, bool> serviceFilter, decimal percent, long roundTo)
    {
        if (percent < (decimal)CatalogConsts.MinBulkPercent || percent > (decimal)CatalogConsts.MaxBulkPercent)
        {
            throw new BusinessException(DentalDomainErrorCodes.PriceInvalid);
        }
        var round = Math.Max(1, roundTo);
        var count = 0;
        foreach (var row in Items.Where(i => serviceFilter(i.ServiceId)))
        {
            row.Price = ApplyPercent(row.Price, percent, round);
            count++;
        }
        return count;
    }

    public static long ApplyPercent(long price, decimal percent, long round)
    {
        var raw = price * (1 + percent / 100m);
        return (long)(Math.Round(raw / round, MidpointRounding.AwayFromZero) * round);
    }
}

public class PriceListItem : Entity<Guid>
{
    public Guid PriceListId { get; private set; }
    public Guid ServiceId { get; private set; }
    public long Price { get; internal set; }

    protected PriceListItem() { }

    internal PriceListItem(Guid id, Guid priceListId, Guid serviceId, long price) : base(id)
    {
        PriceListId = priceListId;
        ServiceId = serviceId;
        Price = price;
    }
}

/// <summary>
/// Техкарта услуги — нормы расхода материалов. Версионная: новая версия деактивирует предыдущие
/// (визиты ссылаются на конкретную версию). ItemId — номенклатура склада (модуль Inventory).
/// </summary>
[Audited]
public class TechCard : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ServiceId { get; private set; }
    public int Version { get; private set; }
    public bool IsActive { get; private set; } = true;
    public List<TechCardItem> Items { get; private set; } = [];

    protected TechCard() { }

    public TechCard(Guid id, Guid? tenantId, Guid serviceId, int version) : base(id)
    {
        TenantId = tenantId;
        ServiceId = serviceId;
        Version = version;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Добавить материал; одинаковые ItemId суммируются. Количество в базовых единицах, > 0.</summary>
    public void AddItem(Guid id, Guid itemId, decimal quantity)
    {
        if (itemId == Guid.Empty || quantity <= 0)
        {
            throw new BusinessException(DentalDomainErrorCodes.TechCardItemInvalid);
        }
        var existing = Items.FirstOrDefault(i => i.ItemId == itemId);
        if (existing is null)
        {
            Items.Add(new TechCardItem(id, Id, itemId, quantity));
        }
        else
        {
            existing.Quantity += quantity;
        }
    }
}

public class TechCardItem : Entity<Guid>
{
    public Guid TechCardId { get; private set; }
    public Guid ItemId { get; private set; }
    /// <summary>Количество в базовых единицах номенклатуры.</summary>
    public decimal Quantity { get; internal set; }

    protected TechCardItem() { }

    internal TechCardItem(Guid id, Guid techCardId, Guid itemId, decimal quantity) : base(id)
    {
        TechCardId = techCardId;
        ItemId = itemId;
        Quantity = quantity;
    }
}
