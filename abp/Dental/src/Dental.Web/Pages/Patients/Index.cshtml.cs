using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Patients;

[Authorize(DentalPermissions.Patients.View)]
public class IndexModel : DentalPageModel
{
    /// <summary>Начальный поиск (из глобального поиска в шапке).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public void OnGet()
    {
    }
}
