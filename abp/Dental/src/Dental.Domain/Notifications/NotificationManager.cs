using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Roles;
using Dental.Staff;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace Dental.Notifications;

/// <summary>
/// Рассылка внутренних уведомлений. Получатели «по праву»: пользователи, которым право выдано через роль
/// или лично, плюс владельцы и встроенный admin; с ограничением по филиалу (Employee.AllBranches/BranchIds).
/// </summary>
public class NotificationManager : DomainService
{
    private readonly IRepository<Notification, Guid> _notifications;
    private readonly IRepository<PermissionGrant, Guid> _grants;
    private readonly IIdentityUserRepository _users;
    private readonly IRepository<Employee, Guid> _employees;

    public NotificationManager(
        IRepository<Notification, Guid> notifications,
        IRepository<PermissionGrant, Guid> grants,
        IIdentityUserRepository users,
        IRepository<Employee, Guid> employees)
    {
        _notifications = notifications;
        _grants = grants;
        _users = users;
        _employees = employees;
    }

    public async Task NotifyAsync(Guid userId, string type, string title, string? body, string? entityType = null, Guid? entityId = null)
    {
        await _notifications.InsertAsync(new Notification(GuidGenerator.Create(), CurrentTenant.Id, userId, type, title, body, entityType, entityId));
    }

    public async Task<List<Guid>> FindUsersWithPermissionAsync(string permission, Guid? branchId)
    {
        var grants = await _grants.GetListAsync(g => g.Name == permission &&
            (g.ProviderName == RolePermissionValueProvider.ProviderName || g.ProviderName == UserPermissionValueProvider.ProviderName));
        var userIds = new HashSet<Guid>();
        foreach (var g in grants.Where(g => g.ProviderName == UserPermissionValueProvider.ProviderName))
        {
            if (Guid.TryParse(g.ProviderKey, out var uid))
            {
                userIds.Add(uid);
            }
        }
        var roles = grants.Where(g => g.ProviderName == RolePermissionValueProvider.ProviderName).Select(g => g.ProviderKey)
            .Append(DentalRoles.Owner).Append("admin").Distinct();
        foreach (var role in roles)
        {
            foreach (var u in await _users.GetListByNormalizedRoleNameAsync(role.ToUpperInvariant()))
            {
                if (u.IsActive)
                {
                    userIds.Add(u.Id);
                }
            }
        }
        if (userIds.Count == 0)
        {
            return [];
        }

        var employees = await _employees.GetListAsync(e => userIds.Contains(e.UserId));
        var byUser = employees.ToDictionary(e => e.UserId);
        return userIds.Where(id =>
        {
            if (!byUser.TryGetValue(id, out var e))
            {
                return true; // встроенный admin без профиля сотрудника
            }
            return e.IsActive && (branchId is null || e.AllBranches || e.BranchIds.Contains(branchId.Value));
        }).ToList();
    }

    public async Task NotifyByPermissionAsync(string permission, string type, string title, string? body, string? entityType, Guid? entityId,
        Guid? branchId, Guid? exceptUserId = null)
    {
        foreach (var userId in await FindUsersWithPermissionAsync(permission, branchId))
        {
            if (userId != exceptUserId)
            {
                await NotifyAsync(userId, type, title, body, entityType, entityId);
            }
        }
    }
}
