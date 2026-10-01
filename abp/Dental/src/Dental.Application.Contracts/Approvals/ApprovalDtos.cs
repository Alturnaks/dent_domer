using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Approvals;

public class ApprovalDto : EntityDto<Guid>
{
    public string Type { get; set; } = "";
    public string TypeTitle { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public decimal Amount { get; set; }
    public string? Summary { get; set; }
    /// <summary>JSON с контекстом запроса.</summary>
    public string Payload { get; set; } = "{}";
    public Guid RequestedBy { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public ApprovalStatus Status { get; set; }
    public Guid? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
    public bool CanDecide { get; set; }
    /// <summary>Право, необходимое для решения (подсказка «кто может подтвердить»).</summary>
    public string RequiredPermission { get; set; } = "";
    /// <summary>Роли, у которых есть это право.</summary>
    public string DecidersHint { get; set; } = "";
}

public class GetApprovalListInput : PagedResultRequestDto
{
    public ApprovalStatus? Status { get; set; }
    public string? Type { get; set; }
}

public class ApprovalDecisionDto
{
    [StringLength(ApprovalConsts.MaxCommentLength)]
    public string? Comment { get; set; }
}

public interface IApprovalAppService : IApplicationService
{
    Task<PagedResultDto<ApprovalDto>> GetListAsync(GetApprovalListInput input);
    Task<int> GetPendingCountAsync();
    Task<ApprovalDto> ApproveAsync(Guid id, ApprovalDecisionDto input);
    Task<ApprovalDto> RejectAsync(Guid id, ApprovalDecisionDto input);
}
