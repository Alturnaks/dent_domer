using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Cash;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Cash.ShiftOpenClose, DentalPermissions.Cash.PaymentCreate, DentalPermissions.Cash.PaymentRefund, DentalPermissions.Cash.ExpenseCreate)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
