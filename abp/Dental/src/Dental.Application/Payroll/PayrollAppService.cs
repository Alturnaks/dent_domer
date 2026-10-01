using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dental.Finance;
using Dental.Permissions;
using Dental.Schedule;
using Dental.Settings;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace Dental.Payroll;
[Authorize]
public class PayrollAppService(IFinanceLock financeLock, AppointmentManager calendar, ISettingProvider settings) : DentalAppService, IPayrollAppService
{
    private IRepository<T,Guid> R<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T,Guid>>();
    private Task<bool> All() => AuthorizationService.IsGrantedAsync(DentalPermissions.Payroll.ViewAll);
    private Task<bool> Manage() => AuthorizationService.IsGrantedAsync(DentalPermissions.Payroll.Manage);
    private async Task Read() { if (!await All() && !await Manage() && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Payroll.ViewOwn)) throw new AbpAuthorizationException(); }
    private async Task Write() { await AuthorizationService.CheckAsync(DentalPermissions.Payroll.Manage); await financeLock.AcquireAsync(); }
    private static void Stamp(string expected, string actual) { if (expected != actual) throw new UserFriendlyException("Данные изменились. Обновите страницу."); }
    public async Task<List<PayrollEmployeeDto>> GetEmployeesAsync(Guid branchId)
    {
        await Read(); await BranchScope.EnsureCanAccessAsync(branchId);
        var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id; var all = await All() || await Manage();
        return (await R<Employee>().GetListAsync()).Where(e => e.IsActive && e.HasBranch(branchId) && (all || e.Id == own)).OrderBy(e => e.FullName).Select(e => new PayrollEmployeeDto(e.Id,e.FullName)).ToList();
    }
    public async Task<List<PayrollSchemeDto>> GetSchemesAsync(Guid branchId)
    {
        var employees = await GetEmployeesAsync(branchId); var ids = employees.Select(e => e.Id).ToList();
        return (await R<PayrollScheme>().GetListAsync(s => ids.Contains(s.EmployeeId))).OrderByDescending(s => s.ValidFrom).Select(s => Map(s, employees.Single(e => e.Id == s.EmployeeId).Name)).ToList();
    }
    private static PayrollSchemeDto Map(PayrollScheme s, string name) => new(s.Id,s.EmployeeId,name,s.Type,s.Percent,s.FixedAmount,s.ShiftRate,s.ValidFrom);
    public async Task<PayrollSchemeDto> SaveSchemeAsync(PayrollSchemeInput input)
    {
        await Write(); var employee = await R<Employee>().GetAsync(input.EmployeeId);
        var accessible = await GetEmployeesForScopeAsync(); if (!accessible.Any(e => e.Id == employee.Id) || !employee.IsActive) throw new AbpAuthorizationException();
        if (await R<PayrollScheme>().AnyAsync(s => s.EmployeeId == input.EmployeeId && s.ValidFrom == input.ValidFrom)) throw new UserFriendlyException("На эту дату уже существует схема. Создайте следующую версию с новой датой.");
        var scheme = new PayrollScheme(GuidGenerator.Create(),CurrentTenant.Id,input.EmployeeId,input.Type,input.Percent,input.FixedAmount,input.ShiftRate,input.ValidFrom);
        await R<PayrollScheme>().InsertAsync(scheme,true); return Map(scheme,employee.FullName);
    }
    private async Task<List<Employee>> GetEmployeesForScopeAsync()
    {
        var branches = await AsyncExecuter.ToListAsync(await BranchScope.ApplyAsync(await R<Dental.Branches.Branch>().GetQueryableAsync(),b => b.Id));
        return (await R<Employee>().GetListAsync()).Where(e => branches.Any(b => e.HasBranch(b.Id))).ToList();
    }
    public async Task<List<PayrollPeriodDto>> GetPeriodsAsync(Guid branchId)
    {
        await Read(); await BranchScope.EnsureCanAccessAsync(branchId); var all = await All() || await Manage(); var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id;
        var periods = await R<PayrollPeriod>().GetListAsync(p => p.BranchId == branchId && (all || p.Status != PayrollPeriodStatus.Draft)); var ids = periods.Select(p => p.Id).ToList();
        var entries = await R<PayrollEntry>().GetListAsync(e => ids.Contains(e.PeriodId) && (all || e.EmployeeId == own));
        return periods.OrderByDescending(p => p.PeriodStart).Select(p => Map(p,entries.Where(e => e.PeriodId == p.Id).Sum(e => e.Total))).ToList();
    }
    private static PayrollPeriodDto Map(PayrollPeriod p, long total) => new(p.Id,p.BranchId,p.PeriodStart,p.PeriodEnd,p.Status,p.ConcurrencyStamp,total,p.ApprovedAt,p.PaidAt);
    public async Task<PayrollDetailDto> GetAsync(Guid id)
    {
        await Read(); var period = await R<PayrollPeriod>().GetAsync(id); await BranchScope.EnsureCanAccessAsync(period.BranchId);
        var all = await All() || await Manage(); if (!all && period.Status == PayrollPeriodStatus.Draft) throw new AbpAuthorizationException(); var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id;
        var entries = await R<PayrollEntry>().GetListAsync(e => e.PeriodId == id && (all || e.EmployeeId == own)); var ids = entries.Select(e => e.EmployeeId).ToList(); var employees = await R<Employee>().GetListAsync(e => ids.Contains(e.Id));
        return new(Map(period,entries.Sum(e => e.Total)),entries.Select(e => new PayrollEntryDto(e.Id,e.EmployeeId,employees.FirstOrDefault(d => d.Id == e.EmployeeId)?.FullName ?? "—",e.BaseRevenue,e.MaterialsCost,e.Accrued,e.Bonus,e.Penalty,e.Total,e.Comment,e.ConcurrencyStamp,JsonSerializer.Deserialize<List<PayrollVisitDetail>>(e.Details) ?? [])).ToList());
    }
    public async Task<PayrollDetailDto> CreateAsync(PayrollPeriodInput input)
    {
        await Write(); await BranchScope.EnsureCanAccessAsync(input.BranchId);
        var period = new PayrollPeriod(GuidGenerator.Create(),CurrentTenant.Id,input.BranchId,input.Start,input.End);
        if (await R<PayrollPeriod>().AnyAsync(p => p.BranchId == input.BranchId && p.PeriodStart <= input.End && p.PeriodEnd >= input.Start)) throw new UserFriendlyException("Периоды ведомостей не должны пересекаться.");
        await R<PayrollPeriod>().InsertAsync(period,true); await Calculate(period); return await GetAsync(period.Id);
    }
    public async Task<PayrollDetailDto> RecalculateAsync(Guid id, PayrollStampInput input)
    { await Write(); var p = await Period(id,input); p.EnsureDraft(); await Calculate(p); await R<PayrollPeriod>().UpdateAsync(p,true); return await GetAsync(id); }
    private async Task<PayrollPeriod> Period(Guid id, PayrollStampInput input)
    { var p = await R<PayrollPeriod>().GetAsync(id); await BranchScope.EnsureCanAccessAsync(p.BranchId); Stamp(input.ConcurrencyStamp,p.ConcurrencyStamp); return p; }
    private async Task Calculate(PayrollPeriod period)
    {
        var tz = await calendar.GetTimezoneAsync(); var from = SlotCalculator.ToUtc(period.PeriodStart.ToDateTime(TimeOnly.MinValue),tz); var to = SlotCalculator.ToUtc(period.PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue),tz);
        var onlyPaid = await settings.GetAsync<bool>(DentalSettings.PayrollOnlyPaidVisits);
        var visits = await AsyncExecuter.ToListAsync((await R<Visit>().WithDetailsAsync(v => v.Items,v => v.Materials)).Where(v => v.BranchId == period.BranchId && v.Status == VisitStatus.Closed && v.ClosedAt >= from && v.ClosedAt < to && (!onlyPaid || v.PaidTotal >= v.Total)));
        var employees = (await R<Employee>().GetListAsync()).Where(e => e.HasBranch(period.BranchId)).ToList();
        var schemes = await R<PayrollScheme>().GetListAsync(s => s.ValidFrom <= period.PeriodEnd); var previous = await R<PayrollEntry>().GetListAsync(e => e.PeriodId == period.Id);
        foreach (var employee in employees)
        {
            var scheme = schemes.Where(s => s.EmployeeId == employee.Id).OrderByDescending(s => s.ValidFrom).FirstOrDefault(); if (scheme == null) continue;
            var details = visits.Select(v => new PayrollVisitDetail(v.Id,DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(v.ClosedAt!.Value,tz)),v.ActiveItems.Where(i => i.DoctorId == employee.Id).Sum(i => i.Total),v.ActiveMaterials.Where(m => m.VisitItemId == null ? v.DoctorId == employee.Id : v.ActiveItems.Any(i => i.Id == m.VisitItemId && i.DoctorId == employee.Id)).Sum(m => m.Cost),v.PaidTotal >= v.Total)).Where(d => d.Revenue > 0 || d.MaterialsCost > 0).ToList();
            var entry = previous.FirstOrDefault(e => e.EmployeeId == employee.Id); var fresh = entry == null; entry ??= new PayrollEntry(GuidGenerator.Create(),CurrentTenant.Id,period.Id,employee.Id);
            var revenue = details.Sum(d => d.Revenue); var materials = details.Sum(d => d.MaterialsCost);
            entry.Calculate(revenue,materials,PayrollCalculator.Accrue(scheme,revenue,materials,details.Select(d => d.Date).Distinct().Count()),JsonSerializer.Serialize(details));
            if (fresh) await R<PayrollEntry>().InsertAsync(entry,true); else await R<PayrollEntry>().UpdateAsync(entry,true);
        }
        foreach (var stale in previous.Where(e => !employees.Any(d => d.Id == e.EmployeeId && schemes.Any(s => s.EmployeeId == d.Id)))) await R<PayrollEntry>().DeleteAsync(stale);
    }
    public async Task<PayrollDetailDto> ApproveAsync(Guid id, PayrollStampInput input)
    { await Write(); var p = await Period(id,input); p.EnsureDraft(); await Calculate(p); p.Approve(CurrentUser.GetId(),Clock.Now); await R<PayrollPeriod>().UpdateAsync(p,true); return await GetAsync(id); }
    public async Task<PayrollDetailDto> MarkPaidAsync(Guid id, PayrollStampInput input)
    { await Write(); var p = await Period(id,input); p.MarkPaid(Clock.Now); await R<PayrollPeriod>().UpdateAsync(p,true); return await GetAsync(id); }
    public async Task<PayrollDetailDto> AdjustAsync(Guid id, PayrollAdjustmentInput input)
    { await Write(); var e = await R<PayrollEntry>().GetAsync(id); var p = await R<PayrollPeriod>().GetAsync(e.PeriodId); await BranchScope.EnsureCanAccessAsync(p.BranchId); p.EnsureDraft(); Stamp(input.ConcurrencyStamp,e.ConcurrencyStamp); e.Adjust(input.Bonus,input.Penalty,input.Comment); await R<PayrollEntry>().UpdateAsync(e,true); await R<PayrollPeriod>().UpdateAsync(p,true); return await GetAsync(p.Id); }
}
