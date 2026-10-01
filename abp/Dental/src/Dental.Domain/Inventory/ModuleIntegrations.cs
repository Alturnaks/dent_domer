using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Localization;
using Dental.Patients;
using Microsoft.Extensions.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Dental.Inventory;

/// <summary>
/// Подтверждения склада через модуль Approvals: списание сверх лимита роли → ApprovalRequest (документ «ожидает подтверждения»),
/// недостача при перемещении → безусловный запрос, отмена документа → отклонение ожидающих запросов.
/// Решение исполняют <see cref="IApprovalHandler"/> склада (WriteoffApprovalHandler / TransferShortageApprovalHandler).
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IApprovalGateway))]
public class ApprovalManagerGateway : IApprovalGateway, ITransientDependency
{
    private readonly ApprovalManager _approvals;

    public ApprovalManagerGateway(ApprovalManager approvals)
    {
        _approvals = approvals;
    }

    public async Task<bool> RequestIfOverWriteoffLimitAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId,
        object? payload = null) =>
        (await _approvals.CheckOrRequestAsync(approvalType, entityType, entityId, amount, title, payload ?? new { }, branchId)).Allowed;

    public async Task RequestAsync(string approvalType, string entityType, Guid entityId, long amount, string title, Guid? branchId, object? payload = null) =>
        await _approvals.RequestAsync(approvalType, entityType, entityId, amount, title, payload ?? new { }, branchId);

    public async Task CancelPendingAsync(string entityType, Guid entityId, string reason) =>
        await _approvals.CancelPendingAsync(entityType, entityId, reason);
}

/// <summary>Номенклатура склада для техкарт каталога (заменяет Catalog.NullItemLookup). Единица — локализованное «шт/г/мл/уп».</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(Dental.Catalog.IItemLookup))]
public class CatalogItemLookup : Dental.Catalog.IItemLookup, ITransientDependency
{
    private readonly IItemLookup _items;
    private readonly IStringLocalizer<DentalResource> _l;

    public CatalogItemLookup(IItemLookup items, IStringLocalizer<DentalResource> l)
    {
        _items = items;
        _l = l;
    }

    public async Task<Dictionary<Guid, Dental.Catalog.ItemLookupInfo>> GetAsync(IReadOnlyCollection<Guid> itemIds) =>
        (await _items.GetAsync(itemIds)).Values.ToDictionary(i => i.Id, Map);

    public async Task<List<Dental.Catalog.ItemLookupInfo>> SearchAsync(string? filter, int maxCount = 20) =>
        (await _items.SearchAsync(filter, maxCount)).Select(Map).ToList();

    private Dental.Catalog.ItemLookupInfo Map(ItemLookupInfo i) => new(i.Id, i.Name, _l["Enum:BaseUnit." + (int)i.BaseUnit]);
}

/// <summary>Слияние пациентов: движения склада (расход по визиту) переносятся на основную карточку.</summary>
[ExposeServices(typeof(IPatientMergeContributor), IncludeSelf = true)]
public class StockMovementsPatientMergeContributor : IPatientMergeContributor, ITransientDependency
{
    private readonly IRepository<StockMovement, Guid> _movements;

    public StockMovementsPatientMergeContributor(IRepository<StockMovement, Guid> movements)
    {
        _movements = movements;
    }

    public string Name => "stockMovements";

    public async Task<int> MoveAsync(Guid fromPatientId, Guid toPatientId)
    {
        var list = await _movements.GetListAsync(m => m.PatientId == fromPatientId);
        foreach (var m in list)
        {
            m.ChangePatient(toPatientId);
        }
        if (list.Count > 0)
        {
            await _movements.UpdateManyAsync(list, autoSave: true);
        }
        return list.Count;
    }
}
