using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Roles;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace Dental.Data.Seed;

/// <summary>
/// Создаёт в ТЕКУЩЕМ арендаторе 4 предустановленные роли (DentalRolePresets), их права и лимиты.
/// Идемпотентно: существующие роли не пересоздаются, права только добавляются, лимиты создаются, если их нет.
/// </summary>
public class DentalTenantRoleSeeder : ITransientDependency
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IRepository<RoleLimit, Guid> _roleLimitRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public DentalTenantRoleSeeder(
        IIdentityRoleRepository roleRepository,
        IdentityRoleManager roleManager,
        IPermissionDataSeeder permissionDataSeeder,
        IRepository<RoleLimit, Guid> roleLimitRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant)
    {
        _roleRepository = roleRepository;
        _roleManager = roleManager;
        _permissionDataSeeder = permissionDataSeeder;
        _roleLimitRepository = roleLimitRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync()
    {
        foreach (var preset in DentalRolePresets.All)
        {
            var role = await _roleRepository.FindByNormalizedNameAsync(_roleManager.NormalizeKey(preset.RoleName));
            if (role == null)
            {
                role = new IdentityRole(_guidGenerator.Create(), preset.RoleName, _currentTenant.Id)
                {
                    IsStatic = true,
                    IsPublic = true,
                };
                (await _roleManager.CreateAsync(role)).CheckErrors();
            }

            await _permissionDataSeeder.SeedAsync(
                RolePermissionValueProvider.ProviderName,
                role.Name,
                preset.Permissions,
                _currentTenant.Id);

            if (!await _roleLimitRepository.AnyAsync(l => l.RoleId == role.Id))
            {
                await _roleLimitRepository.InsertAsync(
                    new RoleLimit(_guidGenerator.Create(), _currentTenant.Id, role.Id, preset.Limits),
                    autoSave: true);
            }
        }
    }
}
