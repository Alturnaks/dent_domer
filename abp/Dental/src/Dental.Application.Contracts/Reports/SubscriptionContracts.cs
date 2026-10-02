using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
namespace Dental.Reports;
public class SubscriptionInput { [Required] public string Code { get; set; } = "revenue"; public Guid? BranchId { get; set; } public TimeOnly LocalTime { get; set; } = new(21,0); public string Channel { get; set; } = "internal"; public string Frequency { get; set; } = "daily"; public int Weekday { get; set; } = 1; public string GroupBy { get; set; } = "default"; }
public record SubscriptionDto(Guid Id,string Code,Guid? BranchId,TimeOnly LocalTime,string Channel,bool Enabled,DateTime? LastSentAt,string? LastError,string Frequency,int Weekday,string GroupBy);
public interface IReportSubscriptionAppService : IApplicationService { Task<List<SubscriptionDto>> GetListAsync(); Task<SubscriptionDto> CreateAsync(SubscriptionInput input); Task SetEnabledAsync(Guid id,bool enabled); Task DeleteAsync(Guid id); }
