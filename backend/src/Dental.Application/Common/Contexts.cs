using Dental.Domain.Organizations;

namespace Dental.Application.Common;

/// <summary>Текущий арендатор (организация) запроса или фоновой задачи.</summary>
public interface ITenantContext
{
    Guid? OrganizationId { get; }
    Guid RequiredOrganizationId { get; }
    void Set(Guid? organizationId);
}

/// <summary>Текущий пользователь с правами и лимитами активного членства.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid OrganizationId { get; }
    Guid MembershipId { get; }
    string RoleCode { get; }
    string? FullName { get; }
    IReadOnlySet<string> Permissions { get; }
    RoleLimits Limits { get; }
    bool AllBranches { get; }
    IReadOnlyList<Guid> BranchIds { get; }
    string? Ip { get; }
    string? UserAgent { get; }

    bool Has(string permission);
    bool HasBranch(Guid branchId);

    /// <summary>403 BRANCH_FORBIDDEN, если у пользователя нет доступа к филиалу.</summary>
    void EnsureBranchAccess(Guid branchId);

    /// <summary>403 FORBIDDEN, если нет права.</summary>
    void EnsurePermission(string permission);
}

/// <summary>Снимок членства, кэшируется в Redis на 5 минут.</summary>
public sealed record MembershipSnapshot(
    Guid UserId,
    Guid OrganizationId,
    Guid MembershipId,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    string FullName,
    string? Email,
    IReadOnlyList<string> Permissions,
    RoleLimits Limits,
    bool AllBranches,
    IReadOnlyList<Guid> BranchIds,
    bool IsActive,
    StaffPosition Position);

public interface IMembershipCache
{
    Task<MembershipSnapshot?> GetAsync(Guid userId, Guid organizationId, CancellationToken ct);
    Task InvalidateAsync(Guid userId, Guid organizationId, CancellationToken ct);
    Task InvalidateRoleAsync(Guid organizationId, Guid roleId, CancellationToken ct);
}
