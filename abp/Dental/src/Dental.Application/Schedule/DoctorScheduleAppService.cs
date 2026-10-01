using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Settings;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace Dental.Schedule;

[Authorize]
public class DoctorScheduleAppService(
    IRepository<DoctorSchedule, Guid> schedules,
    IRepository<ScheduleException, Guid> exceptions,
    IRepository<TimeBlock, Guid> blocks,
    IRepository<Employee, Guid> employees,
    DoctorScheduleManager manager,
    ISettingProvider settings) : DentalAppService, IDoctorScheduleAppService
{
    private async Task EnsureCanReadAsync(Guid branchId, Guid doctorId)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        if (await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll)
            || await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.DoctorSchedulesManage)) return;
        if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewOwn)
            || (await BranchScope.GetCurrentEmployeeAsync())?.Id != doctorId) throw new AbpAuthorizationException();
    }

    public async Task<ListResultDto<DoctorScheduleDto>> GetListAsync(Guid branchId, Guid doctorId)
    {
        await EnsureCanReadAsync(branchId, doctorId);
        var rows = await schedules.GetListAsync(s => s.BranchId == branchId && s.DoctorId == doctorId);
        return new(rows.OrderBy(s => s.Weekday).ThenBy(s => s.StartTime).Select(ToDto).ToList());
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task<ListResultDto<DoctorScheduleDto>> ReplaceWeekAsync(ReplaceDoctorWeekDto input)
    {
        await BranchScope.EnsureCanAccessAsync(input.BranchId);
        var rows = await manager.ReplaceWeekAsync(CurrentTenant.Id, input.BranchId, input.DoctorId, input.ValidFrom,
            input.Days.Select(d => new DoctorShiftData(d.Weekday, d.StartTime, d.EndTime, d.ChairId)).ToList());
        return new(rows.Select(ToDto).ToList());
    }

    private async Task EnsureDoctorScopeAsync(Guid doctorId, Guid? branchId, bool write)
    {
        var doctor = await employees.GetAsync(doctorId);
        var scope = await BranchScope.GetAsync();
        if (branchId is { } b)
        {
            await BranchScope.EnsureCanAccessAsync(b);
            await manager.ValidateResourcesAsync(b, doctorId, null);
        }
        else if (!scope.AllBranches && (doctor.AllBranches || doctor.BranchIds.Any(id => !scope.Contains(id))))
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        if (write) return;
        if (await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll)
            || await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.DoctorSchedulesManage)) return;
        if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewOwn)
            || (await BranchScope.GetCurrentEmployeeAsync())?.Id != doctorId) throw new AbpAuthorizationException();
    }

    public async Task<ListResultDto<ScheduleExceptionDto>> GetExceptionsAsync(Guid doctorId, DateOnly from, DateOnly to)
    {
        if (to < from) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        // Global absences are relevant to every doctor's branch; listing still respects the caller's scope.
        var doctor = await employees.GetAsync(doctorId);
        var scope = await BranchScope.GetAsync();
        if (!scope.AllBranches && !doctor.AllBranches && !doctor.BranchIds.Any(scope.Contains))
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll)
            && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.DoctorSchedulesManage)
            && (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewOwn)
                || (await BranchScope.GetCurrentEmployeeAsync())?.Id != doctorId)) throw new AbpAuthorizationException();
        var rows = await exceptions.GetListAsync(e => e.DoctorId == doctorId && e.DateFrom <= to && e.DateTo >= from);
        return new(rows.Where(e => e.BranchId == null || scope.Contains(e.BranchId.Value)).Select(ToDto).ToList());
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task<ScheduleExceptionDto> CreateExceptionAsync(CreateScheduleExceptionDto input)
    {
        await EnsureDoctorScopeAsync(input.DoctorId, input.BranchId, true);
        var row = new ScheduleException(GuidGenerator.Create(), CurrentTenant.Id, input.DoctorId, input.BranchId,
            input.DateFrom, input.DateTo, input.Type, input.StartTime, input.EndTime, input.Comment);
        await exceptions.InsertAsync(row, autoSave: true);
        return ToDto(row);
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task DeleteExceptionAsync(Guid id)
    {
        var row = await exceptions.GetAsync(id);
        await EnsureDoctorScopeAsync(row.DoctorId, row.BranchId, true);
        await exceptions.DeleteAsync(row);
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task<ListResultDto<TimeBlockDto>> GetBlocksAsync(Guid branchId, DateTimeOffset from, DateTimeOffset to)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        if (to <= from) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var start = from.UtcDateTime; var end = to.UtcDateTime;
        return new((await blocks.GetListAsync(b => b.BranchId == branchId && b.StartsAt < end && b.EndsAt > start)).Select(ToDto).ToList());
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task<TimeBlockDto> CreateBlockAsync(CreateTimeBlockDto input)
    {
        await BranchScope.EnsureCanAccessAsync(input.BranchId);
        await manager.ValidateResourcesAsync(input.BranchId, input.DoctorId, input.ChairId);
        var row = new TimeBlock(GuidGenerator.Create(), CurrentTenant.Id, input.BranchId, input.DoctorId, input.ChairId,
            input.StartsAt.UtcDateTime, input.EndsAt.UtcDateTime, input.Reason);
        await blocks.InsertAsync(row, autoSave: true);
        return ToDto(row);
    }

    [Authorize(DentalPermissions.Schedule.DoctorSchedulesManage)]
    public async Task DeleteBlockAsync(Guid id)
    {
        var row = await blocks.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(row.BranchId);
        await blocks.DeleteAsync(row);
    }

    public async Task<ListResultDto<WorkingIntervalDto>> GetWorkingIntervalsAsync(Guid branchId, Guid doctorId, DateOnly date)
    {
        await EnsureCanReadAsync(branchId, doctorId);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(await settings.GetOrNullAsync(DentalSettings.Timezone) ?? "Asia/Almaty");
        var shifts = await schedules.GetListAsync(s => s.BranchId == branchId && s.DoctorId == doctorId);
        var absences = await exceptions.GetListAsync(e => e.DoctorId == doctorId && e.DateFrom <= date && e.DateTo >= date);
        var working = SlotCalculator.WorkingIntervals(date, branchId, shifts, absences, timezone).ToList();
        var dayStart = SlotCalculator.ToUtc(date.ToDateTime(TimeOnly.MinValue), timezone);
        var dayEnd = SlotCalculator.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone);
        var blocked = await blocks.GetListAsync(b => b.BranchId == branchId && b.StartsAt < dayEnd && b.EndsAt > dayStart);
        foreach (var block in blocked.Where(b => b.DoctorId == doctorId || (b.DoctorId == null && b.ChairId == null)))
            working = SlotCalculator.Subtract(working, new(block.StartsAt, block.EndsAt));
        // Chair-only blocks need the chair selected for the applicable shift.
        foreach (var shift in shifts.Where(s => s.AppliesTo(date) && s.ChairId != null))
            foreach (var block in blocked.Where(b => b.ChairId == shift.ChairId))
            {
                var interval = SlotCalculator.ToUtc(date, shift.StartTime, shift.EndTime, timezone);
                var cut = new TimeRange(block.StartsAt > interval.Start ? block.StartsAt : interval.Start,
                    block.EndsAt < interval.End ? block.EndsAt : interval.End);
                if (cut.End > cut.Start) working = SlotCalculator.Subtract(working, cut);
            }
        return new(working.Select(w => new WorkingIntervalDto { StartsAt = w.Start, EndsAt = w.End }).ToList());
    }

    private static DoctorScheduleDto ToDto(DoctorSchedule s) => new()
    {
        Id = s.Id, DoctorId = s.DoctorId, BranchId = s.BranchId, Weekday = s.Weekday, StartTime = s.StartTime,
        EndTime = s.EndTime, ChairId = s.ChairId, ValidFrom = s.ValidFrom, ValidTo = s.ValidTo
    };
    private static ScheduleExceptionDto ToDto(ScheduleException e) => new()
    {
        Id = e.Id, DoctorId = e.DoctorId, BranchId = e.BranchId, DateFrom = e.DateFrom, DateTo = e.DateTo,
        Type = e.Type, StartTime = e.StartTime, EndTime = e.EndTime, Comment = e.Comment
    };
    private static TimeBlockDto ToDto(TimeBlock b) => new()
    {
        Id = b.Id, BranchId = b.BranchId, DoctorId = b.DoctorId, ChairId = b.ChairId,
        StartsAt = new DateTimeOffset(b.StartsAt, TimeSpan.Zero), EndsAt = new DateTimeOffset(b.EndsAt, TimeSpan.Zero), Reason = b.Reason
    };
}
