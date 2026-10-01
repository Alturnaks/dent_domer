using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Inventory.Items;

[Authorize(DentalPermissions.Inventory.View)]
public class IndexModel : DentalPageModel
{
    public bool CanManage { get; private set; }

    public async Task OnGetAsync()
    {
        CanManage = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.ItemsManage);
    }
}
