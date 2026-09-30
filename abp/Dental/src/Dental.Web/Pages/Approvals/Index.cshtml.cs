using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Approvals;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Purchase.OrderApprove, DentalPermissions.Inventory.CountApprove, DentalPermissions.Visits.EditClosed, DentalPermissions.Org.RolesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
