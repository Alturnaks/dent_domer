using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Staff;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Dental.Schedule;

public class DoctorScheduleManager(
    IRepository<DoctorSchedule, Guid> schedules,
    IRepository<Employee, Guid> employees,
    IRepository<Branch, Guid> branches,
    IRepository<Chair, Guid> chairs) : DomainService
{
    public async Task ValidateResourcesAsync(Guid branchId, Guid? doctorId, Guid? chairId)
    {
        var branch = await branches.GetAsync(branchId);
        if (!branch.IsActive) throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        if (doctorId is { } d)
        {
            var doctor = await employees.GetAsync(d);
            if (!doctor.IsActive || doctor.Position != StaffPosition.Doctor || !doctor.HasBranch(branchId))
                throw new BusinessException(DentalDomainErrorCodes.ScheduleDoctorInvalid);
        }
        if (chairId is { } c)
        {
            var chair = await chairs.GetAsync(c);
            if (!chair.IsActive || chair.BranchId != branchId) throw new BusinessException(DentalDomainErrorCodes.ScheduleChairInvalid);
        }
    }

    public async Task<List<DoctorSchedule>> ReplaceWeekAsync(Guid? tenantId, Guid branchId, Guid doctorId,
        DateOnly from, IReadOnlyList<DoctorShiftData> days)
    {
        if (from == DateOnly.MinValue || days.Count > 28) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        await ValidateResourcesAsync(branchId, doctorId, null);
        var created = new List<DoctorSchedule>();
        foreach (var day in days)
        {
            await ValidateResourcesAsync(branchId, doctorId, day.ChairId);
            created.Add(new DoctorSchedule(GuidGenerator.Create(), tenantId, doctorId, branchId, day.Weekday,
                day.StartTime, day.EndTime, day.ChairId, from));
        }
        var existing = await schedules.GetListAsync(s => s.DoctorId == doctorId && (s.ValidTo == null || s.ValidTo >= from));
        var retained = existing.Where(s => s.BranchId != branchId).ToList();
        var all = retained.Concat(created).ToList();
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                if ((i >= retained.Count || j >= retained.Count) && Overlaps(all[i], all[j]))
                    throw new BusinessException(DentalDomainErrorCodes.ScheduleOverlappingShifts);
        foreach (var old in existing.Where(s => s.BranchId == branchId))
        {
            old.CloseBefore(from);
            await schedules.UpdateAsync(old);
        }
        await schedules.InsertManyAsync(created);
        return created;
    }

    public static bool Overlaps(DoctorSchedule a, DoctorSchedule b) => a.Weekday == b.Weekday
        && a.StartTime < b.EndTime && b.StartTime < a.EndTime
        && a.ValidFrom <= (b.ValidTo ?? DateOnly.MaxValue) && b.ValidFrom <= (a.ValidTo ?? DateOnly.MaxValue);
}

public record DoctorShiftData(int Weekday, TimeOnly StartTime, TimeOnly EndTime, Guid? ChairId);
