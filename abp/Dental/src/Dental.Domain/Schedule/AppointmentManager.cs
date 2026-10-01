using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Patients;
using Dental.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Dental.Schedule;

public class AppointmentManager(IAppointmentStore appointments, IRepository<Patient, Guid> patients,
    IRepository<DoctorSchedule, Guid> schedules, IRepository<ScheduleException, Guid> exceptions,
    IRepository<TimeBlock, Guid> blocks, DoctorScheduleManager resources, ISettingProvider settings) : DomainService
{
    public async Task<TimeZoneInfo> GetTimezoneAsync() => TimeZoneInfo.FindSystemTimeZoneById(
        await settings.GetOrNullAsync(DentalSettings.Timezone) ?? "Asia/Almaty");

    public async Task ValidatePatientAsync(Guid id)
    {
        if ((await patients.GetAsync(id)).MergedIntoId != null) throw new BusinessException(DentalDomainErrorCodes.PatientMergeInvalid);
    }

    public async Task<IReadOnlyList<TimeRange>> GetWorkingAsync(Guid branchId, Guid doctorId, DateOnly date)
    {
        var shifts = await schedules.GetListAsync(s => s.BranchId == branchId && s.DoctorId == doctorId);
        var absences = await exceptions.GetListAsync(e => e.DoctorId == doctorId && e.DateFrom <= date && e.DateTo >= date);
        return SlotCalculator.WorkingIntervals(date, branchId, shifts, absences, await GetTimezoneAsync());
    }

    public async Task ValidateSlotAsync(Guid branchId, Guid doctorId, Guid? chairId, DateTime start, DateTime end, bool force, Guid? excludeId = null)
    {
        if (end <= start || end - start > TimeSpan.FromDays(1)) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        await resources.ValidateResourcesAsync(branchId, doctorId, chairId);
        var timezone = await GetTimezoneAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, timezone));
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end.AddTicks(-1), timezone));
        var slot = new TimeRange(start, end);
        // Force allows an additional working interval, never an absence or a blocked resource.
        var absences = await exceptions.GetListAsync(e => e.DoctorId == doctorId && (e.BranchId == null || e.BranchId == branchId)
            && e.Type != ScheduleExceptionType.ExtraShift && e.DateFrom <= lastDate && e.DateTo >= date);
        for (var day = date; day <= lastDate; day = day.AddDays(1))
            foreach (var absence in absences.Where(e => e.Covers(day)))
                if (absence.StartTime == null || SlotCalculator.ToUtc(day, absence.StartTime.Value, absence.EndTime!.Value, timezone).Overlaps(slot))
                    throw new BusinessException(DentalDomainErrorCodes.AppointmentDoctorNotWorking);
        var working = new List<TimeRange>();
        for (var day = date; day <= lastDate; day = day.AddDays(1)) working.AddRange(await GetWorkingAsync(branchId, doctorId, day));
        if (!force && !SlotCalculator.Merge(working).Any(w => w.Contains(slot)))
            throw new BusinessException(DentalDomainErrorCodes.AppointmentDoctorNotWorking).WithData("working", working);
        var blocked = await blocks.GetListAsync(b => b.StartsAt < end && b.EndsAt > start &&
            (b.DoctorId == doctorId || (b.BranchId == branchId && ((b.DoctorId == null && b.ChairId == null) || (chairId != null && b.ChairId == chairId)))));
        if (blocked.Count > 0) throw new BusinessException(DentalDomainErrorCodes.AppointmentSlotConflict).WithData("blockId", blocked[0].Id);
        var conflict = await appointments.FindAsync(a => a.Id != excludeId && a.StartsAt < end && a.EndsAt > start
            && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow
            && (a.DoctorId == doctorId || (chairId != null && a.ChairId == chairId)));
        if (conflict != null) throw new BusinessException(DentalDomainErrorCodes.AppointmentSlotConflict).WithData("appointmentId", conflict.Id);
    }

    public async Task<List<TimeRange>> GetFreeSlotsAsync(Guid branchId, Guid doctorId, Guid? chairId, DateOnly date, int duration, int step)
    {
        await resources.ValidateResourcesAsync(branchId, doctorId, chairId);
        if (duration < 5 || duration > 1440 || step < 1 || step > 120) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var timezone = await GetTimezoneAsync();
        var start = SlotCalculator.ToUtc(date.ToDateTime(TimeOnly.MinValue), timezone);
        var end = SlotCalculator.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone);
        var occupied = await appointments.GetListAsync(a => a.StartsAt < end && a.EndsAt > start
            && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow
            && (a.DoctorId == doctorId || (chairId != null && a.ChairId == chairId)));
        var blocked = await blocks.GetListAsync(b => b.StartsAt < end && b.EndsAt > start &&
            (b.DoctorId == doctorId || (b.BranchId == branchId && ((b.DoctorId == null && b.ChairId == null) || (chairId != null && b.ChairId == chairId)))));
        return SlotCalculator.FreeSlots(await GetWorkingAsync(branchId, doctorId, date),
            occupied.Select(a => new TimeRange(a.StartsAt, a.EndsAt)).Concat(blocked.Select(b => new TimeRange(b.StartsAt, b.EndsAt))),
            TimeSpan.FromMinutes(duration), TimeSpan.FromMinutes(step), Clock.Now).ToList();
    }
}
