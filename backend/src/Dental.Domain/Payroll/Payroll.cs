using Dental.Domain.Common;

namespace Dental.Domain.Payroll;

public enum PayrollSchemeType { PercentRevenue, PercentRevenueMinusMaterials, FixedPlusPercent, PerShift }

[Audited]
public class PayrollScheme : TenantEntity
{
    public Guid MembershipId { get; set; }
    public PayrollSchemeType Type { get; set; }
    public decimal Percent { get; set; }
    public long FixedAmount { get; set; }
    public long ShiftRate { get; set; }
    public DateOnly ValidFrom { get; set; }
}

public enum PayrollPeriodStatus { Draft, Approved, Paid }

[Audited]
public class PayrollPeriod : TenantEntity
{
    public Guid BranchId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public PayrollPeriodStatus Status { get; set; } = PayrollPeriodStatus.Draft;
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
}

[Audited]
public class PayrollEntry : TenantEntity
{
    public Guid PeriodId { get; set; }
    public Guid MembershipId { get; set; }
    public long BaseRevenue { get; set; }
    public long MaterialsCost { get; set; }
    public long LabCost { get; set; }
    public long Accrued { get; set; }
    public long Bonus { get; set; }
    public long Penalty { get; set; }
    public long Total { get; set; }
    public string? Comment { get; set; }
    /// <summary>JSON: детализация по визитам.</summary>
    public string Details { get; set; } = "[]";

    public void RecalculateTotal() => Total = Accrued + Bonus - Penalty;
}

/// <summary>Входные данные расчёта зарплаты сотрудника за период.</summary>
public sealed record PayrollInput(long Revenue, long MaterialsCost, int Shifts, long LabCost = 0);

public static class PayrollCalculator
{
    /// <summary>Начисление по схеме (тиыны).</summary>
    public static long Accrue(PayrollScheme scheme, PayrollInput input) => scheme.Type switch
    {
        PayrollSchemeType.PercentRevenue => Pct(input.Revenue, scheme.Percent),
        PayrollSchemeType.PercentRevenueMinusMaterials => Pct(Math.Max(0, input.Revenue - input.MaterialsCost - input.LabCost), scheme.Percent),
        PayrollSchemeType.FixedPlusPercent => scheme.FixedAmount + Pct(input.Revenue, scheme.Percent),
        PayrollSchemeType.PerShift => scheme.ShiftRate * input.Shifts,
        _ => 0,
    };

    private static long Pct(long amount, decimal percent) => new Money(amount).Percent(percent).Minor;
}
