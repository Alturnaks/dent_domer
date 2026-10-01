using System.Threading.Tasks;
using Dental.Localization;
using Dental.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.TenantManagement.Web.Navigation;
using Volo.Abp.UI.Navigation;
using P = Dental.Permissions.DentalPermissions;

namespace Dental.Web.Menus;

/// <summary>
/// Главное меню. Каждый раздел закрыт правами (requiresAll: false — достаточно любого).
/// Модули заменяют заглушку страницы, а пункт меню оставляют здесь.
/// </summary>
public class DentalMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }

    private static Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var l = context.GetLocalizer<DentalResource>();
        var menu = context.Menu;

        menu.AddItem(new ApplicationMenuItem(DentalMenus.Dashboard, l["Menu:Dashboard"], "~/", icon: "fa fa-gauge", order: 1));

        menu.AddItem(new ApplicationMenuItem(DentalMenus.Schedule, l["Menu:Schedule"], "~/Schedule", icon: "fa fa-calendar-days", order: 2)
            .RequirePermissions(false, P.Schedule.ViewAll, P.Schedule.ViewOwn));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.DoctorSchedules, l["Menu:DoctorSchedules"], "~/DoctorSchedules", icon: "fa fa-user-clock", order: 3)
            .RequirePermissions(P.Schedule.DoctorSchedulesManage));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Patients, l["Menu:Patients"], "~/Patients", icon: "fa fa-user-injured", order: 4)
            .RequirePermissions(P.Patients.View));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Cash, l["Menu:Cash"], "~/Cash", icon: "fa fa-cash-register", order: 5)
            .RequirePermissions(false, P.Cash.ShiftOpenClose, P.Cash.PaymentCreate, P.Cash.PaymentRefund, P.Cash.ExpenseCreate));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Visits, l["Menu:Visits"], "~/Visits", icon: "fa fa-tooth", order: 5)
            .RequirePermissions(false, P.Visits.EditOpen, P.Visits.Complete, P.Schedule.Manage, P.Cash.PaymentCreate));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Inventory, l["Menu:Inventory"], "~/Inventory", icon: "fa fa-boxes-stacked", order: 6)
            .RequirePermissions(P.Inventory.View));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Purchasing, l["Menu:Purchasing"], "~/Purchasing", icon: "fa fa-truck", order: 7)
            .RequirePermissions(false, P.Purchase.RequestCreate, P.Purchase.OrderCreate, P.Purchase.OrderApprove));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Catalog, l["Menu:Catalog"], "~/Catalog", icon: "fa fa-tooth", order: 8)
            .RequirePermissions(false, P.Catalog.PricesManage, P.Catalog.TechCardsManage));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Staff, l["Menu:Staff"], "~/Staff", icon: "fa fa-user-doctor", order: 9)
            .RequirePermissions(P.Org.StaffManage));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Payroll, l["Menu:Payroll"], "~/Payroll", icon: "fa fa-money-bill-wave", order: 10)
            .RequirePermissions(false, P.Payroll.ViewOwn, P.Payroll.ViewAll, P.Payroll.Manage));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Reports, l["Menu:Reports"], "~/Reports", icon: "fa fa-chart-line", order: 11)
            .RequirePermissions(false, P.Reports.Branch, P.Reports.Network, P.Reports.Finance, P.Reports.Payroll, P.Inventory.View));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.Approvals, l["Menu:Approvals"], "~/Approvals", icon: "fa fa-circle-check", order: 12)
            .RequirePermissions(false, P.Catalog.DiscountsApply, P.Inventory.Writeoff, P.Cash.PaymentRefund, P.Visits.EditClosed,
                P.Purchase.OrderApprove, P.Payroll.Manage, P.Inventory.CountApprove, P.Org.SettingsManage, P.Org.RolesManage));
        menu.AddItem(new ApplicationMenuItem(DentalMenus.AuditLog, l["Menu:AuditLog"], "~/AuditLog", icon: "fa fa-clock-rotate-left", order: 13)
            .RequirePermissions(P.Audit.View));

        var settings = new ApplicationMenuItem(DentalMenus.Settings, l["Menu:Settings"], icon: "fa fa-sliders", order: 14);
        settings.AddItem(new ApplicationMenuItem(DentalMenus.SettingsOrganization, l["Menu:Settings:Organization"], "~/Settings/Organization")
            .RequirePermissions(P.Org.SettingsManage));
        settings.AddItem(new ApplicationMenuItem(DentalMenus.SettingsBranches, l["Menu:Settings:Branches"], "~/Branches")
            .RequirePermissions(P.Org.BranchesManage));
        settings.AddItem(new ApplicationMenuItem(DentalMenus.SettingsRoleLimits, l["Menu:Settings:RoleLimits"], "~/RoleLimits")
            .RequirePermissions(P.Org.RolesManage));
        settings.AddItem(new ApplicationMenuItem(DentalMenus.SettingsReferences, l["Menu:Settings:References"], "~/References")
            .RequirePermissions(P.Org.SettingsManage));
        menu.AddItem(settings);

        // Администрирование ABP (пользователи, роли и права, арендаторы, настройки).
        var administration = menu.GetAdministration();
        administration.Order = 100;
        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 1);
        administration.SetSubItemOrder(TenantManagementMenuNames.GroupName, 2);
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 8);

        return Task.CompletedTask;
    }
}
