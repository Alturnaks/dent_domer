using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Catalog;

public class ServiceCategoryDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public int Sort { get; set; }
}

public class CreateUpdateServiceCategoryDto
{
    [Required, StringLength(CatalogConsts.MaxCategoryNameLength)]
    public string Name { get; set; } = "";

    public Guid? ParentId { get; set; }
    public int Sort { get; set; }
}

public class GetServiceListInput
{
    /// <summary>Категория (включая подкатегории).</summary>
    public Guid? CategoryId { get; set; }
    public string? Filter { get; set; }
    public bool IncludeInactive { get; set; }
    /// <summary>Филиал для цены (null — сетевой прайс).</summary>
    public Guid? BranchId { get; set; }
}

public class ClinicServiceDto : EntityDto<Guid>
{
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DurationMin { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Действующая цена (тиыны) или null, если услуги нет ни в одном прайсе.</summary>
    public long? Price { get; set; }
    public bool HasTechCard { get; set; }
}

public class CreateUpdateClinicServiceDto
{
    [Required]
    public Guid CategoryId { get; set; }

    [Required, StringLength(CatalogConsts.MaxServiceCodeLength)]
    public string Code { get; set; } = "";

    [Required, StringLength(CatalogConsts.MaxServiceNameLength)]
    public string Name { get; set; } = "";

    [Range(CatalogConsts.MinDurationMin, CatalogConsts.MaxDurationMin)]
    public int DurationMin { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}

public class PriceListDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public DateOnly ValidFrom { get; set; }
    public bool IsActive { get; set; }
    public int ItemsCount { get; set; }
}

public class CreateUpdatePriceListDto
{
    [Required, StringLength(CatalogConsts.MaxPriceListNameLength)]
    public string Name { get; set; } = "";

    public Guid? BranchId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Только при создании: скопировать цены из другого прайса.</summary>
    public Guid? CopyFromId { get; set; }
}

public class PriceListItemDto
{
    public Guid ServiceId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public long Price { get; set; }
}

public class PriceListItemInput
{
    [Required]
    public Guid ServiceId { get; set; }

    /// <summary>Тиыны. null — убрать услугу из прайса.</summary>
    public long? Price { get; set; }
}

public class SetPriceItemsInput
{
    public List<PriceListItemInput> Items { get; set; } = [];
}

public class BulkPriceUpdateInput
{
    public Guid? CategoryId { get; set; }

    [Range(CatalogConsts.MinBulkPercent, CatalogConsts.MaxBulkPercent)]
    public decimal Percent { get; set; }

    /// <summary>Округление, тиыны (по умолчанию 10 000 = 100 ₸).</summary>
    public long? RoundTo { get; set; }
}

public class BulkPriceUpdateResultDto
{
    public int Updated { get; set; }
}

public class ResolvePricesInput
{
    public Guid? BranchId { get; set; }
    public List<Guid> ServiceIds { get; set; } = [];
    public DateOnly? OnDate { get; set; }
}

public class ResolvedPriceDto
{
    public Guid ServiceId { get; set; }
    public long Price { get; set; }
}

public class TechCardItemDto
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string BaseUnit { get; set; } = "";
    public decimal Quantity { get; set; }
}

public class TechCardDto : EntityDto<Guid>
{
    public Guid ServiceId { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
    public List<TechCardItemDto> Items { get; set; } = [];
}

public class TechCardItemInput
{
    [Required]
    public Guid ItemId { get; set; }

    [Range(0.0001, 1_000_000)]
    public decimal Quantity { get; set; }
}

public class CreateTechCardVersionInput
{
    public List<TechCardItemInput> Items { get; set; } = [];
}

public class ItemLookupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string BaseUnit { get; set; } = "";
}

/// <summary>
/// Каталог услуг: категории (дерево), услуги, прайс-листы (сеть/филиал) с массовым изменением цен, техкарты (версии).
/// Чтение услуг/категорий — любому сотруднику (нужно расписанию и визитам); изменения — PricesManage / TechCardsManage.
/// </summary>
public interface ICatalogAppService : IApplicationService
{
    Task<ListResultDto<ServiceCategoryDto>> GetCategoriesAsync();
    Task<ServiceCategoryDto> CreateCategoryAsync(CreateUpdateServiceCategoryDto input);
    Task<ServiceCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateServiceCategoryDto input);
    Task DeleteCategoryAsync(Guid id);

    Task<ListResultDto<ClinicServiceDto>> GetServicesAsync(GetServiceListInput input);
    Task<ClinicServiceDto> CreateServiceAsync(CreateUpdateClinicServiceDto input);
    Task<ClinicServiceDto> UpdateServiceAsync(Guid id, CreateUpdateClinicServiceDto input);

    Task<ListResultDto<PriceListDto>> GetPriceListsAsync();
    Task<PriceListDto> CreatePriceListAsync(CreateUpdatePriceListDto input);
    Task<PriceListDto> UpdatePriceListAsync(Guid id, CreateUpdatePriceListDto input);
    Task DeletePriceListAsync(Guid id);
    Task<ListResultDto<PriceListItemDto>> GetPriceItemsAsync(Guid priceListId);
    Task<ListResultDto<PriceListItemDto>> SetPriceItemsAsync(Guid priceListId, SetPriceItemsInput input);
    Task<BulkPriceUpdateResultDto> BulkUpdatePricesAsync(Guid priceListId, BulkPriceUpdateInput input);
    Task<ListResultDto<ResolvedPriceDto>> ResolvePricesAsync(ResolvePricesInput input);

    Task<ListResultDto<TechCardDto>> GetTechCardsAsync(Guid serviceId);
    Task<TechCardDto> CreateTechCardVersionAsync(Guid serviceId, CreateTechCardVersionInput input);
    Task<ListResultDto<ItemLookupDto>> GetItemLookupAsync(string? filter);
}
