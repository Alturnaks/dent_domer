using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dental.Settings;

/// <summary>Настройки организации (арендатора). Деньги — тиыны, время — "HH:mm".</summary>
public class OrganizationSettingsDto
{
    [Required] public string Timezone { get; set; } = "Asia/Almaty";
    [Required] public string Currency { get; set; } = "KZT";
    [Range(5, 120)] public int SlotMinutes { get; set; } = 15;
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] public string DefaultOpen { get; set; } = "09:00";
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] public string DefaultClose { get; set; } = "20:00";
    [Range(0, 1440)] public int NoShowAfterMinutes { get; set; } = 60;
    public bool Reminder24h { get; set; } = true;
    public bool Reminder2h { get; set; } = true;
    public bool AllowNegativeStock { get; set; } = true;
    public bool PayrollOnlyPaidVisits { get; set; }
    [Range(0, long.MaxValue)] public long PurchaseOrderApprovalThreshold { get; set; }
    [Range(0, long.MaxValue)] public long SuspiciousWriteoffAmount { get; set; }
    [Range(0, long.MaxValue)] public long SuspiciousInventoryDiffAmount { get; set; }
    [Required, RegularExpression(@"^\d{2}:\d{2}$")] public string DailySummaryTime { get; set; } = "21:00";
}

public interface IOrganizationSettingsAppService : IApplicationService
{
    Task<OrganizationSettingsDto> GetAsync();
    Task UpdateAsync(OrganizationSettingsDto input);
}
