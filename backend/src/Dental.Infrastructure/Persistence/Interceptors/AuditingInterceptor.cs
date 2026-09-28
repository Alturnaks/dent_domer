using System.Reflection;
using System.Text.Json;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dental.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Перед сохранением: проставляет organization_id и запрещает его менять, заполняет created/updated,
/// инкрементирует version, собирает diff для сущностей с [Audited] и пишет их в audit_log той же транзакцией.
/// </summary>
public sealed class AuditingInterceptor(ITenantContext tenant, IAuditActor actor, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> SkipProps =
        [nameof(IHasTimestamps.CreatedAt), nameof(IHasTimestamps.UpdatedAt), nameof(IHasTimestamps.CreatedBy), nameof(IHasTimestamps.UpdatedBy), nameof(IVersioned.Version)];

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext context)
    {
        var now = clock.GetUtcNow();
        var userId = actor.UserId;
        var audits = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;
            var entity = entry.Entity;

            if (entity is ITenantEntity tenantEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (tenantEntity.OrganizationId == Guid.Empty)
                    {
                        tenantEntity.OrganizationId = tenant.OrganizationId
                            ?? throw new InvalidOperationException($"Tenant is not set while inserting {entity.GetType().Name}");
                    }
                    else if (tenant.OrganizationId is { } org && tenantEntity.OrganizationId != org)
                    {
                        throw new InvalidOperationException("Cannot insert entity for another organization");
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    var orgProp = entry.Property(nameof(ITenantEntity.OrganizationId));
                    if (orgProp.IsModified && !Equals(orgProp.OriginalValue, orgProp.CurrentValue))
                        throw new InvalidOperationException("organization_id cannot be changed");
                }
            }

            if (entity is IHasTimestamps ts)
            {
                if (entry.State == EntityState.Added)
                {
                    if (ts.CreatedAt == default) ts.CreatedAt = now;
                    ts.UpdatedAt = ts.CreatedAt;
                    ts.CreatedBy ??= userId;
                    ts.UpdatedBy ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    ts.UpdatedAt = now;
                    ts.UpdatedBy = userId ?? ts.UpdatedBy;
                }
            }

            if (entity is IVersioned v && entry.State == EntityState.Modified)
            {
                v.Version++;
            }

            if (entry.State == EntityState.Deleted && entity is ISoftDeletable)
            {
                // Справочники не удаляются физически.
                entry.State = EntityState.Modified;
                ((ISoftDeletable)entity).DeletedAt = now;
            }

            if (entity.GetType().GetCustomAttribute<AuditedAttribute>() is not null && entity is Entity e)
            {
                var diff = BuildDiff(entry);
                if (diff.Count == 0 && entry.State == EntityState.Modified) continue;
                var orgId = (entity as ITenantEntity)?.OrganizationId ?? tenant.OrganizationId ?? (entity is Domain.Organizations.Organization o ? o.Id : Guid.Empty);
                if (orgId == Guid.Empty) continue;
                audits.Add(new AuditLog
                {
                    OrganizationId = orgId,
                    UserId = userId,
                    BranchId = TryGetBranch(entity),
                    EntityType = entity.GetType().Name,
                    EntityId = e.Id,
                    Action = entry.State switch
                    {
                        EntityState.Added => "create",
                        EntityState.Deleted => "delete",
                        _ => (entity is ISoftDeletable sd && sd.DeletedAt is not null && entry.Property(nameof(ISoftDeletable.DeletedAt)).IsModified) ? "delete" : "update",
                    },
                    Diff = JsonSerializer.Serialize(diff, JsonOptions),
                    Reason = actor.Reason,
                    Ip = actor.Ip,
                    UserAgent = actor.UserAgent,
                    CreatedAt = now,
                });
            }
        }

        if (audits.Count > 0) context.Set<AuditLog>().AddRange(audits);
    }

    private static Guid? TryGetBranch(object entity)
    {
        var prop = entity.GetType().GetProperty("BranchId");
        return prop?.GetValue(entity) as Guid?;
    }

    private static Dictionary<string, object?> BuildDiff(EntityEntry entry)
    {
        var diff = new Dictionary<string, object?>();
        var type = entry.Entity.GetType();
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (SkipProps.Contains(name) || name == nameof(ITenantEntity.OrganizationId) || name == nameof(Entity.Id)) continue;
            var sensitive = type.GetProperty(name)?.GetCustomAttribute<SensitiveAttribute>() is not null;
            object? Mask(object? val) => sensitive && val is not null ? "***" : val;

            switch (entry.State)
            {
                case EntityState.Added:
                    if (prop.CurrentValue is not null) diff[Camel(name)] = new { @new = Mask(prop.CurrentValue) };
                    break;
                case EntityState.Modified when prop.IsModified && !ValueEquals(prop.OriginalValue, prop.CurrentValue):
                    diff[Camel(name)] = new { old = Mask(prop.OriginalValue), @new = Mask(prop.CurrentValue) };
                    break;
            }
        }

        // Изменения во владеемых JSON-объектах (настройки, лимиты, часы работы).
        foreach (var nav in entry.References.Where(r => r.TargetEntry is not null && r.Metadata.TargetEntityType.IsOwned()))
        {
            var target = nav.TargetEntry!;
            if (target.State is EntityState.Modified or EntityState.Added)
            {
                var changed = target.Properties.Where(p => target.State == EntityState.Added || (p.IsModified && !ValueEquals(p.OriginalValue, p.CurrentValue)))
                    .Where(p => !p.Metadata.IsShadowProperty())
                    .ToDictionary(p => Camel(p.Metadata.Name), p => (object?)new { old = target.State == EntityState.Added ? null : p.OriginalValue, @new = p.CurrentValue });
                if (changed.Count > 0) diff[Camel(nav.Metadata.Name)] = changed;
            }
        }

        return diff;
    }

    private static bool ValueEquals(object? a, object? b)
    {
        if (a is System.Collections.IEnumerable ea && a is not string && b is System.Collections.IEnumerable eb && b is not string)
            return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
        return Equals(a, b);
    }

    private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

/// <summary>Кто выполняет изменение (пользователь из запроса, фоновая задача, seed).</summary>
public interface IAuditActor
{
    Guid? UserId { get; }
    string? Ip { get; }
    string? UserAgent { get; }
    /// <summary>Причина текущей операции (обязательный комментарий), попадает во все записи аудита.</summary>
    string? Reason { get; set; }
}
