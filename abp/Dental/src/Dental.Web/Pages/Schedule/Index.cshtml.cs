using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Schedule;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Schedule.ViewAll, DentalPermissions.Schedule.ViewOwn)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
