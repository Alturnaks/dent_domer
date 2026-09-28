using Dental.Domain.Common;

namespace Dental.Domain.Cash;

[Audited]
public class CashRegister : TenantEntity, ISoftDeletable
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum CashShiftStatus { Open, Closed }

[Audited]
public class CashShift : TenantEntity, IVersioned
{
    public Guid CashRegisterId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OpenedBy { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public long OpeningBalance { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosingBalanceExpected { get; set; }
    public long? ClosingBalanceActual { get; set; }
    public long? Difference { get; set; }
    public CashShiftStatus Status { get; set; } = CashShiftStatus.Open;
    public int Version { get; set; }

    /// <summary>Ожидаемые наличные: начальный остаток + наличные поступления − наличные возвраты − наличные расходы − инкассация + внесения.</summary>
    public static long ExpectedCash(long opening, IEnumerable<Payment> payments, IEnumerable<Expense> expenses, IEnumerable<CashOperation> operations)
    {
        var cashIn = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        var cashOut = expenses.Where(e => e.PaidFromCash).Sum(e => e.Amount);
        var ops = operations.Sum(o => o.Type == CashOperationType.Deposit ? o.Amount : -o.Amount);
        return opening + cashIn - cashOut + ops;
    }
}

public enum PaymentMethod { Cash, Card, KaspiQr, Transfer, Insurance, Balance }
public enum PaymentType { Payment, Refund, Advance }

/// <summary>Платёж. Сумма всегда положительная; направление задаёт Type (Refund = возврат).</summary>
[Audited]
public class Payment : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? VisitId { get; set; }
    public Guid CashShiftId { get; set; }
    public PaymentMethod Method { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = Money.DefaultCurrency;
    public PaymentType Type { get; set; } = PaymentType.Payment;
    public Guid? RefundedPaymentId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Comment { get; set; }
    public bool PendingApproval { get; set; }

    /// <summary>Знаковая сумма для денежного потока кассы.</summary>
    public long SignedAmount => Type == PaymentType.Refund ? -Amount : Amount;
}

public enum ExpenseCategoryType { Rent, Utilities, Salary, Supplies, Lab, Marketing, Other }

[Audited]
public class ExpenseCategory : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public ExpenseCategoryType Type { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class Expense : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid CategoryId { get; set; }
    public long Amount { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public Guid? CashShiftId { get; set; }
    public string? Counterparty { get; set; }
    public string? Comment { get; set; }
    public string? DocumentUrl { get; set; }

    public bool PaidFromCash => CashShiftId is not null;
}

public enum CashOperationType { Collection, Deposit }

[Audited]
public class CashOperation : TenantEntity
{
    public Guid CashShiftId { get; set; }
    public CashOperationType Type { get; set; }
    public long Amount { get; set; }
    public string? Comment { get; set; }
}
