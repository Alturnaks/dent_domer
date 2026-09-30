using Dental.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace Dental.Settings;

/// <summary>Настройки организации (арендатора). См. <see cref="DentalSettings"/>.</summary>
public class DentalSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // Язык по умолчанию — русский.
        var defaultLanguage = context.GetOrNull(LocalizationSettingNames.DefaultLanguage);
        if (defaultLanguage != null)
        {
            defaultLanguage.DefaultValue = "ru";
        }

        Add(context, DentalSettings.Timezone, "Asia/Almaty");
        Add(context, DentalSettings.Currency, "KZT");
        Add(context, DentalSettings.SlotMinutes, "15");
        Add(context, DentalSettings.DefaultOpen, "09:00");
        Add(context, DentalSettings.DefaultClose, "20:00");
        Add(context, DentalSettings.NoShowAfterMinutes, "60");
        Add(context, DentalSettings.Reminder24h, "true");
        Add(context, DentalSettings.Reminder2h, "true");
        Add(context, DentalSettings.AllowNegativeStock, "true");
        Add(context, DentalSettings.PayrollOnlyPaidVisits, "false");
        Add(context, DentalSettings.PurchaseOrderApprovalThreshold, "50000000");
        Add(context, DentalSettings.SuspiciousWriteoffAmount, "5000000");
        Add(context, DentalSettings.SuspiciousInventoryDiffAmount, "2000000");
        Add(context, DentalSettings.DailySummaryTime, "21:00");
    }

    private static void Add(ISettingDefinitionContext context, string name, string defaultValue)
    {
        context.Add(new SettingDefinition(
            name,
            defaultValue,
            L("Setting:" + name),
            isVisibleToClients: true)
            .WithProviders(DefaultValueSettingValueProvider.ProviderName, GlobalSettingValueProvider.ProviderName, TenantSettingValueProvider.ProviderName));
    }

    private static LocalizableString L(string name) => LocalizableString.Create<DentalResource>(name);
}
