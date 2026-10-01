using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Finance;

[Audited]
public abstract class FinanceEntity : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    protected FinanceEntity() { }
    protected FinanceEntity(Guid id, Guid? tenantId) : base(id) => TenantId = tenantId;
}

public class Visit : FinanceEntity
{
    protected Visit() { }
    public Visit(Guid id, Guid? tenantId, Guid branchId, Guid patientId, Guid doctorId, DateTime openedAt, Guid? appointmentId = null) : base(id, tenantId)
    { BranchId = branchId; PatientId = patientId; DoctorId = doctorId; OpenedAt = openedAt; AppointmentId = appointmentId; }
    public Guid? AppointmentId { get; internal set; }
    public Guid BranchId { get; private set; }
    public Guid PatientId { get; internal set; }
    public Guid DoctorId { get; private set; }
    public Guid? AssistantId { get; internal set; }
    public VisitStatus Status { get; internal set; }
    public VisitApprovalState ApprovalState { get; internal set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; internal set; }
    public Guid? ClosedBy { get; internal set; }
    public Guid? CashShiftId { get; internal set; }
    public long Subtotal { get; private set; }
    public long DiscountTotal { get; private set; }
    public long Total { get; private set; }
    public long PaidTotal { get; internal set; }
    public Guid? ConsumptionDocumentId { get; internal set; }
    public string? CancelReason { get; internal set; }
    public List<VisitItem> Items { get; private set; } = [];
    public List<VisitMaterial> Materials { get; private set; } = [];
    public IEnumerable<VisitItem> ActiveItems => Items.Where(i => !i.IsDeleted);
    public IEnumerable<VisitMaterial> ActiveMaterials => Materials.Where(m => !m.IsDeleted);
    public long Debt => Status == VisitStatus.Cancelled ? 0 : Math.Max(0, Total - PaidTotal);
    public void EnsureOpen() { if (Status != VisitStatus.Open) throw new BusinessException("Dental:VisitNotOpen"); }
    public void Recalculate()
    {
        foreach (var item in ActiveItems) item.Recalculate();
        Subtotal = checked(ActiveItems.Sum(i => checked(i.UnitPrice * i.Qty)));
        DiscountTotal = ActiveItems.Sum(i => i.DiscountAmount); Total = ActiveItems.Sum(i => i.Total);
    }
}

public class VisitItem : FinanceEntity
{
    protected VisitItem() { }
    public VisitItem(Guid id, Guid? tenantId, Guid visitId, Guid serviceId, Guid doctorId, int qty, long price, decimal discount, IEnumerable<int>? teeth = null) : base(id, tenantId)
    { VisitId = visitId; ServiceId = serviceId; DoctorId = doctorId; Update(qty, price, discount, teeth); }
    public Guid VisitId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid DoctorId { get; internal set; }
    public int Qty { get; private set; }
    public long UnitPrice { get; private set; }
    public decimal DiscountPct { get; internal set; }
    public long DiscountAmount { get; private set; }
    public long Total { get; private set; }
    public List<int> ToothNumbers { get; private set; } = [];
    public bool DiscountPendingApproval { get; internal set; }
    public void Update(int qty, long price, decimal discount, IEnumerable<int>? teeth)
    {
        if (qty is < 1 or > 100 || price < 0 || discount is < 0 or > 100) throw new BusinessException("Dental:FinanceInvalidInput");
        var numbers = (teeth ?? []).Distinct().ToList();
        if (numbers.Any(t => !((t / 10 is >= 1 and <= 4 && t % 10 is >= 1 and <= 8) || (t / 10 is >= 5 and <= 8 && t % 10 is >= 1 and <= 5)))) throw new BusinessException("Dental:FinanceInvalidInput");
        Qty = qty; UnitPrice = price; DiscountPct = discount; ToothNumbers = numbers; Recalculate();
    }
    public void Recalculate()
    { var gross = checked(UnitPrice * Qty); DiscountAmount = checked((long)Math.Round(gross * DiscountPct / 100m, 0, MidpointRounding.AwayFromZero)); Total = gross - DiscountAmount; }
}

public class VisitMaterial : FinanceEntity
{
    protected VisitMaterial() { }
    public VisitMaterial(Guid id, Guid? tenantId, Guid visitId, Guid? visitItemId, Guid itemId, decimal quantity, decimal norm) : base(id, tenantId)
    { if (quantity <= 0 || itemId == Guid.Empty) throw new BusinessException("Dental:FinanceInvalidInput"); VisitId = visitId; VisitItemId = visitItemId; ItemId = itemId; Quantity = quantity; NormQuantity = norm; }
    public Guid VisitId { get; private set; }
    public Guid? VisitItemId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal NormQuantity { get; private set; }
    public Guid? BatchId { get; internal set; }
    public long Cost { get; internal set; }
}

public class CashRegister : FinanceEntity
{
    protected CashRegister() { }
    public CashRegister(Guid id, Guid? tenantId, Guid branchId, string name) : base(id, tenantId) { BranchId = branchId; Rename(name); }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = "";
    public void Rename(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), 200);
}
public class CashShift : FinanceEntity
{
    protected CashShift() { }
    public CashShift(Guid id, Guid? tenantId, Guid registerId, Guid branchId, Guid userId, DateTime now, long opening) : base(id, tenantId)
    { if (opening < 0) throw new BusinessException("Dental:FinanceInvalidInput"); CashRegisterId = registerId; BranchId = branchId; OpenedBy = userId; OpenedAt = now; OpeningBalance = opening; }
    public Guid CashRegisterId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid OpenedBy { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public long OpeningBalance { get; private set; }
    public Guid? ClosedBy { get; internal set; }
    public DateTime? ClosedAt { get; internal set; }
    public long? ClosingBalanceExpected { get; internal set; }
    public long? ClosingBalanceActual { get; internal set; }
    public long? Difference { get; internal set; }
    public string? CloseComment { get; internal set; }
    public CashShiftStatus Status { get; internal set; }
    public static long ExpectedCash(long opening, IEnumerable<Payment> payments, IEnumerable<Expense> expenses, IEnumerable<CashOperation> operations) => checked(opening + payments.Where(p => p.State == 0 && p.Method == PaymentMethod.Cash).Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount) - expenses.Where(e => e.CashShiftId != null).Sum(e => e.Amount) + operations.Sum(o => o.Type == CashOperationType.Deposit ? o.Amount : -o.Amount));
}
public class Payment : FinanceEntity
{
    protected Payment() { }
    public Payment(Guid id, Guid? tenantId, Guid branchId, Guid patientId, Guid? visitId, Guid shiftId, PaymentMethod method, long amount, PaymentType type, string? key, string? comment) : base(id, tenantId)
    { if (amount <= 0 || !Enum.IsDefined(method)) throw new BusinessException("Dental:FinanceInvalidInput"); BranchId = branchId; PatientId = patientId; VisitId = visitId; CashShiftId = shiftId; Method = method; Amount = amount; Type = type; IdempotencyKey = key; Comment = comment; }
    public Guid BranchId { get; private set; }
    public Guid PatientId { get; internal set; }
    public Guid? VisitId { get; private set; }
    public Guid CashShiftId { get; internal set; }
    public PaymentMethod Method { get; private set; }
    public long Amount { get; private set; }
    public PaymentType Type { get; private set; }
    public Guid? RefundedPaymentId { get; internal set; }
    public string? IdempotencyKey { get; private set; }
    public string? Comment { get; private set; }
    // 0 applied, 1 awaiting approval, 2 rejected. Rejected refunds never reserve money.
    public int State { get; internal set; }
    public string? RequestHash { get; internal set; }
}
public class ExpenseCategory : FinanceEntity
{
    protected ExpenseCategory() { }
    public ExpenseCategory(Guid id, Guid? tenantId, string name, ExpenseCategoryType type) : base(id, tenantId) { Name = Check.NotNullOrWhiteSpace(name, nameof(name), 200); Type = type; }
    public string Name { get; private set; } = "";
    public ExpenseCategoryType Type { get; private set; }
}
public class Expense : FinanceEntity
{
    protected Expense() { }
    public Expense(Guid id, Guid? tenantId, Guid branchId, Guid categoryId, long amount, DateTime paidAt, Guid? shiftId, string? counterparty, string? comment) : base(id, tenantId)
    { if (amount <= 0) throw new BusinessException("Dental:FinanceInvalidInput"); BranchId = branchId; CategoryId = categoryId; Amount = amount; PaidAt = paidAt; CashShiftId = shiftId; Counterparty = counterparty; Comment = comment; }
    public Guid BranchId { get; private set; }
    public Guid CategoryId { get; private set; }
    public long Amount { get; private set; }
    public DateTime PaidAt { get; private set; }
    public Guid? CashShiftId { get; private set; }
    public string? Counterparty { get; private set; }
    public string? Comment { get; private set; }
}
public class CashOperation : FinanceEntity
{
    protected CashOperation() { }
    public CashOperation(Guid id, Guid? tenantId, Guid shiftId, CashOperationType type, long amount, string comment) : base(id, tenantId)
    { if (amount <= 0 || !Enum.IsDefined(type)) throw new BusinessException("Dental:FinanceInvalidInput"); CashShiftId = shiftId; Type = type; Amount = amount; Comment = Check.NotNullOrWhiteSpace(comment, nameof(comment), 2000); }
    public Guid CashShiftId { get; private set; }
    public CashOperationType Type { get; private set; }
    public long Amount { get; private set; }
    public string Comment { get; private set; } = "";
}
