using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Patients;

[Authorize(DentalPermissions.Patients.Merge)]
public class DuplicatesModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
