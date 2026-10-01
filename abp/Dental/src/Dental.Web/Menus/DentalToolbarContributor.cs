using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared.Toolbars;
using Volo.Abp.Users;
using Dental.Web.Components.Toolbar.LoginLink;

namespace Dental.Web.Menus;

public class DentalToolbarContributor : IToolbarContributor
{
    public virtual Task ConfigureToolbarAsync(IToolbarConfigurationContext context)
    {
        if (context.Toolbar.Name != StandardToolbars.Main)
        {
            return Task.CompletedTask;
        }

        if (!context.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated)
        {
            context.Toolbar.Items.Add(new ToolbarItem(typeof(LoginLinkViewComponent)));
        }
        else if (context.ServiceProvider.GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().IsAvailable)
        {
            // Глобальный поиск пациента и колокольчик уведомлений (только внутри арендатора).
            context.Toolbar.Items.Add(new ToolbarItem(typeof(Dental.Web.Components.Toolbar.PatientSearch.PatientSearchViewComponent), order: -20,
                requiredPermissionName: Dental.Permissions.DentalPermissions.Patients.View));
            context.Toolbar.Items.Add(new ToolbarItem(typeof(Dental.Web.Components.Toolbar.Notifications.NotificationBellViewComponent), order: -10));
        }
		
        return Task.CompletedTask;
    }
}
