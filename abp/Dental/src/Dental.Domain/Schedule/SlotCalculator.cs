using System;
using System.Collections.Generic;
using System.Linq;

namespace Dental.Schedule;

public readonly record struct TimeRange(DateTime Start, DateTime End)
{
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
    public bool Contains(TimeRange other) => Start <= other.Start && other.End <= End;
}

public static class SlotCalculator
{
    public static IReadOnlyList<TimeRange> WorkingIntervals(DateOnly date, Guid branchId,
        IEnumerable<DoctorSchedule> schedules, IEnumerable<ScheduleException> exceptions, TimeZoneInfo timezone)
    {
        var applicable = exceptions.Where(e => e.Covers(date) && (e.BranchId == null || e.BranchId == branchId)).ToList();
        // A full-day absence takes precedence over an extra shift.
        if (applicable.Any(e => e.Type != ScheduleExceptionType.ExtraShift && e.StartTime == null)) return [];
        var intervals = schedules.Where(s => s.BranchId == branchId && s.AppliesTo(date))
            .Select(s => ToUtc(date, s.StartTime, s.EndTime, timezone)).ToList();
        intervals.AddRange(applicable.Where(e => e.Type == ScheduleExceptionType.ExtraShift)
            .Select(e => ToUtc(date, e.StartTime!.Value, e.EndTime!.Value, timezone)));
        foreach (var absence in applicable.Where(e => e.Type != ScheduleExceptionType.ExtraShift && e.StartTime != null))
            intervals = Subtract(intervals, ToUtc(date, absence.StartTime!.Value, absence.EndTime!.Value, timezone));
        return Merge(intervals);
    }

    public static IReadOnlyList<TimeRange> FreeSlots(IEnumerable<TimeRange> working, IEnumerable<TimeRange> busy,
        TimeSpan duration, TimeSpan step, DateTime? notBefore = null)
    {
        if (duration <= TimeSpan.Zero || step <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var occupied = busy.ToList();
        var result = new List<TimeRange>();
        foreach (var interval in Merge(working))
            for (var start = interval.Start; start + duration <= interval.End; start += step)
            {
                var slot = new TimeRange(start, start + duration);
                if ((notBefore == null || start >= notBefore) && !occupied.Any(b => b.Overlaps(slot))) result.Add(slot);
            }
        return result;
    }

    public static TimeRange ToUtc(DateOnly date, TimeOnly start, TimeOnly end, TimeZoneInfo timezone) =>
        new(ToUtc(date.ToDateTime(start), timezone), ToUtc(date.ToDateTime(end), timezone));

    public static DateTime ToUtc(DateTime local, TimeZoneInfo timezone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), timezone);

    public static List<TimeRange> Merge(IEnumerable<TimeRange> intervals)
    {
        var result = new List<TimeRange>();
        foreach (var interval in intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start))
        {
            if (result.Count > 0 && result[^1].End >= interval.Start)
                result[^1] = new(result[^1].Start, interval.End > result[^1].End ? interval.End : result[^1].End);
            else result.Add(interval);
        }
        return result;
    }

    public static List<TimeRange> Subtract(IEnumerable<TimeRange> intervals, TimeRange cut)
    {
        var result = new List<TimeRange>();
        foreach (var interval in intervals)
        {
            if (!interval.Overlaps(cut)) { result.Add(interval); continue; }
            if (interval.Start < cut.Start) result.Add(new(interval.Start, cut.Start));
            if (cut.End < interval.End) result.Add(new(cut.End, interval.End));
        }
        return result;
    }
}
