using Dental.Domain.Common;

namespace Dental.Domain.Catalog;

[Audited]
public class ServiceCategory : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public int Sort { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class Service : TenantEntity, ISoftDeletable
{
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DurationMin { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class PriceList : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    /// <summary>NULL = для всей сети.</summary>
    public Guid? BranchId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class PriceListItem : TenantEntity
{
    public Guid PriceListId { get; set; }
    public Guid ServiceId { get; set; }
    public long Price { get; set; }
}

[Audited]
public class TechCard : TenantEntity
{
    public Guid ServiceId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public List<TechCardItem> Items { get; set; } = [];
}

public class TechCardItem : TenantEntity
{
    public Guid TechCardId { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>Количество в базовых единицах номенклатуры.</summary>
    public decimal Quantity { get; set; }
}

public static class PriceResolver
{
    /// <summary>
    /// Действующая цена услуги: сначала прайс филиала, затем сетевой; среди подходящих — с самой поздней датой начала.
    /// </summary>
    public static long? Resolve(
        IEnumerable<(PriceList List, IReadOnlyDictionary<Guid, long> Prices)> lists,
        Guid serviceId, Guid branchId, DateOnly onDate)
    {
        var active = lists
            .Where(l => l.List.IsActive && l.List.DeletedAt is null && l.List.ValidFrom <= onDate && l.Prices.ContainsKey(serviceId))
            .ToList();
        var branch = active.Where(l => l.List.BranchId == branchId).OrderByDescending(l => l.List.ValidFrom).FirstOrDefault();
        if (branch.List is not null) return branch.Prices[serviceId];
        var network = active.Where(l => l.List.BranchId is null).OrderByDescending(l => l.List.ValidFrom).FirstOrDefault();
        return network.List is not null ? network.Prices[serviceId] : null;
    }
}
