using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dental.Application.Common;
using Dental.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dental.Api.Infrastructure;

/// <summary>
/// Идемпотентность POST (платежи, проведение документов): заголовок Idempotency-Key.
/// Повтор с тем же ключом и телом → сохранённый ответ; с другим телом → 422 IDEMPOTENCY_KEY_REUSED.
/// </summary>
public sealed class IdempotencyFilter(bool required) : IEndpointFilter
{
    public const string Header = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return required
                ? ErrorResponses.Result(400, ErrorCodes.IdempotencyKeyRequired, "Нужен заголовок Idempotency-Key")
                : await next(context);
        }
        if (key.Length > 200) return ErrorResponses.Result(400, ErrorCodes.ValidationFailed, "Idempotency-Key слишком длинный");

        var services = http.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var tenant = services.GetRequiredService<ITenantContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var orgId = tenant.RequiredOrganizationId;
        var hash = Hash(http.Request.Method + " " + http.Request.Path + " " + JsonSerializer.Serialize(context.Arguments.Where(a => a is not null && a.GetType().IsClass && a is not HttpContext && a is not CancellationTokenSource && a.GetType().Namespace?.StartsWith("Dental", StringComparison.Ordinal) == true), ErrorResponses.Json));
        var fullKey = key;

        var existing = await db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == fullKey, http.RequestAborted);
        var now = clock.GetUtcNow();
        if (existing is not null && existing.ExpiresAt > now)
        {
            if (existing.RequestHash != hash)
                return ErrorResponses.Result(422, ErrorCodes.IdempotencyKeyReused, "Ключ идемпотентности уже использован для другого запроса");
            if (existing.StatusCode == 0)
                return ErrorResponses.Result(409, ErrorCodes.ConcurrencyConflict, "Запрос с этим ключом ещё выполняется");
            http.Response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(existing.ResponseBody ?? "null", "application/json", Encoding.UTF8, existing.StatusCode);
        }

        if (existing is not null)
        {
            await db.IdempotencyKeys.Where(k => k.Key == fullKey).ExecuteDeleteAsync(http.RequestAborted);
        }

        // Резервируем ключ (status 0 = выполняется). Конкурентный дубль упадёт на PK.
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (organization_id, key, request_hash, status_code, response_body, created_at, expires_at)
            VALUES ({orgId}, {fullKey}, {hash}, 0, NULL, {now}, {now + Ttl})
            ON CONFLICT DO NOTHING
            """, http.RequestAborted);
        if (inserted == 0) return ErrorResponses.Result(409, ErrorCodes.ConcurrencyConflict, "Запрос с этим ключом ещё выполняется");

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await db.IdempotencyKeys.Where(k => k.Key == fullKey).ExecuteDeleteAsync(CancellationToken.None);
            throw;
        }

        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        if (status >= 500 || status == 409)
        {
            await db.IdempotencyKeys.Where(k => k.Key == fullKey).ExecuteDeleteAsync(CancellationToken.None);
            return result;
        }
        var value = (result as IValueHttpResult)?.Value;
        var body = JsonSerializer.Serialize(value, ErrorResponses.Json);
        await db.IdempotencyKeys.Where(k => k.Key == fullKey)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.StatusCode, status).SetProperty(k => k.ResponseBody, body), CancellationToken.None);
        return result;
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}

public static class IdempotencyExtensions
{
    public static RouteHandlerBuilder Idempotent(this RouteHandlerBuilder builder, bool required = true) =>
        builder.AddEndpointFilter(new IdempotencyFilter(required))
            .WithMetadata(new IdempotentMarker());
}

public sealed class IdempotentMarker;

