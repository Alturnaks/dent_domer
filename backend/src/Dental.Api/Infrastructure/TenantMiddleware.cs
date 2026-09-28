using System.Security.Claims;
using Dental.Api.Auth;
using Dental.Application.Common;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Dental.Api.Infrastructure;

/// <summary>
/// После аутентификации: заполняет ITenantContext из claim "org", загружает права членства (кэш Redis 5 мин).
/// Уволенный/отключённый сотрудник получает 401 немедленно после сброса кэша.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, CurrentUser currentUser, IMembershipCache cache)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var org = principal.FindFirstValue(DentalClaims.Organization);
            if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(org, out var orgId))
            {
                await ErrorResponses.WriteAsync(context, 401, ErrorCodes.Unauthorized, "Некорректный токен");
                return;
            }

            tenant.Set(orgId);
            var snapshot = await cache.GetAsync(userId, orgId, context.RequestAborted);
            if (snapshot is null || !snapshot.IsActive)
            {
                await ErrorResponses.WriteAsync(context, 401, ErrorCodes.Unauthorized, "Доступ к организации отозван");
                return;
            }

            currentUser.Initialize(snapshot, context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString());
        }
        else
        {
            tenant.Set(null);
        }

        await next(context);
    }
}
