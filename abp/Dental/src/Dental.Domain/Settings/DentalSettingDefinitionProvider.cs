using Volo.Abp.Settings;

namespace Dental.Settings;

public class DentalSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(DentalSettings.MySetting1));
    }
}
