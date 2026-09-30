using Volo.Abp.Modularity;

namespace Dental;

public abstract class DentalApplicationTestBase<TStartupModule> : DentalTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
