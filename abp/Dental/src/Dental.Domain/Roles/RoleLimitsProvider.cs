using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Staff;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Dental.Roles;

public class RoleLimitsProvider : IRoleLimitsProvider, ITransientDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly IRepository<RoleLimit, Guid> _roleLimitRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IIdentityUserRepository _userRepository;

    public RoleLimitsProvider(
        ICurrentUser currentUser,
        IRepository<RoleLimit, Guid> roleLimitRepository,
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository)
    {
        _currentUser = currentUser;
        _roleLimitRepository = roleLimitRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
    }

    public async Task<RoleLimitsData> GetForCurrentUserAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.Id is null)
        {
            return RoleLimitsData.None;
        }
        return await GetForUserAsync(_currentUser.Id.Value);
    }

    public async Task<RoleLimitsData> GetForUserAsync(Guid userId)
    {
        var roleNames = await _userRepository.GetRoleNamesAsync(userId);
        if (roleNames.Contains(BranchScope.AbpAdminRoleName))
        {
            return RoleLimitsData.Unlimited;
        }
        var roles = await _roleRepository.GetListAsync();
        var roleIds = roles.Where(r => roleNames.Contains(r.Name)).Select(r => r.Id).ToList();
        return await MergeAsync(roleIds);
    }

    public Task<RoleLimitsData> GetForRoleAsync(Guid roleId) => MergeAsync([roleId]);

    public async Task EnsureDiscountAllowedAsync(decimal pct)
    {
        if (!(await GetForCurrentUserAsync()).AllowsDiscount(pct))
        {
            throw new BusinessException(DentalDomainErrorCodes.RoleLimitExceeded).WithData("limit", "discount");
        }
    }

    public async Task EnsureWriteoffAllowedAsync(long amount)
    {
        if (!(await GetForCurrentUserAsync()).AllowsWriteoff(amount))
        {
            throw new BusinessException(DentalDomainErrorCodes.RoleLimitExceeded).WithData("limit", "writeoff");
        }
    }

    public async Task EnsureRefundAllowedAsync(long amount)
    {
        if (!(await GetForCurrentUserAsync()).AllowsRefund(amount))
        {
            throw new BusinessException(DentalDomainErrorCodes.RoleLimitExceeded).WithData("limit", "refund");
        }
    }

    private async Task<RoleLimitsData> MergeAsync(List<Guid> roleIds)
    {
        if (roleIds.Count == 0)
        {
            return RoleLimitsData.None;
        }
        var limits = await _roleLimitRepository.GetListAsync(l => roleIds.Contains(l.RoleId));
        if (limits.Count == 0)
        {
            return RoleLimitsData.None;
        }
        return limits.Select(l => l.ToData()).Aggregate((a, b) => a.MergeMostPermissive(b));
    }
}
