using Dental.Application.Common;
using Dental.Domain.Catalog;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Catalog;

public sealed record ServiceCategoryDto(Guid Id, string Name, Guid? ParentId, int Sort);
public sealed record ServiceCategoryRequest(string Name, Guid? ParentId, int? Sort);

public sealed record ServiceDto(Guid Id, Guid CategoryId, string Code, string Name, int DurationMin, bool IsActive, long? Price);
public sealed record ServiceRequest(Guid CategoryId, string Code, string Name, int DurationMin, bool? IsActive);

public sealed record PriceListDto(Guid Id, string Name, Guid? BranchId, DateOnly ValidFrom, bool IsActive, int ItemsCount);
public sealed record PriceListRequest(string Name, Guid? BranchId, DateOnly ValidFrom, bool? IsActive, Guid? CopyFromId);
public sealed record PriceListItemDto(Guid ServiceId, string ServiceCode, string ServiceName, Guid CategoryId, long Price);
public sealed record PriceListItemInput(Guid ServiceId, long Price);
public sealed record BulkPriceUpdateRequest(Guid? CategoryId, decimal Percent, long? RoundTo);
public sealed record BulkPriceUpdateResult(int Updated);

public sealed record TechCardItemDto(Guid ItemId, string ItemName, string BaseUnit, decimal Quantity);
public sealed record TechCardDto(Guid Id, Guid ServiceId, int Version, bool IsActive, DateTimeOffset CreatedAt, IReadOnlyList<TechCardItemDto> Items);
public sealed record TechCardItemInput(Guid ItemId, decimal Quantity);
public sealed record TechCardRequest(IReadOnlyList<TechCardItemInput> Items);

public sealed class ServiceRequestValidator : AbstractValidator<ServiceRequest>
{
    public ServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.DurationMin).InclusiveBetween(5, 600);
    }
}

public sealed class ServiceCategoryRequestValidator : AbstractValidator<ServiceCategoryRequest>
{
    public ServiceCategoryRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class PriceListRequestValidator : AbstractValidator<PriceListRequest>
{
    public PriceListRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class BulkPriceUpdateRequestValidator : AbstractValidator<BulkPriceUpdateRequest>
{
    public BulkPriceUpdateRequestValidator() => RuleFor(x => x.Percent).InclusiveBetween(-90, 500);
}

public sealed class TechCardRequestValidator : AbstractValidator<TechCardRequest>
{
    public TechCardRequestValidator()
    {
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ItemId).NotEmpty();
            i.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class CatalogService(IAppDbContext db, IAuditService audit, TimeProvider clock)
{
    // Категории
    public async Task<IReadOnlyList<ServiceCategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await db.ServiceCategories.AsNoTracking().NotDeleted().OrderBy(c => c.Sort).ThenBy(c => c.Name)
            .Select(c => new ServiceCategoryDto(c.Id, c.Name, c.ParentId, c.Sort)).ToListAsync(ct);

    public async Task<ServiceCategoryDto> CreateCategoryAsync(ServiceCategoryRequest r, CancellationToken ct)
    {
        var c = new ServiceCategory { Name = r.Name.Trim(), ParentId = r.ParentId, Sort = r.Sort ?? 0 };
        db.ServiceCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new(c.Id, c.Name, c.ParentId, c.Sort);
    }

    public async Task<ServiceCategoryDto> UpdateCategoryAsync(Guid id, ServiceCategoryRequest r, CancellationToken ct)
    {
        var c = await db.ServiceCategories.GetOrThrowAsync(id, "Категория", ct);
        if (r.ParentId == id) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Категория не может быть родителем самой себя");
        c.Name = r.Name.Trim();
        c.ParentId = r.ParentId;
        if (r.Sort is not null) c.Sort = r.Sort.Value;
        await db.SaveChangesAsync(ct);
        return new(c.Id, c.Name, c.ParentId, c.Sort);
    }

    // Услуги (цена — из действующего прайса филиала/сети)
    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(Guid? categoryId, string? q, bool includeInactive, Guid? branchId, CancellationToken ct)
    {
        var query = db.Services.AsNoTracking().NotDeleted();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        if (categoryId is { } c) query = query.Where(s => s.CategoryId == c);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(s => EF.Functions.ILike(s.Name, $"%{q.Trim()}%") || EF.Functions.ILike(s.Code, $"%{q.Trim()}%"));
        var services = await query.OrderBy(s => s.Code).ToListAsync(ct);
        var prices = await ResolvePricesAsync(branchId, services.Select(s => s.Id).ToList(), ct);
        return services.Select(s => new ServiceDto(s.Id, s.CategoryId, s.Code, s.Name, s.DurationMin, s.IsActive, prices.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<ServiceDto> CreateServiceAsync(ServiceRequest r, CancellationToken ct)
    {
        _ = await db.ServiceCategories.GetOrThrowAsync(r.CategoryId, "Категория", ct);
        var s = new Service { CategoryId = r.CategoryId, Code = r.Code.Trim(), Name = r.Name.Trim(), DurationMin = r.DurationMin, IsActive = r.IsActive ?? true };
        db.Services.Add(s);
        await db.SaveChangesAsync(ct);
        return new(s.Id, s.CategoryId, s.Code, s.Name, s.DurationMin, s.IsActive, null);
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid id, ServiceRequest r, CancellationToken ct)
    {
        var s = await db.Services.GetOrThrowAsync(id, "Услуга", ct);
        s.CategoryId = r.CategoryId;
        s.Code = r.Code.Trim();
        s.Name = r.Name.Trim();
        s.DurationMin = r.DurationMin;
        if (r.IsActive is not null) s.IsActive = r.IsActive.Value;
        await db.SaveChangesAsync(ct);
        var price = (await ResolvePricesAsync(null, [s.Id], ct)).GetValueOrDefault(s.Id);
        return new(s.Id, s.CategoryId, s.Code, s.Name, s.DurationMin, s.IsActive, price);
    }

    /// <summary>Цены услуг на дату: прайс филиала, иначе сетевой (последний по дате начала).</summary>
    public async Task<Dictionary<Guid, long>> ResolvePricesAsync(Guid? branchId, IReadOnlyCollection<Guid> serviceIds, CancellationToken ct, DateOnly? onDate = null)
    {
        var date = onDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var lists = await db.PriceLists.AsNoTracking().NotDeleted()
            .Where(l => l.IsActive && l.ValidFrom <= date && (l.BranchId == null || l.BranchId == branchId))
            .ToListAsync(ct);
        if (lists.Count == 0) return [];
        var listIds = lists.Select(l => l.Id).ToList();
        var items = await db.PriceListItems.AsNoTracking()
            .Where(i => listIds.Contains(i.PriceListId) && serviceIds.Contains(i.ServiceId))
            .ToListAsync(ct);
        var byList = items.GroupBy(i => i.PriceListId).ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, long>)g.ToDictionary(i => i.ServiceId, i => i.Price));
        var input = lists.Select(l => (l, byList.GetValueOrDefault(l.Id) ?? new Dictionary<Guid, long>())).ToList();
        var result = new Dictionary<Guid, long>();
        foreach (var sid in serviceIds)
        {
            var price = branchId is { } b
                ? PriceResolver.Resolve(input, sid, b, date)
                : PriceResolver.Resolve(input.Where(x => x.l.BranchId == null), sid, Guid.Empty, date);
            if (price is not null) result[sid] = price.Value;
        }
        return result;
    }

    // Прайс-листы
    public async Task<IReadOnlyList<PriceListDto>> ListPriceListsAsync(CancellationToken ct)
    {
        var lists = await db.PriceLists.AsNoTracking().NotDeleted().OrderBy(l => l.BranchId).ThenByDescending(l => l.ValidFrom).ToListAsync(ct);
        var counts = await db.PriceListItems.AsNoTracking().GroupBy(i => i.PriceListId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        return lists.Select(l => new PriceListDto(l.Id, l.Name, l.BranchId, l.ValidFrom, l.IsActive, counts.GetValueOrDefault(l.Id))).ToList();
    }

    public async Task<PriceListDto> CreatePriceListAsync(PriceListRequest r, CancellationToken ct)
    {
        var l = new PriceList { Name = r.Name.Trim(), BranchId = r.BranchId, ValidFrom = r.ValidFrom, IsActive = r.IsActive ?? true };
        db.PriceLists.Add(l);
        var count = 0;
        if (r.CopyFromId is { } src)
        {
            var srcItems = await db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == src).ToListAsync(ct);
            db.PriceListItems.AddRange(srcItems.Select(i => new PriceListItem { PriceListId = l.Id, ServiceId = i.ServiceId, Price = i.Price }));
            count = srcItems.Count;
        }
        await db.SaveChangesAsync(ct);
        return new(l.Id, l.Name, l.BranchId, l.ValidFrom, l.IsActive, count);
    }

    public async Task<PriceListDto> UpdatePriceListAsync(Guid id, PriceListRequest r, CancellationToken ct)
    {
        var l = await db.PriceLists.GetOrThrowAsync(id, "Прайс-лист", ct);
        l.Name = r.Name.Trim();
        l.BranchId = r.BranchId;
        l.ValidFrom = r.ValidFrom;
        if (r.IsActive is not null) l.IsActive = r.IsActive.Value;
        await db.SaveChangesAsync(ct);
        var count = await db.PriceListItems.CountAsync(i => i.PriceListId == id, ct);
        return new(l.Id, l.Name, l.BranchId, l.ValidFrom, l.IsActive, count);
    }

    public async Task<IReadOnlyList<PriceListItemDto>> ListPriceItemsAsync(Guid priceListId, CancellationToken ct) =>
        await (from i in db.PriceListItems.AsNoTracking()
               join s in db.Services.AsNoTracking() on i.ServiceId equals s.Id
               where i.PriceListId == priceListId
               orderby s.Code
               select new PriceListItemDto(s.Id, s.Code, s.Name, s.CategoryId, i.Price)).ToListAsync(ct);

    /// <summary>Установить цены (upsert). Изменения цен попадают в журнал аудита (история цен).</summary>
    public async Task<IReadOnlyList<PriceListItemDto>> SetPriceItemsAsync(Guid priceListId, IReadOnlyList<PriceListItemInput> items, CancellationToken ct)
    {
        _ = await db.PriceLists.GetOrThrowAsync(priceListId, "Прайс-лист", ct);
        if (items.Any(i => i.Price < 0)) throw AppException.BadRequest(ErrorCodes.AmountInvalid, "Цена не может быть отрицательной");
        var existing = await db.PriceListItems.Where(i => i.PriceListId == priceListId).ToDictionaryAsync(i => i.ServiceId, ct);
        foreach (var input in items)
        {
            if (existing.TryGetValue(input.ServiceId, out var row)) row.Price = input.Price;
            else db.PriceListItems.Add(new PriceListItem { PriceListId = priceListId, ServiceId = input.ServiceId, Price = input.Price });
        }
        await db.SaveChangesAsync(ct);
        return await ListPriceItemsAsync(priceListId, ct);
    }

    /// <summary>Массовое изменение цен на процент (с округлением, по умолчанию до 100 ₸).</summary>
    public async Task<BulkPriceUpdateResult> BulkUpdateAsync(Guid priceListId, BulkPriceUpdateRequest r, CancellationToken ct)
    {
        _ = await db.PriceLists.GetOrThrowAsync(priceListId, "Прайс-лист", ct);
        var q = db.PriceListItems.Where(i => i.PriceListId == priceListId);
        if (r.CategoryId is { } cat)
        {
            var catIds = await CategoryWithChildrenAsync(cat, ct);
            var serviceIds = db.Services.Where(s => catIds.Contains(s.CategoryId)).Select(s => s.Id);
            q = q.Where(i => serviceIds.Contains(i.ServiceId));
        }
        var rows = await q.ToListAsync(ct);
        var round = Math.Max(1, r.RoundTo ?? 10_000);
        foreach (var row in rows)
        {
            var raw = row.Price * (1 + r.Percent / 100m);
            row.Price = (long)(Math.Round(raw / round, MidpointRounding.AwayFromZero) * round);
        }
        audit.Log(nameof(PriceList), priceListId, "bulk_price_update", new { r.CategoryId, r.Percent, count = rows.Count });
        await db.SaveChangesAsync(ct);
        return new BulkPriceUpdateResult(rows.Count);
    }

    private async Task<List<Guid>> CategoryWithChildrenAsync(Guid root, CancellationToken ct)
    {
        var all = await db.ServiceCategories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var result = new List<Guid> { root };
        for (var i = 0; i < result.Count; i++) result.AddRange(all.Where(c => c.ParentId == result[i]).Select(c => c.Id));
        return result;
    }

    // Техкарты (версионные: новая версия деактивирует предыдущую)
    public async Task<IReadOnlyList<TechCardDto>> ListTechCardsAsync(Guid serviceId, CancellationToken ct)
    {
        var cards = await db.TechCards.AsNoTracking().Include(t => t.Items).Where(t => t.ServiceId == serviceId).OrderByDescending(t => t.Version).ToListAsync(ct);
        var itemIds = cards.SelectMany(c => c.Items).Select(i => i.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return cards.Select(c => new TechCardDto(c.Id, c.ServiceId, c.Version, c.IsActive, c.CreatedAt,
            c.Items.Select(i => new TechCardItemDto(i.ItemId, items.GetValueOrDefault(i.ItemId)?.Name ?? "?", items.GetValueOrDefault(i.ItemId)?.BaseUnit.ToString() ?? "", i.Quantity)).ToList())).ToList();
    }

    public async Task<TechCardDto> CreateTechCardVersionAsync(Guid serviceId, TechCardRequest r, CancellationToken ct)
    {
        _ = await db.Services.GetOrThrowAsync(serviceId, "Услуга", ct);
        var current = await db.TechCards.Where(t => t.ServiceId == serviceId).ToListAsync(ct);
        foreach (var c in current) c.IsActive = false;
        var card = new TechCard
        {
            ServiceId = serviceId,
            Version = current.Count == 0 ? 1 : current.Max(c => c.Version) + 1,
            IsActive = true,
        };
        card.Items = r.Items.GroupBy(i => i.ItemId).Select(g => new TechCardItem { TechCardId = card.Id, ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToList();
        db.TechCards.Add(card);
        await db.SaveChangesAsync(ct);
        return (await ListTechCardsAsync(serviceId, ct)).First(c => c.Id == card.Id);
    }

}
