using System;
using System.Linq;
using Dental.Schedule;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

public class ScheduleCalculationTests
{
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _doctor = Guid.NewGuid();
    private readonly DateOnly _date = new(2026, 10, 5); // Monday
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Almaty");

    private DoctorSchedule Shift(int start = 9, int end = 18) => new(Guid.NewGuid(), null, _doctor, _branch,
        1, new(start, 0), new(end, 0), null, _date);
    private ScheduleException Exception(ScheduleExceptionType type, Guid? branch = null, int? start = null, int? end = null) =>
        new(Guid.NewGuid(), null, _doctor, branch, _date, _date, type,
            start == null ? null : new TimeOnly(start.Value, 0), end == null ? null : new TimeOnly(end.Value, 0), null);

    [Fact]
    public void Clinic_Local_Shift_Is_Converted_To_Utc()
    {
        var result = SlotCalculator.WorkingIntervals(_date, _branch, [Shift()], [], Tz).Single();
        result.Start.ShouldBe(new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc));
        result.End.ShouldBe(new DateTime(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(ScheduleExceptionType.Vacation)]
    [InlineData(ScheduleExceptionType.Sick)]
    [InlineData(ScheduleExceptionType.DayOff)]
    public void Full_Day_Absence_Overrides_An_Extra_Shift(ScheduleExceptionType type)
    {
        SlotCalculator.WorkingIntervals(_date, _branch, [Shift()],
            [Exception(type), Exception(ScheduleExceptionType.ExtraShift, _branch, 18, 20)], Tz).ShouldBeEmpty();
    }

    [Fact]
    public void Another_Branch_Absence_Does_Not_Close_This_Branch()
    {
        SlotCalculator.WorkingIntervals(_date, _branch, [Shift()],
            [Exception(ScheduleExceptionType.DayOff, Guid.NewGuid())], Tz).Count.ShouldBe(1);
    }

    [Fact]
    public void Partial_Absence_Splits_A_Shift()
    {
        var working = SlotCalculator.WorkingIntervals(_date, _branch, [Shift()],
            [Exception(ScheduleExceptionType.DayOff, start: 12, end: 13)], Tz);
        working.Count.ShouldBe(2);
        working[0].End.ShouldBe(SlotCalculator.ToUtc(_date.ToDateTime(new(12, 0)), Tz));
        working[1].Start.ShouldBe(SlotCalculator.ToUtc(_date.ToDateTime(new(13, 0)), Tz));
    }

    [Fact]
    public void Adjacent_Slots_Are_Free_And_Overlap_Is_Not()
    {
        var working = SlotCalculator.WorkingIntervals(_date, _branch, [Shift(9, 11)], [], Tz);
        var busy = SlotCalculator.ToUtc(_date, new(9, 30), new(10, 0), Tz);
        var free = SlotCalculator.FreeSlots(working, [busy], TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(15));
        free.ShouldContain(s => s.End == busy.Start);
        free.ShouldContain(s => s.Start == busy.End);
        free.ShouldNotContain(s => s.Overlaps(busy));
    }

    [Fact]
    public void Invalid_Intervals_And_Slot_Step_Are_Rejected()
    {
        Should.Throw<BusinessException>(() => Shift(18, 9)).Code.ShouldBe(DentalDomainErrorCodes.ScheduleInvalidRange);
        Should.Throw<BusinessException>(() => Exception(ScheduleExceptionType.ExtraShift)).Code.ShouldBe(DentalDomainErrorCodes.ScheduleInvalidRange);
        Should.Throw<ArgumentOutOfRangeException>(() => SlotCalculator.FreeSlots([], [], TimeSpan.FromMinutes(30), TimeSpan.Zero));
    }

    [Fact]
    public void Closing_A_Template_Preserves_History()
    {
        var shift = Shift();
        shift.CloseBefore(_date.AddDays(7));
        shift.AppliesTo(_date).ShouldBeTrue();
        shift.AppliesTo(_date.AddDays(7)).ShouldBeFalse();
    }

    [Fact]
    public void Cross_Branch_Shift_Overlap_Respects_Validity_Dates()
    {
        var shift = Shift();
        var other = new DoctorSchedule(Guid.NewGuid(), null, _doctor, Guid.NewGuid(), 1, new(10, 0), new(12, 0), null, _date);
        DoctorScheduleManager.Overlaps(shift, other).ShouldBeTrue();
        shift.CloseBefore(_date);
        DoctorScheduleManager.Overlaps(shift, other).ShouldBeFalse();
    }
}
