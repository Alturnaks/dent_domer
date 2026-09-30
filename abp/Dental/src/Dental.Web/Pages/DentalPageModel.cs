using Dental.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Dental.Web.Pages;

public abstract class DentalPageModel : AbpPageModel
{
    protected DentalPageModel()
    {
        LocalizationResourceType = typeof(DentalResource);
    }
}
