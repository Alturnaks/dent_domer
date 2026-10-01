using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Payroll;

/// <summary>Раздел приложения с проверкой прав текущего пользователя.</summary>
[AnyPermission(DentalPermissions.Payroll.ViewOwn, DentalPermissions.Payroll.ViewAll, DentalPermissions.Payroll.Manage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
