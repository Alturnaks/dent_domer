using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Catalog;
using Dental.Inventory;
using Dental.Notifications;
using Dental.Patients;
using Dental.Permissions;
using Dental.Roles;
using Dental.Schedule;
using Dental.Staff;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Users;

namespace Dental.Finance;

public record VisitLineData(Guid ServiceId, int Qty, decimal DiscountPct, Guid? DoctorId = null, long? UnitPrice = null, List<int>? ToothNumbers = null);
public record MaterialData(Guid ItemId, decimal Quantity, Guid? VisitItemId = null);
public record CorrectionData(List<VisitLineData> Items, List<MaterialData>? Materials, string Reason, long OriginalTotal, string OriginalStamp);
public record PaymentPart(PaymentMethod Method, long Amount);

public class FinanceManager : DomainService
{
    private IRepository<T, Guid> R<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private IBranchScope Scope => LazyServiceProvider.LazyGetRequiredService<IBranchScope>();
    private IPermissionChecker Permissions => LazyServiceProvider.LazyGetRequiredService<IPermissionChecker>();
    private ApprovalManager Approvals => LazyServiceProvider.LazyGetRequiredService<ApprovalManager>();
    private ICurrentUser User => LazyServiceProvider.LazyGetRequiredService<ICurrentUser>();
    private IFinanceLock Lock => LazyServiceProvider.LazyGetRequiredService<IFinanceLock>();
    private IRoleLimitsProvider Limits => LazyServiceProvider.LazyGetRequiredService<IRoleLimitsProvider>();
    public static void CheckStamp(string expected, string? supplied)
    { if (string.IsNullOrWhiteSpace(supplied) || expected != supplied) throw new BusinessException("Dental:FinanceConcurrency"); }
    private async Task RequireAsync(string permission)
    { if (!await Permissions.IsGrantedAsync(permission)) throw new AbpAuthorizationException(); }
    private async Task RequireVisitEditAsync(Visit visit)
    { if (!await Permissions.IsGrantedAsync(DentalPermissions.Visits.EditOpen) && !(await Permissions.IsGrantedAsync(DentalPermissions.Visits.Complete) && visit.DoctorId == (await Scope.GetCurrentEmployeeAsync())?.Id)) throw new AbpAuthorizationException(); }
    public async Task<Visit> GetVisitAsync(Guid id)
    {
        var visit = await AsyncExecuter.FirstAsync((await R<Visit>().WithDetailsAsync(v => v.Items, v => v.Materials)).Where(v => v.Id == id));
        await Scope.EnsureCanAccessAsync(visit.BranchId);
        if (!await Permissions.IsGrantedAsync(DentalPermissions.Schedule.ViewAll) && !await Permissions.IsGrantedAsync(DentalPermissions.Schedule.Manage) && !await Permissions.IsGrantedAsync(DentalPermissions.Schedule.ViewOwn) && !await Permissions.IsGrantedAsync(DentalPermissions.Visits.EditOpen) && !await Permissions.IsGrantedAsync(DentalPermissions.Visits.Complete) && !await Permissions.IsGrantedAsync(DentalPermissions.Cash.PaymentCreate)) throw new AbpAuthorizationException();
        if (!await Permissions.IsGrantedAsync(DentalPermissions.Schedule.ViewAll) && !await Permissions.IsGrantedAsync(DentalPermissions.Schedule.Manage) && !await Permissions.IsGrantedAsync(DentalPermissions.Cash.PaymentCreate) && visit.DoctorId != (await Scope.GetCurrentEmployeeAsync())?.Id) throw new AbpAuthorizationException();
        return visit;
    }
    public async Task<Visit> OpenFromAppointmentAsync(Guid appointmentId)
    {
        await Lock.AcquireAsync();
        var appointment = await LazyServiceProvider.LazyGetRequiredService<IAppointmentStore>().GetAsync(appointmentId);
        await Scope.EnsureCanAccessAsync(appointment.BranchId);
        var existing = await R<Visit>().FirstOrDefaultAsync(v => v.AppointmentId == appointmentId && v.Status != VisitStatus.Cancelled);
        if (existing != null) return await GetVisitAsync(existing.Id);
        if (appointment.Status != AppointmentStatus.Arrived && appointment.Status != AppointmentStatus.InChair) throw new BusinessException("Dental:VisitArrivalRequired");
        var visit = new Visit(GuidGenerator.Create(), CurrentTenant.Id, appointment.BranchId, appointment.PatientId, appointment.DoctorId, Clock.Now, appointment.Id);
        var rows = appointment.Services.Count > 0 ? appointment.Services : await R<AppointmentLine>().GetListAsync(s => s.AppointmentId == appointmentId);
        var prices = await LazyServiceProvider.LazyGetRequiredService<IPriceResolver>().ResolveAsync(visit.BranchId, rows.Select(r => r.ServiceId).Distinct().ToList());
        foreach (var row in rows)
        {
            if (!prices.TryGetValue(row.ServiceId, out var price)) throw new BusinessException("Dental:VisitPriceMissing");
            visit.Items.Add(new VisitItem(GuidGenerator.Create(), CurrentTenant.Id, visit.Id, row.ServiceId, visit.DoctorId, row.Qty, price, 0));
        }
        await FillMaterialsAsync(visit); visit.Recalculate();
        return await R<Visit>().InsertAsync(visit, true);
    }
    private async Task ValidateLinesAsync(Visit visit, IEnumerable<VisitLineData> lines, bool correction)
    {
        foreach (var line in lines)
        {
            var service = await R<ClinicService>().GetAsync(line.ServiceId);
            if (!service.IsActive && (!correction || !visit.ActiveItems.Any(i => i.ServiceId == line.ServiceId))) throw new BusinessException("Dental:FinanceInvalidInput");
            var doctor = await R<Employee>().GetAsync(line.DoctorId ?? visit.DoctorId);
            var historicalDoctor = correction && visit.ActiveItems.Any(i => i.DoctorId == doctor.Id);
            if (!historicalDoctor && (!doctor.IsActive || doctor.Position != StaffPosition.Doctor || !(doctor.AllBranches || doctor.BranchIds.Contains(visit.BranchId)))) throw new BusinessException("Dental:FinanceInvalidInput");
            if (!correction && !await Permissions.IsGrantedAsync(DentalPermissions.Visits.EditOpen) && doctor.Id != visit.DoctorId) throw new AbpAuthorizationException();
            if (line.DiscountPct > 0) await RequireAsync(DentalPermissions.Catalog.DiscountsApply);
            if (!correction && line.UnitPrice != null) await RequireAsync(DentalPermissions.Catalog.PricesManage);
            _ = new VisitItem(Guid.NewGuid(), CurrentTenant.Id, visit.Id, line.ServiceId, doctor.Id, line.Qty, line.UnitPrice ?? 0, line.DiscountPct, line.ToothNumbers);
        }
    }
    public async Task<Visit> ReplaceOpenItemsAsync(Guid id, string stamp, List<VisitLineData> lines, Guid? assistantId)
    {
        await Lock.AcquireAsync();
        if (lines.Count > 100) throw new BusinessException("Dental:FinanceInvalidInput");
        var visit = await GetVisitAsync(id); CheckStamp(visit.ConcurrencyStamp, stamp); visit.EnsureOpen();
        await RequireVisitEditAsync(visit);
        await ValidateLinesAsync(visit, lines, false);
        if (assistantId != null)
        { var assistant = await R<Employee>().GetAsync(assistantId.Value); if (!assistant.IsActive || assistant.Position != StaffPosition.Assistant || !(assistant.AllBranches || assistant.BranchIds.Contains(visit.BranchId))) throw new BusinessException("Dental:FinanceInvalidInput"); }
        visit.AssistantId = assistantId;
        await Approvals.CancelPendingAsync(nameof(Visit), id, "Visit services changed");
        await ReplaceItemsAsync(visit, lines); await ReplaceMaterialsAsync(visit, null); visit.Recalculate();
        var maxDiscount = !visit.ActiveItems.Any() ? 0 : visit.ActiveItems.Max(i => i.DiscountPct);
        var gate = await Approvals.CheckOrRequestAsync(ApprovalTypes.Discount, nameof(Visit), id, maxDiscount, $"Discount {maxDiscount}%", new { visitId = id }, visit.BranchId);
        visit.ApprovalState = gate.Allowed ? VisitApprovalState.None : VisitApprovalState.PendingDiscount;
        foreach (var item in visit.ActiveItems) item.DiscountPendingApproval = !gate.Allowed;
        await R<Visit>().UpdateAsync(visit, true); return visit;
    }
    private async Task ReplaceItemsAsync(Visit visit, List<VisitLineData> lines)
    {
        // Soft delete old snapshots so closed visit corrections retain their history.
        var oldItems = visit.ActiveItems.ToList(); foreach (var item in oldItems) item.IsDeleted = true;
        await R<VisitItem>().UpdateManyAsync(oldItems);
        var prices = await LazyServiceProvider.LazyGetRequiredService<IPriceResolver>().ResolveAsync(visit.BranchId, lines.Select(i => i.ServiceId).Distinct().ToList());
        foreach (var line in lines)
        {
            var price = line.UnitPrice ?? (prices.TryGetValue(line.ServiceId, out var p) ? p : throw new BusinessException("Dental:VisitPriceMissing"));
            visit.Items.Add(new VisitItem(GuidGenerator.Create(), CurrentTenant.Id, visit.Id, line.ServiceId, line.DoctorId ?? visit.DoctorId, line.Qty, price, line.DiscountPct, line.ToothNumbers));
        }
    }
    private async Task FillMaterialsAsync(Visit visit)
    {
        var techCards = LazyServiceProvider.LazyGetRequiredService<TechCardManager>();
        foreach (var line in visit.ActiveItems)
        {
            var card = await techCards.FindActiveAsync(line.ServiceId);
            if (card != null) foreach (var material in card.Items)
                visit.Materials.Add(new VisitMaterial(GuidGenerator.Create(), CurrentTenant.Id, visit.Id, line.Id, material.ItemId, material.Quantity * line.Qty, material.Quantity * line.Qty));
        }
    }
    private async Task ReplaceMaterialsAsync(Visit visit, List<MaterialData>? materials)
    {
        var oldMaterials = visit.ActiveMaterials.ToList(); var norms = oldMaterials.GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.NormQuantity));
        foreach (var material in oldMaterials) material.IsDeleted = true; await R<VisitMaterial>().UpdateManyAsync(oldMaterials);
        if (materials == null) { await FillMaterialsAsync(visit); return; }
        foreach (var m in materials)
        {
            var item = await R<Item>().GetAsync(m.ItemId);
            if (!item.IsActive || m.VisitItemId != null && !visit.ActiveItems.Any(i => i.Id == m.VisitItemId)) throw new BusinessException("Dental:FinanceInvalidInput");
            visit.Materials.Add(new VisitMaterial(GuidGenerator.Create(), CurrentTenant.Id, visit.Id, m.VisitItemId, m.ItemId, m.Quantity, norms.GetValueOrDefault(m.ItemId, m.Quantity)));
        }
    }
    public async Task<Visit> SetMaterialsAsync(Guid id, string stamp, List<MaterialData> materials)
    {
        await Lock.AcquireAsync(); var v = await GetVisitAsync(id); await RequireVisitEditAsync(v);
        CheckStamp(v.ConcurrencyStamp, stamp); v.EnsureOpen(); await ReplaceMaterialsAsync(v, materials); await R<Visit>().UpdateAsync(v, true); return v;
    }
    private async Task ConsumeAsync(Visit visit)
    {
        visit.ConsumptionDocumentId = null;
        if (!visit.ActiveMaterials.Any()) return;
        var result = await LazyServiceProvider.LazyGetRequiredService<IVisitStockConsumer>().ConsumeAsync(visit.Id, visit.BranchId, visit.PatientId, visit.ActiveMaterials.Select(m => new Dental.Inventory.VisitMaterialUsage(m.Id, m.ItemId, m.Quantity)).ToList());
        visit.ConsumptionDocumentId = result.DocumentId;
        foreach (var m in visit.ActiveMaterials) if (result.Costs.TryGetValue(m.Id, out var c)) { m.Cost = c.Cost; m.BatchId = c.BatchId; }
    }
    private async Task AddBalanceAsync(Guid patientId, long delta)
    { var balance = await LazyServiceProvider.LazyGetRequiredService<PatientManager>().GetOrCreateBalanceAsync(patientId); balance.Add(delta); await R<PatientBalance>().UpdateAsync(balance, true); }
    public async Task<Visit> CloseVisitAsync(Guid id, string stamp)
    {
        await RequireAsync(DentalPermissions.Visits.Complete); await Lock.AcquireAsync(); var v = await GetVisitAsync(id); CheckStamp(v.ConcurrencyStamp, stamp); v.EnsureOpen();
        if (v.ApprovalState != VisitApprovalState.None || !v.ActiveItems.Any()) throw new BusinessException("Dental:VisitPendingApproval");
        v.Recalculate(); await ConsumeAsync(v); await AddBalanceAsync(v.PatientId, -v.Total);
        v.Status = VisitStatus.Closed; v.ClosedAt = Clock.Now; v.ClosedBy = User.Id;
        v.CashShiftId = (await R<CashShift>().FirstOrDefaultAsync(s => s.BranchId == v.BranchId && s.Status == CashShiftStatus.Open))?.Id;
        if (v.AppointmentId != null) { var a = await R<Appointment>().GetAsync(v.AppointmentId.Value); a.ChangeStatus(AppointmentStatus.Completed, Clock.Now); await R<Appointment>().UpdateAsync(a); }
        await R<Visit>().UpdateAsync(v, true); return v;
    }
    public async Task<Visit> CorrectVisitAsync(Guid id, string stamp, List<VisitLineData> lines, List<MaterialData>? materials, string reason)
    {
        await RequireAsync(DentalPermissions.Visits.EditClosed); await Lock.AcquireAsync(); var v = await GetVisitAsync(id); CheckStamp(v.ConcurrencyStamp, stamp);
        if (v.Status != VisitStatus.Closed || v.ApprovalState != VisitApprovalState.None || lines.Count == 0) throw new BusinessException("Dental:VisitNotClosed");
        Check.NotNullOrWhiteSpace(reason, nameof(reason), 2000); await ValidateLinesAsync(v, lines, true);
        var limit = await Limits.GetForCurrentUserAsync();
        if (lines.Any(i => !limit.AllowsDiscount(i.DiscountPct))) throw new BusinessException("Dental:RoleLimitExceeded");
        var closedShift = v.CashShiftId == null || (await R<CashShift>().GetAsync(v.CashShiftId.Value)).Status == CashShiftStatus.Closed;
        var payload = new CorrectionData(lines, materials, reason, v.Total, v.ConcurrencyStamp);
        var payrollLocked = await InApprovedPayrollAsync(v);
        if (payrollLocked || closedShift && !limit.CanEditClosedShiftVisits)
        { v.ApprovalState = VisitApprovalState.PendingCorrection; await R<Visit>().UpdateAsync(v, true); payload = payload with { OriginalStamp = v.ConcurrencyStamp }; await Approvals.RequestAsync(payrollLocked ? ApprovalTypes.PayrollPeriodChange : ApprovalTypes.ClosedVisitEdit, nameof(Visit), v.Id, v.Total, reason, payload, v.BranchId); }
        else await ApplyCorrectionAsync(v, payload);
        return v;
    }
    public async Task ApplyCorrectionAsync(Visit v, CorrectionData data)
    {
        if (v.Status != VisitStatus.Closed || v.Total != data.OriginalTotal || v.ConcurrencyStamp != data.OriginalStamp) throw new BusinessException("Dental:FinanceConcurrency");
        var oldTotal = v.Total;
        if (v.ConsumptionDocumentId != null) await LazyServiceProvider.LazyGetRequiredService<IVisitStockConsumer>().ReverseAsync(v.ConsumptionDocumentId.Value, data.Reason);
        await ReplaceItemsAsync(v, data.Items); await ReplaceMaterialsAsync(v, data.Materials); v.Recalculate(); await ConsumeAsync(v);
        await AddBalanceAsync(v.PatientId, oldTotal - v.Total); v.ApprovalState = VisitApprovalState.None; await R<Visit>().UpdateAsync(v, true);
        await NotifySuspiciousAsync(v.BranchId, nameof(Visit), v.Id, data.Reason, $"Visit corrected: {oldTotal} → {v.Total}");
    }
    private async Task<bool> InApprovedPayrollAsync(Visit v)
    {
        if (v.ClosedAt == null) return false;
        var tz = await LazyServiceProvider.LazyGetRequiredService<AppointmentManager>().GetTimezoneAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(v.ClosedAt.Value, tz));
        return await R<Dental.Payroll.PayrollPeriod>().AnyAsync(p => p.BranchId == v.BranchId && p.Status != Dental.Payroll.PayrollPeriodStatus.Draft && p.PeriodStart <= date && p.PeriodEnd >= date);
    }
    public async Task<Visit> CancelVisitAsync(Guid id, string stamp, string reason, bool payrollApproved = false)
    {
        if (!payrollApproved) await RequireAsync(DentalPermissions.Visits.Cancel); await Lock.AcquireAsync(); var v = await GetVisitAsync(id); CheckStamp(v.ConcurrencyStamp, stamp); Check.NotNullOrWhiteSpace(reason, nameof(reason), 2000);
        if (v.Status == VisitStatus.Cancelled) return v;
        if (v.Status == VisitStatus.Closed)
        {
            if (!payrollApproved && !Approvals.IsOwner()) throw new AbpAuthorizationException();
            if (!payrollApproved && await InApprovedPayrollAsync(v))
            {
                v.ApprovalState = VisitApprovalState.PendingCorrection; await R<Visit>().UpdateAsync(v, true);
                await Approvals.RequestAsync(ApprovalTypes.PayrollPeriodChange, nameof(Visit), v.Id, v.Total, reason, new { cancel = true, reason, stamp = v.ConcurrencyStamp }, v.BranchId); return v;
            }
            if (v.ConsumptionDocumentId != null) await LazyServiceProvider.LazyGetRequiredService<IVisitStockConsumer>().ReverseAsync(v.ConsumptionDocumentId.Value, reason);
            await AddBalanceAsync(v.PatientId, v.Total); await NotifySuspiciousAsync(v.BranchId, nameof(Visit), v.Id, reason, "Closed visit cancelled");
        }
        v.Status = VisitStatus.Cancelled; v.CancelReason = reason; v.ApprovalState = VisitApprovalState.None;
        if (v.AppointmentId != null) { var appointment = await R<Appointment>().GetAsync(v.AppointmentId.Value); appointment.CancelFromVisit(reason); await R<Appointment>().UpdateAsync(appointment); }
        await Approvals.CancelPendingAsync(nameof(Visit), v.Id, reason); await R<Visit>().UpdateAsync(v, true); return v;
    }
    public async Task<CashShift> RequireShiftAsync(Guid id)
    { var s = await R<CashShift>().GetAsync(id); await Scope.EnsureCanAccessAsync(s.BranchId); if (s.Status != CashShiftStatus.Open) throw new BusinessException("Dental:ShiftNotOpen"); return s; }
    public async Task<CashShift> OpenShiftAsync(Guid registerId, long opening)
    {
        await RequireAsync(DentalPermissions.Cash.ShiftOpenClose); await Lock.AcquireAsync(); var r = await R<CashRegister>().GetAsync(registerId); await Scope.EnsureCanAccessAsync(r.BranchId);
        if (await R<CashShift>().AnyAsync(s => s.CashRegisterId == registerId && s.Status == CashShiftStatus.Open)) throw new BusinessException("Dental:ShiftAlreadyOpen");
        return await R<CashShift>().InsertAsync(new CashShift(GuidGenerator.Create(), CurrentTenant.Id, registerId, r.BranchId, User.GetId(), Clock.Now, opening), true);
    }
    public async Task<long> ExpectedCashAsync(CashShift s) => CashShift.ExpectedCash(s.OpeningBalance, await R<Payment>().GetListAsync(p => p.CashShiftId == s.Id), await R<Expense>().GetListAsync(e => e.CashShiftId == s.Id), await R<CashOperation>().GetListAsync(o => o.CashShiftId == s.Id));
    public async Task<CashShift> CloseShiftAsync(Guid id, string stamp, long actual, string? comment)
    {
        await RequireAsync(DentalPermissions.Cash.ShiftOpenClose); await Lock.AcquireAsync(); var s = await RequireShiftAsync(id); CheckStamp(s.ConcurrencyStamp, stamp);
        if (actual < 0) throw new BusinessException("Dental:FinanceInvalidInput");
        s.ClosingBalanceExpected = await ExpectedCashAsync(s); s.ClosingBalanceActual = actual; s.Difference = actual - s.ClosingBalanceExpected;
        s.Status = CashShiftStatus.Closed; s.ClosedBy = User.Id; s.ClosedAt = Clock.Now; s.CloseComment = comment;
        await R<CashShift>().UpdateAsync(s, true);
        if (s.Difference != 0) await NotifySuspiciousAsync(s.BranchId, nameof(CashShift), s.Id, comment ?? "", $"Cash difference: {s.Difference}");
        return s;
    }
    public async Task<List<Payment>> PayAsync(Guid shiftId, Guid patientId, Guid? visitId, List<PaymentPart> parts, string key, string? comment)
    {
        await RequireAsync(DentalPermissions.Cash.PaymentCreate); await Lock.AcquireAsync(); Check.NotNullOrWhiteSpace(key, nameof(key), 100);
        if (parts.Count is < 1 or > 6 || parts.Any(p => p.Amount <= 0 || !Enum.IsDefined(p.Method))) throw new BusinessException("Dental:FinanceInvalidInput");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { shiftId, patientId, visitId, parts, comment }))));
        var prior = await R<Payment>().GetListAsync(p => p.IdempotencyKey == key || p.IdempotencyKey!.StartsWith(key + ":"));
        if (prior.Count > 0)
        { await Scope.EnsureCanAccessAsync(prior[0].BranchId); if (prior.Any(p => p.RequestHash != hash)) throw new BusinessException("Dental:FinanceConcurrency"); return prior; }
        var shift = await RequireShiftAsync(shiftId); var patient = await R<Patient>().GetAsync(patientId);
        if (patient.MergedIntoId != null) throw new BusinessException("Dental:FinanceInvalidInput");
        Visit? visit = visitId == null ? null : await GetVisitAsync(visitId.Value);
        if (visit != null && (visit.PatientId != patientId || visit.BranchId != shift.BranchId || visit.Status == VisitStatus.Cancelled || parts.Sum(p => p.Amount) > Math.Max(0, visit.Total - visit.PaidTotal))) throw new BusinessException("Dental:FinanceInvalidInput");
        var balance = await LazyServiceProvider.LazyGetRequiredService<PatientManager>().GetOrCreateBalanceAsync(patientId);
        var fromBalance = parts.Where(p => p.Method == PaymentMethod.Balance).Sum(p => p.Amount);
        if (fromBalance > 0)
        {
            var visits = await R<Visit>().GetListAsync(v => v.PatientId == patientId && v.Status != VisitStatus.Cancelled);
            var available = Math.Max(0, balance.Balance + visits.Where(v => v.Status == VisitStatus.Closed).Sum(v => v.Debt) - visits.Where(v => v.Status == VisitStatus.Open).Sum(v => v.PaidTotal));
            if (visit == null || fromBalance > available) throw new BusinessException("Dental:InsufficientBalance");
        }
        var created = new List<Payment>();
        foreach (var part in parts)
        {
            var p = new Payment(GuidGenerator.Create(), CurrentTenant.Id, shift.BranchId, patientId, visitId, shift.Id, part.Method, part.Amount, visit == null ? PaymentType.Advance : PaymentType.Payment, parts.Count == 1 ? key : key + ":" + created.Count, comment) { RequestHash = hash };
            created.Add(await R<Payment>().InsertAsync(p)); if (part.Method != PaymentMethod.Balance) balance.Add(part.Amount); if (visit != null) visit.PaidTotal = checked(visit.PaidTotal + part.Amount);
        }
        await R<PatientBalance>().UpdateAsync(balance, true); if (visit != null) await R<Visit>().UpdateAsync(visit, true); return created;
    }
    public async Task<Payment> RefundAsync(Guid paymentId, Guid shiftId, long amount, string key, string reason)
    {
        await RequireAsync(DentalPermissions.Cash.PaymentRefund); await Lock.AcquireAsync(); Check.NotNullOrWhiteSpace(key, nameof(key), 100); Check.NotNullOrWhiteSpace(reason, nameof(reason), 2000);
        var original = await R<Payment>().GetAsync(paymentId); await Scope.EnsureCanAccessAsync(original.BranchId);
        var existing = await R<Payment>().FirstOrDefaultAsync(p => p.IdempotencyKey == key);
        if (existing != null) { if (existing.RefundedPaymentId != paymentId || existing.Amount != amount || existing.Comment != reason) throw new BusinessException("Dental:FinanceConcurrency"); return existing; }
        var shift = await RequireShiftAsync(shiftId);
        if (shift.BranchId != original.BranchId || amount <= 0 || original.Type == PaymentType.Refund || original.State != 0) throw new BusinessException("Dental:FinanceInvalidInput");
        var reserved = (await R<Payment>().GetListAsync(p => p.RefundedPaymentId == paymentId && p.State != 2)).Sum(p => p.Amount);
        if (amount > original.Amount - reserved) throw new BusinessException("Dental:RefundExceedsPayment");
        // Balance allocation is reversed to available credit, never paid out twice as new money.
        var refund = new Payment(GuidGenerator.Create(), CurrentTenant.Id, original.BranchId, original.PatientId, original.VisitId, shift.Id, original.Method, amount, PaymentType.Refund, key, reason) { RefundedPaymentId = original.Id, State = 1 };
        await R<Payment>().InsertAsync(refund, true);
        var gate = await Approvals.CheckOrRequestAsync(ApprovalTypes.Refund, nameof(Payment), refund.Id, amount, reason, new { paymentId }, refund.BranchId);
        if (gate.Allowed) await ApplyRefundAsync(refund);
        await NotifySuspiciousAsync(refund.BranchId, nameof(Payment), refund.Id, reason, "Payment refund"); return refund;
    }
    public async Task ApplyRefundAsync(Payment refund)
    {
        await Lock.AcquireAsync(); if (refund.State != 1) throw new BusinessException("Dental:FinanceConcurrency");
        var s = await R<CashShift>().GetAsync(refund.CashShiftId);
        if (s.Status != CashShiftStatus.Open)
        {
            s = await R<CashShift>().FirstOrDefaultAsync(shift => shift.CashRegisterId == s.CashRegisterId && shift.Status == CashShiftStatus.Open) ?? throw new BusinessException("Dental:ShiftNotOpen");
            refund.CashShiftId = s.Id;
        }
        if (refund.Method != PaymentMethod.Balance) await AddBalanceAsync(refund.PatientId, -refund.Amount);
        if (refund.VisitId != null) { var v = await R<Visit>().GetAsync(refund.VisitId.Value); if (refund.Amount > v.PaidTotal) throw new BusinessException("Dental:RefundExceedsPayment"); v.PaidTotal -= refund.Amount; await R<Visit>().UpdateAsync(v, true); }
        refund.State = 0; await R<Payment>().UpdateAsync(refund, true);
    }
    public async Task<Expense> ExpenseAsync(Guid branchId, Guid categoryId, long amount, Guid? shiftId, string? counterparty, string? comment)
    {
        await RequireAsync(DentalPermissions.Cash.ExpenseCreate); await Scope.EnsureCanAccessAsync(branchId); await Lock.AcquireAsync(); await R<ExpenseCategory>().GetAsync(categoryId);
        if (shiftId != null && (await RequireShiftAsync(shiftId.Value)).BranchId != branchId) throw new BusinessException("Dental:FinanceInvalidInput");
        return await R<Expense>().InsertAsync(new Expense(GuidGenerator.Create(), CurrentTenant.Id, branchId, categoryId, amount, Clock.Now, shiftId, counterparty, comment), true);
    }
    public async Task<CashOperation> OperationAsync(Guid shiftId, CashOperationType type, long amount, string comment)
    {
        await RequireAsync(DentalPermissions.Cash.ShiftOpenClose); await Lock.AcquireAsync(); var shift = await RequireShiftAsync(shiftId);
        if (type == CashOperationType.Collection && amount > await ExpectedCashAsync(shift)) throw new BusinessException("Dental:InsufficientBalance");
        return await R<CashOperation>().InsertAsync(new CashOperation(GuidGenerator.Create(), CurrentTenant.Id, shiftId, type, amount, comment), true);
    }
    private async Task NotifySuspiciousAsync(Guid branchId, string entityType, Guid id, string reason, string title)
    {
        await LazyServiceProvider.LazyGetRequiredService<NotificationManager>().NotifyByPermissionAsync(DentalPermissions.Audit.View, "Suspicious", title, reason, entityType, id, branchId);
    }
}
