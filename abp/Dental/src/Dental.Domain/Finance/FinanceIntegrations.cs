using System;
using System.Text.Json;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Patients;
using Dental.Schedule;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Linq;

namespace Dental.Finance;

public class VisitArrivalHandler(FinanceManager manager) : ILocalEventHandler<AppointmentArrivedEvent>, ITransientDependency
{ public async Task HandleEventAsync(AppointmentArrivedEvent eventData) => await manager.OpenFromAppointmentAsync(eventData.AppointmentId); }

public class DiscountApprovalHandler(IRepository<Visit, Guid> visits, IFinanceLock financeLock, IAsyncQueryableExecuter executer) : IApprovalHandler, ITransientDependency
{
    public string Type => ApprovalTypes.Discount;
    public Task OnApprovedAsync(ApprovalRequest request) => ApplyAsync(request, true);
    public Task OnRejectedAsync(ApprovalRequest request) => ApplyAsync(request, false);
    private async Task ApplyAsync(ApprovalRequest request, bool approved)
    {
        await financeLock.AcquireAsync();
        var query = await visits.WithDetailsAsync(v => v.Items);
        var v = await executer.FirstAsync(System.Linq.Queryable.Where(query, x => x.Id == request.EntityId));
        v.EnsureOpen();
        foreach (var item in v.Items) { if (!approved && item.DiscountPendingApproval) item.DiscountPct = 0; item.DiscountPendingApproval = false; }
        v.Recalculate(); v.ApprovalState = VisitApprovalState.None; await visits.UpdateAsync(v, true);
    }
}
public class ClosedVisitEditApprovalHandler(FinanceManager manager, IRepository<Visit, Guid> visits, IFinanceLock financeLock) : IApprovalHandler, ITransientDependency
{
    public string Type => ApprovalTypes.ClosedVisitEdit;
    public async Task OnApprovedAsync(ApprovalRequest request)
    { await financeLock.AcquireAsync(); var v = await manager.GetVisitAsync(request.EntityId); var payload = JsonSerializer.Deserialize<CorrectionData>(request.Payload, ApprovalManager.Json)!; await manager.ApplyCorrectionAsync(v, payload); }
    public async Task OnRejectedAsync(ApprovalRequest request)
    { await financeLock.AcquireAsync(); var v = await visits.GetAsync(request.EntityId); v.ApprovalState = VisitApprovalState.None; await visits.UpdateAsync(v, true); }
}
public class RefundApprovalHandler(FinanceManager manager, IRepository<Payment, Guid> payments, IFinanceLock financeLock) : IApprovalHandler, ITransientDependency
{
    public string Type => ApprovalTypes.Refund;
    public async Task OnApprovedAsync(ApprovalRequest request) => await manager.ApplyRefundAsync(await payments.GetAsync(request.EntityId));
    public async Task OnRejectedAsync(ApprovalRequest request) { await financeLock.AcquireAsync(); var p = await payments.GetAsync(request.EntityId); p.State = 2; await payments.UpdateAsync(p, true); }
}
public class PayrollVisitChangeApprovalHandler(FinanceManager manager, IRepository<Visit, Guid> visits, IFinanceLock financeLock) : IApprovalHandler, ITransientDependency
{
    public string Type => ApprovalTypes.PayrollPeriodChange;
    public async Task OnApprovedAsync(ApprovalRequest request)
    {
        await financeLock.AcquireAsync();
        using var json = JsonDocument.Parse(request.Payload);
        if (json.RootElement.TryGetProperty("cancel", out var cancel) && cancel.GetBoolean())
            await manager.CancelVisitAsync(request.EntityId,json.RootElement.GetProperty("stamp").GetString()!,json.RootElement.GetProperty("reason").GetString()!,true);
        else await manager.ApplyCorrectionAsync(await manager.GetVisitAsync(request.EntityId),JsonSerializer.Deserialize<CorrectionData>(request.Payload,ApprovalManager.Json)!);
    }
    public async Task OnRejectedAsync(ApprovalRequest request)
    { await financeLock.AcquireAsync(); var v = await visits.GetAsync(request.EntityId); v.ApprovalState = VisitApprovalState.None; await visits.UpdateAsync(v,true); }
}
[ExposeServices(typeof(IPatientMergeContributor))]
public class FinancePatientMergeContributor(IRepository<Visit, Guid> visits, IRepository<Payment, Guid> payments, IFinanceLock financeLock) : IPatientMergeContributor, ITransientDependency
{
    public string Name => "finance";
    public async Task<int> MoveAsync(Guid sourcePatientId, Guid targetPatientId)
    { await financeLock.AcquireAsync(); var vs = await visits.GetListAsync(v => v.PatientId == sourcePatientId); foreach (var v in vs) v.PatientId = targetPatientId; await visits.UpdateManyAsync(vs); var ps = await payments.GetListAsync(p => p.PatientId == sourcePatientId); foreach (var p in ps) p.PatientId = targetPatientId; await payments.UpdateManyAsync(ps); return vs.Count + ps.Count; }
}
