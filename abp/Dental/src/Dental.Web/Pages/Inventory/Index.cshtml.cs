using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Inventory;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Inventory.View)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
