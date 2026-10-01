using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Inventory;

/// <summary>Справочники склада: склады, категории номенклатуры, причины списания.</summary>
[Authorize(DentalPermissions.Inventory.View)]
public class SettingsModel : DentalPageModel
{
    public bool CanManage { get; private set; }

    public async Task OnGetAsync()
    {
        CanManage = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.ItemsManage);
    }
}
