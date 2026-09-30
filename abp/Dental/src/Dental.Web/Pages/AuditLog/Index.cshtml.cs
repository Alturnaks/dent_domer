using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.AuditLog;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Audit.View)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
