using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Payroll;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Payroll.ViewOwn, DentalPermissions.Payroll.ViewAll, DentalPermissions.Payroll.Manage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
