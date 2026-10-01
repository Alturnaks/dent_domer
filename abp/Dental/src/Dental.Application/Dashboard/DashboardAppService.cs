using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Branches;
using Dental.Finance;
using Dental.Inventory;
using Dental.Permissions;
using Dental.Schedule;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Dental.Dashboard;

[Authorize]
public class DashboardAppService(AppointmentManager calendar, ApprovalManager approvals) : DentalAppService, IDashboardAppService
{
    private IRepository<T, Guid> R<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private Task<bool> Granted(string permission) => AuthorizationService.IsGrantedAsync(permission);
    public async Task<DashboardDto> GetAsync(DashboardInput input)
    {
        var tz = await calendar.GetTimezoneAsync(); var date = input.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Clock.Now, tz));
        var result = new DashboardDto { Date = date, Timezone = tz.Id, CanSeeFinance = await Granted(DentalPermissions.Reports.Finance) };
        var branches = await AsyncExecuter.ToListAsync(await BranchScope.ApplyAsync(await R<Branch>().GetQueryableAsync(), b => b.Id));
        if (input.BranchId != null) { await BranchScope.EnsureCanAccessAsync(input.BranchId.Value); branches = branches.Where(b => b.Id == input.BranchId).ToList(); }
        var ids = branches.Select(b => b.Id).ToList();
        var from = SlotCalculator.ToUtc(date.ToDateTime(TimeOnly.MinValue), tz); var to = SlotCalculator.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);
        var all = await Granted(DentalPermissions.Schedule.ViewAll) || await Granted(DentalPermissions.Schedule.Manage);
        var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id;
        var rows = new List<Appointment>();
        if (all || await Granted(DentalPermissions.Schedule.ViewOwn))
        {
            rows = await R<Appointment>().GetListAsync(a => ids.Contains(a.BranchId) && a.StartsAt < to && a.EndsAt > from && (all || a.DoctorId == own));
            result.Appointments = rows.Count(a => a.Status != AppointmentStatus.Cancelled); result.Unconfirmed = rows.Count(a => a.Status == AppointmentStatus.Scheduled); result.Arrived = rows.Count(a => a.Status == AppointmentStatus.Arrived || a.Status == AppointmentStatus.InChair); result.Completed = rows.Count(a => a.Status == AppointmentStatus.Completed); result.NoShow = rows.Count(a => a.Status == AppointmentStatus.NoShow);
            var doctors = await R<Employee>().GetListAsync(d => d.IsActive && d.Position == StaffPosition.Doctor && (all || d.Id == own));
            double working = 0; foreach (var branch in branches) foreach (var doctor in doctors.Where(d => d.AllBranches || d.BranchIds.Contains(branch.Id))) working += (await calendar.GetWorkingAsync(branch.Id, doctor.Id, date)).Sum(w => (w.End - w.Start).TotalMinutes);
            var occupied = rows.Where(a => a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow).Sum(a => (new DateTime(Math.Min(a.EndsAt.Ticks, to.Ticks)) - new DateTime(Math.Max(a.StartsAt.Ticks, from.Ticks))).TotalMinutes);
            result.UtilizationPct = working > 0 ? Math.Round((decimal)(occupied / working * 100), 1) : null;
        }
        var closed = new List<Visit>(); var debts = new List<Visit>();
        if (result.CanSeeFinance)
        {
            var weekStart = date.AddDays(-((int)date.DayOfWeek + 6) % 7); var weekFrom = SlotCalculator.ToUtc(weekStart.ToDateTime(TimeOnly.MinValue), tz);
            closed = await R<Visit>().GetListAsync(v => ids.Contains(v.BranchId) && v.Status == VisitStatus.Closed && v.ClosedAt >= weekFrom && v.ClosedAt < to);
            debts = await R<Visit>().GetListAsync(v => ids.Contains(v.BranchId) && v.Status == VisitStatus.Closed && v.Total > v.PaidTotal);
            result.RevenueWeek = closed.Sum(v => v.Total); result.Debt = debts.Sum(v => v.Debt);
            var payments = await R<Payment>().GetListAsync(p => ids.Contains(p.BranchId) && p.CreationTime >= from && p.CreationTime < to && p.State == 0 && p.Method != PaymentMethod.Balance);
            result.ReceivedToday = payments.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        }
        if (all)
        {
            var pending = await R<ApprovalRequest>().GetListAsync(a => a.Status == ApprovalStatus.Pending && (a.BranchId == null || ids.Contains(a.BranchId.Value)));
            foreach (var approval in pending) if (await approvals.CanDecideAsync(approval)) result.PendingApprovals++;
        }
        if (await Granted(DentalPermissions.Cash.ShiftOpenClose)) result.OverdueShifts = await R<CashShift>().CountAsync(s => ids.Contains(s.BranchId) && s.Status == CashShiftStatus.Open && s.OpenedAt < from);
        if (await Granted(DentalPermissions.Inventory.View))
        {
            var ws = await R<Warehouse>().GetListAsync(w => w.BranchId != null && ids.Contains(w.BranchId.Value)); var wids = ws.Select(w => w.Id).ToList();
            var levels = await R<ItemStockLevel>().GetListAsync(l => wids.Contains(l.WarehouseId)); var stock = await R<StockBalance>().GetListAsync(s => wids.Contains(s.WarehouseId));
            result.LowStock = levels.Count(l => stock.Where(s => s.WarehouseId == l.WarehouseId && s.ItemId == l.ItemId).Sum(s => s.Qty) < l.MinQty);
            var batchIds = stock.Where(s => s.Qty > 0 && s.BatchId != null).Select(s => s.BatchId!.Value).Distinct().ToList(); var expiry = date.AddDays(30);
            result.ExpiringBatches = await R<Batch>().CountAsync(b => batchIds.Contains(b.Id) && b.ExpiresAt <= expiry);
        }
        foreach (var b in branches) result.Branches.Add(new BranchDashboardDto(b.Id, b.Name, rows.Count(a => a.BranchId == b.Id && a.Status != AppointmentStatus.Cancelled), rows.Count(a => a.BranchId == b.Id && a.Status == AppointmentStatus.Scheduled), result.CanSeeFinance ? closed.Where(v => v.BranchId == b.Id && v.ClosedAt >= from).Sum(v => v.Total) : null, result.CanSeeFinance ? debts.Where(v => v.BranchId == b.Id).Sum(v => v.Debt) : null));
        return result;
    }
}
