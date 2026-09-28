using System.Security.Cryptography;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dental.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    ISystemDb systemDb,
    ITokenService tokens,
    IPasswordHashing hashing,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IMembershipCache membershipCache,
    IEmailSender email,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    public const int MaxFailedLogins = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthTokens> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = await FindUserAsync(request.Login, ct);
        if (user is null)
        {
            // Выравниваем время ответа, чтобы не раскрывать существование логина.
            hashing.Verify(new User(), "AQAAAAIAAYagAAAAEDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummy==", request.Password);
            throw Invalid();
        }

        if (user.LockedUntil is { } locked && locked > now)
        {
            throw new AppException(ErrorCodes.AccountLocked, "Учётная запись временно заблокирована после неудачных попыток входа", 423,
                new Dictionary<string, object?> { ["lockedUntil"] = locked });
        }

        if (!hashing.Verify(user, user.PasswordHash, request.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockedUntil = now + LockoutDuration;
                user.FailedLoginCount = 0;
                logger.LogWarning("User {UserId} locked after failed logins", user.Id);
            }
            await db.SaveChangesAsync(ct);
            throw Invalid();
        }

        if (!user.IsActive) throw new AppException(ErrorCodes.AccountDisabled, "Учётная запись отключена", 403);

        var orgs = (await systemDb.GetUserOrganizationsAsync(user.Id, ct)).Where(o => o.IsActive).ToList();
        if (orgs.Count == 0) throw new AppException(ErrorCodes.NoMembership, "У пользователя нет доступа ни к одной организации", 403);

        var org = orgs.FirstOrDefault(o => o.OrganizationId == user.LastOrganizationId) ?? orgs[0];

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.LastOrganizationId = org.OrganizationId;

        return await IssueAsync(user, org, ip, userAgent, ct);
    }

    public async Task<AuthTokens> RefreshAsync(string? refreshToken, string? ip, string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw InvalidRefresh();
        var now = clock.GetUtcNow();
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) throw InvalidRefresh();

        if (stored.RevokedAt is not null)
        {
            // Повторное использование отозванного токена — отзываем все токены пользователя.
            await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            throw InvalidRefresh();
        }

        if (stored.ExpiresAt <= now) throw InvalidRefresh();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || !user.IsActive) throw InvalidRefresh();

        var org = (await systemDb.GetUserOrganizationsAsync(user.Id, ct))
            .FirstOrDefault(o => o.OrganizationId == stored.OrganizationId && o.IsActive);
        if (org is null) throw InvalidRefresh();

        stored.RevokedAt = now;
        var issued = await IssueAsync(user, org, ip, userAgent, ct, save: false);
        stored.ReplacedByHash = tokens.Hash(issued.RefreshToken);
        await db.SaveChangesAsync(ct);
        return issued;
    }

    public async Task<AuthTokens> SwitchOrgAsync(Guid organizationId, string? currentRefresh, string? ip, string? userAgent, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.UserId, ct);
        var org = (await systemDb.GetUserOrganizationsAsync(user.Id, ct))
            .FirstOrDefault(o => o.OrganizationId == organizationId && o.IsActive)
            ?? throw AppException.NotFound("Организация");

        if (!string.IsNullOrWhiteSpace(currentRefresh))
        {
            var hash = tokens.Hash(currentRefresh);
            await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
        }

        user.LastOrganizationId = organizationId;
        return await IssueAsync(user, org, ip, userAgent, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokens.Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    /// <summary>Отозвать все refresh-токены пользователя в организации (увольнение, смена роли).</summary>
    public static async Task RevokeAllAsync(IAppDbContext db, Guid userId, Guid organizationId, DateTimeOffset now, CancellationToken ct) =>
        await db.RefreshTokens.Where(t => t.UserId == userId && t.OrganizationId == organizationId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    public async Task<MeResponse> MeAsync(CancellationToken ct)
    {
        var orgId = currentUser.OrganizationId;
        var snapshot = await membershipCache.GetAsync(currentUser.UserId, orgId, ct)
                       ?? throw new AppException(ErrorCodes.Unauthorized, "Сессия недействительна", 401);
        var org = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, ct);
        var branchesQuery = db.Branches.AsNoTracking().Where(b => b.DeletedAt == null && b.IsActive);
        if (!snapshot.AllBranches) branchesQuery = branchesQuery.Where(b => snapshot.BranchIds.Contains(b.Id));
        var branches = await branchesQuery.OrderBy(b => b.Name).Select(b => new MeBranch(b.Id, b.Name)).ToListAsync(ct);
        var orgs = (await systemDb.GetUserOrganizationsAsync(currentUser.UserId, ct))
            .Where(o => o.IsActive).Select(o => new MeOrgOption(o.OrganizationId, o.Slug, o.Name)).ToList();

        var pending = await CountPendingApprovalsAsync(snapshot, ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == currentUser.UserId && n.ReadAt == null, ct);

        var l = snapshot.Limits;
        return new MeResponse(
            new MeUser(snapshot.UserId, snapshot.Email, snapshot.FullName),
            new MeOrganization(org.Id, org.Slug, org.Name, org.Timezone, org.Currency),
            new MeRole(snapshot.RoleId, snapshot.RoleCode, snapshot.RoleName),
            snapshot.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            new MeLimits(l.MaxDiscountPct, l.MaxWriteoffAmount, l.MaxRefundAmount, l.CanEditClosedShiftVisits),
            snapshot.AllBranches,
            branches,
            orgs,
            snapshot.Position.ToString(),
            pending,
            unread);
    }

    private async Task<int> CountPendingApprovalsAsync(MembershipSnapshot s, CancellationToken ct)
    {
        var perms = s.Permissions.ToHashSet();
        var types = new List<ApprovalType>();
        if (perms.Contains(Perm.Catalog.DiscountsApply)) types.Add(ApprovalType.Discount);
        if (perms.Contains(Perm.Inventory.Writeoff)) { types.Add(ApprovalType.Writeoff); types.Add(ApprovalType.TransferShortage); }
        if (perms.Contains(Perm.Cash.PaymentRefund)) types.Add(ApprovalType.Refund);
        if (perms.Contains(Perm.Visits.EditClosed)) types.Add(ApprovalType.ClosedVisitEdit);
        if (perms.Contains(Perm.Purchase.OrderApprove)) types.Add(ApprovalType.PurchaseOrder);
        if (perms.Contains(Perm.Payroll.Manage)) types.Add(ApprovalType.PayrollPeriodChange);
        if (types.Count == 0) return 0;
        return await db.ApprovalRequests.CountAsync(a => a.Status == ApprovalStatus.Pending && types.Contains(a.Type) && a.RequestedBy != s.UserId, ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, string resetUrlBase, CancellationToken ct)
    {
        var user = await FindUserAsync(request.Login, ct);
        if (user?.Email is null) return; // не раскрываем, существует ли пользователь
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var now = clock.GetUtcNow();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokens.Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddHours(1),
        });
        await db.SaveChangesAsync(ct);
        var link = $"{resetUrlBase.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(raw)}";
        await email.SendAsync(user.Email, "Восстановление пароля",
            $"<p>Здравствуйте, {System.Net.WebUtility.HtmlEncode(user.FullName)}!</p><p>Чтобы задать новый пароль, перейдите по ссылке (действует 1 час):</p><p><a href=\"{link}\">{link}</a></p>",
            null, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = tokens.Hash(request.Token);
        var token = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= now)
            throw AppException.BadRequest(ErrorCodes.InvalidResetToken, "Ссылка для сброса пароля недействительна или устарела");
        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);
        user.PasswordHash = hashing.Hash(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        token.UsedAt = now;
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthTokens> IssueAsync(User user, UserOrganization org, string? ip, string? userAgent, CancellationToken ct, bool save = true)
    {
        var now = clock.GetUtcNow();
        tenant.Set(org.OrganizationId);
        var (access, expiresIn) = tokens.CreateAccessToken(user, org.OrganizationId, org.MembershipId);
        var refresh = tokens.CreateRefreshToken();
        var expires = now + tokens.RefreshLifetime;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = org.OrganizationId,
            TokenHash = tokens.Hash(refresh),
            CreatedAt = now,
            ExpiresAt = expires,
            Ip = ip,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
        });
        await membershipCache.InvalidateAsync(user.Id, org.OrganizationId, ct);
        if (save) await db.SaveChangesAsync(ct);
        return new AuthTokens(access, expiresIn, refresh, expires, org.OrganizationId);
    }

    private async Task<User?> FindUserAsync(string login, CancellationToken ct)
    {
        var value = (login ?? "").Trim();
        if (value.Length == 0) return null;
        if (value.Contains('@', StringComparison.Ordinal))
        {
            var emailLower = value.ToLowerInvariant();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == emailLower, ct);
        }
        var phone = Patient.NormalizePhone(value);
        return phone is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct);
    }

    private static AppException Invalid() => new(ErrorCodes.InvalidCredentials, "Неверный логин или пароль", 401);
    private static AppException InvalidRefresh() => new(ErrorCodes.InvalidRefreshToken, "Сессия истекла, войдите заново", 401);
}
