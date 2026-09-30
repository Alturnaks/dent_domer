using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;

namespace Dental.Web.Authorization;

/// <summary>
/// Доступ к странице, если выдано ХОТЯ БЫ ОДНО из прав (стандартный [Authorize(policy)] требует конкретное).
/// Неаутентифицированный → Challenge (редирект на вход), без прав → Forbid.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AnyPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string[] Permissions { get; }

    public AnyPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }
        var checker = context.HttpContext.RequestServices.GetRequiredService<IPermissionChecker>();
        var result = await checker.IsGrantedAsync(Permissions);
        if (!result.Result.Any(x => x.Value == PermissionGrantResult.Granted))
        {
            context.Result = new ForbidResult();
        }
    }
}
