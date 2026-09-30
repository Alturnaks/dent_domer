using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Dental.Staff;

public class BranchScope : IBranchScope, IScopedDependency
{
    /// <summary>Встроенная роль ABP-администратора арендатора — видит все филиалы.</summary>
    public const string AbpAdminRoleName = "admin";

    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<Employee, Guid> _employeeRepository;

    private (Guid? UserId, Guid? TenantId)? _cachedFor;
    private Employee? _employee;
    private BranchScopeInfo? _scope;

    public BranchScope(ICurrentUser currentUser, ICurrentTenant currentTenant, IRepository<Employee, Guid> employeeRepository)
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _employeeRepository = employeeRepository;
    }

    public async Task<Employee?> GetCurrentEmployeeAsync()
    {
        await LoadAsync();
        return _employee;
    }

    public async Task<BranchScopeInfo> GetAsync()
    {
        await LoadAsync();
        return _scope!;
    }

    public async Task<bool> CanAccessAsync(Guid branchId) => (await GetAsync()).Contains(branchId);

    public async Task EnsureCanAccessAsync(Guid branchId)
    {
        if (!await CanAccessAsync(branchId))
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied)
                .WithData("branchId", branchId);
        }
    }

    public async Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, Expression<Func<T, Guid>> branchSelector)
    {
        var scope = await GetAsync();
        if (scope.AllBranches)
        {
            return query;
        }
        var ids = scope.BranchIds.ToList();
        var contains = typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains))!;
        var body = Expression.Call(Expression.Constant(ids), contains, branchSelector.Body);
        return query.Where(Expression.Lambda<Func<T, bool>>(body, branchSelector.Parameters));
    }

    private async Task LoadAsync()
    {
        var key = (_currentUser.Id, _currentTenant.Id);
        if (_cachedFor == key && _scope != null)
        {
            return;
        }
        _cachedFor = key;
        _employee = null;

        if (!_currentUser.IsAuthenticated || _currentUser.Id is null)
        {
            _scope = BranchScopeInfo.Nothing;
            return;
        }

        var userId = _currentUser.Id.Value;
        _employee = await _employeeRepository.FirstOrDefaultAsync(e => e.UserId == userId);

        if (_employee == null)
        {
            _scope = _currentUser.IsInRole(AbpAdminRoleName) ? BranchScopeInfo.All : BranchScopeInfo.Nothing;
        }
        else if (!_employee.IsActive)
        {
            _scope = BranchScopeInfo.Nothing;
        }
        else
        {
            _scope = _employee.AllBranches ? BranchScopeInfo.All : new BranchScopeInfo(false, _employee.BranchIds.ToList());
        }
    }
}
