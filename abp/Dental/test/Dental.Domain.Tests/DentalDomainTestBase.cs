using Volo.Abp.Modularity;

namespace Dental;

/* Inherit from this class for your domain layer tests. */
public abstract class DentalDomainTestBase<TStartupModule> : DentalTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
