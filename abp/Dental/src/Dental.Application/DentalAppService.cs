using Dental.Localization;
using Volo.Abp.Application.Services;

namespace Dental;

/* Inherit your application services from this class.
 */
public abstract class DentalAppService : ApplicationService
{
    protected DentalAppService()
    {
        LocalizationResource = typeof(DentalResource);
    }
}
