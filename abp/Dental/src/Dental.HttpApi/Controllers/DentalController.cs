using Dental.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Dental.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class DentalController : AbpControllerBase
{
    protected DentalController()
    {
        LocalizationResource = typeof(DentalResource);
    }
}
