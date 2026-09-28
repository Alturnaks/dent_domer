using Dental.Domain.Common;

namespace Dental.Domain.Visits;

public enum VisitStatus { Open, Closed, Cancelled }
public enum VisitApprovalState { None, PendingDiscount, PendingCorrection }

[Audited]
public class Visit : TenantEntity, IVersioned
{
    public Guid? AppointmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? AssistantId { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.Open;
    public VisitApprovalState ApprovalState { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public Guid? CashShiftId { get; set; }
    public string Currency { get; set; } = Money.DefaultCurrency;
    public long Subtotal { get; set; }
    public long DiscountTotal { get; set; }
    public long Total { get; set; }
    public long PaidTotal { get; set; }
    /// <summary>Документ склада, которым списаны материалы при закрытии.</summary>
    public Guid? ConsumptionDocumentId { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }
    public List<VisitItem> Items { get; set; } = [];
    public List<VisitMaterialUsage> Materials { get; set; } = [];

    public long Debt => Math.Max(0, Total - PaidTotal);

    /// <summary>Пересчёт итогов визита по позициям.</summary>
    public void Recalculate()
    {
        foreach (var i in Items) i.Recalculate();
        Subtotal = Items.Sum(i => i.UnitPrice * i.Qty);
        DiscountTotal = Items.Sum(i => i.DiscountAmount);
        Total = Items.Sum(i => i.Total);
    }

    public void EnsureOpen()
    {
        if (Status != VisitStatus.Open)
            throw new DomainException("VISIT_NOT_OPEN", "Визит уже закрыт или отменён", status: 409);
    }
}

[Audited]
public class VisitItem : TenantEntity
{
    public Guid VisitId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid DoctorId { get; set; }
    public int Qty { get; set; } = 1;
    public long UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public long DiscountAmount { get; set; }
    public long Total { get; set; }
    public List<int> ToothNumbers { get; set; } = [];
    public bool DiscountPendingApproval { get; set; }

    public void Recalculate()
    {
        var gross = new Money(UnitPrice * Qty);
        DiscountAmount = DiscountCalculator.Discount(gross, DiscountPct).Minor;
        Total = gross.Minor - DiscountAmount;
    }
}

public class VisitMaterialUsage : TenantEntity
{
    public Guid VisitId { get; set; }
    public Guid? VisitItemId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? BatchId { get; set; }
    /// <summary>Себестоимость списанного (тиыны), заполняется при закрытии.</summary>
    public long Cost { get; set; }
    /// <summary>Норма по техкарте (для отчёта о перерасходе).</summary>
    public decimal NormQuantity { get; set; }
}

public static class DiscountCalculator
{
    public static Money Discount(Money gross, decimal pct)
    {
        if (pct < 0 || pct > 100)
            throw new DomainException("DISCOUNT_INVALID", "Скидка должна быть от 0 до 100%", status: 400);
        return gross.Percent(pct);
    }

    /// <summary>Нужно ли подтверждение скидки для роли с лимитом maxPct (null = без лимита).</summary>
    public static bool RequiresApproval(decimal pct, decimal? maxPct) => pct > 0 && maxPct is not null && pct > maxPct.Value;
}
