using Dental.Application.Common;
using Dental.Application.Orgs;
using Dental.Application.Permissions;
using Dental.Domain.Cash;
using Dental.Domain.Common;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Visits;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Cashdesk;

public sealed record CashShiftDto(
    Guid Id, Guid CashRegisterId, string CashRegisterName, Guid BranchId, Guid OpenedBy, string OpenedByName, DateTimeOffset OpenedAt, long OpeningBalance,
    Guid? ClosedBy, string? ClosedByName, DateTimeOffset? ClosedAt, long? ClosingBalanceExpected, long? ClosingBalanceActual, long? Difference, CashShiftStatus Status,
    long CashIn, long CardIn, long OtherIn, long Refunds, long Expenses, long Collections, long Deposits, long ExpectedNow, int Version);
public sealed record OpenShiftRequest(Guid CashRegisterId, long OpeningBalance);
public sealed record CloseShiftRequest(long ClosingBalanceActual, string? Comment, int? Version);

public sealed record PaymentSplit(PaymentMethod Method, long Amount);
public sealed record CreatePaymentRequest(Guid PatientId, Guid? VisitId, Guid BranchId, PaymentMethod? Method, long? Amount, IReadOnlyList<PaymentSplit>? Splits, string? Comment);
public sealed record PaymentDto(Guid Id, Guid BranchId, Guid PatientId, string PatientName, Guid? VisitId, Guid CashShiftId, PaymentMethod Method, long Amount, PaymentType Type,
    Guid? RefundedPaymentId, string? Comment, bool PendingApproval, DateTimeOffset CreatedAt, string? CreatedByName);
public sealed record CreatePaymentResult(IReadOnlyList<PaymentDto> Payments, long PatientBalance, long? VisitDebt);
public sealed record RefundRequest(long Amount, PaymentMethod? Method, string Reason);

public sealed record ExpenseDto(Guid Id, Guid BranchId, Guid CategoryId, string CategoryName, long Amount, DateTimeOffset PaidAt, Guid? CashShiftId, string? Counterparty, string? Comment, string? DocumentUrl);
public sealed record ExpenseRequest(Guid BranchId, Guid CategoryId, long Amount, DateTimeOffset? PaidAt, bool FromCash, string? Counterparty, string? Comment, string? DocumentUrl);
public sealed record CashOperationRequest(CashOperationType Type, long Amount, string? Comment);
public sealed record CashOperationDto(Guid Id, Guid CashShiftId, CashOperationType Type, long Amount, string? Comment, DateTimeOffset CreatedAt);

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x).Must(x => (x.Splits is { Count: > 0 }) || (x.Method is not null && x.Amount is > 0))
            .WithMessage("Укажите способ и сумму оплаты").WithName("amount");
        RuleForEach(x => x.Splits).ChildRules(s => s.RuleFor(x => x.Amount).GreaterThan(0));
    }
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class ExpenseRequestValidator : AbstractValidator<ExpenseRequest>
{
    public ExpenseRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}

public sealed class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator() => RuleFor(x => x.ClosingBalanceActual).GreaterThanOrEqualTo(0);
}

public sealed class CashService(
    IAppDbContext db,
    ICurrentUser user,
    IAuditService audit,
    ApprovalService approvals,
    TimeProvider clock)
{
    // ---------- Смены ----------
    public async Task<IReadOnlyList<CashShiftDto>> ListShiftsAsync(Guid? branchId, CashShiftStatus? status, int limit, CancellationToken ct)
    {
        var q = db.CashShifts.AsNoTracking();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(s => s.BranchId == b); }
        else if (!user.AllBranches) q = q.Where(s => user.BranchIds.Contains(s.BranchId));
        if (status is { } st) q = q.Where(s => s.Status == st);
        var rows = await q.OrderByDescending(s => s.OpenedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
        return await MapShiftsAsync(rows, ct);
    }

    public async Task<CashShiftDto> GetShiftAsync(Guid id, CancellationToken ct)
    {
        var s = await db.CashShifts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Смена");
        user.EnsureBranchAccess(s.BranchId);
        return (await MapShiftsAsync([s], ct))[0];
    }

    public async Task<CashShiftDto> OpenShiftAsync(OpenShiftRequest r, CancellationToken ct)
    {
        var reg = await db.CashRegisters.FirstOrDefaultAsync(c => c.Id == r.CashRegisterId && c.DeletedAt == null, ct) ?? throw AppException.NotFound("Касса");
        user.EnsureBranchAccess(reg.BranchId);
        if (await db.CashShifts.AnyAsync(s => s.CashRegisterId == reg.Id && s.Status == CashShiftStatus.Open, ct))
            throw AppException.Conflict(ErrorCodes.ShiftAlreadyOpen, "Смена на этой кассе уже открыта");
        var s = new CashShift { CashRegisterId = reg.Id, BranchId = reg.BranchId, OpenedBy = user.UserId, OpenedAt = clock.GetUtcNow(), OpeningBalance = r.OpeningBalance };
        db.CashShifts.Add(s);
        await db.SaveChangesAsync(ct);
        return await GetShiftAsync(s.Id, ct);
    }

    /// <summary>Закрытие: ожидаемый остаток наличных считается автоматически, фактический вводит кассир, разница фиксируется (≠ 0 → владельцу).</summary>
    public async Task<CashShiftDto> CloseShiftAsync(Guid id, CloseShiftRequest r, CancellationToken ct)
    {
        var s = await db.CashShifts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Смена");
        user.EnsureBranchAccess(s.BranchId);
        db.SetExpectedVersion(s, r.Version);
        if (s.Status != CashShiftStatus.Open) throw AppException.Conflict(ErrorCodes.ShiftNotOpen, "Смена уже закрыта");
        var expected = await ExpectedCashAsync(s, ct);
        s.ClosingBalanceExpected = expected;
        s.ClosingBalanceActual = r.ClosingBalanceActual;
        s.Difference = r.ClosingBalanceActual - expected;
        s.Status = CashShiftStatus.Closed;
        s.ClosedAt = clock.GetUtcNow();
        s.ClosedBy = user.UserId;
        if (s.Difference != 0)
        {
            audit.Log(nameof(CashShift), s.Id, "cash_difference", new { expected, actual = r.ClosingBalanceActual, difference = s.Difference },
                $"Расхождение кассы {new Money(s.Difference.Value)}" + (r.Comment is null ? "" : $": {r.Comment}"), suspicious: true, branchId: s.BranchId);
        }
        await db.SaveChangesAsync(ct);
        return await GetShiftAsync(s.Id, ct);
    }

    private async Task<long> ExpectedCashAsync(CashShift s, CancellationToken ct)
    {
        var payments = await db.Payments.AsNoTracking().Where(p => p.CashShiftId == s.Id && !p.PendingApproval).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.CashShiftId == s.Id).ToListAsync(ct);
        var ops = await db.CashOperations.AsNoTracking().Where(o => o.CashShiftId == s.Id).ToListAsync(ct);
        return CashShift.ExpectedCash(s.OpeningBalance, payments, expenses, ops);
    }

    private async Task<CashShift> RequireOpenShiftAsync(Guid branchId, CancellationToken ct) =>
        await db.CashShifts.Where(s => s.BranchId == branchId && s.Status == CashShiftStatus.Open).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct)
        ?? throw AppException.Conflict(ErrorCodes.ShiftNotOpen, "Кассовая смена не открыта");

    // ---------- Платежи ----------
    /// <summary>
    /// Приём оплаты (в т.ч. сплит). Без открытой смены — SHIFT_NOT_OPEN. Оплата с баланса — только в пределах доступного аванса.
    /// Платёж без визита = аванс.
    /// </summary>
    public async Task<CreatePaymentResult> CreatePaymentAsync(CreatePaymentRequest r, string? idempotencyKey, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var shift = await RequireOpenShiftAsync(r.BranchId, ct);
        _ = await db.Patients.FirstOrDefaultAsync(p => p.Id == r.PatientId && p.DeletedAt == null, ct) ?? throw AppException.NotFound("Пациент");
        Visit? visit = null;
        if (r.VisitId is { } vid)
        {
            visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == vid, ct) ?? throw AppException.NotFound("Визит");
            if (visit.PatientId != r.PatientId) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Визит принадлежит другому пациенту");
            if (visit.Status == VisitStatus.Cancelled) throw AppException.Conflict(ErrorCodes.VisitNotOpen, "Визит отменён");
        }
        var splits = r.Splits is { Count: > 0 } ? r.Splits : [new PaymentSplit(r.Method!.Value, r.Amount!.Value)];
        if (splits.Any(s => s.Amount <= 0)) throw AppException.BadRequest(ErrorCodes.AmountInvalid, "Сумма должна быть больше нуля");
        if (splits.Any(s => s.Method == PaymentMethod.Balance) && visit is null)
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Оплата с баланса возможна только за визит");

        var balance = await db.PatientBalances.FirstOrDefaultAsync(b => b.PatientId == r.PatientId, ct);
        if (balance is null) { balance = new PatientBalance { PatientId = r.PatientId }; db.PatientBalances.Add(balance); }
        var fromBalance = splits.Where(s => s.Method == PaymentMethod.Balance).Sum(s => s.Amount);
        if (fromBalance > 0 && fromBalance > await AvailableAdvanceAsync(r.PatientId, balance.Balance, ct))
            throw AppException.Conflict(ErrorCodes.InsufficientBalance, "Недостаточно средств на балансе пациента");

        var created = new List<Payment>();
        foreach (var (split, idx) in splits.Select((s, i) => (s, i)))
        {
            var p = new Payment
            {
                BranchId = r.BranchId, PatientId = r.PatientId, VisitId = visit?.Id, CashShiftId = shift.Id, Method = split.Method, Amount = split.Amount,
                Type = visit is null ? PaymentType.Advance : PaymentType.Payment, Comment = r.Comment.NullIfEmpty(),
                IdempotencyKey = idempotencyKey is null ? null : splits.Count == 1 ? idempotencyKey : $"{idempotencyKey}:{idx}",
            };
            db.Payments.Add(p);
            created.Add(p);
            // Деньги поступили (кроме оплаты с баланса — это перенос уже учтённого аванса).
            if (split.Method != PaymentMethod.Balance) balance.Balance += split.Amount;
            if (visit is not null)
            {
                visit.PaidTotal += split.Amount;
                // Оплата с баланса по закрытому визиту гасит долг: баланс не меняется, аванс «переходит» в оплату.
            }
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new CreatePaymentResult(await MapPaymentsAsync(created, ct), balance.Balance, visit?.Debt);
    }

    /// <summary>Доступный аванс = баланс − уже поступившие оплаты открытых визитов (они ещё не списаны закрытием).</summary>
    private async Task<long> AvailableAdvanceAsync(Guid patientId, long balance, CancellationToken ct)
    {
        var earmarked = await db.Visits.Where(v => v.PatientId == patientId && v.Status == VisitStatus.Open).SumAsync(v => v.PaidTotal, ct);
        return Math.Max(0, balance - earmarked);
    }

    /// <summary>Возврат (удалить платёж нельзя). Больше лимита роли → approval_request. Всегда подозрительное событие.</summary>
    public async Task<PaymentDto> RefundAsync(Guid paymentId, RefundRequest r, string? idempotencyKey, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var original = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw AppException.NotFound("Платёж");
        user.EnsureBranchAccess(original.BranchId);
        if (original.Type == PaymentType.Refund) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Нельзя вернуть возврат");
        var alreadyRefunded = await db.Payments.Where(p => p.RefundedPaymentId == original.Id).SumAsync(p => p.Amount, ct);
        if (r.Amount > original.Amount - alreadyRefunded)
            throw AppException.Conflict(ErrorCodes.RefundExceedsPayment, "Сумма возврата больше остатка платежа",
                new Dictionary<string, object?> { ["available"] = original.Amount - alreadyRefunded });
        var shift = await RequireOpenShiftAsync(original.BranchId, ct);
        var refund = new Payment
        {
            BranchId = original.BranchId, PatientId = original.PatientId, VisitId = original.VisitId, CashShiftId = shift.Id,
            Method = r.Method ?? (original.Method == PaymentMethod.Balance ? PaymentMethod.Cash : original.Method), Amount = r.Amount, Type = PaymentType.Refund,
            RefundedPaymentId = original.Id, Comment = r.Reason, IdempotencyKey = idempotencyKey,
        };
        db.Payments.Add(refund);
        if (!user.Limits.AllowsRefund(r.Amount))
        {
            refund.PendingApproval = true;
            await approvals.RequestAsync(ApprovalType.Refund, nameof(Payment), refund.Id, r.Amount, $"Возврат {new Money(r.Amount)}: {r.Reason}",
                new { originalPaymentId = original.Id, r.Amount, r.Reason }, original.BranchId, ct);
        }
        else
        {
            await ApplyRefundAsync(refund, ct);
        }
        audit.Log(nameof(Payment), refund.Id, "payment_refund", new { originalPaymentId = original.Id, r.Amount, pending = refund.PendingApproval }, r.Reason,
            suspicious: true, branchId: original.BranchId);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (await MapPaymentsAsync([refund], ct))[0];
    }

    internal async Task ApplyRefundAsync(Payment refund, CancellationToken ct)
    {
        refund.PendingApproval = false;
        var balance = await db.PatientBalances.FirstOrDefaultAsync(b => b.PatientId == refund.PatientId, ct);
        if (balance is null) { balance = new PatientBalance { PatientId = refund.PatientId }; db.PatientBalances.Add(balance); }
        balance.Balance -= refund.Amount;
        if (refund.VisitId is { } vid)
        {
            var visit = await db.Visits.FirstAsync(v => v.Id == vid, ct);
            visit.PaidTotal -= refund.Amount;
        }
    }

    public async Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(Guid? branchId, Guid? shiftId, Guid? patientId, DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct)
    {
        var q = db.Payments.AsNoTracking();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(p => p.BranchId == b); }
        else if (!user.AllBranches) q = q.Where(p => user.BranchIds.Contains(p.BranchId));
        if (shiftId is { } s) q = q.Where(p => p.CashShiftId == s);
        if (patientId is { } pid) q = q.Where(p => p.PatientId == pid);
        if (from is { } f) q = q.Where(p => p.CreatedAt >= f);
        if (to is { } t) q = q.Where(p => p.CreatedAt < t);
        return await MapPaymentsAsync(await q.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct), ct);
    }

    // ---------- Расходы и операции ----------
    public async Task<IReadOnlyList<ExpenseDto>> ListExpensesAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var q = db.Expenses.AsNoTracking();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(e => e.BranchId == b); }
        else if (!user.AllBranches) q = q.Where(e => user.BranchIds.Contains(e.BranchId));
        if (from is { } f) q = q.Where(e => e.PaidAt >= f);
        if (to is { } t) q = q.Where(e => e.PaidAt < t);
        var rows = await q.OrderByDescending(e => e.PaidAt).Take(500).ToListAsync(ct);
        var cats = await db.ExpenseCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(e => new ExpenseDto(e.Id, e.BranchId, e.CategoryId, cats.GetValueOrDefault(e.CategoryId, "?"), e.Amount, e.PaidAt, e.CashShiftId, e.Counterparty, e.Comment, e.DocumentUrl)).ToList();
    }

    public async Task<ExpenseDto> CreateExpenseAsync(ExpenseRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var cat = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == r.CategoryId, ct) ?? throw AppException.NotFound("Категория");
        Guid? shiftId = null;
        if (r.FromCash) shiftId = (await RequireOpenShiftAsync(r.BranchId, ct)).Id;
        var e = new Expense
        {
            BranchId = r.BranchId, CategoryId = cat.Id, Amount = r.Amount, PaidAt = r.PaidAt ?? clock.GetUtcNow(), CashShiftId = shiftId,
            Counterparty = r.Counterparty.NullIfEmpty(), Comment = r.Comment.NullIfEmpty(), DocumentUrl = r.DocumentUrl.NullIfEmpty(),
        };
        db.Expenses.Add(e);
        await db.SaveChangesAsync(ct);
        return new ExpenseDto(e.Id, e.BranchId, e.CategoryId, cat.Name, e.Amount, e.PaidAt, e.CashShiftId, e.Counterparty, e.Comment, e.DocumentUrl);
    }

    public async Task<CashOperationDto> CreateOperationAsync(Guid shiftId, CashOperationRequest r, CancellationToken ct)
    {
        var s = await db.CashShifts.FirstOrDefaultAsync(x => x.Id == shiftId, ct) ?? throw AppException.NotFound("Смена");
        user.EnsureBranchAccess(s.BranchId);
        if (s.Status != CashShiftStatus.Open) throw AppException.Conflict(ErrorCodes.ShiftNotOpen, "Смена закрыта");
        if (r.Amount <= 0) throw AppException.BadRequest(ErrorCodes.AmountInvalid, "Сумма должна быть больше нуля");
        if (r.Type == CashOperationType.Collection && r.Amount > await ExpectedCashAsync(s, ct))
            throw AppException.Conflict(ErrorCodes.AmountInvalid, "Сумма инкассации больше наличных в кассе");
        var op = new CashOperation { CashShiftId = s.Id, Type = r.Type, Amount = r.Amount, Comment = r.Comment.NullIfEmpty() };
        db.CashOperations.Add(op);
        await db.SaveChangesAsync(ct);
        return new CashOperationDto(op.Id, op.CashShiftId, op.Type, op.Amount, op.Comment, op.CreatedAt);
    }

    public async Task<IReadOnlyList<CashOperationDto>> ListOperationsAsync(Guid shiftId, CancellationToken ct) =>
        await db.CashOperations.AsNoTracking().Where(o => o.CashShiftId == shiftId).OrderBy(o => o.CreatedAt)
            .Select(o => new CashOperationDto(o.Id, o.CashShiftId, o.Type, o.Amount, o.Comment, o.CreatedAt)).ToListAsync(ct);

    // ---------- Мапперы ----------
    private async Task<List<CashShiftDto>> MapShiftsAsync(List<CashShift> rows, CancellationToken ct)
    {
        var ids = rows.Select(s => s.Id).ToList();
        var regIds = rows.Select(s => s.CashRegisterId).Distinct().ToList();
        var regs = await db.CashRegisters.AsNoTracking().Where(r => regIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var userIds = rows.Select(s => s.OpenedBy).Concat(rows.Where(s => s.ClosedBy != null).Select(s => s.ClosedBy!.Value)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var payments = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.CashShiftId) && !p.PendingApproval).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.CashShiftId != null && ids.Contains(e.CashShiftId.Value)).ToListAsync(ct);
        var ops = await db.CashOperations.AsNoTracking().Where(o => ids.Contains(o.CashShiftId)).ToListAsync(ct);
        return rows.Select(s =>
        {
            var p = payments.Where(x => x.CashShiftId == s.Id).ToList();
            var e = expenses.Where(x => x.CashShiftId == s.Id).ToList();
            var o = ops.Where(x => x.CashShiftId == s.Id).ToList();
            var cashIn = p.Where(x => x.Method == PaymentMethod.Cash && x.Type != PaymentType.Refund).Sum(x => x.Amount);
            var cardIn = p.Where(x => x.Method == PaymentMethod.Card && x.Type != PaymentType.Refund).Sum(x => x.Amount);
            var otherIn = p.Where(x => x.Method is not (PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.Balance) && x.Type != PaymentType.Refund).Sum(x => x.Amount);
            var refunds = p.Where(x => x.Type == PaymentType.Refund).Sum(x => x.Amount);
            return new CashShiftDto(s.Id, s.CashRegisterId, regs.GetValueOrDefault(s.CashRegisterId, "?"), s.BranchId, s.OpenedBy, users.GetValueOrDefault(s.OpenedBy, "?"),
                s.OpenedAt, s.OpeningBalance, s.ClosedBy, s.ClosedBy is { } cb ? users.GetValueOrDefault(cb) : null, s.ClosedAt, s.ClosingBalanceExpected,
                s.ClosingBalanceActual, s.Difference, s.Status, cashIn, cardIn, otherIn, refunds, e.Sum(x => x.Amount),
                o.Where(x => x.Type == CashOperationType.Collection).Sum(x => x.Amount), o.Where(x => x.Type == CashOperationType.Deposit).Sum(x => x.Amount),
                CashShift.ExpectedCash(s.OpeningBalance, p, e, o), s.Version);
        }).ToList();
    }

    private async Task<List<PaymentDto>> MapPaymentsAsync(List<Payment> rows, CancellationToken ct)
    {
        var pids = rows.Select(p => p.PatientId).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        var uids = rows.Where(p => p.CreatedBy != null).Select(p => p.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(p => new PaymentDto(p.Id, p.BranchId, p.PatientId, patients.GetValueOrDefault(p.PatientId, "?"), p.VisitId, p.CashShiftId, p.Method, p.Amount,
            p.Type, p.RefundedPaymentId, p.Comment, p.PendingApproval, p.CreatedAt, p.CreatedBy is { } c ? users.GetValueOrDefault(c) : null)).ToList();
    }
}

public sealed class RefundApprovalHandler(IAppDbContext db, CashService cash) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.Refund;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var refund = await db.Payments.FirstAsync(p => p.Id == request.EntityId, ct);
        if (refund.PendingApproval) await cash.ApplyRefundAsync(refund, ct);
    }

    /// <summary>Отклонённый возврат остаётся в журнале как несостоявшийся (сумма 0 не проводится): помечаем комментарием.</summary>
    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var refund = await db.Payments.FirstAsync(p => p.Id == request.EntityId, ct);
        refund.Comment = "[ОТКЛОНЁН] " + refund.Comment;
    }
}
