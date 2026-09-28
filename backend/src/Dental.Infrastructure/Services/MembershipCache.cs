using System.Text.Json;
using Dental.Application.Common;
using Microsoft.Extensions.Caching.Distributed;

namespace Dental.Infrastructure.Services;

/// <summary>Права и лимиты членства: Redis (или in-memory) на 5 минут, сброс при изменении роли/сотрудника.</summary>
public sealed class MembershipCache(IDistributedCache cache, SystemDb systemDb) : IMembershipCache
{
    private static readonly DistributedCacheEntryOptions Ttl = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Key(Guid userId, Guid orgId) => $"membership:{orgId}:{userId}";
    private static string RoleIndexKey(Guid orgId, Guid roleId) => $"membership-role:{orgId}:{roleId}";

    public async Task<MembershipSnapshot?> GetAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        var key = Key(userId, organizationId);
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) return JsonSerializer.Deserialize<MembershipSnapshot>(cached, Json);

        var snapshot = await systemDb.LoadMembershipAsync(userId, organizationId, ct);
        if (snapshot is null) return null;
        await cache.SetStringAsync(key, JsonSerializer.Serialize(snapshot, Json), Ttl, ct);

        // Индекс «роль → пользователи» для сброса кэша при изменении роли.
        var idxKey = RoleIndexKey(organizationId, snapshot.RoleId);
        var idx = await cache.GetStringAsync(idxKey, ct);
        var users = idx is null ? [] : JsonSerializer.Deserialize<HashSet<Guid>>(idx, Json) ?? [];
        if (users.Add(userId))
            await cache.SetStringAsync(idxKey, JsonSerializer.Serialize(users, Json), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
        return snapshot;
    }

    public Task InvalidateAsync(Guid userId, Guid organizationId, CancellationToken ct) =>
        cache.RemoveAsync(Key(userId, organizationId), ct);

    public async Task InvalidateRoleAsync(Guid organizationId, Guid roleId, CancellationToken ct)
    {
        var idxKey = RoleIndexKey(organizationId, roleId);
        var idx = await cache.GetStringAsync(idxKey, ct);
        if (idx is null) return;
        foreach (var userId in JsonSerializer.Deserialize<HashSet<Guid>>(idx, Json) ?? [])
            await cache.RemoveAsync(Key(userId, organizationId), ct);
        await cache.RemoveAsync(idxKey, ct);
    }
}
