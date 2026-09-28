using System.Text.Json;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Audit;

public sealed record AuditEntryDto(
    Guid Id, DateTimeOffset CreatedAt, Guid? UserId, string? UserName, Guid? BranchId, string EntityType, Guid? EntityId, string Action,
    JsonElement? Diff, string? Reason, string? Ip, bool IsSuspicious);

public sealed record AuditQuery(string? EntityType, Guid? EntityId, Guid? UserId, bool? Suspicious, DateTimeOffset? From, DateTimeOffset? To, string? Cursor, int Limit = 50);

/// <summary>Журнал действий. Старший админ видит только свои филиалы; владелец — всё.</summary>
public sealed class AuditQueryService(IAppDbContext db, ICurrentUser user)
{
    public async Task<CursorPage<AuditEntryDto>> ListAsync(AuditQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit, 1, 200);
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        if (q.EntityId is { } eid) query = query.Where(a => a.EntityId == eid);
        if (q.UserId is { } uid) query = query.Where(a => a.UserId == uid);
        if (q.Suspicious == true) query = query.Where(a => a.IsSuspicious);
        if (q.From is { } from) query = query.Where(a => a.CreatedAt >= from);
        if (q.To is { } to) query = query.Where(a => a.CreatedAt < to);
        if (!user.AllBranches && user.RoleCode != RolePresets.Owner)
            query = query.Where(a => a.BranchId == null || user.BranchIds.Contains(a.BranchId.Value));
        if (Cursor.Decode(q.Cursor) is { } c)
            query = query.Where(a => a.CreatedAt < c.At || (a.CreatedAt == c.At && a.Id.CompareTo(c.Id) < 0));

        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var userIds = page.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var items = page.Select(a => new AuditEntryDto(a.Id, a.CreatedAt, a.UserId, a.UserId is { } u ? names.GetValueOrDefault(u) : "Система", a.BranchId,
            a.EntityType, a.EntityId, a.Action, Parse(a.Diff), a.Reason, a.Ip, a.IsSuspicious)).ToList();
        return new CursorPage<AuditEntryDto>(items, rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null);
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}
