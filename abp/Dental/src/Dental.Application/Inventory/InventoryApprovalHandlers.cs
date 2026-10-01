using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Dental.Inventory;

/// <summary>
/// Обработчик решения по запросу подтверждения склада (временный контракт до слияния с модулем Approvals:
/// при слиянии адаптировать к его IApprovalHandler — ApprovalType = <see cref="InventoryApprovalTypes"/>, EntityId = Id документа).
/// </summary>
public interface IInventoryApprovalHandler
{
    string ApprovalType { get; }
    Task OnApprovedAsync(Guid entityId);
    Task OnRejectedAsync(Guid entityId);
}

/// <summary>Подтверждение списания сверх лимита роли: проведение документа.</summary>
public class WriteoffApprovalHandler : IInventoryApprovalHandler, ITransientDependency
{
    private readonly StockManager _stock;

    public WriteoffApprovalHandler(StockManager stock)
    {
        _stock = stock;
    }

    public string ApprovalType => InventoryApprovalTypes.Writeoff;

    [UnitOfWork]
    public virtual async Task OnApprovedAsync(Guid entityId)
    {
        var doc = await _stock.GetWithLinesAsync(entityId);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await _stock.CompleteWriteoffAsync(doc);
    }

    [UnitOfWork]
    public virtual async Task OnRejectedAsync(Guid entityId) => await _stock.RejectPendingAsync(await _stock.GetWithLinesAsync(entityId));
}

/// <summary>Недостача при приёмке перемещения: подтверждение проводит списание (без движений — товар уже ушёл с отправителя).</summary>
public class TransferShortageApprovalHandler : IInventoryApprovalHandler, ITransientDependency
{
    private readonly StockManager _stock;

    public TransferShortageApprovalHandler(StockManager stock)
    {
        _stock = stock;
    }

    public string ApprovalType => InventoryApprovalTypes.TransferShortage;

    [UnitOfWork]
    public virtual async Task OnApprovedAsync(Guid entityId)
    {
        var doc = await _stock.GetWithLinesAsync(entityId);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await _stock.CompleteWriteoffAsync(doc);
    }

    /// <summary>Недостачу нельзя «отменить»: при отклонении документ остаётся на разбор (черновик).</summary>
    [UnitOfWork]
    public virtual async Task OnRejectedAsync(Guid entityId) => await _stock.RejectPendingAsync(await _stock.GetWithLinesAsync(entityId));
}
