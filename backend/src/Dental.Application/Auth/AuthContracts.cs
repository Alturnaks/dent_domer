using Dental.Domain.Organizations;

namespace Dental.Application.Auth;

public sealed record LoginRequest(string Login, string Password);
public sealed record SwitchOrgRequest(Guid OrganizationId);
public sealed record ForgotPasswordRequest(string Login);
public sealed record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>Ответ входа. RefreshToken уходит только в httpOnly cookie, в JSON не сериализуется эндпоинтом.</summary>
public sealed record AuthTokens(string AccessToken, int ExpiresIn, string RefreshToken, DateTimeOffset RefreshExpiresAt, Guid OrganizationId);

public sealed record AccessTokenResponse(string AccessToken, int ExpiresIn, Guid OrganizationId);

public sealed record UserOrganization(Guid OrganizationId, string Slug, string Name, Guid MembershipId, string RoleCode, bool IsActive);

public sealed record MeUser(Guid Id, string? Email, string FullName);
public sealed record MeOrganization(Guid Id, string Slug, string Name, string Timezone, string Currency);
public sealed record MeRole(Guid Id, string Code, string Name);
public sealed record MeLimits(decimal? MaxDiscountPct, long? MaxWriteoffAmount, long? MaxRefundAmount, bool CanEditClosedShiftVisits);
public sealed record MeBranch(Guid Id, string Name);
public sealed record MeOrgOption(Guid Id, string Slug, string Name);

/// <summary>GET /auth/me — контракт SPEC §19.</summary>
public sealed record MeResponse(
    MeUser User,
    MeOrganization Organization,
    MeRole Role,
    IReadOnlyList<string> Permissions,
    MeLimits Limits,
    bool AllBranches,
    IReadOnlyList<MeBranch> Branches,
    IReadOnlyList<MeOrgOption> Organizations,
    string Position,
    int PendingApprovals,
    int UnreadNotifications);

public interface ITokenService
{
    (string Token, int ExpiresIn) CreateAccessToken(User user, Guid organizationId, Guid membershipId);
    string CreateRefreshToken();
    string Hash(string token);
    TimeSpan RefreshLifetime { get; }
}

public interface IPasswordHashing
{
    string Hash(User user, string password);
    bool Verify(User user, string hash, string password);
}

/// <summary>Доступ к данным в обход RLS (роль-владелец): вход, фоновые задачи, seed. Только чтение глобальных связей.</summary>
public interface ISystemDb
{
    Task<IReadOnlyList<UserOrganization>> GetUserOrganizationsAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<(Guid Id, string Timezone)>> ListOrganizationsAsync(CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, IReadOnlyList<(string FileName, byte[] Content, string ContentType)>? attachments, CancellationToken ct);
}
