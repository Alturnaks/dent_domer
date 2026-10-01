using System;
using Dental.Finance;
using Volo.Abp;

namespace Dental.Payroll;

public class PayrollScheme : FinanceEntity
{
    protected PayrollScheme() { }
    public PayrollScheme(Guid id, Guid? tenantId, Guid employeeId, PayrollSchemeType type, decimal percent, long fixedAmount, long shiftRate, DateOnly validFrom) : base(id, tenantId)
    {
        if (!Enum.IsDefined(type) || percent is < 0 or > 100 || fixedAmount < 0 || shiftRate < 0) throw new UserFriendlyException("Некорректная схема зарплаты.");
        EmployeeId = employeeId; Type = type; Percent = percent; FixedAmount = fixedAmount; ShiftRate = shiftRate; ValidFrom = validFrom;
    }
    public Guid EmployeeId { get; private set; }
    public PayrollSchemeType Type { get; private set; }
    public decimal Percent { get; private set; }
    public long FixedAmount { get; private set; }
    public long ShiftRate { get; private set; }
    public DateOnly ValidFrom { get; private set; }
}
public class PayrollPeriod : FinanceEntity
{
    protected PayrollPeriod() { }
    public PayrollPeriod(Guid id, Guid? tenantId, Guid branchId, DateOnly start, DateOnly end) : base(id, tenantId)
    {
        if (end < start || end.DayNumber - start.DayNumber > 91) throw new UserFriendlyException("Период должен составлять от 1 до 92 дней.");
        BranchId = branchId; PeriodStart = start; PeriodEnd = end;
    }
    public Guid BranchId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public PayrollPeriodStatus Status { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public void EnsureDraft() { if (Status != PayrollPeriodStatus.Draft) throw new UserFriendlyException("Утверждённую ведомость нельзя изменять."); }
    public void Approve(Guid user, DateTime now) { EnsureDraft(); Status = PayrollPeriodStatus.Approved; ApprovedBy = user; ApprovedAt = now; }
    public void MarkPaid(DateTime now) { if (Status != PayrollPeriodStatus.Approved) throw new UserFriendlyException("Сначала утвердите ведомость."); Status = PayrollPeriodStatus.Paid; PaidAt = now; }
}
public class PayrollEntry : FinanceEntity
{
    protected PayrollEntry() { }
    public PayrollEntry(Guid id, Guid? tenantId, Guid periodId, Guid employeeId) : base(id, tenantId) { PeriodId = periodId; EmployeeId = employeeId; }
    public Guid PeriodId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public long BaseRevenue { get; private set; }
    public long MaterialsCost { get; private set; }
    public long Accrued { get; private set; }
    public long Bonus { get; private set; }
    public long Penalty { get; private set; }
    public long Total { get; private set; }
    public string? Comment { get; private set; }
    public string Details { get; private set; } = "[]";
    public void Calculate(long revenue, long materials, long accrued, string details) { BaseRevenue = revenue; MaterialsCost = materials; Accrued = accrued; Details = details; Total = checked(Accrued + Bonus - Penalty); }
    public void Adjust(long bonus, long penalty, string comment)
    {
        if (bonus < 0 || penalty < 0 || string.IsNullOrWhiteSpace(comment) || comment.Length > 2000 || penalty > checked(Accrued + bonus)) throw new UserFriendlyException("Укажите причину и допустимые суммы бонуса и штрафа.");
        Bonus = bonus; Penalty = penalty; Comment = comment.Trim(); Total = checked(Accrued + Bonus - Penalty);
    }
}
public static class PayrollCalculator
{
    public static long Accrue(PayrollScheme scheme, long revenue, long materials, int days) => scheme.Type switch
    {
        PayrollSchemeType.PercentRevenue => Percent(revenue, scheme.Percent),
        PayrollSchemeType.PercentRevenueMinusMaterials => Percent(Math.Max(0, revenue - materials), scheme.Percent),
        PayrollSchemeType.FixedPlusPercent => checked(scheme.FixedAmount + Percent(revenue, scheme.Percent)),
        PayrollSchemeType.PerShift => checked(scheme.ShiftRate * days),
        _ => throw new ArgumentOutOfRangeException(nameof(scheme))
    };
    private static long Percent(long value, decimal percent) => checked((long)Math.Round(value * percent / 100m, MidpointRounding.AwayFromZero));
}
