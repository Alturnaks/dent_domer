using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;
namespace Dental.Reports;
public class ReportInput { public string Code { get; set; } = "revenue"; public Guid? BranchId { get; set; } public Guid? DoctorId { get; set; } public DateOnly From { get; set; } public DateOnly To { get; set; } public int InactiveMonths { get; set; } = 6; public string Format { get; set; } = "xlsx"; }
public interface IReportsAppService : IApplicationService
{
    Task<List<ReportDefinition>> GetCatalogAsync();
    Task<ReportTable> GetAsync(ReportInput input);
    Task<IRemoteStreamContent> ExportAsync(ReportInput input);
}
