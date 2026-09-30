using System.Collections.Generic;
using System.Linq;

namespace Dental.Permissions;

/// <summary>
/// Права приложения (SPEC §5.2). Одна ABP-группа на раздел старой системы.
/// Имена: Dental.{Раздел}.{Действие}. Соответствие старым кодам — <see cref="LegacyCodes"/>.
/// Живут в Domain.Shared, чтобы домен (seed пресетов ролей) и Contracts видели одни константы.
/// </summary>
public static class DentalPermissions
{
    public const string GroupName = "Dental";

    public static class Org
    {
        public const string Group = GroupName + ".Org";
        public const string SettingsManage = Group + ".SettingsManage";
        public const string BranchesManage = Group + ".BranchesManage";
        public const string StaffManage = Group + ".StaffManage";
        public const string RolesManage = Group + ".RolesManage";
    }

    public static class Patients
    {
        public const string Group = GroupName + ".Patients";
        public const string View = Group + ".View";
        public const string Edit = Group + ".Edit";
        public const string Merge = Group + ".Merge";
        public const string ViewMedical = Group + ".ViewMedical";
    }

    public static class Schedule
    {
        public const string Group = GroupName + ".Schedule";
        public const string ViewAll = Group + ".ViewAll";
        public const string ViewOwn = Group + ".ViewOwn";
        public const string Manage = Group + ".Manage";
        public const string DoctorSchedulesManage = Group + ".DoctorSchedulesManage";
    }

    public static class Visits
    {
        public const string Group = GroupName + ".Visits";
        public const string Complete = Group + ".Complete";
        public const string EditOpen = Group + ".EditOpen";
        public const string EditClosed = Group + ".EditClosed";
        public const string Cancel = Group + ".Cancel";
    }

    public static class Catalog
    {
        public const string Group = GroupName + ".Catalog";
        public const string DiscountsApply = Group + ".DiscountsApply";
        public const string PricesManage = Group + ".PricesManage";
        public const string TechCardsManage = Group + ".TechCardsManage";
    }

    public static class Cash
    {
        public const string Group = GroupName + ".Cash";
        public const string ShiftOpenClose = Group + ".ShiftOpenClose";
        public const string PaymentCreate = Group + ".PaymentCreate";
        public const string PaymentRefund = Group + ".PaymentRefund";
        public const string ExpenseCreate = Group + ".ExpenseCreate";
    }

    public static class Inventory
    {
        public const string Group = GroupName + ".Inventory";
        public const string View = Group + ".View";
        public const string Receive = Group + ".Receive";
        public const string TransferCreate = Group + ".TransferCreate";
        public const string TransferReceive = Group + ".TransferReceive";
        public const string Writeoff = Group + ".Writeoff";
        public const string Count = Group + ".Count";
        public const string CountApprove = Group + ".CountApprove";
        public const string ItemsManage = Group + ".ItemsManage";
    }

    public static class Purchase
    {
        public const string Group = GroupName + ".Purchase";
        public const string RequestCreate = Group + ".RequestCreate";
        public const string OrderCreate = Group + ".OrderCreate";
        public const string OrderApprove = Group + ".OrderApprove";
    }

    public static class Payroll
    {
        public const string Group = GroupName + ".Payroll";
        public const string ViewOwn = Group + ".ViewOwn";
        public const string ViewAll = Group + ".ViewAll";
        public const string Manage = Group + ".Manage";
    }

    public static class Reports
    {
        public const string Group = GroupName + ".Reports";
        public const string Branch = Group + ".Branch";
        public const string Network = Group + ".Network";
        public const string Finance = Group + ".Finance";
        public const string Payroll = Group + ".Payroll";
    }

    public static class Audit
    {
        public const string Group = GroupName + ".Audit";
        public const string View = Group + ".View";
    }

    /// <summary>Старый код права (backend/Permissions.cs) → новое имя. Для портирования модулей.</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyCodes = new Dictionary<string, string>
    {
        ["org.settings.manage"] = Org.SettingsManage,
        ["branches.manage"] = Org.BranchesManage,
        ["staff.manage"] = Org.StaffManage,
        ["roles.manage"] = Org.RolesManage,
        ["patients.view"] = Patients.View,
        ["patients.edit"] = Patients.Edit,
        ["patients.merge"] = Patients.Merge,
        ["patients.view_medical"] = Patients.ViewMedical,
        ["schedule.view_all"] = Schedule.ViewAll,
        ["schedule.view_own"] = Schedule.ViewOwn,
        ["schedule.manage"] = Schedule.Manage,
        ["doctor_schedules.manage"] = Schedule.DoctorSchedulesManage,
        ["visits.complete"] = Visits.Complete,
        ["visits.edit_open"] = Visits.EditOpen,
        ["visits.edit_closed"] = Visits.EditClosed,
        ["visits.cancel"] = Visits.Cancel,
        ["discounts.apply"] = Catalog.DiscountsApply,
        ["prices.manage"] = Catalog.PricesManage,
        ["techcards.manage"] = Catalog.TechCardsManage,
        ["cash.shift.open_close"] = Cash.ShiftOpenClose,
        ["cash.payment.create"] = Cash.PaymentCreate,
        ["cash.payment.refund"] = Cash.PaymentRefund,
        ["cash.expense.create"] = Cash.ExpenseCreate,
        ["inventory.view"] = Inventory.View,
        ["inventory.receive"] = Inventory.Receive,
        ["inventory.transfer.create"] = Inventory.TransferCreate,
        ["inventory.transfer.receive"] = Inventory.TransferReceive,
        ["inventory.writeoff"] = Inventory.Writeoff,
        ["inventory.count"] = Inventory.Count,
        ["inventory.count.approve"] = Inventory.CountApprove,
        ["inventory.items.manage"] = Inventory.ItemsManage,
        ["purchase.request.create"] = Purchase.RequestCreate,
        ["purchase.order.create"] = Purchase.OrderCreate,
        ["purchase.order.approve"] = Purchase.OrderApprove,
        ["payroll.view_own"] = Payroll.ViewOwn,
        ["payroll.view_all"] = Payroll.ViewAll,
        ["payroll.manage"] = Payroll.Manage,
        ["reports.branch"] = Reports.Branch,
        ["reports.network"] = Reports.Network,
        ["reports.finance"] = Reports.Finance,
        ["reports.payroll"] = Reports.Payroll,
        ["audit.view"] = Audit.View,
    };

    /// <summary>(группа, право) в порядке отображения.</summary>
    public static readonly IReadOnlyList<(string Group, string Name)> All = LegacyCodes.Values
        .Select(n => (n.Substring(0, n.LastIndexOf('.')), n))
        .ToList();
}
