using Volo.Abp.Modularity;

namespace Dental;

[DependsOn(
    typeof(DentalApplicationModule),
    typeof(DentalDomainTestModule)
)]
public class DentalApplicationTestModule : AbpModule
{

}
