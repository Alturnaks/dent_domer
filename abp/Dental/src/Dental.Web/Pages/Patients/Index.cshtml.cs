using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Patients;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Patients.View)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
