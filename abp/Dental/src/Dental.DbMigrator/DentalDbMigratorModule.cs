using Dental.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Dental.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(DentalEntityFrameworkCoreModule),
    typeof(DentalApplicationContractsModule)
)]
public class DentalDbMigratorModule : AbpModule
{
}
