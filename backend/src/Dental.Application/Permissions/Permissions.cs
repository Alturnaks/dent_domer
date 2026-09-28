using Dental.Domain.Organizations;

namespace Dental.Application.Permissions;

/// <summary>Коды прав (SPEC §5.2).</summary>
public static class Perm
{
    public static class Org
    {
        public const string SettingsManage = "org.settings.manage";
        public const string BranchesManage = "branches.manage";
        public const string StaffManage = "staff.manage";
        public const string RolesManage = "roles.manage";
    }

    public static class Patients
    {
        public const string View = "patients.view";
        public const string Edit = "patients.edit";
        public const string Merge = "patients.merge";
        public const string ViewMedical = "patients.view_medical";
    }

    public static class Schedule
    {
        public const string ViewAll = "schedule.view_all";
        public const string ViewOwn = "schedule.view_own";
        public const string Manage = "schedule.manage";
        public const string DoctorSchedulesManage = "doctor_schedules.manage";
    }

    public static class Visits
    {
        public const string Complete = "visits.complete";
        public const string EditOpen = "visits.edit_open";
        public const string EditClosed = "visits.edit_closed";
        public const string Cancel = "visits.cancel";
    }

    public static class Catalog
    {
        public const string DiscountsApply = "discounts.apply";
        public const string PricesManage = "prices.manage";
        public const string TechCardsManage = "techcards.manage";
    }

    public static class Cash
    {
        public const string ShiftOpenClose = "cash.shift.open_close";
        public const string PaymentCreate = "cash.payment.create";
        public const string PaymentRefund = "cash.payment.refund";
        public const string ExpenseCreate = "cash.expense.create";
    }

    public static class Inventory
    {
        public const string View = "inventory.view";
        public const string Receive = "inventory.receive";
        public const string TransferCreate = "inventory.transfer.create";
        public const string TransferReceive = "inventory.transfer.receive";
        public const string Writeoff = "inventory.writeoff";
        public const string Count = "inventory.count";
        public const string CountApprove = "inventory.count.approve";
        public const string ItemsManage = "inventory.items.manage";
    }

    public static class Purchase
    {
        public const string RequestCreate = "purchase.request.create";
        public const string OrderCreate = "purchase.order.create";
        public const string OrderApprove = "purchase.order.approve";
    }

    public static class Payroll
    {
        public const string ViewOwn = "payroll.view_own";
        public const string ViewAll = "payroll.view_all";
        public const string Manage = "payroll.manage";
    }

    public static class Reports
    {
        public const string Branch = "reports.branch";
        public const string Network = "reports.network";
        public const string Finance = "reports.finance";
        public const string Payroll = "reports.payroll";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    /// <summary>Все права с группами — для редактора ролей.</summary>
    public static readonly IReadOnlyList<(string Group, string Code)> All =
    [
        ("org", Org.SettingsManage), ("org", Org.BranchesManage), ("org", Org.StaffManage), ("org", Org.RolesManage),
        ("patients", Patients.View), ("patients", Patients.Edit), ("patients", Patients.Merge), ("patients", Patients.ViewMedical),
        ("schedule", Schedule.ViewAll), ("schedule", Schedule.ViewOwn), ("schedule", Schedule.Manage), ("schedule", Schedule.DoctorSchedulesManage),
        ("visits", Visits.Complete), ("visits", Visits.EditOpen), ("visits", Visits.EditClosed), ("visits", Visits.Cancel),
        ("catalog", Catalog.DiscountsApply), ("catalog", Catalog.PricesManage), ("catalog", Catalog.TechCardsManage),
        ("cash", Cash.ShiftOpenClose), ("cash", Cash.PaymentCreate), ("cash", Cash.PaymentRefund), ("cash", Cash.ExpenseCreate),
        ("inventory", Inventory.View), ("inventory", Inventory.Receive), ("inventory", Inventory.TransferCreate), ("inventory", Inventory.TransferReceive),
        ("inventory", Inventory.Writeoff), ("inventory", Inventory.Count), ("inventory", Inventory.CountApprove), ("inventory", Inventory.ItemsManage),
        ("purchase", Purchase.RequestCreate), ("purchase", Purchase.OrderCreate), ("purchase", Purchase.OrderApprove),
        ("payroll", Payroll.ViewOwn), ("payroll", Payroll.ViewAll), ("payroll", Payroll.Manage),
        ("reports", Reports.Branch), ("reports", Reports.Network), ("reports", Reports.Finance), ("reports", Reports.Payroll),
        ("audit", Audit.View),
    ];

    public static readonly IReadOnlySet<string> AllCodes = All.Select(p => p.Code).ToHashSet();
}

/// <summary>Пресет роли для seed новой организации (SPEC §5.4, scripts/roles-matrix.json).</summary>
public sealed record RolePreset(string Code, string Name, StaffPosition Position, IReadOnlyList<string> Permissions, RoleLimits Limits);

public static class RolePresets
{
    public const string Owner = "owner";
    public const string SeniorAdmin = "senior_admin";
    public const string Admin = "admin";
    public const string Doctor = "doctor";

    private const long Tenge = 100;

    public static readonly RolePreset OwnerPreset = new(Owner, "Владелец", StaffPosition.Owner,
        Perm.All.Select(p => p.Code).ToList(),
        new RoleLimits { MaxDiscountPct = 100, MaxWriteoffAmount = null, MaxRefundAmount = null, CanEditClosedShiftVisits = true });

    public static readonly RolePreset SeniorAdminPreset = new(SeniorAdmin, "Старший администратор", StaffPosition.SeniorAdmin,
    [
        Perm.Org.BranchesManage, Perm.Org.StaffManage,
        Perm.Patients.View, Perm.Patients.Edit, Perm.Patients.Merge, Perm.Patients.ViewMedical,
        Perm.Schedule.ViewAll, Perm.Schedule.Manage, Perm.Schedule.DoctorSchedulesManage,
        Perm.Visits.Complete, Perm.Visits.EditOpen, Perm.Visits.EditClosed, Perm.Visits.Cancel,
        Perm.Catalog.DiscountsApply,
        Perm.Cash.ShiftOpenClose, Perm.Cash.PaymentCreate, Perm.Cash.PaymentRefund, Perm.Cash.ExpenseCreate,
        Perm.Inventory.View, Perm.Inventory.Receive, Perm.Inventory.TransferCreate, Perm.Inventory.TransferReceive, Perm.Inventory.Writeoff,
        Perm.Inventory.Count, Perm.Inventory.CountApprove, Perm.Inventory.ItemsManage,
        Perm.Purchase.RequestCreate,
        Perm.Reports.Branch, Perm.Reports.Finance,
        Perm.Audit.View,
    ],
        new RoleLimits { MaxDiscountPct = 15, MaxWriteoffAmount = 100_000 * Tenge, MaxRefundAmount = 100_000 * Tenge, CanEditClosedShiftVisits = true });

    // Кладовщик и кассир упразднены (решение заказчика 2026-09-28): их права переданы администратору.
    public static readonly RolePreset AdminPreset = new(Admin, "Администратор", StaffPosition.Admin,
    [
        Perm.Patients.View, Perm.Patients.Edit,
        Perm.Schedule.ViewAll, Perm.Schedule.Manage,
        Perm.Visits.Complete, Perm.Visits.EditOpen,
        Perm.Catalog.DiscountsApply,
        Perm.Cash.ShiftOpenClose, Perm.Cash.PaymentCreate, Perm.Cash.PaymentRefund, Perm.Cash.ExpenseCreate,
        Perm.Inventory.View, Perm.Inventory.Receive, Perm.Inventory.TransferCreate, Perm.Inventory.TransferReceive,
        Perm.Inventory.Writeoff, Perm.Inventory.Count, Perm.Inventory.ItemsManage,
        Perm.Purchase.RequestCreate,
        Perm.Reports.Branch,
    ],
        new RoleLimits { MaxDiscountPct = 5, MaxWriteoffAmount = 50_000 * Tenge, MaxRefundAmount = 20_000 * Tenge, CanEditClosedShiftVisits = false });

    public static readonly RolePreset DoctorPreset = new(Doctor, "Врач", StaffPosition.Doctor,
    [
        Perm.Patients.View, Perm.Patients.ViewMedical,
        Perm.Schedule.ViewOwn,
        Perm.Visits.Complete,
        Perm.Payroll.ViewOwn,
    ],
        new RoleLimits { MaxDiscountPct = 0, MaxWriteoffAmount = 0, MaxRefundAmount = 0 });

    public static readonly IReadOnlyList<RolePreset> All =
        [OwnerPreset, SeniorAdminPreset, AdminPreset, DoctorPreset];
}
