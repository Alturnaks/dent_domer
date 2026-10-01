namespace Dental.Web.Menus;

/// <summary>Имена пунктов меню (используются в PageLayout.Content.MenuItemName для подсветки).</summary>
public static class DentalMenus
{
    private const string Prefix = "Dental";

    public const string Home = Prefix + ".Home";
    public const string Dashboard = Prefix + ".Dashboard";
    public const string Schedule = Prefix + ".Schedule";
    public const string DoctorSchedules = Prefix + ".DoctorSchedules";
    public const string Patients = Prefix + ".Patients";
    public const string Cash = Prefix + ".Cash";
    public const string Inventory = Prefix + ".Inventory";
    public const string Purchasing = Prefix + ".Purchasing";
    public const string Catalog = Prefix + ".Catalog";
    public const string Staff = Prefix + ".Staff";
    public const string Payroll = Prefix + ".Payroll";
    public const string Reports = Prefix + ".Reports";
    public const string Approvals = Prefix + ".Approvals";
    public const string AuditLog = Prefix + ".AuditLog";
    public const string Settings = Prefix + ".Settings";
    public const string SettingsOrganization = Settings + ".Organization";
    public const string SettingsBranches = Settings + ".Branches";
    public const string SettingsRoleLimits = Settings + ".RoleLimits";
    public const string SettingsReferences = Settings + ".References";
}
