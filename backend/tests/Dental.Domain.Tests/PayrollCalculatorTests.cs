using Dental.Domain.Payroll;
using Shouldly;
using Xunit;

namespace Dental.Domain.Tests;

/// <summary>Ведомость считается по 4 схемам (SPEC §16, этап 8). Суммы в тиынах.</summary>
public class PayrollCalculatorTests
{
    private static readonly PayrollInput Input = new(Revenue: 1_000_000_00, MaterialsCost: 150_000_00, Shifts: 12);

    [Fact]
    public void PercentRevenue() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.PercentRevenue, Percent = 30 }, Input).ShouldBe(300_000_00);

    [Fact]
    public void PercentRevenueMinusMaterials() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.PercentRevenueMinusMaterials, Percent = 40 }, Input).ShouldBe(340_000_00);

    [Fact]
    public void PercentRevenueMinusMaterials_NeverNegative() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.PercentRevenueMinusMaterials, Percent = 40 }, new PayrollInput(100_00, 500_00, 1)).ShouldBe(0);

    [Fact]
    public void FixedPlusPercent() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.FixedPlusPercent, Percent = 10, FixedAmount = 100_000_00 }, Input).ShouldBe(200_000_00);

    [Fact]
    public void PerShift() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.PerShift, ShiftRate = 25_000_00 }, Input).ShouldBe(300_000_00);

    [Fact]
    public void Percent_RoundsToTiyn() =>
        PayrollCalculator.Accrue(new PayrollScheme { Type = PayrollSchemeType.PercentRevenue, Percent = 12.5m }, new PayrollInput(333, 0, 0)).ShouldBe(42);

    [Fact]
    public void Entry_Total_IncludesBonusAndPenalty()
    {
        var e = new PayrollEntry { Accrued = 100_00, Bonus = 30_00, Penalty = 10_00 };
        e.RecalculateTotal();
        e.Total.ShouldBe(120_00);
    }
}
