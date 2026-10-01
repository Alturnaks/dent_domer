using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Schedule;
using Dental.Staff;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreDoctorScheduleTests : DentalApplicationTestBase<DentalEntityFrameworkCoreTestModule>
{
    [Fact]
    public async Task Replace_Template_Closes_Old_Shifts_And_Rejects_Cross_Branch_Overlap()
    {
        var manager = GetRequiredService<DoctorScheduleManager>();
        var branchId = Guid.NewGuid(); var secondBranchId = Guid.NewGuid(); var doctorId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 5);
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<Branch, Guid>>().InsertAsync(new Branch(branchId, null, "First"));
            await GetRequiredService<IRepository<Branch, Guid>>().InsertAsync(new Branch(secondBranchId, null, "Second"));
            var doctor = new Employee(doctorId, null, Guid.NewGuid(), "Doctor", StaffPosition.Doctor);
            doctor.SetBranches(true, []);
            await GetRequiredService<IRepository<Employee, Guid>>().InsertAsync(doctor);
        });
        await WithUnitOfWorkAsync(() => manager.ReplaceWeekAsync(null, branchId, doctorId, date, [new(1, new(9, 0), new(12, 0), null)]));
        await WithUnitOfWorkAsync(() => manager.ReplaceWeekAsync(null, branchId, doctorId, date.AddDays(7), [new(1, new(13, 0), new(18, 0), null)]));
        await WithUnitOfWorkAsync(async () =>
        {
            var rows = await GetRequiredService<IRepository<DoctorSchedule, Guid>>().GetListAsync(s => s.DoctorId == doctorId);
            rows.Count.ShouldBe(2);
            rows.Single(s => s.ValidFrom == date).ValidTo.ShouldBe(date.AddDays(6));
            var error = await Should.ThrowAsync<BusinessException>(() => manager.ReplaceWeekAsync(null, secondBranchId, doctorId,
                date.AddDays(7), [new(1, new(14, 0), new(16, 0), null)]));
            error.Code.ShouldBe(DentalDomainErrorCodes.ScheduleOverlappingShifts);
        });
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await WithUnitOfWorkAsync(async () => (await GetRequiredService<IRepository<DoctorSchedule, Guid>>().GetCountAsync()).ShouldBe(0));
    }

    [Fact]
    public async Task Chair_From_Another_Branch_Is_Rejected_And_Exceptions_Are_Soft_Deleted()
    {
        var branchId = Guid.NewGuid(); var otherId = Guid.NewGuid(); var chairId = Guid.NewGuid(); var exceptionId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var branches = GetRequiredService<IRepository<Branch, Guid>>();
            await branches.InsertAsync(new Branch(branchId, null, "First"), autoSave: true);
            await branches.InsertAsync(new Branch(otherId, null, "Other"), autoSave: true);
            await GetRequiredService<IRepository<Chair, Guid>>().InsertAsync(new Chair(chairId, null, otherId, "Chair"), autoSave: true);
            var error = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<DoctorScheduleManager>().ValidateResourcesAsync(branchId, null, chairId));
            error.Code.ShouldBe(DentalDomainErrorCodes.ScheduleChairInvalid);
            var exceptions = GetRequiredService<IRepository<ScheduleException, Guid>>();
            await exceptions.InsertAsync(new ScheduleException(exceptionId, null, Guid.NewGuid(), branchId, new(2026, 10, 5),
                new(2026, 10, 5), ScheduleExceptionType.DayOff, null, null, null), autoSave: true);
            await exceptions.DeleteAsync(exceptionId, autoSave: true);
        });
        await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<IRepository<ScheduleException, Guid>>().FindAsync(exceptionId)).ShouldBeNull());
    }
}
