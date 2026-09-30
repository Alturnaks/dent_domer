using Volo.Abp.Modularity;

namespace Dental;

[DependsOn(
    typeof(DentalDomainModule),
    typeof(DentalTestBaseModule)
)]
public class DentalDomainTestModule : AbpModule
{

}
