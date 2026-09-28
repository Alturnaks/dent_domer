using System.Security.Cryptography;
using Dental.Api.Infrastructure;
using Dental.Application.Auth;
using Dental.Application.Common;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Dental.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshCookie = "dental_rt";
    public const string CsrfCookie = "dental_csrf";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        g.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var tokens = await auth.LoginAsync(request, Ip(http), http.Request.Headers.UserAgent.ToString(), ct);
                SetCookies(http, tokens);
                return TypedResults.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.ExpiresIn, tokens.OrganizationId));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .Validate<LoginRequest>()
            .WithName("Login")
            .Produces<ErrorResponse>(401);

        g.MapPost("/refresh", async (AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                EnsureCsrf(http);
                var tokens = await auth.RefreshAsync(http.Request.Cookies[RefreshCookie], Ip(http), http.Request.Headers.UserAgent.ToString(), ct);
                SetCookies(http, tokens);
                return TypedResults.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.ExpiresIn, tokens.OrganizationId));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .WithName("Refresh")
            .Produces<ErrorResponse>(401);

        g.MapPost("/logout", async (AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                await auth.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
                ClearCookies(http);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithName("Logout");

        g.MapPost("/switch-org", async (SwitchOrgRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var tokens = await auth.SwitchOrgAsync(request.OrganizationId, http.Request.Cookies[RefreshCookie], Ip(http), http.Request.Headers.UserAgent.ToString(), ct);
                SetCookies(http, tokens);
                return TypedResults.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.ExpiresIn, tokens.OrganizationId));
            })
            .RequireAuthorization()
            .WithName("SwitchOrg")
            .Produces<ErrorResponse>(404);

        g.MapGet("/me", async (AuthService auth, CancellationToken ct) => TypedResults.Ok(await auth.MeAsync(ct)))
            .RequireAuthorization()
            .WithName("Me")
            .Produces<ErrorResponse>(401);

        g.MapPost("/password/forgot", async (ForgotPasswordRequest request, AuthService auth, IOptions<Dental.Infrastructure.AppOptions> app, CancellationToken ct) =>
            {
                await auth.ForgotPasswordAsync(request, app.Value.FrontendUrl, ct);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .Validate<ForgotPasswordRequest>()
            .WithName("ForgotPassword");

        g.MapPost("/password/reset", async (ResetPasswordRequest request, AuthService auth, CancellationToken ct) =>
            {
                await auth.ResetPasswordAsync(request, ct);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .Validate<ResetPasswordRequest>()
            .WithName("ResetPassword");

        return app;
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static void SetCookies(HttpContext http, AuthTokens tokens)
    {
        var secure = http.Request.IsHttps;
        http.Response.Cookies.Append(RefreshCookie, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = tokens.RefreshExpiresAt,
        });
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        http.Response.Cookies.Append(CsrfCookie, csrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = tokens.RefreshExpiresAt,
        });
    }

    private static void ClearCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/v1/auth" });
        http.Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/" });
    }

    /// <summary>Double submit: значение заголовка должно совпадать с cookie.</summary>
    private static void EnsureCsrf(HttpContext http)
    {
        var cookie = http.Request.Cookies[CsrfCookie];
        var header = http.Request.Headers[CsrfHeader].ToString();
        if (string.IsNullOrEmpty(cookie) || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(cookie), System.Text.Encoding.UTF8.GetBytes(header)))
        {
            throw new AppException(ErrorCodes.CsrfFailed, "Проверка CSRF не пройдена", 403);
        }
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Login).NotEmpty().MaximumLength(254);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(200);
    }
}
