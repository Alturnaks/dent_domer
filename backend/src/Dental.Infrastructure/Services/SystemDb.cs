using Dapper;
using Dental.Application.Auth;
using Dental.Application.Common;
using Dental.Domain.Organizations;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dental.Infrastructure.Services;

/// <summary>Выборки в обход RLS через роль-владельца. Только то, что нужно до выбора организации.</summary>
public sealed class SystemDb(IOptions<DatabaseOptions> options) : ISystemDb
{
    private NpgsqlConnection Open() => new(options.Value.OwnerConnectionString);

    public async Task<IReadOnlyList<UserOrganization>> GetUserOrganizationsAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<(Guid OrgId, string Slug, string Name, Guid MembershipId, string RoleCode, bool IsActive)>(
            new CommandDefinition("""
                SELECT o.id, o.slug, o.name, m.id, r.code, m.is_active
                FROM memberships m
                JOIN organizations o ON o.id = m.organization_id
                JOIN roles r ON r.id = m.role_id
                WHERE m.user_id = @userId
                ORDER BY o.name
                """, new { userId }, cancellationToken: ct));
        return rows.Select(r => new UserOrganization(r.OrgId, r.Slug, r.Name, r.MembershipId, r.RoleCode, r.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, string Timezone)>> ListOrganizationsAsync(CancellationToken ct)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<(Guid, string)>(new CommandDefinition(
            "SELECT id, timezone FROM organizations WHERE type = 'ClinicNetwork' ORDER BY created_at", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<MembershipSnapshot?> LoadMembershipAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        await using var conn = Open();
        var row = await conn.QuerySingleOrDefaultAsync<MembershipRow>(new CommandDefinition("""
            SELECT m.id AS MembershipId, m.role_id AS RoleId, r.code AS RoleCode, r.name AS RoleName,
                   u.full_name AS FullName, u.email AS Email, r.permissions AS Permissions, r.limits::text AS LimitsJson,
                   m.all_branches AS AllBranches, m.branch_ids AS BranchIds,
                   (m.is_active AND u.is_active AND m.fired_at IS NULL AND r.deleted_at IS NULL) AS IsActive,
                   m.position AS Position
            FROM memberships m
            JOIN users u ON u.id = m.user_id
            JOIN roles r ON r.id = m.role_id
            WHERE m.user_id = @userId AND m.organization_id = @organizationId
            """, new { userId, organizationId }, cancellationToken: ct));
        if (row is null) return null;
        var limits = string.IsNullOrEmpty(row.LimitsJson)
            ? new RoleLimits()
            : System.Text.Json.JsonSerializer.Deserialize<RoleLimits>(row.LimitsJson, JsonOpts) ?? new RoleLimits();
        return new MembershipSnapshot(userId, organizationId, row.MembershipId, row.RoleId, row.RoleCode, row.RoleName, row.FullName, row.Email,
            row.Permissions ?? [], limits, row.AllBranches, row.BranchIds ?? [], row.IsActive,
            Enum.TryParse<StaffPosition>(row.Position, out var pos) ? pos : StaffPosition.Other);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class MembershipRow
    {
        public Guid MembershipId { get; set; }
        public Guid RoleId { get; set; }
        public string RoleCode { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Email { get; set; }
        public string[]? Permissions { get; set; }
        public string? LimitsJson { get; set; }
        public bool AllBranches { get; set; }
        public Guid[]? BranchIds { get; set; }
        public bool IsActive { get; set; }
        public string Position { get; set; } = "";
    }
}
