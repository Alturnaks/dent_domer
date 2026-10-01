namespace Dental.Approvals;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// Типы запросов на подтверждение. Строки, а не enum: модули могут добавлять свои типы
/// (регистрируют IApprovalHandler и, при необходимости, право в ApprovalTypes.PermissionFor).
/// </summary>
public static class ApprovalTypes
{
    public const string Discount = "Discount";
    public const string Writeoff = "Writeoff";
    public const string Refund = "Refund";
    public const string ClosedVisitEdit = "ClosedVisitEdit";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string TransferShortage = "TransferShortage";
    public const string PayrollPeriodChange = "PayrollPeriodChange";

    public static readonly string[] All = [Discount, Writeoff, Refund, ClosedVisitEdit, PurchaseOrder, TransferShortage, PayrollPeriodChange];

    /// <summary>Право, необходимое для решения по запросу данного типа.</summary>
    public static string PermissionFor(string type) => type switch
    {
        Discount => Permissions.DentalPermissions.Catalog.DiscountsApply,
        Writeoff or TransferShortage => Permissions.DentalPermissions.Inventory.Writeoff,
        Refund => Permissions.DentalPermissions.Cash.PaymentRefund,
        ClosedVisitEdit => Permissions.DentalPermissions.Visits.EditClosed,
        PurchaseOrder => Permissions.DentalPermissions.Purchase.OrderApprove,
        PayrollPeriodChange => Permissions.DentalPermissions.Payroll.Manage,
        _ => Permissions.DentalPermissions.Org.SettingsManage,
    };
}

public static class ApprovalConsts
{
    public const int MaxTypeLength = 64;
    public const int MaxEntityTypeLength = 128;
    public const int MaxSummaryLength = 1000;
    public const int MaxCommentLength = 1000;
}
