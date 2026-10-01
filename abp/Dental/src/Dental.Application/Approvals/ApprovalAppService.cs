using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Roles;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace Dental.Approvals;

/// <summary>Экран «Подтверждения»: ожидающие/история в пределах филиалов пользователя, решение с комментарием.</summary>
[Authorize]
public class ApprovalAppService : DentalAppService, IApprovalAppService
{
    private readonly IRepository<ApprovalRequest, Guid> _requests;
    private readonly ApprovalManager _manager;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IIdentityUserRepository _users;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IRepository<PermissionGrant, Guid> _grants;

    public ApprovalAppService(
        IRepository<ApprovalRequest, Guid> requests,
        ApprovalManager manager,
        IRepository<Employee, Guid> employees,
        IIdentityUserRepository users,
        IRepository<Branch, Guid> branches,
        IRepository<PermissionGrant, Guid> grants)
    {
        _requests = requests;
        _manager = manager;
        _employees = employees;
        _users = users;
        _branches = branches;
        _grants = grants;
    }

    private async Task<IQueryable<ApprovalRequest>> ScopedQueryAsync()
    {
        var scope = await BranchScope.GetAsync();
        var query = await _requests.GetQueryableAsync();
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            query = query.Where(a => a.BranchId == null || ids.Contains(a.BranchId.Value));
        }
        return query;
    }

    public async Task<PagedResultDto<ApprovalDto>> GetListAsync(GetApprovalListInput input)
    {
        var query = (await ScopedQueryAsync())
            .WhereIf(input.Status.HasValue, a => a.Status == input.Status)
            .WhereIf(!input.Type.IsNullOrWhiteSpace(), a => a.Type == input.Type);
        var total = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(a => a.CreationTime).PageBy(input));
        return new PagedResultDto<ApprovalDto>(total, await ToDtosAsync(rows));
    }

    public async Task<int> GetPendingCountAsync()
    {
        var rows = await AsyncExecuter.ToListAsync((await ScopedQueryAsync()).Where(a => a.Status == ApprovalStatus.Pending).Take(500));
        var count = 0;
        foreach (var r in rows)
        {
            if (await _manager.CanDecideAsync(r))
            {
                count++;
            }
        }
        return count;
    }

    public async Task<ApprovalDto> ApproveAsync(Guid id, ApprovalDecisionDto input) =>
        (await ToDtosAsync([await _manager.DecideAsync(id, true, input.Comment)]))[0];

    public async Task<ApprovalDto> RejectAsync(Guid id, ApprovalDecisionDto input) =>
        (await ToDtosAsync([await _manager.DecideAsync(id, false, input.Comment)]))[0];

    private async Task<List<ApprovalDto>> ToDtosAsync(List<ApprovalRequest> rows)
    {
        var userIds = rows.Select(r => r.RequestedBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var employees = (await _employees.GetListAsync(e => userIds.Contains(e.UserId))).ToDictionary(e => e.UserId, e => e.FullName);
        var missing = userIds.Where(id => !employees.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            foreach (var u in await _users.GetListByIdsAsync(missing))
            {
                employees[u.Id] = string.IsNullOrWhiteSpace(u.Name) ? u.UserName : $"{u.Surname} {u.Name}".Trim();
            }
        }
        var branches = (await _branches.GetListAsync()).ToDictionary(b => b.Id, b => b.Name);

        var permissions = rows.Select(r => ApprovalTypes.PermissionFor(r.Type)).Distinct().ToList();
        var grants = await _grants.GetListAsync(g => permissions.Contains(g.Name) && g.ProviderName == RolePermissionValueProvider.ProviderName);
        var hints = permissions.ToDictionary(p => p, p => string.Join(", ",
            grants.Where(g => g.Name == p).Select(g => g.ProviderKey).Where(r => r != "admin").Append(DentalRoles.Owner).Distinct()));

        var result = new List<ApprovalDto>();
        foreach (var a in rows)
        {
            var perm = ApprovalTypes.PermissionFor(a.Type);
            result.Add(new ApprovalDto
            {
                Id = a.Id,
                Type = a.Type,
                TypeTitle = _manager.TypeTitle(a.Type),
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                BranchId = a.BranchId,
                BranchName = a.BranchId is { } b ? branches.GetValueOrDefault(b) : null,
                Amount = a.Amount,
                Summary = a.Summary,
                Payload = a.Payload,
                RequestedBy = a.RequestedBy,
                RequestedByName = employees.GetValueOrDefault(a.RequestedBy) ?? "?",
                CreationTime = a.CreationTime,
                Status = a.Status,
                DecidedBy = a.DecidedBy,
                DecidedByName = a.DecidedBy is { } d ? employees.GetValueOrDefault(d) : null,
                DecidedAt = a.DecidedAt,
                Comment = a.Comment,
                CanDecide = await _manager.CanDecideAsync(a),
                RequiredPermission = perm,
                DecidersHint = hints.GetValueOrDefault(perm) ?? DentalRoles.Owner,
            });
        }
        return result;
    }
}
