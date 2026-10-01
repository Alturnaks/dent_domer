using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dental.Payroll;
public record PayrollEmployeeDto(Guid Id, string Name);
public record PayrollSchemeDto(Guid Id, Guid EmployeeId, string EmployeeName, PayrollSchemeType Type, decimal Percent, long FixedAmount, long ShiftRate, DateOnly ValidFrom);
public class PayrollSchemeInput
{
    public Guid EmployeeId { get; set; }
    public PayrollSchemeType Type { get; set; }
    [Range(0,100)] public decimal Percent { get; set; }
    [Range(0,long.MaxValue)] public long FixedAmount { get; set; }
    [Range(0,long.MaxValue)] public long ShiftRate { get; set; }
    public DateOnly ValidFrom { get; set; }
}
public class PayrollPeriodInput { public Guid BranchId { get; set; } public DateOnly Start { get; set; } public DateOnly End { get; set; } }
public class PayrollStampInput { [Required] public string ConcurrencyStamp { get; set; } = ""; }
public class PayrollAdjustmentInput : PayrollStampInput { public long Bonus { get; set; } public long Penalty { get; set; } [Required, StringLength(2000)] public string Comment { get; set; } = ""; }
public record PayrollPeriodDto(Guid Id, Guid BranchId, DateOnly Start, DateOnly End, PayrollPeriodStatus Status, string ConcurrencyStamp, long Total, DateTime? ApprovedAt, DateTime? PaidAt);
public record PayrollVisitDetail(Guid VisitId, DateOnly Date, long Revenue, long MaterialsCost, bool Paid);
public record PayrollEntryDto(Guid Id, Guid EmployeeId, string EmployeeName, long BaseRevenue, long MaterialsCost, long Accrued, long Bonus, long Penalty, long Total, string? Comment, string ConcurrencyStamp, List<PayrollVisitDetail> Details);
public record PayrollDetailDto(PayrollPeriodDto Period, List<PayrollEntryDto> Entries);
public interface IPayrollAppService : IApplicationService
{
    Task<List<PayrollEmployeeDto>> GetEmployeesAsync(Guid branchId);
    Task<List<PayrollSchemeDto>> GetSchemesAsync(Guid branchId);
    Task<PayrollSchemeDto> SaveSchemeAsync(PayrollSchemeInput input);
    Task<List<PayrollPeriodDto>> GetPeriodsAsync(Guid branchId);
    Task<PayrollDetailDto> GetAsync(Guid id);
    Task<PayrollDetailDto> CreateAsync(PayrollPeriodInput input);
    Task<PayrollDetailDto> RecalculateAsync(Guid id, PayrollStampInput input);
    Task<PayrollDetailDto> ApproveAsync(Guid id, PayrollStampInput input);
    Task<PayrollDetailDto> MarkPaidAsync(Guid id, PayrollStampInput input);
    Task<PayrollDetailDto> AdjustAsync(Guid id, PayrollAdjustmentInput input);
}
