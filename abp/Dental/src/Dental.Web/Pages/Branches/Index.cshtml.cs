using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Branches;

[Authorize(DentalPermissions.Org.BranchesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
