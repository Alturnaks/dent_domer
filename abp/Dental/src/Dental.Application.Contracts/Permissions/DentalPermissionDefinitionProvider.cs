using Dental.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Dental.Permissions;

/// <summary>
/// Одна группа прав на раздел. Отображаемые имена — ключи "Permission:{имя права}" в Localization/Dental/*.json.
/// Все права — только для арендатора (клиники), хосту они не видны.
/// </summary>
public class DentalPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        foreach (var groupName in new[]
                 {
                     DentalPermissions.Org.Group, DentalPermissions.Patients.Group, DentalPermissions.Schedule.Group,
                     DentalPermissions.Visits.Group, DentalPermissions.Catalog.Group, DentalPermissions.Cash.Group,
                     DentalPermissions.Inventory.Group, DentalPermissions.Purchase.Group, DentalPermissions.Payroll.Group,
                     DentalPermissions.Reports.Group, DentalPermissions.Audit.Group,
                 })
        {
            var group = context.AddGroup(groupName, L("PermissionGroup:" + groupName));
            foreach (var (g, name) in DentalPermissions.All)
            {
                if (g == groupName)
                {
                    group.AddPermission(name, L("Permission:" + name), MultiTenancySides.Tenant);
                }
            }
        }
    }

    private static LocalizableString L(string name) => LocalizableString.Create<DentalResource>(name);
}
