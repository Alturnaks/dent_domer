using System.Threading.Tasks;
using Dental.Approvals;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Dental.Inventory;

/// <summary>Подтверждение списания сверх лимита роли: проведение документа; отклонение — документ возвращается в черновик.</summary>
[ExposeServices(typeof(IApprovalHandler), IncludeSelf = true)]
public class WriteoffApprovalHandler : IApprovalHandler, ITransientDependency
{
    private readonly StockManager _stock;

    public WriteoffApprovalHandler(StockManager stock)
    {
        _stock = stock;
    }

    public string Type => ApprovalTypes.Writeoff;

    [UnitOfWork]
    public virtual async Task OnApprovedAsync(ApprovalRequest request)
    {
        var doc = await _stock.GetWithLinesAsync(request.EntityId);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await _stock.CompleteWriteoffAsync(doc);
    }

    [UnitOfWork]
    public virtual async Task OnRejectedAsync(ApprovalRequest request) =>
        await _stock.RejectPendingAsync(await _stock.GetWithLinesAsync(request.EntityId));
}

/// <summary>Недостача при приёмке перемещения: подтверждение проводит списание (без движений — товар уже ушёл с отправителя).</summary>
[ExposeServices(typeof(IApprovalHandler), IncludeSelf = true)]
public class TransferShortageApprovalHandler : IApprovalHandler, ITransientDependency
{
    private readonly StockManager _stock;

    public TransferShortageApprovalHandler(StockManager stock)
    {
        _stock = stock;
    }

    public string Type => ApprovalTypes.TransferShortage;

    [UnitOfWork]
    public virtual async Task OnApprovedAsync(ApprovalRequest request)
    {
        var doc = await _stock.GetWithLinesAsync(request.EntityId);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await _stock.CompleteWriteoffAsync(doc);
    }

    /// <summary>Недостачу нельзя «отменить»: при отклонении документ остаётся на разбор (черновик).</summary>
    [UnitOfWork]
    public virtual async Task OnRejectedAsync(ApprovalRequest request) =>
        await _stock.RejectPendingAsync(await _stock.GetWithLinesAsync(request.EntityId));
}
