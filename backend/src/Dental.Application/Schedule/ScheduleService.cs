using System.Data.Common;
using Dental.Application.Catalog;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Schedule;

public sealed class ScheduleService(
    IAppDbContext db,
    ICurrentUser user,
    IAuditService audit,
    IOutbox outbox,
    CatalogService catalog,
    TimeProvider clock,
    IEnumerable<IAppointmentArrivalHandler> arrivalHandlers,
    IEnumerable<IReminderSender> reminderSenders)
{
    // ---------- Общие ----------
    public async Task<(TimeZoneInfo Tz, Organization Org)> OrgTimeAsync(CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        return (FindTz(org.Timezone), org);
    }

    public static TimeZoneInfo FindTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("UTC+05", TimeSpan.FromHours(5), "UTC+05", "UTC+05"); }
    }

    private bool CanViewAll => user.Has(Perm.Schedule.ViewAll) || user.Has(Perm.Schedule.Manage);

    /// <summary>Врач без права view_all видит только своё расписание.</summary>
    private void EnsureCanSeeDoctor(Guid doctorId)
    {
        if (!CanViewAll && doctorId != user.MembershipId) throw AppException.Forbidden("Можно смотреть только своё расписание");
    }

    // ---------- Графики ----------
    public async Task<IReadOnlyList<DoctorScheduleDto>> ListSchedulesAsync(Guid? branchId, Guid? doctorId, CancellationToken ct)
    {
        var q = db.DoctorSchedules.AsNoTracking();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(s => s.BranchId == b); }
        if (doctorId is { } d) q = q.Where(s => s.MembershipId == d);
        if (!CanViewAll) q = q.Where(s => s.MembershipId == user.MembershipId);
        return await q.OrderBy(s => s.MembershipId).ThenBy(s => s.Weekday).ThenBy(s => s.StartTime).Select(s => ToDto(s)).ToListAsync(ct);
    }

    public async Task<DoctorScheduleDto> CreateScheduleAsync(DoctorScheduleRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var s = new DoctorSchedule
        {
            MembershipId = r.MembershipId, BranchId = r.BranchId, Weekday = r.Weekday, StartTime = r.StartTime, EndTime = r.EndTime,
            ChairId = r.ChairId, ValidFrom = r.ValidFrom ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ValidTo = r.ValidTo,
        };
        db.DoctorSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task<DoctorScheduleDto> UpdateScheduleAsync(Guid id, DoctorScheduleRequest r, CancellationToken ct)
    {
        var s = await db.DoctorSchedules.GetOrThrowAsync(id, "График", ct);
        user.EnsureBranchAccess(s.BranchId);
        user.EnsureBranchAccess(r.BranchId);
        s.BranchId = r.BranchId;
        s.Weekday = r.Weekday;
        s.StartTime = r.StartTime;
        s.EndTime = r.EndTime;
        s.ChairId = r.ChairId;
        if (r.ValidFrom is not null) s.ValidFrom = r.ValidFrom.Value;
        s.ValidTo = r.ValidTo;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    /// <summary>
    /// Удаление строки шаблона: графики не удаляются физически, а закрываются датой (valid_to = вчера),
    /// чтобы отчёт о загрузке за прошлые периоды оставался корректным.
    /// </summary>
    public async Task DeleteScheduleAsync(Guid id, CancellationToken ct)
    {
        var s = await db.DoctorSchedules.GetOrThrowAsync(id, "График", ct);
        user.EnsureBranchAccess(s.BranchId);
        var (tz, _) = await OrgTimeAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        s.ValidTo = today.AddDays(-1) < s.ValidFrom ? s.ValidFrom.AddDays(-1) : today.AddDays(-1);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Замена недельного шаблона: действующие строки закрываются, создаются новые с даты validFrom.</summary>
    public async Task<IReadOnlyList<DoctorScheduleDto>> ReplaceWeekTemplateAsync(DoctorWeekTemplateRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var (tz, _) = await OrgTimeAsync(ct);
        var from = r.ValidFrom ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        var current = await db.DoctorSchedules.Where(s => s.MembershipId == r.MembershipId && s.BranchId == r.BranchId && (s.ValidTo == null || s.ValidTo >= from)).ToListAsync(ct);
        foreach (var s in current) s.ValidTo = from.AddDays(-1) < s.ValidFrom ? s.ValidFrom.AddDays(-1) : from.AddDays(-1);
        var created = r.Days.Select(d => new DoctorSchedule
        {
            MembershipId = r.MembershipId, BranchId = r.BranchId, Weekday = d.Weekday, StartTime = d.StartTime, EndTime = d.EndTime, ChairId = d.ChairId, ValidFrom = from,
        }).ToList();
        db.DoctorSchedules.AddRange(created);
        await db.SaveChangesAsync(ct);
        return created.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ScheduleExceptionDto>> ListExceptionsAsync(Guid? doctorId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = db.ScheduleExceptions.AsNoTracking();
        if (doctorId is { } d) q = q.Where(e => e.MembershipId == d);
        if (!CanViewAll) q = q.Where(e => e.MembershipId == user.MembershipId);
        if (from is { } f) q = q.Where(e => e.DateTo >= f);
        if (to is { } t) q = q.Where(e => e.DateFrom <= t);
        return await q.OrderByDescending(e => e.DateFrom).Select(e => ToDto(e)).ToListAsync(ct);
    }

    public async Task<ScheduleExceptionDto> CreateExceptionAsync(ScheduleExceptionRequest r, CancellationToken ct)
    {
        if (r.BranchId is { } b) user.EnsureBranchAccess(b);
        var e = new ScheduleException
        {
            MembershipId = r.MembershipId, BranchId = r.BranchId, DateFrom = r.DateFrom, DateTo = r.DateTo, Type = r.Type,
            StartTime = r.StartTime, EndTime = r.EndTime, Comment = r.Comment.NullIfEmpty(),
        };
        db.ScheduleExceptions.Add(e);
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    public async Task<IReadOnlyList<TimeBlockDto>> ListBlocksAsync(Guid branchId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        return await db.TimeBlocks.AsNoTracking().Where(b => b.BranchId == branchId && b.StartsAt < to && b.EndsAt > from)
            .OrderBy(b => b.StartsAt).Select(b => ToDto(b)).ToListAsync(ct);
    }

    public async Task<TimeBlockDto> CreateBlockAsync(TimeBlockRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var b = new TimeBlock { BranchId = r.BranchId, DoctorId = r.DoctorId, ChairId = r.ChairId, StartsAt = r.StartsAt.ToUniversalTime(), EndsAt = r.EndsAt.ToUniversalTime(), Reason = r.Reason.NullIfEmpty() };
        db.TimeBlocks.Add(b);
        await db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    /// <summary>Блокировки — служебные отметки; удаление снимает блокировку (фиксируется в аудите).</summary>
    public async Task DeleteBlockAsync(Guid id, CancellationToken ct)
    {
        var b = await db.TimeBlocks.GetOrThrowAsync(id, "Блокировка", ct);
        user.EnsureBranchAccess(b.BranchId);
        audit.Log(nameof(TimeBlock), b.Id, "delete", new { b.StartsAt, b.EndsAt, b.DoctorId, b.ChairId, b.Reason }, branchId: b.BranchId);
        db.TimeBlocks.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Рабочее время ----------
    public async Task<IReadOnlyList<TimeRange>> WorkingIntervalsAsync(Guid doctorId, Guid branchId, DateOnly date, TimeZoneInfo tz, CancellationToken ct)
    {
        var schedules = await db.DoctorSchedules.AsNoTracking().Where(s => s.MembershipId == doctorId && s.BranchId == branchId).ToListAsync(ct);
        var exceptions = await db.ScheduleExceptions.AsNoTracking().Where(e => e.MembershipId == doctorId && e.DateFrom <= date && e.DateTo >= date).ToListAsync(ct);
        return SlotCalculator.WorkingIntervals(date, branchId, schedules, exceptions, tz);
    }

    private async Task EnsureDoctorWorkingAsync(Guid doctorId, Guid branchId, DateTimeOffset start, DateTimeOffset end, bool force, TimeZoneInfo tz, CancellationToken ct)
    {
        var localStart = TimeZoneInfo.ConvertTime(start, tz);
        var date = DateOnly.FromDateTime(localStart.DateTime);
        var working = await WorkingIntervalsAsync(doctorId, branchId, date, tz, ct);
        var ok = SlotCalculator.IsWithinWorkingTime(new TimeRange(start, end), working);
        if (ok) return;
        if (force && user.Has(Perm.Schedule.DoctorSchedulesManage))
        {
            audit.Log(nameof(Appointment), null, "force_outside_schedule", new { doctorId, start, end }, "Запись вне графика врача (force)", branchId: branchId);
            return;
        }
        throw new AppException(ErrorCodes.DoctorNotWorking, "Врач не работает в это время по графику", 422,
            new Dictionary<string, object?> { ["working"] = working.Select(w => new { start = w.Start, end = w.End }).ToList(), ["canForce"] = user.Has(Perm.Schedule.DoctorSchedulesManage) });
    }

    // ---------- Записи ----------
    public async Task<AppointmentDto> GetAppointmentAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().Include(x => x.Services).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        EnsureCanSeeDoctor(a.DoctorId);
        return (await MapAsync([a], ct))[0];
    }

    public async Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var (tz, _) = await OrgTimeAsync(ct);
        _ = await db.Patients.FirstOrDefaultAsync(p => p.Id == r.PatientId && p.DeletedAt == null && p.MergedIntoId == null, ct) ?? throw AppException.NotFound("Пациент");
        var doctor = await db.Memberships.FirstOrDefaultAsync(m => m.Id == r.DoctorId && m.IsActive, ct) ?? throw AppException.NotFound("Врач");
        if (!doctor.HasBranch(r.BranchId)) throw AppException.BadRequest(ErrorCodes.DoctorNotWorking, "Врач не работает в этом филиале");

        var services = await BuildServicesAsync(r.BranchId, r.Services, ct);
        var start = r.StartsAt.ToUniversalTime();
        var duration = services.Count == 0 ? 30 : Math.Max(15, services.Sum(s => s.Duration * s.Row.Qty));
        var end = (r.EndsAt ?? start.AddMinutes(duration)).ToUniversalTime();

        await EnsureDoctorWorkingAsync(r.DoctorId, r.BranchId, start, end, r.Force == true, tz, ct);
        await EnsureNoBlocksAsync(r.BranchId, r.DoctorId, r.ChairId, start, end, ct);

        var a = new Appointment
        {
            BranchId = r.BranchId, PatientId = r.PatientId, DoctorId = r.DoctorId, ChairId = r.ChairId, StartsAt = start, EndsAt = end,
            Source = r.Source ?? AppointmentSource.Admin, Comment = r.Comment.NullIfEmpty(), Status = AppointmentStatus.Scheduled,
        };
        a.Services = services.Select(s => { s.Row.AppointmentId = a.Id; return s.Row; }).ToList();
        db.Appointments.Add(a);
        await SaveWithSlotCheckAsync(a, ct);
        return await GetAppointmentAsync(a.Id, ct);
    }

    public async Task<AppointmentDto> UpdateAppointmentAsync(Guid id, UpdateAppointmentRequest r, CancellationToken ct)
    {
        var a = await db.Appointments.Include(x => x.Services).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        db.SetExpectedVersion(a, r.Version);
        if (r.Comment is not null) a.Comment = r.Comment.NullIfEmpty();
        if (r.ChairId is not null) a.ChairId = r.ChairId == Guid.Empty ? null : r.ChairId;
        if (r.Services is not null)
        {
            if (a.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed or AppointmentStatus.Arrived or AppointmentStatus.InChair))
                throw AppException.Conflict(ErrorCodes.InvalidStatusTransition, "Услуги можно менять только у активной записи");
            var services = await BuildServicesAsync(a.BranchId, r.Services, ct);
            a.Services.Clear();
            foreach (var s in services) { s.Row.AppointmentId = a.Id; a.Services.Add(s.Row); }
        }
        await SaveWithSlotCheckAsync(a, ct);
        return await GetAppointmentAsync(a.Id, ct);
    }

    /// <summary>Перенос = изменение времени той же записи; старое и новое время попадают в аудит.</summary>
    public async Task<AppointmentDto> MoveAppointmentAsync(Guid id, MoveAppointmentRequest r, CancellationToken ct)
    {
        var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        db.SetExpectedVersion(a, r.Version);
        if (a.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed))
            throw AppException.Conflict(ErrorCodes.InvalidStatusTransition, "Перенести можно только запланированную или подтверждённую запись");
        var (tz, _) = await OrgTimeAsync(ct);

        var duration = a.EndsAt - a.StartsAt;
        var start = r.StartsAt.ToUniversalTime();
        var end = (r.EndsAt ?? start + duration).ToUniversalTime();
        if (end <= start) throw AppException.BadRequest(ErrorCodes.InvalidTimeRange, "Окончание позже начала");
        var doctorId = r.DoctorId ?? a.DoctorId;
        if (doctorId != a.DoctorId)
        {
            var doctor = await db.Memberships.FirstOrDefaultAsync(m => m.Id == doctorId && m.IsActive, ct) ?? throw AppException.NotFound("Врач");
            if (!doctor.HasBranch(a.BranchId)) throw AppException.BadRequest(ErrorCodes.DoctorNotWorking, "Врач не работает в этом филиале");
        }
        await EnsureDoctorWorkingAsync(doctorId, a.BranchId, start, end, r.Force == true, tz, ct);
        await EnsureNoBlocksAsync(a.BranchId, doctorId, r.ChairId ?? a.ChairId, start, end, ct);

        string? reasonName = null;
        if (r.ReasonId is { } reasonId)
        {
            var reason = await db.CancelReasons.FirstOrDefaultAsync(x => x.Id == reasonId, ct) ?? throw AppException.NotFound("Причина");
            reasonName = reason.Name;
        }
        var old = new { a.StartsAt, a.EndsAt, a.DoctorId, a.ChairId };
        a.StartsAt = start;
        a.EndsAt = end;
        a.DoctorId = doctorId;
        if (r.ChairId is not null) a.ChairId = r.ChairId == Guid.Empty ? null : r.ChairId;
        // Перенос сбрасывает подтверждение и напоминания.
        a.Status = AppointmentStatus.Scheduled;
        a.ConfirmedAt = null;
        a.Reminder24hSentAt = null;
        a.Reminder2hSentAt = null;
        audit.Log(nameof(Appointment), a.Id, "move", new { old, @new = new { a.StartsAt, a.EndsAt, a.DoctorId, a.ChairId } }, reasonName, branchId: a.BranchId);
        await SaveWithSlotCheckAsync(a, ct);
        return await GetAppointmentAsync(a.Id, ct);
    }

    public async Task<AppointmentDto> ChangeStatusAsync(Guid id, AppointmentStatusRequest r, CancellationToken ct)
    {
        var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        EnsureCanSeeDoctor(a.DoctorId);
        db.SetExpectedVersion(a, r.Version);
        if (!Appointment.CanTransition(a.Status, r.Status))
            throw AppException.Conflict(ErrorCodes.InvalidStatusTransition, $"Нельзя перевести запись из статуса {a.Status} в {r.Status}");
        if (a.Status == r.Status) return await GetAppointmentAsync(a.Id, ct);

        var now = clock.GetUtcNow();
        switch (r.Status)
        {
            case AppointmentStatus.Cancelled:
                if (r.ReasonId is null) throw AppException.BadRequest(ErrorCodes.CancelReasonRequired, "Укажите причину отмены");
                _ = await db.CancelReasons.FirstOrDefaultAsync(x => x.Id == r.ReasonId, ct) ?? throw AppException.NotFound("Причина");
                a.CancelReasonId = r.ReasonId;
                a.CancelComment = r.Comment.NullIfEmpty();
                a.Status = AppointmentStatus.Cancelled;
                outbox.Enqueue(OutboxTypes.AppointmentCancelled, new AppointmentCancelledEvent(a.Id, a.BranchId, a.DoctorId, a.StartsAt, a.EndsAt));
                break;
            case AppointmentStatus.Confirmed:
                a.Status = AppointmentStatus.Confirmed;
                a.ConfirmedAt = now;
                a.ConfirmedVia = r.Comment.NullIfEmpty() ?? "admin";
                break;
            case AppointmentStatus.Arrived:
                a.Status = AppointmentStatus.Arrived;
                await db.SaveChangesAsync(ct);
                foreach (var h in arrivalHandlers) await h.OnArrivedAsync(a, ct);
                break;
            case AppointmentStatus.Completed:
                if (!user.Has(Perm.Visits.Complete) && !user.Has(Perm.Schedule.Manage)) throw AppException.Forbidden();
                a.Status = AppointmentStatus.Completed;
                break;
            default:
                a.Status = r.Status;
                break;
        }
        await db.SaveChangesAsync(ct);
        return await GetAppointmentAsync(a.Id, ct);
    }

    public async Task<bool> RemindAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        var sent = false;
        foreach (var s in reminderSenders) sent |= await s.SendAsync(a, "reminder_24h", ct);
        await db.SaveChangesAsync(ct);
        return sent;
    }

    private sealed record ServiceRow(AppointmentService Row, int Duration);

    private async Task<List<ServiceRow>> BuildServicesAsync(Guid branchId, IReadOnlyList<AppointmentServiceInput>? inputs, CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0) return [];
        var ids = inputs.Select(i => i.ServiceId).Distinct().ToList();
        var services = await db.Services.AsNoTracking().Where(s => ids.Contains(s.Id) && s.DeletedAt == null).ToDictionaryAsync(s => s.Id, ct);
        if (services.Count != ids.Count) throw AppException.NotFound("Услуга");
        var prices = await catalog.ResolvePricesAsync(branchId, ids, ct);
        return inputs.Select(i => new ServiceRow(new AppointmentService { ServiceId = i.ServiceId, Qty = Math.Max(1, i.Qty ?? 1), PlannedPrice = prices.GetValueOrDefault(i.ServiceId) },
            services[i.ServiceId].DurationMin)).ToList();
    }

    private async Task EnsureNoBlocksAsync(Guid branchId, Guid doctorId, Guid? chairId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var block = await db.TimeBlocks.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == branchId && b.StartsAt < end && b.EndsAt > start &&
            ((b.DoctorId == null && b.ChairId == null) || b.DoctorId == doctorId || (chairId != null && b.ChairId == chairId)), ct);
        if (block is not null)
            throw AppException.Conflict(ErrorCodes.SlotConflict, "Время заблокировано" + (block.Reason is null ? "" : $": {block.Reason}"),
                new Dictionary<string, object?> { ["blockId"] = block.Id, ["startsAt"] = block.StartsAt, ["endsAt"] = block.EndsAt });
    }

    /// <summary>Пересечения запрещены на уровне БД (EXCLUDE USING gist) → 409 SLOT_CONFLICT с данными о конфликтующей записи.</summary>
    private async Task SaveWithSlotCheckAsync(Appointment a, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23P01" })
        {
            var conflict = await db.Appointments.AsNoTracking()
                .Where(x => x.Id != a.Id && x.StartsAt < a.EndsAt && x.EndsAt > a.StartsAt && Appointment.ActiveStatuses.Contains(x.Status)
                            && (x.DoctorId == a.DoctorId || (a.ChairId != null && x.ChairId == a.ChairId)))
                .Select(x => new { x.Id, x.StartsAt, x.EndsAt, x.DoctorId, x.ChairId, x.PatientId })
                .FirstOrDefaultAsync(ct);
            throw AppException.Conflict(ErrorCodes.SlotConflict, "Время уже занято другой записью",
                new Dictionary<string, object?> { ["conflict"] = conflict });
        }
    }

    // ---------- Календарь ----------
    public async Task<CalendarResponse> CalendarAsync(Guid branchId, DateOnly from, DateOnly to, IReadOnlyList<Guid>? doctorIds, string view, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 62) to = from.AddDays(62);
        var (tz, org) = await OrgTimeAsync(ct);
        var startUtc = SlotCalculator.ToUtc(from.ToDateTime(TimeOnly.MinValue), tz);
        var endUtc = SlotCalculator.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);

        var doctorsQuery = from m in db.Memberships.AsNoTracking()
                           join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                           where m.IsActive && m.Position == StaffPosition.Doctor && (m.AllBranches || m.BranchIds.Contains(branchId))
                           select new { m.Id, u.FullName, m.Color, m.Specialty };
        var doctors = await doctorsQuery.OrderBy(d => d.FullName).ToListAsync(ct);
        if (!CanViewAll) doctors = doctors.Where(d => d.Id == user.MembershipId).ToList();
        if (doctorIds is { Count: > 0 }) doctors = doctors.Where(d => doctorIds.Contains(d.Id)).ToList();
        var docIds = doctors.Select(d => d.Id).ToList();

        var apptQuery = db.Appointments.AsNoTracking().Include(a => a.Services)
            .Where(a => a.BranchId == branchId && a.StartsAt < endUtc && a.EndsAt > startUtc);
        if (!CanViewAll || doctorIds is { Count: > 0 }) apptQuery = apptQuery.Where(a => docIds.Contains(a.DoctorId));
        var appointments = await apptQuery.OrderBy(a => a.StartsAt).ToListAsync(ct);

        var schedules = await db.DoctorSchedules.AsNoTracking().Where(s => s.BranchId == branchId && docIds.Contains(s.MembershipId)).ToListAsync(ct);
        var exceptions = await db.ScheduleExceptions.AsNoTracking().Where(e => docIds.Contains(e.MembershipId) && e.DateFrom <= to && e.DateTo >= from).ToListAsync(ct);
        var working = new List<CalendarWorkingInterval>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            foreach (var doc in docIds)
            {
                var chair = schedules.FirstOrDefault(s => s.MembershipId == doc && s.AppliesTo(d))?.ChairId;
                foreach (var w in SlotCalculator.WorkingIntervals(d, branchId, schedules.Where(s => s.MembershipId == doc), exceptions.Where(e => e.MembershipId == doc), tz))
                    working.Add(new CalendarWorkingInterval(doc, w.Start, w.End, chair));
            }
        }

        var blocks = await db.TimeBlocks.AsNoTracking().Where(b => b.BranchId == branchId && b.StartsAt < endUtc && b.EndsAt > startUtc).Select(b => ToDto(b)).ToListAsync(ct);

        IReadOnlyList<CalendarResource> resources;
        if (view == "chairs")
        {
            resources = await db.Chairs.AsNoTracking().Where(c => c.BranchId == branchId && c.DeletedAt == null && c.IsActive).OrderBy(c => c.Name)
                .Select(c => new CalendarResource(c.Id, c.Name, null, "chair", null)).ToListAsync(ct);
        }
        else
        {
            resources = doctors.Select(d => new CalendarResource(d.Id, d.FullName, d.Color, "doctor", d.Specialty)).ToList();
        }

        return new CalendarResponse(resources, await MapAsync(appointments, ct), working, blocks, exceptions.Select(ToDto).ToList(), org.Timezone, org.Settings.SlotMinutes);
    }

    public async Task<IReadOnlyList<AvailableSlot>> AvailableSlotsAsync(Guid branchId, Guid? doctorId, IReadOnlyList<Guid>? serviceIds, DateOnly date, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        var (tz, org) = await OrgTimeAsync(ct);
        var duration = 30;
        if (serviceIds is { Count: > 0 })
            duration = Math.Max(15, await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).SumAsync(s => s.DurationMin, ct));

        var doctorQuery = db.Memberships.AsNoTracking().Where(m => m.IsActive && m.Position == StaffPosition.Doctor && (m.AllBranches || m.BranchIds.Contains(branchId)));
        if (doctorId is { } d) doctorQuery = doctorQuery.Where(m => m.Id == d);
        var doctors = await doctorQuery.Select(m => m.Id).ToListAsync(ct);

        var dayStart = SlotCalculator.ToUtc(date.ToDateTime(TimeOnly.MinValue), tz);
        var dayEnd = dayStart.AddDays(1);
        var busy = await db.Appointments.AsNoTracking()
            .Where(a => doctors.Contains(a.DoctorId) && a.StartsAt < dayEnd && a.EndsAt > dayStart && Appointment.ActiveStatuses.Contains(a.Status))
            .Select(a => new { a.DoctorId, a.StartsAt, a.EndsAt }).ToListAsync(ct);
        var blocks = await db.TimeBlocks.AsNoTracking().Where(b => b.BranchId == branchId && b.StartsAt < dayEnd && b.EndsAt > dayStart)
            .Select(b => new { b.DoctorId, b.ChairId, b.StartsAt, b.EndsAt }).ToListAsync(ct);

        var now = clock.GetUtcNow();
        var result = new List<AvailableSlot>();
        foreach (var doc in doctors)
        {
            var working = await WorkingIntervalsAsync(doc, branchId, date, tz, ct);
            var docBusy = busy.Where(b => b.DoctorId == doc).Select(b => new TimeRange(b.StartsAt, b.EndsAt))
                .Concat(blocks.Where(b => b.DoctorId == doc || (b.DoctorId == null && b.ChairId == null)).Select(b => new TimeRange(b.StartsAt, b.EndsAt)));
            result.AddRange(SlotCalculator.FreeSlots(working, docBusy, TimeSpan.FromMinutes(duration), TimeSpan.FromMinutes(org.Settings.SlotMinutes), now)
                .Select(s => new AvailableSlot(s.Start, s.End, doc)));
        }
        return result.OrderBy(s => s.Start).ToList();
    }

    public async Task<IReadOnlyList<AppointmentDto>> MapAsync(IReadOnlyList<Appointment> list, CancellationToken ct)
    {
        if (list.Count == 0) return [];
        var patientIds = list.Select(a => a.PatientId).Distinct().ToList();
        var doctorIds = list.Select(a => a.DoctorId).Distinct().ToList();
        var chairIds = list.Where(a => a.ChairId != null).Select(a => a.ChairId!.Value).Distinct().ToList();
        var serviceIds = list.SelectMany(a => a.Services).Select(s => s.ServiceId).Distinct().ToList();
        var apptIds = list.Select(a => a.Id).ToList();

        var patients = await db.Patients.AsNoTracking().Where(p => patientIds.Contains(p.Id))
            .Select(p => new { p.Id, p.LastName, p.FirstName, p.MiddleName, p.Phone, p.IsVip }).ToDictionaryAsync(p => p.Id, ct);
        var balances = await db.PatientBalances.AsNoTracking().Where(b => patientIds.Contains(b.PatientId)).ToDictionaryAsync(b => b.PatientId, b => b.Balance, ct);
        var doctors = await (from m in db.Memberships.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                             where doctorIds.Contains(m.Id) select new { m.Id, u.FullName, m.Color }).ToDictionaryAsync(d => d.Id, ct);
        var chairs = await db.Chairs.AsNoTracking().Where(c => chairIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var visits = await db.Visits.AsNoTracking().Where(v => v.AppointmentId != null && apptIds.Contains(v.AppointmentId.Value))
            .Select(v => new { v.Id, v.AppointmentId }).ToDictionaryAsync(v => v.AppointmentId!.Value, v => v.Id, ct);

        return list.Select(a =>
        {
            var p = patients.GetValueOrDefault(a.PatientId);
            var d = doctors.GetValueOrDefault(a.DoctorId);
            var svc = a.Services.Select(s => new AppointmentServiceDto(s.ServiceId, services.GetValueOrDefault(s.ServiceId)?.Name ?? "?",
                services.GetValueOrDefault(s.ServiceId)?.DurationMin ?? 0, s.PlannedPrice, s.Qty)).ToList();
            var name = p is null ? "?" : string.Join(' ', new[] { p.LastName, p.FirstName, p.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return new AppointmentDto(a.Id, a.BranchId, a.PatientId, name, p?.Phone, p?.IsVip ?? false, balances.GetValueOrDefault(a.PatientId),
                a.DoctorId, d?.FullName ?? "?", d?.Color, a.ChairId, a.ChairId is { } c ? chairs.GetValueOrDefault(c) : null,
                a.StartsAt, a.EndsAt, a.Status, a.Source, a.Comment, a.CancelReasonId, a.CancelComment, a.ConfirmedAt, a.ConfirmedVia,
                visits.TryGetValue(a.Id, out var vid) ? vid : null, a.Version, svc, svc.Sum(s => s.PlannedPrice * s.Qty));
        }).ToList();
    }

    // ---------- Лист ожидания ----------
    public async Task<IReadOnlyList<WaitlistDto>> ListWaitlistAsync(Guid? branchId, WaitlistStatus? status, CancellationToken ct)
    {
        var q = db.Waitlist.AsNoTracking();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(w => w.BranchId == b); }
        else if (!user.AllBranches) q = q.Where(w => user.BranchIds.Contains(w.BranchId));
        q = q.Where(w => w.Status == (status ?? WaitlistStatus.Waiting));
        var rows = await q.OrderBy(w => w.CreatedAt).Take(500).ToListAsync(ct);
        return await MapWaitlistAsync(rows, ct);
    }

    public async Task<WaitlistDto> CreateWaitlistAsync(WaitlistRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var w = new WaitlistEntry
        {
            BranchId = r.BranchId, PatientId = r.PatientId, DoctorId = r.DoctorId, ServiceId = r.ServiceId, PreferredFrom = r.PreferredFrom,
            PreferredTo = r.PreferredTo, PreferredTimes = r.PreferredTimes.NullIfEmpty(), Comment = r.Comment.NullIfEmpty(),
        };
        db.Waitlist.Add(w);
        await db.SaveChangesAsync(ct);
        return (await MapWaitlistAsync([w], ct))[0];
    }

    public async Task<WaitlistDto> UpdateWaitlistAsync(Guid id, WaitlistRequest r, CancellationToken ct)
    {
        var w = await db.Waitlist.GetOrThrowAsync(id, "Запись листа ожидания", ct);
        user.EnsureBranchAccess(w.BranchId);
        w.DoctorId = r.DoctorId;
        w.ServiceId = r.ServiceId;
        w.PreferredFrom = r.PreferredFrom;
        w.PreferredTo = r.PreferredTo;
        w.PreferredTimes = r.PreferredTimes.NullIfEmpty();
        w.Comment = r.Comment.NullIfEmpty();
        if (r.Status is not null) w.Status = r.Status.Value;
        await db.SaveChangesAsync(ct);
        return (await MapWaitlistAsync([w], ct))[0];
    }

    private async Task<IReadOnlyList<WaitlistDto>> MapWaitlistAsync(IReadOnlyList<WaitlistEntry> rows, CancellationToken ct)
    {
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var dids = rows.Where(r => r.DoctorId != null).Select(r => r.DoctorId!.Value).Distinct().ToList();
        var sids = rows.Where(r => r.ServiceId != null).Select(r => r.ServiceId!.Value).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var doctors = await (from m in db.Memberships.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                             where dids.Contains(m.Id) select new { m.Id, u.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var services = await db.Services.AsNoTracking().Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return rows.Select(w => new WaitlistDto(w.Id, w.BranchId, w.PatientId, patients.GetValueOrDefault(w.PatientId)?.FullName ?? "?",
            patients.GetValueOrDefault(w.PatientId)?.Phone, w.DoctorId, w.DoctorId is { } d ? doctors.GetValueOrDefault(d) : null, w.ServiceId,
            w.ServiceId is { } s ? services.GetValueOrDefault(s) : null, w.PreferredFrom, w.PreferredTo, w.PreferredTimes, w.Status, w.Comment, w.CreatedAt)).ToList();
    }

    // ---------- Мапперы ----------
    public static DoctorScheduleDto ToDto(DoctorSchedule s) => new(s.Id, s.MembershipId, s.BranchId, s.Weekday, s.StartTime, s.EndTime, s.ChairId, s.ValidFrom, s.ValidTo);
    public static ScheduleExceptionDto ToDto(ScheduleException e) => new(e.Id, e.MembershipId, e.BranchId, e.DateFrom, e.DateTo, e.Type, e.StartTime, e.EndTime, e.Comment);
    public static TimeBlockDto ToDto(TimeBlock b) => new(b.Id, b.BranchId, b.DoctorId, b.ChairId, b.StartsAt, b.EndsAt, b.Reason);
}

public sealed record AppointmentCancelledEvent(Guid AppointmentId, Guid BranchId, Guid DoctorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
