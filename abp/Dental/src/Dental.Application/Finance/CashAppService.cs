using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Patients;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Dental.Finance;

[Authorize]
public class CashAppService(FinanceManager manager, IRepository<CashRegister, Guid> registers, IRepository<CashShift, Guid> shifts, IRepository<Payment, Guid> payments, IRepository<Patient, Guid> patients, IRepository<ExpenseCategory, Guid> categories, IRepository<Expense, Guid> expenses, IRepository<CashOperation, Guid> operations) : DentalAppService, ICashAppService
{
    private async Task CanReadAsync()
    { if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Cash.PaymentCreate) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Cash.PaymentRefund) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Cash.ShiftOpenClose) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Cash.ExpenseCreate) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Reports.Finance)) throw new AbpAuthorizationException(); }
    public async Task<List<CashRegisterDto>> GetRegistersAsync(Guid branchId) { await CanReadAsync(); await BranchScope.EnsureCanAccessAsync(branchId); return (await registers.GetListAsync(r => r.BranchId == branchId)).Select(r => new CashRegisterDto(r.Id, r.BranchId, r.Name)).ToList(); }
    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<CashRegisterDto> CreateRegisterAsync(CreateRegisterInput input)
    { await BranchScope.EnsureCanAccessAsync(input.BranchId); await LazyServiceProvider.LazyGetRequiredService<IRepository<Dental.Branches.Branch, Guid>>().GetAsync(input.BranchId); var r = await registers.InsertAsync(new CashRegister(GuidGenerator.Create(), CurrentTenant.Id, input.BranchId, input.Name), true); return new(r.Id, r.BranchId, r.Name); }
    public async Task<List<CashShiftDto>> GetShiftsAsync(Guid branchId) { await CanReadAsync(); await BranchScope.EnsureCanAccessAsync(branchId); var result = new List<CashShiftDto>(); foreach (var s in (await shifts.GetListAsync(s => s.BranchId == branchId)).OrderByDescending(s => s.OpenedAt).Take(100)) result.Add(await MapShiftAsync(s)); return result; }
    public async Task<CashShiftDto> OpenShiftAsync(OpenShiftInput input) => await MapShiftAsync(await manager.OpenShiftAsync(input.CashRegisterId, input.OpeningBalance));
    public async Task<CashShiftDto> CloseShiftAsync(Guid id, CloseShiftInput input) => await MapShiftAsync(await manager.CloseShiftAsync(id, input.ConcurrencyStamp, input.Actual, input.Comment));
    private async Task<CashShiftDto> MapShiftAsync(CashShift s) => new() { Id = s.Id, BranchId = s.BranchId, CashRegisterId = s.CashRegisterId, RegisterName = (await registers.GetAsync(s.CashRegisterId)).Name, ConcurrencyStamp = s.ConcurrencyStamp, OpenedAt = s.OpenedAt, ClosedAt = s.ClosedAt, Status = s.Status, OpeningBalance = s.OpeningBalance, ExpectedCash = await manager.ExpectedCashAsync(s), ClosingBalanceActual = s.ClosingBalanceActual, Difference = s.Difference };
    private async Task<List<PaymentDto>> MapPaymentsAsync(List<Payment> rows)
    { var ids = rows.Select(p => p.PatientId).Distinct().ToList(); var names = (await patients.GetListAsync(p => ids.Contains(p.Id))).ToDictionary(p => p.Id, p => p.FullName); return rows.Select(p => new PaymentDto(p.Id, p.BranchId, p.PatientId, names.GetValueOrDefault(p.PatientId, ""), p.VisitId, p.CashShiftId, p.Method, p.Amount, p.Type, p.State, p.RefundedPaymentId, p.Comment, p.CreationTime)).ToList(); }
    public async Task<List<PaymentDto>> GetPaymentsAsync(FinanceListInput input)
    {
        await CanReadAsync(); var q = await BranchScope.ApplyAsync(await payments.GetQueryableAsync(), p => p.BranchId);
        if (input.BranchId != null) q = q.Where(p => p.BranchId == input.BranchId); if (input.PatientId != null) q = q.Where(p => p.PatientId == input.PatientId); if (input.ShiftId != null) q = q.Where(p => p.CashShiftId == input.ShiftId);
        if (input.From != null) q = q.Where(p => p.CreationTime >= input.From); if (input.To != null) q = q.Where(p => p.CreationTime < input.To);
        return await MapPaymentsAsync(await AsyncExecuter.ToListAsync(q.OrderByDescending(p => p.CreationTime).Take(Math.Clamp(input.MaxResultCount, 1, 500))));
    }
    public async Task<List<PaymentDto>> PayAsync(PayInput input) => await MapPaymentsAsync(await manager.PayAsync(input.ShiftId, input.PatientId, input.VisitId, input.Parts.Select(p => new PaymentPart(p.Method, p.Amount)).ToList(), input.IdempotencyKey, input.Comment));
    public async Task<PaymentDto> RefundAsync(Guid id, RefundInput input) => (await MapPaymentsAsync([await manager.RefundAsync(id, input.ShiftId, input.Amount, input.IdempotencyKey, input.Reason)]))[0];
    public async Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync() { await CanReadAsync(); return (await categories.GetListAsync()).Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Type)).ToList(); }
    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<ExpenseCategoryDto> CreateExpenseCategoryAsync(CreateExpenseCategoryInput input)
    { if (!Enum.IsDefined(input.Type)) throw new BusinessException("Dental:FinanceInvalidInput"); var c = await categories.InsertAsync(new ExpenseCategory(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Type), true); return new(c.Id, c.Name, c.Type); }
    private async Task<ExpenseDto> MapExpenseAsync(Expense e) => new(e.Id, e.BranchId, e.CategoryId, (await categories.GetAsync(e.CategoryId)).Name, e.Amount, e.PaidAt, e.CashShiftId, e.Counterparty, e.Comment);
    public async Task<List<ExpenseDto>> GetExpensesAsync(FinanceListInput input)
    { await CanReadAsync(); var q = await BranchScope.ApplyAsync(await expenses.GetQueryableAsync(), e => e.BranchId); if (input.BranchId != null) q = q.Where(e => e.BranchId == input.BranchId); if (input.From != null) q = q.Where(e => e.PaidAt >= input.From); if (input.To != null) q = q.Where(e => e.PaidAt < input.To); var rows = await AsyncExecuter.ToListAsync(q.OrderByDescending(e => e.PaidAt).Take(500)); var result = new List<ExpenseDto>(); foreach (var e in rows) result.Add(await MapExpenseAsync(e)); return result; }
    public async Task<ExpenseDto> CreateExpenseAsync(ExpenseInput input) => await MapExpenseAsync(await manager.ExpenseAsync(input.BranchId, input.CategoryId, input.Amount, input.ShiftId, input.Counterparty, input.Comment));
    public async Task<List<CashOperationDto>> GetOperationsAsync(Guid shiftId) { await CanReadAsync(); var s = await shifts.GetAsync(shiftId); await BranchScope.EnsureCanAccessAsync(s.BranchId); return (await operations.GetListAsync(o => o.CashShiftId == shiftId)).Select(MapOperation).ToList(); }
    private static CashOperationDto MapOperation(CashOperation o) => new(o.Id, o.CashShiftId, o.Type, o.Amount, o.Comment, o.CreationTime);
    public async Task<CashOperationDto> CreateOperationAsync(CashOperationInput input) => MapOperation(await manager.OperationAsync(input.ShiftId, input.Type, input.Amount, input.Comment));
}
