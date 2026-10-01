using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Dental.Catalog;

/// <summary>Техкарты: создание новой версии (предыдущие деактивируются), выбор активной версии для визита.</summary>
public class TechCardManager : DomainService
{
    private readonly IRepository<TechCard, Guid> _techCards;
    private readonly IRepository<ClinicService, Guid> _services;

    public TechCardManager(IRepository<TechCard, Guid> techCards, IRepository<ClinicService, Guid> services)
    {
        _techCards = techCards;
        _services = services;
    }

    public async Task<TechCard> CreateVersionAsync(Guid serviceId, IEnumerable<(Guid ItemId, decimal Quantity)> items)
    {
        await _services.GetAsync(serviceId);
        var current = await _techCards.GetListAsync(t => t.ServiceId == serviceId);
        foreach (var c in current.Where(c => c.IsActive))
        {
            c.Deactivate();
            await _techCards.UpdateAsync(c);
        }
        var card = new TechCard(GuidGenerator.Create(), CurrentTenant.Id, serviceId, current.Count == 0 ? 1 : current.Max(c => c.Version) + 1);
        foreach (var (itemId, qty) in items)
        {
            card.AddItem(GuidGenerator.Create(), itemId, qty);
        }
        return await _techCards.InsertAsync(card, autoSave: true);
    }

    /// <summary>Активная техкарта услуги (для списания материалов при закрытии визита).</summary>
    public async Task<TechCard?> FindActiveAsync(Guid serviceId)
    {
        var query = (await _techCards.WithDetailsAsync(t => t.Items)).Where(t => t.ServiceId == serviceId && t.IsActive);
        return await AsyncExecuter.FirstOrDefaultAsync(query);
    }
}
