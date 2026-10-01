using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dental.Localization;
using Dental.Notifications;
using Dental.Roles;
using Dental.Staff;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Users;

namespace Dental.Approvals;

/// <summary>Результат проверки лимита: либо можно выполнять сразу, либо создан запрос на подтверждение.</summary>
public record ApprovalGateResult(bool Allowed, ApprovalRequest? Request)
{
    public static readonly ApprovalGateResult Ok = new(true, null);
}

/// <summary>
/// Подтверждения (бывший ApprovalService): создание запросов, проверка лимитов, решение с вызовом IApprovalHandler,
/// уведомления подтверждающим и автору.
/// </summary>
public class ApprovalManager : DomainService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ApprovalRequest, Guid> _requests;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IRoleLimitsProvider _roleLimits;
    private readonly IBranchScope _branchScope;
    private readonly NotificationManager _notifications;
    private readonly IStringLocalizer<DentalResource> _l;

    public ApprovalManager(
        IRepository<ApprovalRequest, Guid> requests,
        ICurrentUser currentUser,
        IPermissionChecker permissionChecker,
        IRoleLimitsProvider roleLimits,
        IBranchScope branchScope,
        NotificationManager notifications,
        IStringLocalizer<DentalResource> l)
    {
        _requests = requests;
        _currentUser = currentUser;
        _permissionChecker = permissionChecker;
        _roleLimits = roleLimits;
        _branchScope = branchScope;
        _notifications = notifications;
        _l = l;
    }

    public string TypeTitle(string type)
    {
        var s = _l["ApprovalType:" + type];
        return s.ResourceNotFound ? type : s.Value;
    }

    /// <summary>
    /// Хелпер для модулей: проверить лимит текущего пользователя для действия; если не хватает — создать запрос.
    /// Вызывающий при Allowed=false переводит свою сущность в статус «ожидает подтверждения» (решение исполнит его IApprovalHandler).
    /// </summary>
    public async Task<ApprovalGateResult> CheckOrRequestAsync(string type, string entityType, Guid entityId, decimal amount, string summary, object payload, Guid? branchId)
    {
        var limits = await _roleLimits.GetForCurrentUserAsync();
        if (AllowsByLimits(type, amount, limits))
        {
            return ApprovalGateResult.Ok;
        }
        return new ApprovalGateResult(false, await RequestAsync(type, entityType, entityId, amount, summary, payload, branchId));
    }

    /// <summary>Создать (или обновить ожидающий) запрос и уведомить подтверждающих.</summary>
    public async Task<ApprovalRequest> RequestAsync(string type, string entityType, Guid entityId, decimal amount, string summary, object payload, Guid? branchId)
    {
        var json = payload as string ?? JsonSerializer.Serialize(payload, Json);
        var existing = await _requests.FirstOrDefaultAsync(a => a.EntityId == entityId && a.Type == type && a.Status == ApprovalStatus.Pending);
        if (existing is not null)
        {
            existing.SetDetails(amount, summary, json);
            await _requests.UpdateAsync(existing, autoSave: true);
            return existing;
        }
        var request = new ApprovalRequest(GuidGenerator.Create(), CurrentTenant.Id, type, entityType, entityId, branchId, _currentUser.GetId());
        request.SetDetails(amount, summary, json);
        await _requests.InsertAsync(request, autoSave: true);
        await _notifications.NotifyByPermissionAsync(ApprovalTypes.PermissionFor(type), NotificationConsts.TypeApproval,
            _l["Approval:NotifyRequested", TypeTitle(type)], summary, nameof(ApprovalRequest), request.Id, branchId, _currentUser.Id);
        return request;
    }

    /// <summary>
    /// Отклонить ожидающие запросы по сущности, которую её модуль отменил (документ отменён): без вызова IApprovalHandler.
    /// Возвращает число закрытых запросов.
    /// </summary>
    public async Task<int> CancelPendingAsync(string entityType, Guid entityId, string reason)
    {
        var pending = await _requests.GetListAsync(a => a.EntityId == entityId && a.EntityType == entityType && a.Status == ApprovalStatus.Pending);
        foreach (var a in pending)
        {
            a.Decide(false, _currentUser.Id ?? a.RequestedBy, Clock.Now, reason);
        }
        if (pending.Count > 0)
        {
            await _requests.UpdateManyAsync(pending, autoSave: true);
        }
        return pending.Count;
    }

    public static bool AllowsByLimits(string type, decimal amount, RoleLimitsData l) => type switch
    {
        ApprovalTypes.Discount => l.AllowsDiscount(amount),
        ApprovalTypes.Writeoff or ApprovalTypes.TransferShortage => l.AllowsWriteoff((long)amount),
        ApprovalTypes.Refund => l.AllowsRefund((long)amount),
        ApprovalTypes.ClosedVisitEdit => l.CanEditClosedShiftVisits,
        _ => true,
    };

    public bool IsOwner() => _currentUser.IsInRole(DentalRoles.Owner) || _currentUser.IsInRole("admin");

    /// <summary>Может ли текущий пользователь решить запрос: право типа + достаточный лимит + не свой запрос (кроме владельца).</summary>
    public async Task<bool> CanDecideAsync(ApprovalRequest a)
    {
        if (a.Status != ApprovalStatus.Pending)
        {
            return false;
        }
        if (a.RequestedBy == _currentUser.Id && !IsOwner())
        {
            return false;
        }
        return await HasAuthorityAsync(a);
    }

    private async Task<bool> HasAuthorityAsync(ApprovalRequest a)
    {
        if (!await _permissionChecker.IsGrantedAsync(ApprovalTypes.PermissionFor(a.Type)))
        {
            return false;
        }
        var limits = await _roleLimits.GetForCurrentUserAsync();
        var custom = FindHandler(a.Type) as IApprovalLimitCheck;
        return custom?.AllowsByLimits(a, limits) ?? AllowsByLimits(a.Type, a.Amount, limits);
    }

    public IApprovalHandler? FindHandler(string type) =>
        LazyServiceProvider.LazyGetRequiredService<IEnumerable<IApprovalHandler>>().FirstOrDefault(h => h.Type == type);

    public async Task<ApprovalRequest> DecideAsync(Guid id, bool approve, string? comment)
    {
        var a = await _requests.GetAsync(id);
        if (a.Status != ApprovalStatus.Pending)
        {
            throw new BusinessException(DentalDomainErrorCodes.ApprovalNotPending);
        }
        if (a.BranchId is { } b)
        {
            await _branchScope.EnsureCanAccessAsync(b);
        }
        if (a.RequestedBy == _currentUser.Id && !IsOwner())
        {
            throw new BusinessException(DentalDomainErrorCodes.ApprovalOwnRequest);
        }
        if (!await HasAuthorityAsync(a))
        {
            throw new BusinessException(DentalDomainErrorCodes.ApprovalLimitExceeded);
        }

        a.Decide(approve, _currentUser.GetId(), Clock.Now, comment);

        // Обработчики берутся лениво: они зависят от сервисов модулей, которые сами используют ApprovalManager.
        var handler = FindHandler(a.Type);
        if (handler is not null)
        {
            if (approve)
            {
                await handler.OnApprovedAsync(a);
            }
            else
            {
                await handler.OnRejectedAsync(a);
            }
        }
        await _requests.UpdateAsync(a, autoSave: true);
        await _notifications.NotifyAsync(a.RequestedBy, NotificationConsts.TypeApprovalDecision,
            _l[approve ? "Approval:NotifyApproved" : "Approval:NotifyRejected", TypeTitle(a.Type)], a.Comment ?? a.Summary,
            nameof(ApprovalRequest), a.Id);
        return a;
    }
}
