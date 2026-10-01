using System;
using System.Collections.Generic;
namespace Dental.Audit;
public record AuditRow(Guid Id, DateTime Date, string? User, string? EntityType, string? EntityId, string Action, List<AuditProperty> Changes);
public record AuditProperty(string Name, string? Before, string? After);
public record AuditQuery(Guid TenantId, Guid[] BranchIds, bool Network, DateTime From, DateTime To, int Offset, int Limit);
public record AuditPage(List<AuditRow> Items, long TotalCount);
