using Dental.Application.Common;
using Dental.Domain.Payroll;
using FluentValidation;

namespace Dental.Application.Payroll;

public sealed record PayrollSchemeDto(
    Guid Id, Guid MembershipId, string DoctorName, PayrollSchemeType Type, decimal Percent, long FixedAmount, long ShiftRate, DateOnly ValidFrom,
    bool IsCurrent, DateTimeOffset CreatedAt);

/// <summary>Новая версия схемы (история сохраняется: действует последняя с valid_from ≤ даты).</summary>
public sealed record PayrollSchemeRequest(Guid MembershipId, PayrollSchemeType Type, decimal? Percent, long? FixedAmount, long? ShiftRate, DateOnly ValidFrom);

public sealed record PayrollPeriodDto(
    Guid Id, Guid BranchId, string BranchName, DateOnly PeriodStart, DateOnly PeriodEnd, PayrollPeriodStatus Status, DateTimeOffset? ApprovedAt,
    string? ApprovedByName, int EntriesCount, long AccruedTotal, long BonusTotal, long PenaltyTotal, long Total, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PayrollVisitLine(
    Guid VisitId, DateTimeOffset ClosedAt, DateOnly Date, Guid PatientId, string PatientName, string Services, long Revenue, long MaterialsCost, bool Paid);

public sealed record PayrollSchemeSnapshot(PayrollSchemeType Type, decimal Percent, long FixedAmount, long ShiftRate, DateOnly ValidFrom);

/// <summary>Содержимое payroll_entries.details (JSONB).</summary>
public sealed record PayrollEntryDetails(PayrollSchemeSnapshot? Scheme, int Shifts, IReadOnlyList<DateOnly> WorkedDays, IReadOnlyList<PayrollVisitLine> Visits);

public sealed record PayrollEntryDto(
    Guid Id, Guid PeriodId, Guid MembershipId, string DoctorName, PayrollSchemeSnapshot? Scheme, int VisitsCount, int Shifts,
    long BaseRevenue, long MaterialsCost, long LabCost, long Accrued, long Bonus, long Penalty, long Total, string? Comment);

public sealed record PayrollEntryDetailDto(PayrollEntryDto Entry, IReadOnlyList<DateOnly> WorkedDays, IReadOnlyList<PayrollVisitLine> Visits);

public sealed record PayrollPeriodDetailDto(PayrollPeriodDto Period, IReadOnlyList<PayrollEntryDto> Entries, bool OnlyPaidVisits);

public sealed record CreatePayrollPeriodRequest(Guid BranchId, DateOnly PeriodStart, DateOnly PeriodEnd);

/// <summary>Бонус/штраф (тиыны) с обязательным комментарием, если что-то из них не 0.</summary>
public sealed record UpdatePayrollEntryRequest(long? Bonus, long? Penalty, string? Comment);

/// <summary>Начисление за текущий месяц (на лету, не сохраняется).</summary>
public sealed record PayrollAccrualDto(
    DateOnly PeriodStart, DateOnly PeriodEnd, PayrollSchemeSnapshot? Scheme, int VisitsCount, int Shifts, long BaseRevenue, long MaterialsCost, long Accrued,
    IReadOnlyList<PayrollVisitLine> Visits);

public sealed record MyPayrollEntryDto(
    Guid EntryId, Guid PeriodId, string BranchName, DateOnly PeriodStart, DateOnly PeriodEnd, PayrollPeriodStatus Status, PayrollSchemeSnapshot? Scheme,
    int VisitsCount, int Shifts, long BaseRevenue, long MaterialsCost, long Accrued, long Bonus, long Penalty, long Total, string? Comment,
    IReadOnlyList<PayrollVisitLine> Visits);

public sealed record MyPayrollDto(Guid MembershipId, string DoctorName, bool OnlyPaidVisits, PayrollAccrualDto CurrentMonth, IReadOnlyList<MyPayrollEntryDto> Periods);

public sealed class PayrollSchemeRequestValidator : AbstractValidator<PayrollSchemeRequest>
{
    public PayrollSchemeRequestValidator()
    {
        RuleFor(x => x.MembershipId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Percent).InclusiveBetween(0, 100).When(x => x.Percent is not null);
        RuleFor(x => x.Percent).Must(v => v is > 0)
            .When(x => x.Type is PayrollSchemeType.PercentRevenue or PayrollSchemeType.PercentRevenueMinusMaterials)
            .WithMessage("Укажите процент");
        RuleFor(x => x.FixedAmount).GreaterThanOrEqualTo(0).When(x => x.FixedAmount is not null);
        RuleFor(x => x.FixedAmount).Must(v => v is > 0).When(x => x.Type == PayrollSchemeType.FixedPlusPercent).WithMessage("Укажите оклад");
        RuleFor(x => x.ShiftRate).Must(v => v is > 0).When(x => x.Type == PayrollSchemeType.PerShift).WithMessage("Укажите ставку за смену");
    }
}

public sealed class CreatePayrollPeriodRequestValidator : AbstractValidator<CreatePayrollPeriodRequest>
{
    public CreatePayrollPeriodRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart).WithErrorCode(ErrorCodes.InvalidTimeRange).WithMessage("Конец периода раньше начала");
        RuleFor(x => x).Must(x => x.PeriodEnd.DayNumber - x.PeriodStart.DayNumber <= 92).WithMessage("Период не длиннее 3 месяцев");
    }
}

public sealed class UpdatePayrollEntryRequestValidator : AbstractValidator<UpdatePayrollEntryRequest>
{
    public UpdatePayrollEntryRequestValidator()
    {
        RuleFor(x => x.Bonus).GreaterThanOrEqualTo(0).When(x => x.Bonus is not null);
        RuleFor(x => x.Penalty).GreaterThanOrEqualTo(0).When(x => x.Penalty is not null);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}
