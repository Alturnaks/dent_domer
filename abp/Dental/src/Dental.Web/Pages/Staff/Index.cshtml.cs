using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Staff;

[Authorize(DentalPermissions.Org.StaffManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
