using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Settings;

/// <summary>Настройки организации (арендатора).</summary>
[Authorize(DentalPermissions.Org.SettingsManage)]
public class OrganizationModel : DentalPageModel
{
    private readonly IOrganizationSettingsAppService _settingsAppService;

    [BindProperty]
    public OrganizationSettingsDto Settings { get; set; } = new();

    public bool Saved { get; private set; }

    public OrganizationModel(IOrganizationSettingsAppService settingsAppService)
    {
        _settingsAppService = settingsAppService;
    }

    public async Task OnGetAsync()
    {
        Settings = await _settingsAppService.GetAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        await _settingsAppService.UpdateAsync(Settings);
        Saved = true;
        Settings = await _settingsAppService.GetAsync();
        return Page();
    }
}
