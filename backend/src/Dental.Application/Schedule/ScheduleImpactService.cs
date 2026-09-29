using System.Globalization;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using Dental.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Schedule;

/// <summary>Что сделать с записями пациентов, которые перестают помещаться в график после изменения.</summary>
public enum AffectedAppointmentsAction
{
    /// <summary>Оставить записи как есть — администратор перенесёт их вручную (уведомление + аудит).</summary>
    Keep,
    /// <summary>Отменить записи, поставить пациентов в лист ожидания и отправить им сообщение.</summary>
    CancelToWaitlist,
}

/// <summary>Какие записи проверять после изменения. Null — без ограничения по этому признаку.</summary>
public sealed record ImpactScope(IReadOnlyCollection<Guid>? DoctorIds, Guid? BranchId, Guid? ChairId, DateTimeOffset From, DateTimeOffset? To);

public sealed record AffectedAppointmentDto(
    Guid Id, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid DoctorId, string DoctorName,
    Guid PatientId, string PatientName, string? PatientPhone, string Reason);

/// <summary>
/// Согласованность расписания: любое изменение графика, выходного, блокировки, кресла или сотрудника
/// применяется в транзакции, после чего проверяются будущие записи. Если какие-то записи больше не помещаются
/// в график, изменение откатывается с ошибкой 409 SCHEDULE_HAS_APPOINTMENTS и списком записей — пока пользователь
/// явно не выберет, что с ними сделать (<see cref="AffectedAppointmentsAction"/>).
/// </summary>
public sealed class ScheduleImpactService(
    IAppDbContext db,
    IAuditService audit,
    INotificationService notifications,
    IEnumerable<IReminderSender> senders,
    TimeProvider clock)
{
    public const string CancelReasonName = "Изменение графика врача";
    public const string PatientMessageTemplate = "clinic_cancel";
    private static readonly AppointmentStatus[] Pending = [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed];

    /// <summary>Применяет изменение и разбирает затронутые записи. <paramref name="change"/> должен сам вызвать SaveChanges.</summary>
    public async Task<T> ApplyAsync<T>(Func<Task<T>> change, Func<T, ImpactScope> scope, AffectedAppointmentsAction? action, string title, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await change();
        var affected = await FindAffectedAsync(scope(result), ct);
        List<Appointment> cancelled = [];
        if (affected.Count > 0)
        {
            if (action is null)
            {
                throw new AppException(ErrorCodes.ScheduleHasAppointments,
                    $"На это время уже есть записи пациентов ({affected.Count}). Выберите, что с ними сделать.", 409,
                    new Dictionary<string, object?> { ["appointments"] = affected.Select(x => x.Dto).ToList() });
            }
            if (action == AffectedAppointmentsAction.CancelToWaitlist) cancelled = await CancelToWaitlistAsync(affected, title, ct);
            else KeepForManualReschedule(affected, title);
            await NotifyAsync(affected, action.Value, title, ct);
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        await SendPatientMessagesAsync(cancelled, ct);
        return result;
    }

    public Task ApplyAsync(Func<Task> change, ImpactScope scope, AffectedAppointmentsAction? action, string title, CancellationToken ct) =>
        ApplyAsync(async () => { await change(); return true; }, _ => scope, action, title, ct);

    private sealed record Affected(Appointment Appointment, AffectedAppointmentDto Dto);

    private async Task<List<Affected>> FindAffectedAsync(ImpactScope s, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var from = s.From > now ? s.From : now;
        var to = s.To ?? from.AddDays(366);
        if (to <= from) return [];

        var q = db.Appointments.Include(a => a.Services).Where(a => Pending.Contains(a.Status) && a.StartsAt < to && a.EndsAt > from);
        if (s.DoctorIds is { Count: > 0 } ids) q = q.Where(a => ids.Contains(a.DoctorId));
        if (s.BranchId is { } b) q = q.Where(a => a.BranchId == b);
        if (s.ChairId is { } c) q = q.Where(a => a.ChairId == c);
        var list = await q.OrderBy(a => a.StartsAt).Take(1000).ToListAsync(ct);
        if (list.Count == 0) return [];

        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var tz = ScheduleService.FindTz(org.Timezone);
        var doctorIds = list.Select(a => a.DoctorId).Distinct().ToList();
        var chairIds = list.Where(a => a.ChairId != null).Select(a => a.ChairId!.Value).Distinct().ToList();
        var branchIds = list.Select(a => a.BranchId).Distinct().ToList();
        var minStart = list.Min(a => a.StartsAt);
        var maxEnd = list.Max(a => a.EndsAt);

        var doctors = await (from m in db.Memberships.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                             where doctorIds.Contains(m.Id) select new { m, u.FullName }).ToDictionaryAsync(x => x.m.Id, ct);
        var chairs = await db.Chairs.AsNoTracking().Where(c => chairIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var schedules = (await db.DoctorSchedules.AsNoTracking().Where(x => doctorIds.Contains(x.MembershipId)).ToListAsync(ct)).ToLookup(x => x.MembershipId);
        var exceptions = (await db.ScheduleExceptions.AsNoTracking().Where(x => doctorIds.Contains(x.MembershipId)).ToListAsync(ct)).ToLookup(x => x.MembershipId);
        var blocks = await db.TimeBlocks.AsNoTracking().Where(x => branchIds.Contains(x.BranchId) && x.StartsAt < maxEnd && x.EndsAt > minStart).ToListAsync(ct);
        var inactiveBranches = await db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id) && !x.IsActive).Select(x => x.Id).ToListAsync(ct);
        var pids = list.Select(a => a.PatientId).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var result = new List<Affected>();
        foreach (var a in list)
        {
            var doc = doctors.GetValueOrDefault(a.DoctorId);
            string? reason = null;
            if (inactiveBranches.Contains(a.BranchId)) reason = "Филиал закрыт";
            else if (doc is null || !doc.m.IsActive) reason = "Врач уволен";
            else if (!doc.m.HasBranch(a.BranchId)) reason = "Врач больше не работает в этом филиале";
            else if (a.ChairId is { } cid && (!chairs.TryGetValue(cid, out var chair) || !chair.IsActive || chair.DeletedAt != null)) reason = "Кресло отключено";
            else
            {
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.StartsAt, tz).DateTime);
                var working = SlotCalculator.WorkingIntervals(date, a.BranchId, schedules[a.DoctorId], exceptions[a.DoctorId], tz);
                if (!SlotCalculator.IsWithinWorkingTime(new TimeRange(a.StartsAt, a.EndsAt), working))
                {
                    var absence = exceptions[a.DoctorId].FirstOrDefault(e => e.Type != ScheduleExceptionType.ExtraShift && e.Covers(date));
                    reason = absence?.Type switch
                    {
                        ScheduleExceptionType.Vacation => "Отпуск врача",
                        ScheduleExceptionType.Sick => "Больничный врача",
                        ScheduleExceptionType.DayOff => "Выходной врача",
                        _ => "Нет смены врача в это время",
                    };
                }
                else if (blocks.Any(bl => bl.BranchId == a.BranchId && bl.StartsAt < a.EndsAt && bl.EndsAt > a.StartsAt &&
                                          ((bl.DoctorId == null && bl.ChairId == null) || bl.DoctorId == a.DoctorId || (a.ChairId != null && bl.ChairId == a.ChairId))))
                    reason = "Время заблокировано";
            }
            if (reason is null) continue;
            var p = patients.GetValueOrDefault(a.PatientId);
            result.Add(new Affected(a, new AffectedAppointmentDto(a.Id, a.BranchId, a.StartsAt, a.EndsAt, a.DoctorId, doc?.FullName ?? "?",
                a.PatientId, p?.FullName ?? "?", p?.Phone, reason)));
        }
        return result;
    }

    private async Task<List<Appointment>> CancelToWaitlistAsync(List<Affected> affected, string title, CancellationToken ct)
    {
        var reason = await db.CancelReasons.FirstOrDefaultAsync(r => r.Name == CancelReasonName && r.DeletedAt == null, ct);
        if (reason is null)
        {
            reason = new CancelReason { Name = CancelReasonName, Type = CancelReasonType.Cancel };
            db.CancelReasons.Add(reason);
        }
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var tz = ScheduleService.FindTz(org.Timezone);
        var activeDoctors = await db.Memberships.AsNoTracking().Where(m => m.IsActive).Select(m => m.Id).ToListAsync(ct);

        var result = new List<Appointment>();
        foreach (var x in affected)
        {
            var a = x.Appointment;
            var local = TimeZoneInfo.ConvertTime(a.StartsAt, tz);
            a.Status = AppointmentStatus.Cancelled;
            a.CancelReasonId = reason.Id;
            a.CancelComment = $"{title}: {x.Dto.Reason}";
            db.Waitlist.Add(new WaitlistEntry
            {
                BranchId = a.BranchId,
                PatientId = a.PatientId,
                DoctorId = activeDoctors.Contains(a.DoctorId) ? a.DoctorId : null,
                ServiceId = a.Services.FirstOrDefault()?.ServiceId,
                PreferredFrom = DateOnly.FromDateTime(local.DateTime),
                Comment = $"Запись {local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} к врачу {x.Dto.DoctorName} отменена: {title}",
            });
            audit.Log(nameof(Appointment), a.Id, "cancel_by_schedule_change", new { a.StartsAt, a.EndsAt, a.DoctorId, x.Dto.Reason }, title, branchId: a.BranchId);
            result.Add(a);
        }
        return result;
    }

    private void KeepForManualReschedule(List<Affected> affected, string title)
    {
        foreach (var x in affected)
        {
            audit.Log(nameof(Appointment), x.Appointment.Id, "needs_reschedule", new { x.Appointment.StartsAt, x.Appointment.DoctorId, x.Dto.Reason }, title,
                branchId: x.Appointment.BranchId);
        }
    }

    private async Task NotifyAsync(List<Affected> affected, AffectedAppointmentsAction action, string title, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var tz = ScheduleService.FindTz(org.Timezone);
        foreach (var g in affected.GroupBy(x => x.Appointment.BranchId))
        {
            var lines = g.Take(20).Select(x =>
                $"{TimeZoneInfo.ConvertTime(x.Appointment.StartsAt, tz).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture)} — {x.Dto.PatientName} ({x.Dto.DoctorName})");
            var head = action == AffectedAppointmentsAction.CancelToWaitlist
                ? $"Отменено записей: {g.Count()} — пациенты в листе ожидания"
                : $"Нужно перенести записи: {g.Count()}";
            await notifications.NotifyByPermissionAsync(Perm.Schedule.Manage, "schedule_conflict", head,
                $"{title}.\n" + string.Join("\n", lines), nameof(Appointment), g.First().Appointment.Id, g.Key, ct);
        }
    }

    /// <summary>Сообщения пациентам — только после коммита, чтобы не отправить их при откате.</summary>
    private async Task SendPatientMessagesAsync(List<Appointment> cancelled, CancellationToken ct)
    {
        if (cancelled.Count == 0) return;
        try
        {
            foreach (var a in cancelled)
                foreach (var s in senders)
                    await s.SendAsync(a, PatientMessageTemplate, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Изменение графика уже сохранено; сбой отправки не должен его откатывать — сообщение видно в журнале отправок.
        }
    }
}
