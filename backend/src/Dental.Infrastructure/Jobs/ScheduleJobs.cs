using System.Globalization;
using System.Text.Json;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Application.Schedule;
using Dental.Domain.Audit;
using Dental.Domain.Scheduling;
using Dental.Infrastructure.Messaging;
using Dental.Infrastructure.Persistence;
using Dental.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dental.Infrastructure.Jobs;

/// <summary>Отправка напоминания по шаблону через провайдер сообщений (ConsoleProvider в dev).</summary>
public sealed class ReminderSender(AppDbContext db, IEnumerable<IMessageProvider> providers, TimeProvider clock, ILogger<ReminderSender> logger) : IReminderSender
{
    public async Task<bool> SendAsync(Appointment a, string templateType, CancellationToken ct)
    {
        var template = await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Type == templateType && t.IsActive, ct);
        if (template is null) return false;
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == a.PatientId, ct);
        if (patient?.Phone is null) return false;
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var tz = TimeZones.Find(org.Timezone);
        var local = TimeZoneInfo.ConvertTime(a.StartsAt, tz);
        var doctor = await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.Id == a.DoctorId select u.FullName).FirstOrDefaultAsync(ct);
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == a.BranchId, ct);
        var text = template.Render(new Dictionary<string, string>
        {
            ["patient_name"] = patient.FirstName + (string.IsNullOrWhiteSpace(patient.MiddleName) ? "" : " " + patient.MiddleName),
            ["date"] = local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["time"] = local.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["doctor"] = doctor ?? "",
            ["branch_address"] = branch?.Address ?? "",
        });
        var provider = providers.FirstOrDefault(p => p.Channel == template.Channel) ?? providers.First();
        var result = await provider.SendAsync(patient.Phone, text, ct);
        db.OutgoingMessages.Add(new OutgoingMessage
        {
            PatientId = patient.Id,
            AppointmentId = a.Id,
            Channel = template.Channel,
            Recipient = patient.Phone,
            Text = text,
            Kind = templateType,
            Status = result.Success ? OutgoingMessageStatus.Sent : OutgoingMessageStatus.Failed,
            ProviderMessageId = result.ProviderMessageId,
            SentAt = result.Success ? clock.GetUtcNow() : null,
            Error = result.Error,
        });
        if (templateType == "reminder_24h") a.Reminder24hSentAt = clock.GetUtcNow();
        if (templateType == "reminder_2h") a.Reminder2hSentAt = clock.GetUtcNow();
        logger.LogInformation("Reminder {Type} for appointment {Id}: {Result}", templateType, a.Id, result.Success);
        return result.Success;
    }
}

/// <summary>send_reminders: каждые 5 минут — напоминания за 24 ч и за 2 ч.</summary>
public sealed class SendRemindersJob(TenantJobRunner runner, TimeProvider clock) : IDentalJob
{
    public Task RunAsync(CancellationToken ct) => runner.ForEachOrganizationAsync("send_reminders", async (sp, _, _, token) =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var sender = sp.GetRequiredService<IReminderSender>();
        var org = await db.Organizations.AsNoTracking().FirstAsync(token);
        var now = clock.GetUtcNow();
        var active = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Confirmed };

        if (org.Settings.Reminder24h)
        {
            var due = await db.Appointments.Where(a => active.Contains(a.Status) && a.Reminder24hSentAt == null
                                                       && a.StartsAt > now.AddHours(23) && a.StartsAt <= now.AddHours(24)).ToListAsync(token);
            foreach (var a in due) await sender.SendAsync(a, "reminder_24h", token);
        }
        if (org.Settings.Reminder2h)
        {
            var due = await db.Appointments.Where(a => active.Contains(a.Status) && a.Reminder2hSentAt == null
                                                       && a.StartsAt > now.AddMinutes(90) && a.StartsAt <= now.AddHours(2)).ToListAsync(token);
            foreach (var a in due) await sender.SendAsync(a, "reminder_2h", token);
        }
        await db.SaveChangesAsync(token);
    }, ct);
}

/// <summary>mark_no_shows: через N минут после начала (настройка), если статус остался scheduled/confirmed.</summary>
public sealed class MarkNoShowsJob(TenantJobRunner runner, TimeProvider clock) : IDentalJob
{
    public Task RunAsync(CancellationToken ct) => runner.ForEachOrganizationAsync("mark_no_shows", async (sp, _, _, token) =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var org = await db.Organizations.AsNoTracking().FirstAsync(token);
        var threshold = clock.GetUtcNow().AddMinutes(-org.Settings.NoShowAfterMinutes);
        var rows = await db.Appointments.Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed) && a.StartsAt < threshold)
            .ToListAsync(token);
        foreach (var a in rows) a.Status = AppointmentStatus.NoShow;
        await db.SaveChangesAsync(token);
    }, ct);
}

/// <summary>waitlist_match: при отмене записи ищет подходящих пациентов в листе ожидания и уведомляет администраторов.</summary>
public sealed class WaitlistMatchHandler(AppDbContext db, INotificationService notifications) : IOutboxHandler
{
    public string Type => OutboxTypes.AppointmentCancelled;

    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<AppointmentCancelledEvent>(message.Payload, AuditingInterceptor.JsonOptions);
        if (e is null) return;
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var tz = TimeZones.Find(org.Timezone);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.StartsAt, tz).DateTime);
        var candidates = await db.Waitlist.Where(w => w.BranchId == e.BranchId && w.Status == WaitlistStatus.Waiting
                                                     && (w.DoctorId == null || w.DoctorId == e.DoctorId)
                                                     && (w.PreferredFrom == null || w.PreferredFrom <= date)
                                                     && (w.PreferredTo == null || w.PreferredTo >= date))
            .OrderBy(w => w.CreatedAt).Take(5).ToListAsync(ct);
        if (candidates.Count == 0) return;
        var names = await db.Patients.AsNoTracking().Where(p => candidates.Select(c => c.PatientId).Contains(p.Id))
            .Select(p => p.LastName + " " + p.FirstName).ToListAsync(ct);
        var local = TimeZoneInfo.ConvertTime(e.StartsAt, tz);
        await notifications.NotifyByPermissionAsync(Perm.Schedule.Manage, "waitlist_match",
            $"Освободилось окно {local:dd.MM HH:mm} — есть кандидаты из листа ожидания",
            string.Join(", ", names), nameof(Appointment), e.AppointmentId, e.BranchId, ct);
        await db.SaveChangesAsync(ct);
    }
}
