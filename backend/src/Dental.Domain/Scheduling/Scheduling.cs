using Dental.Domain.Common;

namespace Dental.Domain.Scheduling;

/// <summary>Недельный шаблон рабочего времени врача (время — локальное для организации).</summary>
[Audited]
public class DoctorSchedule : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid BranchId { get; set; }
    /// <summary>0 = воскресенье … 6 = суббота.</summary>
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid? ChairId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public bool AppliesTo(DateOnly date) =>
        (int)date.DayOfWeek == Weekday && date >= ValidFrom && (ValidTo is null || date <= ValidTo);
}

public enum ScheduleExceptionType { Vacation, Sick, DayOff, ExtraShift }

[Audited]
public class ScheduleException : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid? BranchId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public ScheduleExceptionType Type { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Comment { get; set; }

    public bool Covers(DateOnly date) => date >= DateFrom && date <= DateTo;
}

[Audited]
public class TimeBlock : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Reason { get; set; }
}

public enum AppointmentStatus { Scheduled, Confirmed, Arrived, InChair, Completed, Cancelled, NoShow }
public enum AppointmentSource { Admin, Phone, Online, WalkIn }

[Audited]
public class Appointment : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public AppointmentSource Source { get; set; } = AppointmentSource.Admin;
    public string? Comment { get; set; }
    public Guid? CancelReasonId { get; set; }
    public string? CancelComment { get; set; }
    public Guid? RescheduledFromId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedVia { get; set; }
    public DateTimeOffset? Reminder24hSentAt { get; set; }
    public DateTimeOffset? Reminder2hSentAt { get; set; }
    public int Version { get; set; }
    public List<AppointmentService> Services { get; set; } = [];

    public static readonly AppointmentStatus[] ActiveStatuses =
        [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.Arrived, AppointmentStatus.InChair, AppointmentStatus.Completed];

    public bool IsActive => ActiveStatuses.Contains(Status);

    /// <summary>Допустимые переходы статусов.</summary>
    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to) => (from, to) switch
    {
        (AppointmentStatus.Scheduled, AppointmentStatus.Confirmed) => true,
        (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed, AppointmentStatus.Arrived) => true,
        (AppointmentStatus.Arrived, AppointmentStatus.InChair) => true,
        (AppointmentStatus.Arrived or AppointmentStatus.InChair, AppointmentStatus.Completed) => true,
        (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed, AppointmentStatus.Cancelled) => true,
        (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed, AppointmentStatus.NoShow) => true,
        (AppointmentStatus.NoShow, AppointmentStatus.Arrived) => true,
        _ => from == to,
    };
}

public class AppointmentService : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid ServiceId { get; set; }
    public long PlannedPrice { get; set; }
    public int Qty { get; set; } = 1;
}

public enum CancelReasonType { Cancel, Reschedule }

[Audited]
public class CancelReason : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public CancelReasonType Type { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum WaitlistStatus { Waiting, Offered, Booked, Cancelled }

[Audited]
public class WaitlistEntry : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? ServiceId { get; set; }
    public DateOnly? PreferredFrom { get; set; }
    public DateOnly? PreferredTo { get; set; }
    /// <summary>JSON: например ["morning","evening"].</summary>
    public string? PreferredTimes { get; set; }
    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public string? Comment { get; set; }
}

/// <summary>Интервал времени [Start, End).</summary>
public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
    public bool Contains(TimeRange other) => Start <= other.Start && other.End <= End;
    public TimeSpan Duration => End - Start;
}

/// <summary>Расчёт рабочего времени врача и свободных слотов (чистая логика, без БД).</summary>
public static class SlotCalculator
{
    /// <summary>Рабочие интервалы врача на дату в UTC с учётом шаблона и исключений.</summary>
    public static IReadOnlyList<TimeRange> WorkingIntervals(
        DateOnly date,
        Guid branchId,
        IEnumerable<DoctorSchedule> schedules,
        IEnumerable<ScheduleException> exceptions,
        TimeZoneInfo tz)
    {
        var ex = exceptions.Where(e => e.Covers(date)).ToList();
        if (ex.Any(e => e.Type is ScheduleExceptionType.Vacation or ScheduleExceptionType.Sick or ScheduleExceptionType.DayOff
                        && e.StartTime is null))
        {
            // Целый день отсутствует, но дополнительные смены всё ещё возможны.
            return ex.Where(e => e.Type == ScheduleExceptionType.ExtraShift && e.StartTime is not null && e.EndTime is not null
                                 && (e.BranchId is null || e.BranchId == branchId))
                     .Select(e => ToUtc(date, e.StartTime!.Value, e.EndTime!.Value, tz)).ToList();
        }

        var result = schedules.Where(s => s.BranchId == branchId && s.AppliesTo(date))
            .Select(s => ToUtc(date, s.StartTime, s.EndTime, tz))
            .ToList();

        foreach (var e in ex.Where(e => e.Type == ScheduleExceptionType.ExtraShift && e.StartTime is not null && e.EndTime is not null
                                        && (e.BranchId is null || e.BranchId == branchId)))
        {
            result.Add(ToUtc(date, e.StartTime!.Value, e.EndTime!.Value, tz));
        }

        // Частичные отсутствия (например, отгул на половину дня) вырезаем.
        foreach (var e in ex.Where(e => e.Type != ScheduleExceptionType.ExtraShift && e.StartTime is not null && e.EndTime is not null))
        {
            result = Subtract(result, ToUtc(date, e.StartTime!.Value, e.EndTime!.Value, tz));
        }

        return Merge(result);
    }

    public static bool IsWithinWorkingTime(TimeRange slot, IReadOnlyList<TimeRange> working) =>
        working.Any(w => w.Contains(slot));

    /// <summary>Свободные слоты заданной длительности с шагом step внутри рабочих интервалов.</summary>
    public static IReadOnlyList<TimeRange> FreeSlots(
        IReadOnlyList<TimeRange> working,
        IEnumerable<TimeRange> busy,
        TimeSpan duration,
        TimeSpan step,
        DateTimeOffset? notBefore = null)
    {
        var busyList = busy.ToList();
        var slots = new List<TimeRange>();
        foreach (var w in working)
        {
            for (var start = w.Start; start + duration <= w.End; start += step)
            {
                if (notBefore is not null && start < notBefore) continue;
                var candidate = new TimeRange(start, start + duration);
                if (!busyList.Any(b => b.Overlaps(candidate))) slots.Add(candidate);
            }
        }
        return slots;
    }

    public static TimeRange ToUtc(DateOnly date, TimeOnly from, TimeOnly to, TimeZoneInfo tz)
    {
        var start = ToUtc(date.ToDateTime(from), tz);
        var end = ToUtc(date.ToDateTime(to), tz);
        return new TimeRange(start, end);
    }

    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    public static List<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        var sorted = ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start).ToList();
        var merged = new List<TimeRange>();
        foreach (var r in sorted)
        {
            if (merged.Count > 0 && merged[^1].End >= r.Start)
            {
                var last = merged[^1];
                merged[^1] = new TimeRange(last.Start, r.End > last.End ? r.End : last.End);
            }
            else
            {
                merged.Add(r);
            }
        }
        return merged;
    }

    public static List<TimeRange> Subtract(IEnumerable<TimeRange> ranges, TimeRange cut)
    {
        var result = new List<TimeRange>();
        foreach (var r in ranges)
        {
            if (!r.Overlaps(cut)) { result.Add(r); continue; }
            if (r.Start < cut.Start) result.Add(new TimeRange(r.Start, cut.Start));
            if (cut.End < r.End) result.Add(new TimeRange(cut.End, r.End));
        }
        return result;
    }
}
