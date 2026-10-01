using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dental.Catalog;

/// <summary>
/// Справка по номенклатуре склада для техкарт (каталог не зависит от модуля Inventory).
/// Реализует модуль Inventory: [Dependency(ReplaceServices = true)] + ExposeServices(typeof(IItemLookup)).
/// </summary>
public interface IItemLookup
{
    Task<Dictionary<Guid, ItemLookupInfo>> GetAsync(IReadOnlyCollection<Guid> itemIds);

    /// <summary>Поиск номенклатуры для выбора в техкарте (по названию/артикулу).</summary>
    Task<List<ItemLookupInfo>> SearchAsync(string? filter, int maxCount = 20);
}

public record ItemLookupInfo(Guid Id, string Name, string BaseUnit);

/// <summary>Заглушка до переноса склада: названий нет, поиск пуст.</summary>
[Dependency(TryRegister = true)]
public class NullItemLookup : IItemLookup, ITransientDependency
{
    public Task<Dictionary<Guid, ItemLookupInfo>> GetAsync(IReadOnlyCollection<Guid> itemIds) =>
        Task.FromResult(new Dictionary<Guid, ItemLookupInfo>());

    public Task<List<ItemLookupInfo>> SearchAsync(string? filter, int maxCount = 20) =>
        Task.FromResult(new List<ItemLookupInfo>());
}
