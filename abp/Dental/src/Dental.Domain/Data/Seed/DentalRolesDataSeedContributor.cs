using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dental.Data.Seed;

/// <summary>Для каждого арендатора (при создании и при каждом запуске DbMigrator) — пресеты ролей.</summary>
public class DentalRolesDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly DentalTenantRoleSeeder _roleSeeder;
    private readonly ICurrentTenant _currentTenant;

    public DentalRolesDataSeedContributor(DentalTenantRoleSeeder roleSeeder, ICurrentTenant currentTenant)
    {
        _roleSeeder = roleSeeder;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return;
        }
        using (_currentTenant.Change(context.TenantId))
        {
            await _roleSeeder.SeedAsync();
        }
    }
}
