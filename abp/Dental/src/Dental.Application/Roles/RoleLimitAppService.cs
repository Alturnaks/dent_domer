using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace Dental.Roles;

[Authorize]
public class RoleLimitAppService : DentalAppService, IRoleLimitAppService
{
    private readonly IRepository<RoleLimit, Guid> _roleLimitRepository;
    private readonly IIdentityRoleRepository _roleRepository;

    public RoleLimitAppService(IRepository<RoleLimit, Guid> roleLimitRepository, IIdentityRoleRepository roleRepository)
    {
        _roleLimitRepository = roleLimitRepository;
        _roleRepository = roleRepository;
    }

    [Authorize(DentalPermissions.Org.RolesManage)]
    public async Task<ListResultDto<RoleLimitDto>> GetListAsync()
    {
        var roles = (await _roleRepository.GetListAsync())
            .Where(r => r.Name != Dental.Staff.BranchScope.AbpAdminRoleName)
            .ToList();
        var limits = (await _roleLimitRepository.GetListAsync()).ToDictionary(l => l.RoleId);
        var presetOrder = DentalRolePresets.All.Select(p => p.RoleName).ToList();
        return new ListResultDto<RoleLimitDto>(roles
            .OrderBy(r => presetOrder.IndexOf(r.Name) is var i && i < 0 ? int.MaxValue : i).ThenBy(r => r.Name)
            .Select(r => ToDto(r, limits.GetValueOrDefault(r.Id)))
            .ToList());
    }

    [Authorize(DentalPermissions.Org.RolesManage)]
    public async Task<RoleLimitDto> GetAsync(Guid id)
    {
        var roleId = id;
        var role = await _roleRepository.GetAsync(roleId);
        return ToDto(role, await _roleLimitRepository.FirstOrDefaultAsync(l => l.RoleId == roleId));
    }

    [Authorize(DentalPermissions.Org.RolesManage)]
    public async Task<RoleLimitDto> UpdateAsync(Guid id, UpdateRoleLimitDto input)
    {
        var roleId = id;
        var role = await _roleRepository.GetAsync(roleId);
        var data = new RoleLimitsData
        {
            MaxDiscountPct = input.MaxDiscountPct,
            MaxWriteoffAmount = input.MaxWriteoffAmount,
            MaxRefundAmount = input.MaxRefundAmount,
            CanEditClosedShiftVisits = input.CanEditClosedShiftVisits,
        };
        var limit = await _roleLimitRepository.FirstOrDefaultAsync(l => l.RoleId == roleId);
        if (limit == null)
        {
            limit = await _roleLimitRepository.InsertAsync(new RoleLimit(GuidGenerator.Create(), CurrentTenant.Id, roleId, data), autoSave: true);
        }
        else
        {
            limit.Set(data);
            await _roleLimitRepository.UpdateAsync(limit, autoSave: true);
        }
        return ToDto(role, limit);
    }

    public Task<RoleLimitsData> GetCurrentAsync() => RoleLimits.GetForCurrentUserAsync();

    private static RoleLimitDto ToDto(IdentityRole role, RoleLimit? limit)
    {
        var d = limit?.ToData() ?? RoleLimitsData.None;
        return new RoleLimitDto
        {
            RoleId = role.Id,
            RoleName = role.Name,
            MaxDiscountPct = d.MaxDiscountPct,
            MaxWriteoffAmount = d.MaxWriteoffAmount,
            MaxRefundAmount = d.MaxRefundAmount,
            CanEditClosedShiftVisits = d.CanEditClosedShiftVisits,
        };
    }
}
