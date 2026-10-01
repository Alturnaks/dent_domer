using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.Approvals;

/// <summary>Подтверждения: доступны всем, кто может решать хотя бы один тип запросов (см. ApprovalTypes.PermissionFor).</summary>
[AnyPermission(
    DentalPermissions.Catalog.DiscountsApply, DentalPermissions.Inventory.Writeoff, DentalPermissions.Cash.PaymentRefund,
    DentalPermissions.Visits.EditClosed, DentalPermissions.Purchase.OrderApprove, DentalPermissions.Payroll.Manage,
    DentalPermissions.Inventory.CountApprove, DentalPermissions.Org.SettingsManage, DentalPermissions.Org.RolesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
