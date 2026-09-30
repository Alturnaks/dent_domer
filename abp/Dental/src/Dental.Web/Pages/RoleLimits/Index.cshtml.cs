using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.RoleLimits;

[Authorize(DentalPermissions.Org.RolesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
