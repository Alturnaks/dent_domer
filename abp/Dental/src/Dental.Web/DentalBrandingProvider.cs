using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Localization;
using Dental.Localization;

namespace Dental.Web;

[Dependency(ReplaceServices = true)]
public class DentalBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<DentalResource> _localizer;

    public DentalBrandingProvider(IStringLocalizer<DentalResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
