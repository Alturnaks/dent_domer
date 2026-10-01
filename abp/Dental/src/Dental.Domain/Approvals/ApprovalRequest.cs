using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Approvals;

/// <summary>
/// Запрос на подтверждение действия сверх лимита роли (скидка, списание, возврат, правка закрытого визита, заказ поставщику…).
/// Сущность модуля уже переведена в статус «ожидает подтверждения»; решение исполняет IApprovalHandler модуля.
/// RequestedBy = CreatorId (IdentityUser.Id).
/// </summary>
[Audited]
public class ApprovalRequest : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Type { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    /// <summary>JSON с контекстом (было/стало, суммы).</summary>
    public string Payload { get; private set; } = "{}";
    /// <summary>Сумма (тиыны) или процент, по которой проверяется лимит подтверждающего.</summary>
    public decimal Amount { get; private set; }
    public string? Summary { get; private set; }
    public Guid RequestedBy { get; private set; }
    public ApprovalStatus Status { get; private set; } = ApprovalStatus.Pending;
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? Comment { get; private set; }

    protected ApprovalRequest() { }

    public ApprovalRequest(Guid id, Guid? tenantId, string type, string entityType, Guid entityId, Guid? branchId, Guid requestedBy) : base(id)
    {
        TenantId = tenantId;
        Type = Check.NotNullOrWhiteSpace(type, nameof(type), ApprovalConsts.MaxTypeLength);
        EntityType = Check.NotNullOrWhiteSpace(entityType, nameof(entityType), ApprovalConsts.MaxEntityTypeLength);
        EntityId = entityId;
        BranchId = branchId;
        RequestedBy = requestedBy;
    }

    /// <summary>Обновить содержимое ожидающего запроса (повторная попытка того же действия).</summary>
    public void SetDetails(decimal amount, string? summary, string payload)
    {
        Amount = amount;
        Summary = summary is { Length: > ApprovalConsts.MaxSummaryLength } ? summary[..ApprovalConsts.MaxSummaryLength] : summary;
        Payload = string.IsNullOrWhiteSpace(payload) ? "{}" : payload;
    }

    public void Decide(bool approve, Guid decidedBy, DateTime at, string? comment)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new BusinessException(DentalDomainErrorCodes.ApprovalNotPending);
        }
        Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = at;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Check.Length(comment.Trim(), nameof(comment), ApprovalConsts.MaxCommentLength);
    }
}
