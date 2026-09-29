using Dental.Domain.Organizations;
using Shouldly;
using Xunit;

namespace Dental.Domain.Tests;

/// <summary>«Вход вне рабочего времени филиала» (SPEC §8.8). По умолчанию: пн–пт 9–20, сб 9–15, вс выходной.</summary>
public class WorkingHoursTests
{
    private static readonly Branch Main = new() { WorkingHours = WorkingHours.Default() };
    private static readonly OrganizationSettings Settings = new();

    // 2026-09-28 — понедельник, 2026-10-03 — суббота, 2026-10-04 — воскресенье.
    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(5));

    [Theory]
    [InlineData(9, 28, 10, false)]
    [InlineData(9, 28, 8, true)]
    [InlineData(9, 28, 20, true)]
    [InlineData(9, 28, 23, true)]
    [InlineData(10, 3, 14, false)]
    [InlineData(10, 3, 16, true)]
    [InlineData(10, 4, 12, true)]
    public void SingleBranch(int month, int day, int hour, bool expected) =>
        Branch.IsOutsideWorkingHours([Main], Settings, At(month, day, hour)).ShouldBe(expected);

    [Fact]
    public void AnyBranchOpen_MeansWorkingTime()
    {
        var late = new Branch { WorkingHours = WorkingHours.Default() };
        late.WorkingHours.Days.First(d => d.DayOfWeek == (int)DayOfWeek.Sunday).IsWorking = true;
        Branch.IsOutsideWorkingHours([Main, late], Settings, At(10, 4, 12)).ShouldBeFalse();
    }

    [Fact]
    public void NoBranches_UsesOrganizationDefaults()
    {
        Branch.IsOutsideWorkingHours([], Settings, At(9, 28, 12)).ShouldBeFalse();
        Branch.IsOutsideWorkingHours([], Settings, At(9, 28, 22)).ShouldBeTrue();
    }
}
