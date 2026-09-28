using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dental.Application.Auth;
using Dental.Domain.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dental.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    [Required] public string Issuer { get; set; } = "dental-api";
    [Required] public string Audience { get; set; } = "dental-web";
    /// <summary>Секрет подписи HMAC-SHA256, не короче 32 символов.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public static class DentalClaims
{
    public const string Organization = "org";
    public const string Membership = "mid";
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public TimeSpan RefreshLifetime => TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public (string Token, int ExpiresIn) CreateAccessToken(User user, Guid organizationId, Guid membershipId)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(o.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(DentalClaims.Organization, organizationId.ToString()),
                new Claim(DentalClaims.Membership, membershipId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256),
        };
        return (_handler.CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));
}

/// <summary>Пароли — PasswordHasher из ASP.NET Core Identity (без полной Identity-схемы).</summary>
public sealed class PasswordHashing : IPasswordHashing
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
