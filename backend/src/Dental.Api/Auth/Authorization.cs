using Dental.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Dental.Api.Auth;

/// <summary>Требование: у текущего членства есть хотя бы одно из прав.</summary>
public sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

public sealed class PermissionHandler(ICurrentUser currentUser) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.IsAuthenticated && requirement.Permissions.Any(currentUser.Has)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Динамические политики "perm:a|b" — создаются по коду права без регистрации заранее.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        var perms = policyName[Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(perms)).Build();
    }
}

/// <summary>401/403 в едином формате ошибок.</summary>
public sealed class ErrorAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await Infrastructure.ErrorResponses.WriteAsync(context, 401, ErrorCodes.Unauthorized, "Требуется вход в систему");
            return;
        }
        if (authorizeResult.Forbidden)
        {
            var perms = policy.Requirements.OfType<PermissionRequirement>().SelectMany(r => r.Permissions).ToList();
            await Infrastructure.ErrorResponses.WriteAsync(context, 403, ErrorCodes.Forbidden, "Недостаточно прав",
                perms.Count > 0 ? new Dictionary<string, object?> { ["permissions"] = perms } : null);
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Эндпоинт доступен при наличии любого из перечисленных прав.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + string.Join('|', permissions));
}
