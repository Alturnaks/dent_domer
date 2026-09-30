using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Catalog;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Catalog.PricesManage, DentalPermissions.Catalog.TechCardsManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
