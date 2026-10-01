using Dental.Permissions;
using Dental.Web.Authorization;

namespace Dental.Web.Pages.DoctorSchedules;

/// <summary>Недельные графики врачей, исключения и блокировки.</summary>
[AnyPermission(DentalPermissions.Schedule.DoctorSchedulesManage)]
public class IndexModel : DentalPageModel
{
    public void OnGet()
    {
    }
}
