using System.Collections.Generic;
using System.Linq;
using Dental.Permissions;
using Dental.Staff;
using P = Dental.Permissions.DentalPermissions;

namespace Dental.Roles;

public sealed record DentalRolePreset(string RoleName, StaffPosition Position, IReadOnlyList<string> Permissions, RoleLimitsData Limits);

/// <summary>Пресеты ролей для seed арендатора (перенесено из backend RolePresets).</summary>
public static class DentalRolePresets
{
    private const long Tenge = 100;

    /// <summary>Права модулей ABP, которые получает владелец: управление пользователями, ролями и их правами.</summary>
    public static readonly IReadOnlyList<string> OwnerAbpPermissions =
    [
        "AbpIdentity.Roles", "AbpIdentity.Roles.Create", "AbpIdentity.Roles.Update", "AbpIdentity.Roles.ManagePermissions",
        "AbpIdentity.Users", "AbpIdentity.Users.Update", "AbpIdentity.Users.ManageRoles",
    ];

    public static readonly DentalRolePreset Owner = new(DentalRoles.Owner, StaffPosition.Owner,
        P.All.Select(p => p.Name).Concat(OwnerAbpPermissions).ToList(),
        new RoleLimitsData { MaxDiscountPct = 100, MaxWriteoffAmount = null, MaxRefundAmount = null, CanEditClosedShiftVisits = true });

    public static readonly DentalRolePreset SeniorAdmin = new(DentalRoles.SeniorAdmin, StaffPosition.SeniorAdmin,
    [
        P.Org.BranchesManage, P.Org.StaffManage,
        P.Patients.View, P.Patients.Edit, P.Patients.Merge, P.Patients.ViewMedical,
        P.Schedule.ViewAll, P.Schedule.Manage, P.Schedule.DoctorSchedulesManage,
        P.Visits.Complete, P.Visits.EditOpen, P.Visits.EditClosed, P.Visits.Cancel,
        P.Catalog.DiscountsApply,
        P.Cash.ShiftOpenClose, P.Cash.PaymentCreate, P.Cash.PaymentRefund, P.Cash.ExpenseCreate,
        P.Inventory.View, P.Inventory.Receive, P.Inventory.TransferCreate, P.Inventory.TransferReceive, P.Inventory.Writeoff,
        P.Inventory.Count, P.Inventory.CountApprove, P.Inventory.ItemsManage,
        P.Purchase.RequestCreate,
        P.Reports.Branch, P.Reports.Finance,
        P.Audit.View,
    ],
        new RoleLimitsData { MaxDiscountPct = 15, MaxWriteoffAmount = 100_000 * Tenge, MaxRefundAmount = 100_000 * Tenge, CanEditClosedShiftVisits = true });

    // Кладовщик и кассир упразднены (решение заказчика 2026-09-28): их права переданы администратору.
    public static readonly DentalRolePreset Admin = new(DentalRoles.Admin, StaffPosition.Admin,
    [
        P.Patients.View, P.Patients.Edit,
        P.Schedule.ViewAll, P.Schedule.Manage,
        P.Visits.Complete, P.Visits.EditOpen,
        P.Catalog.DiscountsApply,
        P.Cash.ShiftOpenClose, P.Cash.PaymentCreate, P.Cash.PaymentRefund, P.Cash.ExpenseCreate,
        P.Inventory.View, P.Inventory.Receive, P.Inventory.TransferCreate, P.Inventory.TransferReceive,
        P.Inventory.Writeoff, P.Inventory.Count, P.Inventory.ItemsManage,
        P.Purchase.RequestCreate,
        P.Reports.Branch,
    ],
        new RoleLimitsData { MaxDiscountPct = 5, MaxWriteoffAmount = 50_000 * Tenge, MaxRefundAmount = 20_000 * Tenge, CanEditClosedShiftVisits = false });

    public static readonly DentalRolePreset Doctor = new(DentalRoles.Doctor, StaffPosition.Doctor,
    [
        P.Patients.View, P.Patients.ViewMedical,
        P.Schedule.ViewOwn,
        P.Visits.Complete,
        P.Payroll.ViewOwn,
    ],
        new RoleLimitsData { MaxDiscountPct = 0, MaxWriteoffAmount = 0, MaxRefundAmount = 0 });

    public static readonly IReadOnlyList<DentalRolePreset> All = [Owner, SeniorAdmin, Admin, Doctor];
}
