using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Notifications;
using Dental.Patients;
using Dental.Permissions;
using Dental.Settings;
using Dental.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Dental.Schedule;

public record AppointmentArrivedEvent(Guid AppointmentId);

// The message provider can be replaced when a clinic connects SMS or WhatsApp.
public interface IAppointmentMessageSender
{
    Task<bool> SendAsync(Guid appointmentId, string phone, DateTime start, int hours);
}
[Dependency(TryRegister = true)]
public class ConsoleAppointmentMessageSender(ILogger<ConsoleAppointmentMessageSender> logger) : IAppointmentMessageSender, ITransientDependency
{
    public Task<bool> SendAsync(Guid appointmentId, string phone, DateTime start, int hours)
    {
        logger.LogInformation("DEMO reminder {Hours}h: appointment {AppointmentId}, phone {Phone}, start {StartUtc}. No external message sent.", hours, appointmentId, phone, start);
        return Task.FromResult(true);
    }
}

public class AppointmentReminderManager(IAppointmentStore appointments, IRepository<Patient, Guid> patients,
    IRepository<WaitlistEntry, Guid> waitlist, NotificationManager notifications, AppointmentManager manager,
    IAppointmentMessageSender sender, ISettingProvider settings, IStringLocalizer<DentalResource> localizer) : DomainService
{
    public async Task<bool> SendAsync(Appointment row, int hours)
    {
        if (row.Status != AppointmentStatus.Scheduled && row.Status != AppointmentStatus.Confirmed) return false;
        if (row.StartsAt <= Clock.Now || (hours == 24 ? row.Reminder24hSentAt : row.Reminder2hSentAt) != null) return false;
        var patient = await patients.FindAsync(row.PatientId);
        if (string.IsNullOrWhiteSpace(patient?.Phone)) return false;
        if (!await sender.SendAsync(row.Id, patient.Phone, row.StartsAt, hours)) return false;
        row.MarkReminder(hours, Clock.Now); await appointments.SaveAsync(row, false); return true;
    }

    public async Task RunAsync()
    {
        var now = Clock.Now;
        var delay = int.Parse(await settings.GetOrNullAsync(DentalSettings.NoShowAfterMinutes) ?? "30");
        var threshold = now.AddMinutes(-delay);
        var overdue = await appointments.GetListAsync(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed) && a.StartsAt <= threshold);
        foreach (var row in overdue)
        {
            row.ChangeStatus(AppointmentStatus.NoShow, now); await appointments.SaveAsync(row, false);
        }
        var until = now.AddHours(24);
        var pending = await appointments.GetListAsync(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
            && a.StartsAt > now && a.StartsAt <= until);
        var send24 = await settings.GetOrNullAsync(DentalSettings.Reminder24h) ?? "true";
        var send2 = await settings.GetOrNullAsync(DentalSettings.Reminder2h) ?? "true";
        foreach (var row in pending)
        {
            if (row.StartsAt <= now.AddHours(2))
            {
                if (row.StartsAt > now.AddMinutes(90) && bool.Parse(send2)) await SendAsync(row, 2);
            }
            else if (row.StartsAt > now.AddHours(23) && bool.Parse(send24)) await SendAsync(row, 24);
        }
    }

    public async Task NotifyWaitlistAsync(Appointment cancelled)
    {
        if (cancelled.StartsAt <= Clock.Now) return;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(cancelled.StartsAt, await manager.GetTimezoneAsync()));
        var matches = await waitlist.GetListAsync(w => w.BranchId == cancelled.BranchId && w.Status == WaitlistStatus.Waiting
            && (w.DoctorId == null || w.DoctorId == cancelled.DoctorId)
            && (w.PreferredFrom == null || w.PreferredFrom <= date) && (w.PreferredTo == null || w.PreferredTo >= date));
        var serviceIds = cancelled.Services.Select(s => s.ServiceId).ToList();
        matches = matches.Where(w => w.ServiceId == null || serviceIds.Contains(w.ServiceId.Value)).ToList();
        if (matches.Count == 0) return;
        await notifications.NotifyByPermissionAsync(DentalPermissions.Schedule.Manage, "waitlist_match",
            localizer["Calendar:WaitlistNotificationTitle"], localizer["Calendar:WaitlistNotificationBody", date.ToString("yyyy-MM-dd"), matches.Count],
            "Appointment", cancelled.Id, cancelled.BranchId);
    }
}
