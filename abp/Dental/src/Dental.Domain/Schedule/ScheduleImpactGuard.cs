using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Dental.Schedule;

public class ScheduleImpactGuard(IAppointmentStore appointments, AppointmentManager manager, IRepository<TimeBlock, Guid> blocks) : DomainService
{
    // Called after flushing the proposed schedule inside the same transaction. A failure rolls it back.
    public async Task EnsureAsync(Guid? doctorId, Guid? branchId, DateTime? from = null, DateTime? to = null)
    {
        var start = from > Clock.Now ? from.Value : Clock.Now;
        var rows = await appointments.GetListAsync(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
            && a.EndsAt > start && (to == null || a.StartsAt < to) && (doctorId == null || a.DoctorId == doctorId) && (branchId == null || a.BranchId == branchId));
        var timezone = await manager.GetTimezoneAsync();
        var affected = new List<Guid>();
        foreach (var row in rows)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(row.StartsAt, timezone));
            var working = await manager.GetWorkingAsync(row.BranchId, row.DoctorId, day);
            var blocked = await blocks.AnyAsync(b => b.StartsAt < row.EndsAt && b.EndsAt > row.StartsAt &&
                (b.DoctorId == row.DoctorId || (b.BranchId == row.BranchId && ((b.DoctorId == null && b.ChairId == null) || (row.ChairId != null && b.ChairId == row.ChairId)))));
            var invalid = (!row.ForcedOutsideSchedule && !working.Any(w => w.Contains(new(row.StartsAt, row.EndsAt)))) || blocked;
            if (row.ForcedOutsideSchedule && !invalid)
            {
                try { await manager.ValidateSlotAsync(row.BranchId, row.DoctorId, row.ChairId, row.StartsAt, row.EndsAt, true, row.Id); }
                catch (BusinessException ex) when (ex.Code == DentalDomainErrorCodes.AppointmentDoctorNotWorking || ex.Code == DentalDomainErrorCodes.AppointmentSlotConflict) { invalid = true; }
            }
            if (invalid) affected.Add(row.Id);
        }
        if (affected.Count > 0) throw new BusinessException(DentalDomainErrorCodes.ScheduleHasAppointments).WithData("appointmentIds", affected);
    }
}
