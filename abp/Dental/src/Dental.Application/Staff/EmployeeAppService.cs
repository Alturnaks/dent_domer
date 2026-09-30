using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Dental.Roles;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace Dental.Staff;

[Authorize]
public class EmployeeAppService : DentalAppService, IEmployeeAppService
{
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly EmployeeManager _employeeManager;

    public EmployeeAppService(
        IRepository<Employee, Guid> employeeRepository,
        IRepository<Branch, Guid> branchRepository,
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        EmployeeManager employeeManager)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _employeeManager = employeeManager;
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task<PagedResultDto<EmployeeDto>> GetListAsync(GetEmployeeListInput input)
    {
        var scope = await BranchScope.GetAsync();
        var query = await _employeeRepository.GetQueryableAsync();
        query = query
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(), e => e.FullName.ToLower().Contains(input.Filter!.ToLower()) || (e.Phone != null && e.Phone.Contains(input.Filter!)) || (e.Specialty != null && e.Specialty.ToLower().Contains(input.Filter!.ToLower())))
            .WhereIf(input.Position.HasValue, e => e.Position == input.Position)
            .WhereIf(input.IsActive.HasValue, e => e.IsActive == input.IsActive)
            .WhereIf(input.BranchId.HasValue, e => e.AllBranches || e.BranchIds.Contains(input.BranchId!.Value));
        if (!scope.AllBranches)
        {
            var allowed = scope.BranchIds.ToList();
            query = query.Where(e => e.AllBranches || e.BranchIds.Any(b => allowed.Contains(b)));
        }

        var total = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(e => e.IsActive ? 0 : 1).ThenBy(e => e.Position).ThenBy(e => e.FullName)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<EmployeeDto>(total, await ToDtosAsync(items));
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task<EmployeeDto> GetAsync(Guid id)
    {
        var employee = await _employeeRepository.GetAsync(id);
        return (await ToDtosAsync([employee]))[0];
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto input)
    {
        await CheckAssignmentAsync(input);
        var employee = await _employeeManager.CreateAsync(
            input.Email, input.Password, input.FullName, input.Phone, input.Position, [input.RoleName],
            input.AllBranches, input.BranchIds, input.Specialty, input.Color);
        return await GetAsync(employee.Id);
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto input)
    {
        await CheckAssignmentAsync(input);
        var employee = await _employeeRepository.GetAsync(id);
        await _employeeManager.UpdateUserAsync(employee, input.FullName, input.Phone, [input.RoleName]);
        employee.Position = input.Position;
        employee.Specialty = input.Specialty;
        employee.Color = input.Color;
        employee.SetBranches(input.AllBranches, input.BranchIds);
        await _employeeRepository.UpdateAsync(employee, autoSave: true);
        return await GetAsync(id);
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task FireAsync(Guid id)
    {
        var employee = await _employeeRepository.GetAsync(id);
        if (employee.UserId == CurrentUser.Id)
        {
            throw new BusinessException(DentalDomainErrorCodes.CannotFireYourself);
        }
        await _employeeManager.FireAsync(employee, Clock.Now);
        await _employeeRepository.UpdateAsync(employee);
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task RehireAsync(Guid id)
    {
        var employee = await _employeeRepository.GetAsync(id);
        await _employeeManager.RehireAsync(employee);
        await _employeeRepository.UpdateAsync(employee);
    }

    [Authorize(DentalPermissions.Org.StaffManage)]
    public async Task<ListResultDto<RoleLookupDto>> GetRoleLookupAsync()
    {
        var roles = await _roleRepository.GetListAsync();
        var presetOrder = DentalRolePresets.All.Select(p => p.RoleName).ToList();
        return new ListResultDto<RoleLookupDto>(roles
            .Where(r => r.Name != Dental.Staff.BranchScope.AbpAdminRoleName)
            .OrderBy(r => presetOrder.IndexOf(r.Name) is var i && i < 0 ? int.MaxValue : i).ThenBy(r => r.Name)
            .Select(r => new RoleLookupDto { Id = r.Id, Name = r.Name })
            .ToList());
    }

    public async Task<ListResultDto<EmployeeLookupDto>> GetLookupAsync(GetEmployeeLookupInput input)
    {
        var branchId = input.BranchId;
        var position = input.Position;
        var scope = await BranchScope.GetAsync();
        var pos = position ?? StaffPosition.Doctor;
        var query = (await _employeeRepository.GetQueryableAsync())
            .Where(e => e.IsActive && e.Position == pos)
            .WhereIf(branchId.HasValue, e => e.AllBranches || e.BranchIds.Contains(branchId!.Value));
        if (!scope.AllBranches)
        {
            var allowed = scope.BranchIds.ToList();
            query = query.Where(e => e.AllBranches || e.BranchIds.Any(b => allowed.Contains(b)));
        }
        var items = await AsyncExecuter.ToListAsync(query.OrderBy(e => e.FullName));
        return new ListResultDto<EmployeeLookupDto>(items.Select(e => new EmployeeLookupDto
        {
            Id = e.Id, UserId = e.UserId, FullName = e.FullName, Position = e.Position, Specialty = e.Specialty, Color = e.Color,
        }).ToList());
    }

    public async Task<CurrentEmployeeDto> GetCurrentAsync()
    {
        var employee = await BranchScope.GetCurrentEmployeeAsync();
        var scope = await BranchScope.GetAsync();
        return new CurrentEmployeeDto
        {
            EmployeeId = employee?.Id,
            FullName = employee?.FullName ?? CurrentUser.Name ?? CurrentUser.UserName,
            Position = employee?.Position,
            AllBranches = scope.AllBranches,
            BranchIds = scope.BranchIds.ToList(),
            Limits = await RoleLimits.GetForCurrentUserAsync(),
        };
    }

    /// <summary>Нельзя выдать филиалы вне своей зоны; роль «Владелец» выдаёт только тот, у кого есть RolesManage.</summary>
    private async Task CheckAssignmentAsync(EmployeeInputBase input)
    {
        var scope = await BranchScope.GetAsync();
        if (!scope.AllBranches && (input.AllBranches || input.BranchIds.Any(b => !scope.Contains(b))))
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        }
        if (input.RoleName == DentalRoles.Owner && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Org.RolesManage))
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }
        if (await _roleRepository.FindByNormalizedNameAsync(input.RoleName.ToUpperInvariant()) == null)
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(IdentityRole), input.RoleName);
        }
        if (!input.AllBranches && input.BranchIds.Count > 0)
        {
            var existing = await _branchRepository.CountAsync(b => input.BranchIds.Contains(b.Id));
            if (existing != input.BranchIds.Distinct().Count())
            {
                throw new BusinessException(DentalDomainErrorCodes.EmployeeBranchesRequired);
            }
        }
    }

    private async Task<List<EmployeeDto>> ToDtosAsync(List<Employee> employees)
    {
        var userIds = employees.Select(e => e.UserId).ToList();
        var users = (await _userRepository.GetListByIdsAsync(userIds)).ToDictionary(u => u.Id);
        var roleNames = new Dictionary<Guid, List<string>>();
        foreach (var uid in userIds)
        {
            roleNames[uid] = await _userRepository.GetRoleNamesAsync(uid);
        }
        var branchNames = (await _branchRepository.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);

        return employees.Select(e =>
        {
            users.TryGetValue(e.UserId, out var user);
            return new EmployeeDto
            {
                Id = e.Id,
                UserId = e.UserId,
                UserName = user?.UserName,
                Email = user?.Email,
                FullName = e.FullName,
                Phone = e.Phone,
                Position = e.Position,
                Specialty = e.Specialty,
                Color = e.Color,
                AllBranches = e.AllBranches,
                BranchIds = e.BranchIds.ToList(),
                BranchNames = e.BranchIds.Select(id => branchNames.GetValueOrDefault(id)).Where(n => n != null).Select(n => n!).ToList(),
                RoleNames = roleNames.GetValueOrDefault(e.UserId) ?? [],
                IsActive = e.IsActive,
                FiredAt = e.FiredAt,
                CreationTime = e.CreationTime,
                CreatorId = e.CreatorId,
                LastModificationTime = e.LastModificationTime,
                LastModifierId = e.LastModifierId,
            };
        }).ToList();
    }
}
