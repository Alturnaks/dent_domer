using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.AuditLog;

/// <summary>Раздел приложения с проверкой прав текущего пользователя.</summary>
[AnyPermission(DentalPermissions.Audit.View)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
