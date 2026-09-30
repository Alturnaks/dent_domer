using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.DoctorSchedules;

/// <summary>Заглушка раздела — будет заменена модулем.</summary>
[AnyPermission(DentalPermissions.Schedule.DoctorSchedulesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
