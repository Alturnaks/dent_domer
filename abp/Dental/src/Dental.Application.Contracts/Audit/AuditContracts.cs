using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
namespace Dental.Audit;
public class AuditInput { public Guid? BranchId { get; set; } public DateOnly From { get; set; } public DateOnly To { get; set; } public int SkipCount { get; set; } public int MaxResultCount { get; set; } = 50; }
public record SuspiciousDto(Guid Id, DateTime Date, string Title, string? Reason, string? EntityType, Guid? EntityId);
public interface IAuditAppService : IApplicationService { Task<AuditPage> GetAsync(AuditInput input); Task<List<SuspiciousDto>> GetSuspiciousAsync(AuditInput input); }
