using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using Dental.Infrastructure.Persistence.Interceptors;

namespace Dental.Api.Auth;

/// <summary>
/// Текущий пользователь запроса. Заполняется TenantMiddleware из JWT и кэша членства.
/// Для фоновых задач и seed остаётся пустым (системный актор).
/// </summary>
public sealed class CurrentUser : ICurrentUser, IAuditActor
{
    private MembershipSnapshot? _snapshot;
    private HashSet<string> _permissions = [];

    public void Initialize(MembershipSnapshot snapshot, string? ip, string? userAgent)
    {
        _snapshot = snapshot;
        _permissions = snapshot.RoleCode == RolePresets.Owner
            ? Perm.AllCodes.Concat(snapshot.Permissions).ToHashSet(StringComparer.Ordinal)
            : snapshot.Permissions.ToHashSet(StringComparer.Ordinal);
        Ip = ip;
        UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent;
    }

    /// <summary>Для фоновых задач: действовать от имени конкретного пользователя (например, автор подписки).</summary>
    public void InitializeSystem(Guid? userId = null) => _systemUserId = userId;

    private Guid? _systemUserId;

    public bool IsAuthenticated => _snapshot is not null;
    public Guid UserId => _snapshot?.UserId ?? throw new InvalidOperationException("No authenticated user");
    public Guid OrganizationId => _snapshot?.OrganizationId ?? throw new InvalidOperationException("No authenticated user");
    public Guid MembershipId => _snapshot?.MembershipId ?? throw new InvalidOperationException("No authenticated user");
    public string RoleCode => _snapshot?.RoleCode ?? "";
    public string? FullName => _snapshot?.FullName;
    public IReadOnlySet<string> Permissions => _permissions;
    public RoleLimits Limits => _snapshot?.Limits ?? new RoleLimits { MaxDiscountPct = 0, MaxWriteoffAmount = 0, MaxRefundAmount = 0 };
    public bool AllBranches => _snapshot?.AllBranches ?? false;
    public IReadOnlyList<Guid> BranchIds => _snapshot?.BranchIds ?? [];
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Reason { get; set; }

    Guid? IAuditActor.UserId => _snapshot?.UserId ?? _systemUserId;

    public bool Has(string permission) => _permissions.Contains(permission);

    public bool HasBranch(Guid branchId) => AllBranches || BranchIds.Contains(branchId);

    public void EnsureBranchAccess(Guid branchId)
    {
        if (!HasBranch(branchId))
            throw new AppException(ErrorCodes.BranchForbidden, "Нет доступа к филиалу", 403, new Dictionary<string, object?> { ["branchId"] = branchId });
    }

    public void EnsurePermission(string permission)
    {
        if (!Has(permission))
            throw new AppException(ErrorCodes.Forbidden, "Недостаточно прав", 403, new Dictionary<string, object?> { ["permission"] = permission });
    }
}
