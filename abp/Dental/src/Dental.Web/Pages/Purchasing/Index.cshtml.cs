using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Purchasing;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Purchase.RequestCreate, DentalPermissions.Purchase.OrderCreate, DentalPermissions.Purchase.OrderApprove)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
