namespace Dental.Settings;

/// <summary>
/// Настройки организации (арендатора). Бывший Organization/OrganizationSettings.
/// Все — уровня арендатора (провайдер "T"), видимы клиенту (abp.setting.get).
/// Деньги — long в тиынах.
/// </summary>
public static class DentalSettings
{
    private const string Prefix = "Dental.Org";

    public const string Timezone = Prefix + ".Timezone";
    public const string Currency = Prefix + ".Currency";
    public const string SlotMinutes = Prefix + ".SlotMinutes";
    public const string DefaultOpen = Prefix + ".DefaultOpen";
    public const string DefaultClose = Prefix + ".DefaultClose";
    public const string NoShowAfterMinutes = Prefix + ".NoShowAfterMinutes";
    public const string Reminder24h = Prefix + ".Reminder24h";
    public const string Reminder2h = Prefix + ".Reminder2h";
    public const string AllowNegativeStock = Prefix + ".AllowNegativeStock";
    public const string PayrollOnlyPaidVisits = Prefix + ".PayrollOnlyPaidVisits";
    public const string PurchaseOrderApprovalThreshold = Prefix + ".PurchaseOrderApprovalThreshold";
    public const string SuspiciousWriteoffAmount = Prefix + ".SuspiciousWriteoffAmount";
    public const string SuspiciousInventoryDiffAmount = Prefix + ".SuspiciousInventoryDiffAmount";
    public const string DailySummaryTime = Prefix + ".DailySummaryTime";
}
