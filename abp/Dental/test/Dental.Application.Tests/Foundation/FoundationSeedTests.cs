using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Data.Seed;
using Dental.Roles;
using Dental.Staff;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace Dental.Foundation;

/// <summary>Проверка демо-seed: арендатор, филиалы, 12 сотрудников, пресеты ролей и лимиты.</summary>
public abstract class FoundationSeedTests<TStartupModule> : DentalApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Demo_Tenant_Is_Seeded()
    {
        var tenant = await GetRequiredService<ITenantRepository>().FindByNameAsync(DentalDemoDataSeedContributor.DemoTenantName.ToUpperInvariant());
        tenant.ShouldNotBeNull();

        using (GetRequiredService<ICurrentTenant>().Change(tenant.Id))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                (await GetRequiredService<IRepository<Branch, Guid>>().GetCountAsync()).ShouldBe(3);
                (await GetRequiredService<IRepository<Chair, Guid>>().GetCountAsync()).ShouldBe(6);
                var employees = await GetRequiredService<IRepository<Employee, Guid>>().GetListAsync();
                employees.Count.ShouldBe(12);
                employees.Count(e => e.Position == StaffPosition.Doctor).ShouldBe(6);

                var roleNames = (await GetRequiredService<IIdentityRoleRepository>().GetListAsync()).Select(r => r.Name).ToList();
                foreach (var preset in DentalRolePresets.All)
                {
                    roleNames.ShouldContain(preset.RoleName);
                }

                var users = GetRequiredService<IIdentityUserRepository>();
                var limits = GetRequiredService<IRoleLimitsProvider>();
                var owner = await users.FindByNormalizedUserNameAsync("OWNER@DEMO.KZ");
                var admin = await users.FindByNormalizedUserNameAsync("ADMIN1@DEMO.KZ");
                var doctor = await users.FindByNormalizedUserNameAsync("DOCTOR1@DEMO.KZ");

                var ownerLimits = await limits.GetForUserAsync(owner!.Id);
                ownerLimits.MaxWriteoffAmount.ShouldBeNull();
                ownerLimits.CanEditClosedShiftVisits.ShouldBeTrue();

                var adminLimits = await limits.GetForUserAsync(admin!.Id);
                adminLimits.MaxDiscountPct.ShouldBe(5);
                adminLimits.AllowsRefund(20_000 * 100).ShouldBeTrue();
                adminLimits.AllowsRefund(20_000 * 100 + 1).ShouldBeFalse();

                (await limits.GetForUserAsync(doctor!.Id)).AllowsDiscount(1).ShouldBeFalse();
            });
        }
    }
}
