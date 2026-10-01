using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Dental.Catalog;

[Authorize]
public class CatalogAppService : DentalAppService, ICatalogAppService
{
    private readonly IRepository<ServiceCategory, Guid> _categories;
    private readonly IRepository<ClinicService, Guid> _services;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<TechCard, Guid> _techCards;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IPriceResolver _priceResolver;
    private readonly TechCardManager _techCardManager;
    private readonly IItemLookup _itemLookup;

    public CatalogAppService(
        IRepository<ServiceCategory, Guid> categories,
        IRepository<ClinicService, Guid> services,
        IRepository<PriceList, Guid> priceLists,
        IRepository<TechCard, Guid> techCards,
        IRepository<Branch, Guid> branches,
        IPriceResolver priceResolver,
        TechCardManager techCardManager,
        IItemLookup itemLookup)
    {
        _categories = categories;
        _services = services;
        _priceLists = priceLists;
        _techCards = techCards;
        _branches = branches;
        _priceResolver = priceResolver;
        _techCardManager = techCardManager;
        _itemLookup = itemLookup;
    }

    // ---------- Категории ----------

    public async Task<ListResultDto<ServiceCategoryDto>> GetCategoriesAsync() =>
        new((await _categories.GetListAsync()).OrderBy(c => c.Sort).ThenBy(c => c.Name).Select(ToDto).ToList());

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<ServiceCategoryDto> CreateCategoryAsync(CreateUpdateServiceCategoryDto input)
    {
        if (input.ParentId is { } pid)
        {
            await _categories.GetAsync(pid);
        }
        var c = new ServiceCategory(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.ParentId, input.Sort);
        await _categories.InsertAsync(c, autoSave: true);
        return ToDto(c);
    }

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<ServiceCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateServiceCategoryDto input)
    {
        var c = await _categories.GetAsync(id);
        if (input.ParentId is { } pid)
        {
            await _categories.GetAsync(pid);
        }
        var descendants = await CategoryWithChildrenAsync(id);
        c.SetParent(input.ParentId, descendants);
        c.SetName(input.Name);
        c.Sort = input.Sort;
        await _categories.UpdateAsync(c, autoSave: true);
        return ToDto(c);
    }

    /// <summary>Мягкое удаление пустой категории (без подкатегорий и услуг).</summary>
    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task DeleteCategoryAsync(Guid id)
    {
        if (await _categories.AnyAsync(c => c.ParentId == id) || await _services.AnyAsync(s => s.CategoryId == id))
        {
            throw new BusinessException(DentalDomainErrorCodes.CategoryParentInvalid).WithData("reason", "not_empty");
        }
        await _categories.DeleteAsync(id);
    }

    private async Task<List<Guid>> CategoryWithChildrenAsync(Guid root)
    {
        var all = await _categories.GetListAsync();
        var result = new List<Guid> { root };
        for (var i = 0; i < result.Count; i++)
        {
            result.AddRange(all.Where(c => c.ParentId == result[i] && !result.Contains(c.Id)).Select(c => c.Id));
        }
        return result;
    }

    private static ServiceCategoryDto ToDto(ServiceCategory c) => new() { Id = c.Id, Name = c.Name, ParentId = c.ParentId, Sort = c.Sort };

    // ---------- Услуги ----------

    public async Task<ListResultDto<ClinicServiceDto>> GetServicesAsync(GetServiceListInput input)
    {
        if (input.BranchId is { } b)
        {
            await BranchScope.EnsureCanAccessAsync(b);
        }
        var query = await _services.GetQueryableAsync();
        if (!input.IncludeInactive)
        {
            query = query.Where(s => s.IsActive);
        }
        if (input.CategoryId is { } c)
        {
            var catIds = await CategoryWithChildrenAsync(c);
            query = query.Where(s => catIds.Contains(s.CategoryId));
        }
        if (!input.Filter.IsNullOrWhiteSpace())
        {
            var f = input.Filter!.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(f) || s.Code.ToLower().Contains(f));
        }
        var services = await AsyncExecuter.ToListAsync(query.OrderBy(s => s.Code));
        var ids = services.Select(s => s.Id).ToList();
        var prices = await _priceResolver.ResolveAsync(input.BranchId, ids);
        var withCards = (await AsyncExecuter.ToListAsync((await _techCards.GetQueryableAsync())
            .Where(t => t.IsActive && ids.Contains(t.ServiceId)).Select(t => t.ServiceId))).ToHashSet();
        return new ListResultDto<ClinicServiceDto>(services.Select(s => ToDto(s, prices.TryGetValue(s.Id, out var p) ? p : null, withCards.Contains(s.Id))).ToList());
    }

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<ClinicServiceDto> CreateServiceAsync(CreateUpdateClinicServiceDto input)
    {
        await _categories.GetAsync(input.CategoryId);
        await EnsureUniqueCodeAsync(input.Code, null);
        var s = new ClinicService(GuidGenerator.Create(), CurrentTenant.Id, input.CategoryId, input.Code, input.Name, input.DurationMin) { IsActive = input.IsActive };
        await _services.InsertAsync(s, autoSave: true);
        return ToDto(s, null, false);
    }

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<ClinicServiceDto> UpdateServiceAsync(Guid id, CreateUpdateClinicServiceDto input)
    {
        var s = await _services.GetAsync(id);
        await _categories.GetAsync(input.CategoryId);
        await EnsureUniqueCodeAsync(input.Code, id);
        s.CategoryId = input.CategoryId;
        s.SetCode(input.Code);
        s.SetName(input.Name);
        s.SetDuration(input.DurationMin);
        s.IsActive = input.IsActive;
        await _services.UpdateAsync(s, autoSave: true);
        return ToDto(s, await _priceResolver.ResolveAsync(null, s.Id), await _techCards.AnyAsync(t => t.ServiceId == id && t.IsActive));
    }

    private async Task EnsureUniqueCodeAsync(string code, Guid? exceptId)
    {
        var c = code.Trim();
        if (await _services.AnyAsync(s => s.Code == c && s.Id != exceptId))
        {
            throw new BusinessException(DentalDomainErrorCodes.ServiceCodeAlreadyExists).WithData("code", c);
        }
    }

    private static ClinicServiceDto ToDto(ClinicService s, long? price, bool hasTechCard) => new()
    {
        Id = s.Id,
        CategoryId = s.CategoryId,
        Code = s.Code,
        Name = s.Name,
        DurationMin = s.DurationMin,
        IsActive = s.IsActive,
        Price = price,
        HasTechCard = hasTechCard,
    };

    // ---------- Прайс-листы ----------

    public async Task<ListResultDto<PriceListDto>> GetPriceListsAsync()
    {
        var lists = await _priceLists.GetListAsync(includeDetails: true);
        var branches = (await _branches.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);
        return new ListResultDto<PriceListDto>(lists
            .OrderBy(l => l.BranchId.HasValue).ThenBy(l => l.BranchId).ThenByDescending(l => l.ValidFrom)
            .Select(l => ToDto(l, branches)).ToList());
    }

    private static PriceListDto ToDto(PriceList l, IReadOnlyDictionary<Guid, string> branches) => new()
    {
        Id = l.Id,
        Name = l.Name,
        BranchId = l.BranchId,
        BranchName = l.BranchId is { } b ? branches.GetValueOrDefault(b) : null,
        ValidFrom = l.ValidFrom,
        IsActive = l.IsActive,
        ItemsCount = l.Items.Count,
    };

    private async Task<Dictionary<Guid, string>> BranchNamesAsync() => (await _branches.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<PriceListDto> CreatePriceListAsync(CreateUpdatePriceListDto input)
    {
        if (input.BranchId is { } b)
        {
            await BranchScope.EnsureCanAccessAsync(b);
        }
        var l = new PriceList(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.BranchId, input.ValidFrom) { IsActive = input.IsActive };
        if (input.CopyFromId is { } src)
        {
            foreach (var i in (await _priceLists.GetAsync(src)).Items)
            {
                l.SetPrice(GuidGenerator.Create(), i.ServiceId, i.Price);
            }
        }
        await _priceLists.InsertAsync(l, autoSave: true);
        return ToDto(l, await BranchNamesAsync());
    }

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<PriceListDto> UpdatePriceListAsync(Guid id, CreateUpdatePriceListDto input)
    {
        var l = await _priceLists.GetAsync(id);
        if (input.BranchId is { } b)
        {
            await BranchScope.EnsureCanAccessAsync(b);
        }
        l.SetName(input.Name);
        l.BranchId = input.BranchId;
        l.ValidFrom = input.ValidFrom;
        l.IsActive = input.IsActive;
        await _priceLists.UpdateAsync(l, autoSave: true);
        return ToDto(l, await BranchNamesAsync());
    }

    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task DeletePriceListAsync(Guid id) => await _priceLists.DeleteAsync(id);

    public async Task<ListResultDto<PriceListItemDto>> GetPriceItemsAsync(Guid priceListId)
    {
        var l = await _priceLists.GetAsync(priceListId);
        var ids = l.Items.Select(i => i.ServiceId).ToList();
        var services = (await _services.GetListAsync(s => ids.Contains(s.Id))).ToDictionary(s => s.Id);
        return new ListResultDto<PriceListItemDto>(l.Items
            .Where(i => services.ContainsKey(i.ServiceId))
            .Select(i => new PriceListItemDto
            {
                ServiceId = i.ServiceId,
                ServiceCode = services[i.ServiceId].Code,
                ServiceName = services[i.ServiceId].Name,
                CategoryId = services[i.ServiceId].CategoryId,
                Price = i.Price,
            })
            .OrderBy(i => i.ServiceCode).ToList());
    }

    /// <summary>Установить цены (upsert, null — убрать). История цен — в журнале аудита ABP (сущность PriceList аудируется).</summary>
    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<ListResultDto<PriceListItemDto>> SetPriceItemsAsync(Guid priceListId, SetPriceItemsInput input)
    {
        var l = await _priceLists.GetAsync(priceListId);
        var ids = input.Items.Select(i => i.ServiceId).Distinct().ToList();
        var existing = (await _services.GetListAsync(s => ids.Contains(s.Id))).Select(s => s.Id).ToHashSet();
        foreach (var i in input.Items)
        {
            if (!existing.Contains(i.ServiceId))
            {
                throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(ClinicService), i.ServiceId);
            }
            if (i.Price is { } price)
            {
                l.SetPrice(GuidGenerator.Create(), i.ServiceId, price);
            }
            else
            {
                l.RemovePrice(i.ServiceId);
            }
        }
        await _priceLists.UpdateAsync(l, autoSave: true);
        return await GetPriceItemsAsync(priceListId);
    }

    /// <summary>Массовое изменение цен на процент (по категории с подкатегориями или по всему прайсу), с округлением.</summary>
    [Authorize(DentalPermissions.Catalog.PricesManage)]
    public async Task<BulkPriceUpdateResultDto> BulkUpdatePricesAsync(Guid priceListId, BulkPriceUpdateInput input)
    {
        var l = await _priceLists.GetAsync(priceListId);
        HashSet<Guid>? serviceIds = null;
        if (input.CategoryId is { } cat)
        {
            var catIds = await CategoryWithChildrenAsync(cat);
            serviceIds = (await _services.GetListAsync(s => catIds.Contains(s.CategoryId))).Select(s => s.Id).ToHashSet();
        }
        var count = l.BulkChange(id => serviceIds is null || serviceIds.Contains(id), input.Percent, input.RoundTo ?? CatalogConsts.DefaultRoundTo);
        await _priceLists.UpdateAsync(l, autoSave: true);
        Logger.LogInformation("Bulk price update {PriceList}: {Percent}% category {Category}, {Count} rows", priceListId, input.Percent, input.CategoryId, count);
        return new BulkPriceUpdateResultDto { Updated = count };
    }

    public async Task<ListResultDto<ResolvedPriceDto>> ResolvePricesAsync(ResolvePricesInput input)
    {
        if (input.BranchId is { } b)
        {
            await BranchScope.EnsureCanAccessAsync(b);
        }
        var prices = await _priceResolver.ResolveAsync(input.BranchId, input.ServiceIds, input.OnDate);
        return new ListResultDto<ResolvedPriceDto>(prices.Select(p => new ResolvedPriceDto { ServiceId = p.Key, Price = p.Value }).ToList());
    }

    // ---------- Техкарты ----------

    public async Task<ListResultDto<TechCardDto>> GetTechCardsAsync(Guid serviceId)
    {
        var cards = (await _techCards.GetListAsync(t => t.ServiceId == serviceId, includeDetails: true)).OrderByDescending(t => t.Version).ToList();
        var items = await _itemLookup.GetAsync(cards.SelectMany(c => c.Items).Select(i => i.ItemId).Distinct().ToList());
        return new ListResultDto<TechCardDto>(cards.Select(c => ToDto(c, items)).ToList());
    }

    private static TechCardDto ToDto(TechCard c, IReadOnlyDictionary<Guid, ItemLookupInfo> items) => new()
    {
        Id = c.Id,
        ServiceId = c.ServiceId,
        Version = c.Version,
        IsActive = c.IsActive,
        CreationTime = c.CreationTime,
        Items = c.Items.Select(i => new TechCardItemDto
        {
            ItemId = i.ItemId,
            ItemName = items.GetValueOrDefault(i.ItemId)?.Name ?? i.ItemId.ToString()[..8],
            BaseUnit = items.GetValueOrDefault(i.ItemId)?.BaseUnit ?? "",
            Quantity = i.Quantity,
        }).ToList(),
    };

    [Authorize(DentalPermissions.Catalog.TechCardsManage)]
    public async Task<TechCardDto> CreateTechCardVersionAsync(Guid serviceId, CreateTechCardVersionInput input)
    {
        var card = await _techCardManager.CreateVersionAsync(serviceId, input.Items.Select(i => (i.ItemId, i.Quantity)));
        var items = await _itemLookup.GetAsync(card.Items.Select(i => i.ItemId).ToList());
        return ToDto(card, items);
    }

    [Authorize(DentalPermissions.Catalog.TechCardsManage)]
    public async Task<ListResultDto<ItemLookupDto>> GetItemLookupAsync(string? filter) =>
        new((await _itemLookup.SearchAsync(filter)).Select(i => new ItemLookupDto { Id = i.Id, Name = i.Name, BaseUnit = i.BaseUnit }).ToList());
}
