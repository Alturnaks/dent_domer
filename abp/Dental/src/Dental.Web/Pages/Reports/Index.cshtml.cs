using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Reports;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Reports.Branch, DentalPermissions.Reports.Network, DentalPermissions.Reports.Finance, DentalPermissions.Reports.Payroll)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
