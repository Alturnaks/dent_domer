namespace Dental.Roles;

/// <summary>Лимиты роли. null = без ограничения. Деньги — тиыны.</summary>
public class RoleLimitsData
{
    public decimal? MaxDiscountPct { get; set; }
    public long? MaxWriteoffAmount { get; set; }
    public long? MaxRefundAmount { get; set; }
    public bool CanEditClosedShiftVisits { get; set; }

    public bool AllowsDiscount(decimal pct) => MaxDiscountPct is null || pct <= MaxDiscountPct.Value;
    public bool AllowsWriteoff(long amount) => MaxWriteoffAmount is null || amount <= MaxWriteoffAmount.Value;
    public bool AllowsRefund(long amount) => MaxRefundAmount is null || amount <= MaxRefundAmount.Value;

    /// <summary>Лимиты пользователя без ролей/без записей: всё запрещено.</summary>
    public static RoleLimitsData None => new() { MaxDiscountPct = 0, MaxWriteoffAmount = 0, MaxRefundAmount = 0 };

    /// <summary>Без ограничений (владелец, встроенный admin).</summary>
    public static RoleLimitsData Unlimited => new() { CanEditClosedShiftVisits = true };

    /// <summary>Объединение лимитов нескольких ролей: берётся самый мягкий.</summary>
    public RoleLimitsData MergeMostPermissive(RoleLimitsData other) => new()
    {
        MaxDiscountPct = MaxDiscountPct is null || other.MaxDiscountPct is null ? null : System.Math.Max(MaxDiscountPct.Value, other.MaxDiscountPct.Value),
        MaxWriteoffAmount = MaxWriteoffAmount is null || other.MaxWriteoffAmount is null ? null : System.Math.Max(MaxWriteoffAmount.Value, other.MaxWriteoffAmount.Value),
        MaxRefundAmount = MaxRefundAmount is null || other.MaxRefundAmount is null ? null : System.Math.Max(MaxRefundAmount.Value, other.MaxRefundAmount.Value),
        CanEditClosedShiftVisits = CanEditClosedShiftVisits || other.CanEditClosedShiftVisits,
    };
}
