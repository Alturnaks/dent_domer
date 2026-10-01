using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Catalog;

/// <summary>Услуги и прайсы: дерево категорий, услуги с действующей ценой, прайс-листы, техкарты.</summary>
[AnyPermission(DentalPermissions.Catalog.PricesManage, DentalPermissions.Catalog.TechCardsManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
