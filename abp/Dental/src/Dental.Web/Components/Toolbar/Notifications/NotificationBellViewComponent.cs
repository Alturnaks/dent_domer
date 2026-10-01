using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Dental.Web.Components.Toolbar.Notifications;

/// <summary>Колокольчик уведомлений (опрос раз в минуту, логика — wwwroot/js/dental-common.js).</summary>
public class NotificationBellViewComponent : AbpViewComponent
{
    public virtual IViewComponentResult Invoke() => View("~/Components/Toolbar/Notifications/Default.cshtml");
}
