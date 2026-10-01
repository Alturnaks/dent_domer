using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Catalog;
using Dental.Patients;
using Dental.Permissions;
using Dental.References;
using Dental.Settings;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace Dental.Schedule;

[Authorize]
public class AppointmentAppService(IAppointmentStore appointments, AppointmentManager manager,
    IRepository<Patient, Guid> patients, IRepository<Employee, Guid> employees, IRepository<ClinicService, Guid> services,
    IRepository<CancelReason, Guid> reasons, IRepository<TimeBlock, Guid> blocks, IRepository<WaitlistEntry, Guid> waitlist,
    IPriceResolver prices, ISettingProvider settings, AppointmentReminderManager reminders) : DentalAppService, IAppointmentAppService
{
    private async Task<bool> CanViewAllAsync() => await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll)
        || await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.Manage);
    private async Task EnsureReadAsync(Guid branchId, Guid doctorId)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        if (await CanViewAllAsync()) return;
        if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewOwn)
            || (await BranchScope.GetCurrentEmployeeAsync())?.Id != doctorId) throw new AbpAuthorizationException();
    }
    private async Task CheckForceAsync(bool force)
    {
        if (force) await AuthorizationService.CheckAsync(DentalPermissions.Schedule.DoctorSchedulesManage);
    }
    private static void CheckStamp(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || actual != expected) throw new AbpDbConcurrencyException();
    }
    private async Task CheckReasonAsync(Guid? id, CancelReasonType type, bool required)
    {
        if (id == null)
        {
            if (required) throw new BusinessException(DentalDomainErrorCodes.AppointmentReasonRequired);
            return;
        }
        if ((await reasons.GetAsync(id.Value)).Type != type) throw new BusinessException(DentalDomainErrorCodes.AppointmentReasonRequired);
    }
    private async Task<List<AppointmentDto>> MapAsync(List<Appointment> rows)
    {
        if (rows.Count == 0) return [];
        var patientIds = rows.Select(a => a.PatientId).Distinct().ToList();
        var doctorIds = rows.Select(a => a.DoctorId).Distinct().ToList();
        var serviceIds = rows.SelectMany(a => a.Services).Select(s => s.ServiceId).Distinct().ToList();
        var patientNames = (await patients.GetListAsync(p => patientIds.Contains(p.Id))).ToDictionary(p => p.Id);
        var doctorNames = (await employees.GetListAsync(d => doctorIds.Contains(d.Id))).ToDictionary(d => d.Id);
        var serviceNames = (await services.GetListAsync(s => serviceIds.Contains(s.Id))).ToDictionary(s => s.Id);
        return rows.Select(a => new AppointmentDto
        {
            Id = a.Id, BranchId = a.BranchId, PatientId = a.PatientId, PatientName = patientNames.GetValueOrDefault(a.PatientId)?.FullName ?? "",
            PatientPhone = patientNames.GetValueOrDefault(a.PatientId)?.Phone, DoctorId = a.DoctorId,
            DoctorName = doctorNames.GetValueOrDefault(a.DoctorId)?.FullName ?? "", ChairId = a.ChairId,
            StartsAt = new DateTimeOffset(a.StartsAt, TimeSpan.Zero), EndsAt = new DateTimeOffset(a.EndsAt, TimeSpan.Zero),
            Status = a.Status, Source = a.Source, Comment = a.Comment, ConcurrencyStamp = a.ConcurrencyStamp,
            ForcedOutsideSchedule = a.ForcedOutsideSchedule, PlannedTotal = a.Services.Sum(s => s.PlannedPrice * s.Qty),
            Services = a.Services.Select(s => new AppointmentServiceDto { ServiceId = s.ServiceId,
                Name = serviceNames.GetValueOrDefault(s.ServiceId)?.Name ?? "", Qty = s.Qty, DurationMinutes = s.DurationMinutes, PlannedPrice = s.PlannedPrice }).ToList()
        }).ToList();
    }
    public async Task<AppointmentDto> GetAsync(Guid id)
    {
        var row = await appointments.GetAsync(id);
        await EnsureReadAsync(row.BranchId, row.DoctorId);
        return (await MapAsync([row]))[0];
    }
    public async Task<ListResultDto<AppointmentDto>> GetPatientAppointmentsAsync(Guid patientId)
    {
        var q = await appointments.WithDetailsAsync();
        q = await BranchScope.ApplyAsync(q.Where(a => a.PatientId == patientId), a => a.BranchId);
        if (!await CanViewAllAsync())
        {
            await AuthorizationService.CheckAsync(DentalPermissions.Schedule.ViewOwn);
            var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id;
            q = q.Where(a => a.DoctorId == own);
        }
        return new(await MapAsync(await AsyncExecuter.ToListAsync(q.OrderByDescending(a => a.StartsAt).Take(200))));
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<AppointmentDto> CreateAsync(CreateAppointmentDto input)
    {
        await BranchScope.EnsureCanAccessAsync(input.BranchId);
        await manager.ValidatePatientAsync(input.PatientId);
        await CheckForceAsync(input.Force);
        if (!Enum.IsDefined(input.Source) || input.Services.Count > 100) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var id = GuidGenerator.Create();
        var ids = input.Services.Select(s => s.ServiceId).Distinct().ToList();
        var available = (await services.GetListAsync(s => ids.Contains(s.Id) && s.IsActive)).ToDictionary(s => s.Id);
        if (available.Count != ids.Count) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(input.StartsAt, await manager.GetTimezoneAsync()).DateTime);
        var resolved = await prices.ResolveAsync(input.BranchId, ids, date);
        var lines = input.Services.Select(s => new AppointmentLine(GuidGenerator.Create(), CurrentTenant.Id, id, s.ServiceId,
            s.Qty, available[s.ServiceId].DurationMin, resolved.GetValueOrDefault(s.ServiceId))).ToList();
        var duration = lines.Count == 0 ? 30 : Math.Max(15, lines.Sum(s => s.DurationMinutes * s.Qty));
        var start = input.StartsAt.UtcDateTime; var end = (input.EndsAt ?? input.StartsAt.AddMinutes(duration)).UtcDateTime;
        await manager.ValidateSlotAsync(input.BranchId, input.DoctorId, input.ChairId, start, end, input.Force);
        var row = new Appointment(id, CurrentTenant.Id, input.BranchId, input.PatientId, input.DoctorId, input.ChairId, start, end, input.Source, input.Comment);
        row.Move(input.DoctorId, input.ChairId, start, end, null, input.Force); row.ReplaceServices(lines);
        await appointments.SaveAsync(row, true);
        return (await MapAsync([row]))[0];
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<AppointmentDto> MoveAsync(Guid id, MoveAppointmentDto input)
    {
        var row = await appointments.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(row.BranchId);
        CheckStamp(row.ConcurrencyStamp, input.ConcurrencyStamp); await CheckForceAsync(input.Force);
        await CheckReasonAsync(input.ReasonId, CancelReasonType.Reschedule, false);
        await manager.ValidateSlotAsync(row.BranchId, input.DoctorId, input.ChairId, input.StartsAt.UtcDateTime, input.EndsAt.UtcDateTime, input.Force, id);
        row.Move(input.DoctorId, input.ChairId, input.StartsAt.UtcDateTime, input.EndsAt.UtcDateTime, input.ReasonId, input.Force);
        await appointments.SaveAsync(row, false);
        return (await MapAsync([row]))[0];
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<AppointmentDto> ChangeStatusAsync(Guid id, ChangeAppointmentStatusDto input)
    {
        var row = await appointments.GetAsync(id); await BranchScope.EnsureCanAccessAsync(row.BranchId);
        CheckStamp(row.ConcurrencyStamp, input.ConcurrencyStamp);
        await CheckReasonAsync(input.ReasonId, CancelReasonType.Cancel, input.Status == AppointmentStatus.Cancelled);
        if (input.Status == AppointmentStatus.Arrived && row.Status == AppointmentStatus.NoShow)
            await manager.ValidateSlotAsync(row.BranchId, row.DoctorId, row.ChairId, row.StartsAt, row.EndsAt, true, row.Id);
        var old = row.Status;
        row.ChangeStatus(input.Status, Clock.Now, input.ReasonId, input.Comment);
        await appointments.SaveAsync(row, false);
        if (old != row.Status && row.Status == AppointmentStatus.Cancelled) await reminders.NotifyWaitlistAsync(row);
        if (old != row.Status && row.Status == AppointmentStatus.Arrived)
            await LazyServiceProvider.LazyGetRequiredService<Volo.Abp.EventBus.Local.ILocalEventBus>().PublishAsync(new AppointmentArrivedEvent(row.Id));
        return (await MapAsync([row]))[0];
    }
    public async Task<CalendarDto> GetCalendarAsync(CalendarInput input)
    {
        await BranchScope.EnsureCanAccessAsync(input.BranchId);
        if (input.To < input.From || input.To.DayNumber - input.From.DayNumber > 31) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var canAll = await CanViewAllAsync();
        Guid? own = null;
        if (!canAll)
        {
            await AuthorizationService.CheckAsync(DentalPermissions.Schedule.ViewOwn);
            own = (await BranchScope.GetCurrentEmployeeAsync())?.Id ?? Guid.Empty;
            if (input.DoctorId != null && input.DoctorId != own) throw new AbpAuthorizationException();
        }
        var timezone = await manager.GetTimezoneAsync();
        var start = SlotCalculator.ToUtc(input.From.ToDateTime(TimeOnly.MinValue), timezone);
        var end = SlotCalculator.ToUtc(input.To.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone);
        var q = (await appointments.WithDetailsAsync()).Where(a => a.BranchId == input.BranchId && a.StartsAt < end && a.EndsAt > start);
        if (!canAll) q = q.Where(a => a.DoctorId == own);
        if (input.DoctorId is { } filter) q = q.Where(a => a.DoctorId == filter);
        var rows = await AsyncExecuter.ToListAsync(q.OrderBy(a => a.StartsAt));
        var docs = await employees.GetListAsync(d => d.IsActive && d.Position == StaffPosition.Doctor && (d.AllBranches || d.BranchIds.Contains(input.BranchId)));
        docs = docs.Where(d => (canAll || d.Id == own) && (input.DoctorId == null || d.Id == input.DoctorId)).ToList();
        var extraIds = rows.Select(a => a.DoctorId).Except(docs.Select(d => d.Id)).Distinct().ToList();
        docs.AddRange(await employees.GetListAsync(d => extraIds.Contains(d.Id)));
        var result = new CalendarDto { Doctors = docs.Select(d => new CalendarResourceDto { Id = d.Id, Name = d.FullName, Color = d.Color }).ToList(),
            Appointments = await MapAsync(rows), Timezone = timezone.Id, SlotMinutes = int.Parse(await settings.GetOrNullAsync(DentalSettings.SlotMinutes) ?? "15") };
        for (var day = input.From; day <= input.To; day = day.AddDays(1))
            foreach (var doctor in docs)
                foreach (var w in await manager.GetWorkingAsync(input.BranchId, doctor.Id, day))
                    result.Working.Add(new CalendarWorkingDto { DoctorId = doctor.Id, StartsAt = w.Start, EndsAt = w.End });
        var timeBlocks = await blocks.GetListAsync(b => b.BranchId == input.BranchId && b.StartsAt < end && b.EndsAt > start);
        result.Blocks = timeBlocks.Where(b => canAll || b.DoctorId == own || b.DoctorId == null).Select(b => new TimeBlockDto
            { Id = b.Id, BranchId = b.BranchId, DoctorId = b.DoctorId, ChairId = b.ChairId, StartsAt = b.StartsAt, EndsAt = b.EndsAt, Reason = b.Reason }).ToList();
        return result;
    }
    public async Task<ListResultDto<WorkingIntervalDto>> GetAvailableSlotsAsync(AvailableSlotsInput input)
    {
        await EnsureReadAsync(input.BranchId, input.DoctorId);
        var step = int.Parse(await settings.GetOrNullAsync(DentalSettings.SlotMinutes) ?? "15");
        return new((await manager.GetFreeSlotsAsync(input.BranchId, input.DoctorId, input.ChairId, input.Date, input.DurationMinutes, step))
            .Select(s => new WorkingIntervalDto { StartsAt = s.Start, EndsAt = s.End }).ToList());
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<bool> RemindAsync(Guid id)
    {
        var row = await appointments.GetAsync(id); await BranchScope.EnsureCanAccessAsync(row.BranchId);
        return await reminders.SendAsync(row, 24);
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<ListResultDto<WaitlistDto>> GetWaitlistAsync(Guid branchId)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        var rows = await waitlist.GetListAsync(w => w.BranchId == branchId && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered));
        return new(await MapWaitlistAsync(rows.OrderBy(w => w.CreationTime).ToList()));
    }
    private async Task<List<WaitlistDto>> MapWaitlistAsync(List<WaitlistEntry> rows)
    {
        var ids = rows.Select(w => w.PatientId).Distinct().ToList();
        var names = (await patients.GetListAsync(p => ids.Contains(p.Id))).ToDictionary(p => p.Id);
        return rows.Select(w => new WaitlistDto { Id = w.Id, PatientId = w.PatientId, PatientName = names.GetValueOrDefault(w.PatientId)?.FullName ?? "",
            DoctorId = w.DoctorId, ServiceId = w.ServiceId, PreferredFrom = w.PreferredFrom, PreferredTo = w.PreferredTo,
            Comment = w.Comment, Status = w.Status, ConcurrencyStamp = w.ConcurrencyStamp }).ToList();
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<WaitlistDto> CreateWaitlistAsync(CreateWaitlistDto input)
    {
        await BranchScope.EnsureCanAccessAsync(input.BranchId); await manager.ValidatePatientAsync(input.PatientId);
        if (input.DoctorId != null) await LazyServiceProvider.LazyGetRequiredService<DoctorScheduleManager>().ValidateResourcesAsync(input.BranchId, input.DoctorId, null);
        if (input.ServiceId != null && !(await services.GetAsync(input.ServiceId.Value)).IsActive) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        var row = new WaitlistEntry(GuidGenerator.Create(), CurrentTenant.Id, input.BranchId, input.PatientId, input.DoctorId, input.ServiceId,
            input.PreferredFrom, input.PreferredTo, input.Comment);
        await waitlist.InsertAsync(row, autoSave: true); return (await MapWaitlistAsync([row]))[0];
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task ChangeWaitlistStatusAsync(Guid id, ChangeWaitlistStatusDto input)
    {
        var row = await waitlist.GetAsync(id); await BranchScope.EnsureCanAccessAsync(row.BranchId);
        CheckStamp(row.ConcurrencyStamp, input.ConcurrencyStamp); row.ChangeStatus(input.Status); await waitlist.UpdateAsync(row, autoSave: true);
    }
}
