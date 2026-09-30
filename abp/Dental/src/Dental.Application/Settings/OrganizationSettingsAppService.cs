using System.Globalization;
using Volo.Abp.Settings;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.SettingManagement;

namespace Dental.Settings;

/// <summary>Настройки организации: чтение — любой пользователь арендатора, изменение — Dental.Org.SettingsManage.</summary>
[Authorize]
public class OrganizationSettingsAppService : DentalAppService, IOrganizationSettingsAppService
{
    private readonly ISettingManager _settingManager;

    public OrganizationSettingsAppService(ISettingManager settingManager)
    {
        _settingManager = settingManager;
    }

    public async Task<OrganizationSettingsDto> GetAsync()
    {
        async Task<string> S(string name) => await SettingProvider.GetOrNullAsync(name) ?? "";
        return new OrganizationSettingsDto
        {
            Timezone = await S(DentalSettings.Timezone),
            Currency = await S(DentalSettings.Currency),
            SlotMinutes = await SettingProvider.GetAsync<int>(DentalSettings.SlotMinutes),
            DefaultOpen = await S(DentalSettings.DefaultOpen),
            DefaultClose = await S(DentalSettings.DefaultClose),
            NoShowAfterMinutes = await SettingProvider.GetAsync<int>(DentalSettings.NoShowAfterMinutes),
            Reminder24h = await SettingProvider.IsTrueAsync(DentalSettings.Reminder24h),
            Reminder2h = await SettingProvider.IsTrueAsync(DentalSettings.Reminder2h),
            AllowNegativeStock = await SettingProvider.IsTrueAsync(DentalSettings.AllowNegativeStock),
            PayrollOnlyPaidVisits = await SettingProvider.IsTrueAsync(DentalSettings.PayrollOnlyPaidVisits),
            PurchaseOrderApprovalThreshold = await SettingProvider.GetAsync<long>(DentalSettings.PurchaseOrderApprovalThreshold),
            SuspiciousWriteoffAmount = await SettingProvider.GetAsync<long>(DentalSettings.SuspiciousWriteoffAmount),
            SuspiciousInventoryDiffAmount = await SettingProvider.GetAsync<long>(DentalSettings.SuspiciousInventoryDiffAmount),
            DailySummaryTime = await S(DentalSettings.DailySummaryTime),
        };
    }

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task UpdateAsync(OrganizationSettingsDto input)
    {
        async Task Set(string name, object value) =>
            await _settingManager.SetForCurrentTenantAsync(name, value is bool b
                ? (b ? "true" : "false")
                : System.Convert.ToString(value, CultureInfo.InvariantCulture));

        await Set(DentalSettings.Timezone, input.Timezone);
        await Set(DentalSettings.Currency, input.Currency);
        await Set(DentalSettings.SlotMinutes, input.SlotMinutes);
        await Set(DentalSettings.DefaultOpen, input.DefaultOpen);
        await Set(DentalSettings.DefaultClose, input.DefaultClose);
        await Set(DentalSettings.NoShowAfterMinutes, input.NoShowAfterMinutes);
        await Set(DentalSettings.Reminder24h, input.Reminder24h);
        await Set(DentalSettings.Reminder2h, input.Reminder2h);
        await Set(DentalSettings.AllowNegativeStock, input.AllowNegativeStock);
        await Set(DentalSettings.PayrollOnlyPaidVisits, input.PayrollOnlyPaidVisits);
        await Set(DentalSettings.PurchaseOrderApprovalThreshold, input.PurchaseOrderApprovalThreshold);
        await Set(DentalSettings.SuspiciousWriteoffAmount, input.SuspiciousWriteoffAmount);
        await Set(DentalSettings.SuspiciousInventoryDiffAmount, input.SuspiciousInventoryDiffAmount);
        await Set(DentalSettings.DailySummaryTime, input.DailySummaryTime);
    }
}
