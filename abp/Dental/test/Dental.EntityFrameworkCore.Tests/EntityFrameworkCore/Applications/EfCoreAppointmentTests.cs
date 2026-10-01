using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Patients;
using Dental.Schedule;
using Dental.Staff;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreAppointmentTests : DentalApplicationTestBase<DentalEntityFrameworkCoreTestModule>
{
    private readonly Guid _branch = Guid.NewGuid(), _doctor = Guid.NewGuid(), _otherDoctor = Guid.NewGuid(), _chair = Guid.NewGuid(), _patient = Guid.NewGuid();
    private readonly DateOnly _day = new(2030, 10, 7);
    private DateTime At(int hour, int minute = 0) => new(2030, 10, 7, hour - 5, minute, 0, DateTimeKind.Utc);
    private async Task SeedAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<Branch, Guid>>().InsertAsync(new Branch(_branch, null, "Test"));
            await GetRequiredService<IRepository<Chair, Guid>>().InsertAsync(new Chair(_chair, null, _branch, "Chair"));
            await GetRequiredService<IRepository<Patient, Guid>>().InsertAsync(new Patient(_patient, null, "Test", "Patient"));
            foreach (var id in new[] { _doctor, _otherDoctor })
            {
                var employee = new Employee(id, null, Guid.NewGuid(), "Doctor", StaffPosition.Doctor); employee.SetBranches(true, []);
                await GetRequiredService<IRepository<Employee, Guid>>().InsertAsync(employee);
                await GetRequiredService<IRepository<DoctorSchedule, Guid>>().InsertAsync(new DoctorSchedule(Guid.NewGuid(), null, id, _branch,
                    (int)_day.DayOfWeek, new(9, 0), new(18, 0), _chair, _day));
            }
        });
    }
    private Appointment New(int startHour = 9, int endHour = 10, Guid? doctor = null) =>
        new(Guid.NewGuid(), null, _branch, _patient, doctor ?? _doctor, _chair, At(startHour), At(endHour), AppointmentSource.Admin, null);

    [Fact]
    public async Task Adjacent_Slots_Are_Allowed_But_Doctor_And_Chair_Overlaps_Are_Rejected()
    {
        await SeedAsync();
        await WithUnitOfWorkAsync(() => GetRequiredService<IAppointmentStore>().SaveAsync(New(), true));
        await WithUnitOfWorkAsync(async () =>
        {
            var manager = GetRequiredService<AppointmentManager>();
            await manager.ValidateSlotAsync(_branch, _doctor, _chair, At(10), At(11), false);
            var doctorError = await Should.ThrowAsync<BusinessException>(() => manager.ValidateSlotAsync(_branch, _doctor, null, At(9, 30), At(10, 30), false));
            doctorError.Code.ShouldBe(DentalDomainErrorCodes.AppointmentSlotConflict);
            var chairError = await Should.ThrowAsync<BusinessException>(() => manager.ValidateSlotAsync(_branch, _otherDoctor, _chair, At(9), At(10), false));
            chairError.Code.ShouldBe(DentalDomainErrorCodes.AppointmentSlotConflict);
            var slots = await manager.GetFreeSlotsAsync(_branch, _otherDoctor, _chair, _day, 30, 15);
            slots.Any(s => s.Start < At(10) && s.End > At(9)).ShouldBeFalse();
        });
    }
    [Fact]
    public async Task Force_Allows_Outside_Shift_But_Never_Absence_Or_Block()
    {
        await SeedAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var manager = GetRequiredService<AppointmentManager>();
            (await Should.ThrowAsync<BusinessException>(() => manager.ValidateSlotAsync(_branch, _doctor, null, At(8), At(9), false)))
                .Code.ShouldBe(DentalDomainErrorCodes.AppointmentDoctorNotWorking);
            await manager.ValidateSlotAsync(_branch, _doctor, null, At(8), At(9), true);
            await GetRequiredService<IRepository<ScheduleException, Guid>>().InsertAsync(new ScheduleException(Guid.NewGuid(), null, _doctor,
                _branch, _day, _day, ScheduleExceptionType.Sick, new(10, 0), new(11, 0), null), autoSave: true);
            (await Should.ThrowAsync<BusinessException>(() => manager.ValidateSlotAsync(_branch, _doctor, null, At(10), At(11), true)))
                .Code.ShouldBe(DentalDomainErrorCodes.AppointmentDoctorNotWorking);
            await GetRequiredService<IRepository<TimeBlock, Guid>>().InsertAsync(new TimeBlock(Guid.NewGuid(), null, _branch, null, _chair, At(12), At(13), null), autoSave: true);
            (await Should.ThrowAsync<BusinessException>(() => manager.ValidateSlotAsync(_branch, _doctor, _chair, At(12), At(13), true)))
                .Code.ShouldBe(DentalDomainErrorCodes.AppointmentSlotConflict);
        });
    }
    [Fact]
    public async Task Cancellation_Frees_Slot_And_Details_Are_Persisted()
    {
        await SeedAsync(); var row = New(); var serviceId = Guid.NewGuid();
        row.ReplaceServices([new AppointmentLine(Guid.NewGuid(), null, row.Id, serviceId, 2, 30, 100000)]);
        await WithUnitOfWorkAsync(() => GetRequiredService<IAppointmentStore>().SaveAsync(row, true));
        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<IAppointmentStore>();
            var loaded = await store.GetAsync(row.Id); loaded.Services.Count.ShouldBe(1); loaded.Services[0].Qty.ShouldBe(2);
            loaded.ChangeStatus(AppointmentStatus.Cancelled, DateTime.UtcNow, Guid.NewGuid()); await store.SaveAsync(loaded, false);
            await GetRequiredService<AppointmentManager>().ValidateSlotAsync(_branch, _doctor, _chair, At(9), At(10), false);
        });
    }
    [Fact]
    public async Task Tenant_Isolation_And_Patient_Merge_Include_Appointments_And_Waitlist()
    {
        await SeedAsync(); var row = New(); var target = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IAppointmentStore>().SaveAsync(row, true);
            await GetRequiredService<IRepository<WaitlistEntry, Guid>>().InsertAsync(new WaitlistEntry(Guid.NewGuid(), null, _branch, _patient, null, null, null, null, null));
        });
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await WithUnitOfWorkAsync(async () => (await GetRequiredService<IAppointmentStore>().GetCountAsync()).ShouldBe(0));
        await WithUnitOfWorkAsync(async () => (await GetRequiredService<SchedulePatientMergeContributor>().MoveAsync(_patient, target)).ShouldBe(2));
        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IAppointmentStore>().GetAsync(row.Id)).PatientId.ShouldBe(target);
            (await GetRequiredService<IRepository<WaitlistEntry, Guid>>().GetListAsync()).Single().PatientId.ShouldBe(target);
        });
    }
    [Fact]
    public async Task Schedule_Impact_Reports_Future_Appointments()
    {
        await SeedAsync(); var row = New();
        await WithUnitOfWorkAsync(() => GetRequiredService<IAppointmentStore>().SaveAsync(row, true));
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<ScheduleException, Guid>>().InsertAsync(new ScheduleException(Guid.NewGuid(), null, _doctor, _branch,
                _day, _day, ScheduleExceptionType.Vacation, null, null, null), autoSave: true);
            var error = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<ScheduleImpactGuard>().EnsureAsync(_doctor, _branch));
            error.Code.ShouldBe(DentalDomainErrorCodes.ScheduleHasAppointments);
        });
    }
    [Fact]
    public async Task Stale_Concurrency_Stamp_Is_Rejected_By_Store()
    {
        await SeedAsync(); var row = New();
        await WithUnitOfWorkAsync(() => GetRequiredService<IAppointmentStore>().SaveAsync(row, true));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<IAppointmentStore>(); var loaded = await store.GetAsync(row.Id);
            loaded.ConcurrencyStamp = "stale"; loaded.SetComment("Conflicting edit");
            await store.SaveAsync(loaded, false);
        }));
    }
    [Fact]
    public async Task Reminder_Is_Recorded_Once_And_NoShow_Task_Releases_Overdue_Appointment()
    {
        await SeedAsync(); var future = New();
        var now = DateTime.UtcNow;
        var past = new Appointment(Guid.NewGuid(), null, _branch, _patient, _doctor, _chair, now.AddHours(-3), now.AddHours(1), AppointmentSource.Admin, null);
        await WithUnitOfWorkAsync(async () =>
        {
            var patients = GetRequiredService<IRepository<Patient, Guid>>(); var patient = await patients.GetAsync(_patient);
            patient.SetPhones("77011234567", null); await patients.UpdateAsync(patient);
            await GetRequiredService<IAppointmentStore>().SaveAsync(future, true);
            await GetRequiredService<IAppointmentStore>().SaveAsync(past, true);
        });
        await WithUnitOfWorkAsync(async () =>
        {
            var reminders = GetRequiredService<AppointmentReminderManager>(); var store = GetRequiredService<IAppointmentStore>();
            var row = await store.GetAsync(future.Id);
            (await reminders.SendAsync(row, 24)).ShouldBeTrue();
            (await reminders.SendAsync(row, 24)).ShouldBeFalse();
            await reminders.RunAsync();
        });
        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<IAppointmentStore>();
            (await store.GetAsync(future.Id)).Reminder24hSentAt.ShouldNotBeNull();
            (await store.GetAsync(past.Id)).Status.ShouldBe(AppointmentStatus.NoShow);
        });
    }
    [Fact]
    public void Statuses_Require_Valid_Transitions_And_Cancel_Reason()
    {
        var row = New();
        Should.Throw<BusinessException>(() => row.ChangeStatus(AppointmentStatus.Completed, DateTime.UtcNow));
        Should.Throw<BusinessException>(() => row.ChangeStatus(AppointmentStatus.Cancelled, DateTime.UtcNow));
        row.ChangeStatus(AppointmentStatus.Confirmed, DateTime.UtcNow);
        row.ChangeStatus(AppointmentStatus.Arrived, DateTime.UtcNow);
        row.ChangeStatus(AppointmentStatus.InChair, DateTime.UtcNow);
        row.ChangeStatus(AppointmentStatus.Completed, DateTime.UtcNow);
        Should.Throw<BusinessException>(() => row.Move(_doctor, null, At(10), At(11), null, false));
    }
    [Fact]
    public void Waitlist_Rejects_Reversed_Dates_And_Changes_To_Closed_Entries()
    {
        Should.Throw<BusinessException>(() => new WaitlistEntry(Guid.NewGuid(), null, _branch, _patient, null, null, _day, _day.AddDays(-1), null));
        var row = new WaitlistEntry(Guid.NewGuid(), null, _branch, _patient, null, null, null, null, null);
        row.ChangeStatus(WaitlistStatus.Offered); row.ChangeStatus(WaitlistStatus.Booked);
        Should.Throw<BusinessException>(() => row.ChangeStatus(WaitlistStatus.Waiting));
    }
}
