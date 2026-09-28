using System.Text.Json;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Organizations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dental.Application.Orgs;

public sealed record ApprovalDto(
    Guid Id, ApprovalType Type, string EntityType, Guid EntityId, Guid? BranchId, decimal Amount, string? Summary, JsonElement Payload,
    Guid RequestedBy, string RequestedByName, DateTimeOffset CreatedAt, ApprovalStatus Status, Guid? DecidedBy, string? DecidedByName,
    DateTimeOffset? DecidedAt, string? Comment, bool CanDecide);
public sealed record ApprovalDecisionRequest(string? Comment);

public sealed class ApprovalDecisionRequestValidator : AbstractValidator<ApprovalDecisionRequest>
{
    public ApprovalDecisionRequestValidator() => RuleFor(x => x.Comment).MaximumLength(1000);
}

/// <summary>Обработчик подтверждённого/отклонённого запроса конкретного типа (реализации — в модулях).</summary>
public interface IApprovalHandler
{
    ApprovalType Type { get; }
    Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct);
    Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct);
}

public sealed class ApprovalService(
    IAppDbContext db,
    ICurrentUser user,
    IServiceProvider services,
    INotificationService notifications,
    TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Создать запрос на подтверждение (сущность уже переведена в статус «ожидает подтверждения»).</summary>
    public async Task<ApprovalRequest> RequestAsync(ApprovalType type, string entityType, Guid entityId, decimal amount, string summary, object payload,
        Guid? branchId, CancellationToken ct)
    {
        var existing = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.EntityId == entityId && a.Type == type && a.Status == ApprovalStatus.Pending, ct);
        if (existing is not null)
        {
            existing.Amount = amount;
            existing.Summary = summary;
            existing.Payload = JsonSerializer.Serialize(payload, Json);
            return existing;
        }
        var request = new ApprovalRequest
        {
            Type = type,
            EntityType = entityType,
            EntityId = entityId,
            BranchId = branchId,
            Amount = amount,
            Summary = summary,
            Payload = JsonSerializer.Serialize(payload, Json),
            RequestedBy = user.UserId,
        };
        db.ApprovalRequests.Add(request);
        await notifications.NotifyByPermissionAsync(PermissionFor(type), "approval", "Нужно подтверждение: " + TypeTitle(type), summary,
            nameof(ApprovalRequest), request.Id, branchId, ct);
        return request;
    }

    public async Task<IReadOnlyList<ApprovalDto>> ListAsync(ApprovalStatus? status, CancellationToken ct)
    {
        var q = db.ApprovalRequests.AsNoTracking();
        if (status is { } s) q = q.Where(a => a.Status == s);
        if (!user.AllBranches) q = q.Where(a => a.BranchId == null || user.BranchIds.Contains(a.BranchId.Value));
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        var userIds = rows.Select(r => r.RequestedBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(r => ToDto(r, names)).ToList();
    }

    public async Task<ApprovalDto> ApproveAsync(Guid id, ApprovalDecisionRequest r, CancellationToken ct) => await DecideAsync(id, true, r.Comment, ct);

    public async Task<ApprovalDto> RejectAsync(Guid id, ApprovalDecisionRequest r, CancellationToken ct) => await DecideAsync(id, false, r.Comment, ct);

    private async Task<ApprovalDto> DecideAsync(Guid id, bool approve, string? comment, CancellationToken ct)
    {
        var a = await db.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Запрос");
        if (a.Status != ApprovalStatus.Pending) throw AppException.Conflict(ErrorCodes.ApprovalNotPending, "Запрос уже обработан");
        if (a.BranchId is { } b) user.EnsureBranchAccess(b);
        if (a.RequestedBy == user.UserId && user.RoleCode != RolePresets.Owner)
            throw AppException.Forbidden("Нельзя подтвердить собственный запрос");
        EnsureCanDecide(a);

        a.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        a.DecidedBy = user.UserId;
        a.DecidedAt = clock.GetUtcNow();
        a.Comment = comment.NullIfEmpty();

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Обработчики берутся лениво: они сами зависят от сервисов модулей, которые используют ApprovalService.
        var handler = services.GetServices<IApprovalHandler>().FirstOrDefault(h => h.Type == a.Type);
        if (handler is not null)
        {
            if (approve) await handler.OnApprovedAsync(a, ct);
            else await handler.OnRejectedAsync(a, ct);
        }
        notifications.Notify(a.RequestedBy, "approval_decision",
            (approve ? "Подтверждено: " : "Отклонено: ") + TypeTitle(a.Type), a.Comment ?? a.Summary, nameof(ApprovalRequest), a.Id);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        var names = await db.Users.AsNoTracking().Where(u => u.Id == a.RequestedBy || u.Id == user.UserId).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return ToDto(a, names);
    }

    /// <summary>Подтверждать может пользователь с правом и лимитом, достаточным для этого действия.</summary>
    private void EnsureCanDecide(ApprovalRequest a)
    {
        if (!CanDecide(a)) throw new AppException(ErrorCodes.ApprovalLimitExceeded, "Ваших прав или лимита недостаточно для подтверждения", 403);
    }

    public bool CanDecide(ApprovalRequest a)
    {
        if (!user.Has(PermissionFor(a.Type))) return false;
        var l = user.Limits;
        return a.Type switch
        {
            ApprovalType.Discount => l.AllowsDiscount(a.Amount),
            ApprovalType.Writeoff or ApprovalType.TransferShortage => l.AllowsWriteoff((long)a.Amount),
            ApprovalType.Refund => l.AllowsRefund((long)a.Amount),
            ApprovalType.ClosedVisitEdit => l.CanEditClosedShiftVisits,
            _ => true,
        };
    }

    public static string PermissionFor(ApprovalType type) => type switch
    {
        ApprovalType.Discount => Perm.Catalog.DiscountsApply,
        ApprovalType.Writeoff or ApprovalType.TransferShortage => Perm.Inventory.Writeoff,
        ApprovalType.Refund => Perm.Cash.PaymentRefund,
        ApprovalType.ClosedVisitEdit => Perm.Visits.EditClosed,
        ApprovalType.PurchaseOrder => Perm.Purchase.OrderApprove,
        ApprovalType.PayrollPeriodChange => Perm.Payroll.Manage,
        _ => Perm.Org.SettingsManage,
    };

    public static string TypeTitle(ApprovalType type) => type switch
    {
        ApprovalType.Discount => "скидка выше лимита",
        ApprovalType.Writeoff => "списание выше лимита",
        ApprovalType.TransferShortage => "недостача при приёмке перемещения",
        ApprovalType.Refund => "возврат выше лимита",
        ApprovalType.ClosedVisitEdit => "изменение закрытого визита",
        ApprovalType.PurchaseOrder => "заказ поставщику",
        ApprovalType.PayrollPeriodChange => "изменение визита в утверждённом периоде зарплаты",
        _ => type.ToString(),
    };

    private ApprovalDto ToDto(ApprovalRequest a, Dictionary<Guid, string> names)
    {
        JsonElement payload;
        try { payload = JsonDocument.Parse(string.IsNullOrWhiteSpace(a.Payload) ? "{}" : a.Payload).RootElement.Clone(); }
        catch (JsonException) { payload = JsonDocument.Parse("{}").RootElement.Clone(); }
        return new ApprovalDto(a.Id, a.Type, a.EntityType, a.EntityId, a.BranchId, a.Amount, a.Summary, payload, a.RequestedBy,
            names.GetValueOrDefault(a.RequestedBy, "?"), a.CreatedAt, a.Status, a.DecidedBy,
            a.DecidedBy is { } d ? names.GetValueOrDefault(d) : null, a.DecidedAt, a.Comment,
            a.Status == ApprovalStatus.Pending && CanDecide(a) && (a.RequestedBy != user.UserId || user.RoleCode == RolePresets.Owner));
    }
}
