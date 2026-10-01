using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Inventory;

/// <summary>Остатки по складам: фильтры (склад, категория, ниже минимума, истекающие), пересборка кэша.</summary>
[Authorize(DentalPermissions.Inventory.View)]
public class IndexModel : DentalPageModel
{
    public bool CanRebuild { get; private set; }

    public async Task OnGetAsync()
    {
        CanRebuild = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.CountApprove);
    }
}
