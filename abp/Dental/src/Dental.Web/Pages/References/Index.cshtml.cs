using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.References;

/// <summary>Справочники организации: источники привлечения, причины отмены/переноса.</summary>
[Authorize(DentalPermissions.Org.SettingsManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
