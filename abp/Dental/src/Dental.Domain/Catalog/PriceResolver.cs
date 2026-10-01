using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Timing;

namespace Dental.Catalog;

/// <summary>
/// Действующие цены услуг. Используется расписанием (плановая цена), визитами (цена позиции), каталогом.
/// Правило: прайс филиала, иначе сетевой; среди подходящих — активный с самой поздней ValidFrom ≤ даты, содержащий услугу.
/// </summary>
public interface IPriceResolver
{
    /// <summary>branchId = null — только сетевые прайсы. onDate = null — сегодня (UTC).</summary>
    Task<Dictionary<Guid, long>> ResolveAsync(Guid? branchId, IReadOnlyCollection<Guid> serviceIds, DateOnly? onDate = null);

    Task<long?> ResolveAsync(Guid? branchId, Guid serviceId, DateOnly? onDate = null);
}

public class PriceResolver : IPriceResolver, ITransientDependency
{
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IAsyncQueryableExecuter _executer;
    private readonly IClock _clock;

    public PriceResolver(IRepository<PriceList, Guid> priceLists, IAsyncQueryableExecuter executer, IClock clock)
    {
        _priceLists = priceLists;
        _executer = executer;
        _clock = clock;
    }

    public async Task<long?> ResolveAsync(Guid? branchId, Guid serviceId, DateOnly? onDate = null) =>
        (await ResolveAsync(branchId, [serviceId], onDate)).TryGetValue(serviceId, out var p) ? p : null;

    public async Task<Dictionary<Guid, long>> ResolveAsync(Guid? branchId, IReadOnlyCollection<Guid> serviceIds, DateOnly? onDate = null)
    {
        var date = onDate ?? DateOnly.FromDateTime(_clock.Now);
        if (serviceIds.Count == 0)
        {
            return [];
        }
        var ids = serviceIds.ToList();
        var query = (await _priceLists.WithDetailsAsync(l => l.Items))
            .Where(l => l.IsActive && l.ValidFrom <= date && (l.BranchId == null || l.BranchId == branchId));
        var lists = await _executer.ToListAsync(query);
        var input = lists.Select(l => new PriceListPrices(l.BranchId, l.ValidFrom, l.IsActive,
            l.Items.Where(i => ids.Contains(i.ServiceId)).ToDictionary(i => i.ServiceId, i => i.Price))).ToList();
        var result = new Dictionary<Guid, long>();
        foreach (var sid in ids)
        {
            if (Resolve(input, sid, branchId, date) is { } price)
            {
                result[sid] = price;
            }
        }
        return result;
    }

    /// <summary>Чистая функция выбора цены (для тестов и повторного использования).</summary>
    public static long? Resolve(IEnumerable<PriceListPrices> lists, Guid serviceId, Guid? branchId, DateOnly onDate)
    {
        var active = lists.Where(l => l.IsActive && l.ValidFrom <= onDate && l.Prices.ContainsKey(serviceId)).ToList();
        if (branchId is { } b)
        {
            var branch = active.Where(l => l.BranchId == b).OrderByDescending(l => l.ValidFrom).FirstOrDefault();
            if (branch is not null)
            {
                return branch.Prices[serviceId];
            }
        }
        var network = active.Where(l => l.BranchId is null).OrderByDescending(l => l.ValidFrom).FirstOrDefault();
        return network?.Prices[serviceId];
    }
}

public record PriceListPrices(Guid? BranchId, DateOnly ValidFrom, bool IsActive, IReadOnlyDictionary<Guid, long> Prices);
