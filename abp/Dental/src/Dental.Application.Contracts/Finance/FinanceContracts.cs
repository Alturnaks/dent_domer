using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dental.Finance;

public class VisitDto
{
    public Guid Id { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public Guid DoctorId { get; set; }
    public string DoctorName { get; set; } = "";
    public Guid? AppointmentId { get; set; }
    public Guid? AssistantId { get; set; }
    public VisitStatus Status { get; set; }
    public VisitApprovalState ApprovalState { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long Subtotal { get; set; }
    public long DiscountTotal { get; set; }
    public long Total { get; set; }
    public long PaidTotal { get; set; }
    public long Debt { get; set; }
    public long PatientBalance { get; set; }
    public List<VisitItemDto> Items { get; set; } = [];
    public List<VisitMaterialDto> Materials { get; set; } = [];
}
public record VisitItemDto(Guid Id, Guid ServiceId, string ServiceName, Guid DoctorId, int Qty, long UnitPrice, decimal DiscountPct, long DiscountAmount, long Total, List<int> ToothNumbers);
public record VisitMaterialDto(Guid Id, Guid? VisitItemId, Guid ItemId, string ItemName, decimal Quantity, decimal NormQuantity, Guid? BatchId, long Cost);
public class FinanceStampInput { [Required] public string ConcurrencyStamp { get; set; } = ""; }
public class VisitLineInput
{
    public Guid ServiceId { get; set; }
    [Range(1, 100)] public int Qty { get; set; } = 1;
    [Range(0, 100)] public decimal DiscountPct { get; set; }
    public Guid? DoctorId { get; set; }
    public long? UnitPrice { get; set; }
    public List<int>? ToothNumbers { get; set; }
}
public class MaterialInput { public Guid ItemId { get; set; } public decimal Quantity { get; set; } public Guid? VisitItemId { get; set; } }
public class EditVisitInput : FinanceStampInput { public List<VisitLineInput> Items { get; set; } = []; public Guid? AssistantId { get; set; } }
public class EditMaterialsInput : FinanceStampInput { public List<MaterialInput> Materials { get; set; } = []; }
public class CorrectVisitInput : EditVisitInput { public List<MaterialInput>? Materials { get; set; } [Required, StringLength(2000)] public string Reason { get; set; } = ""; }
public class CancelVisitInput : FinanceStampInput { [Required, StringLength(2000)] public string Reason { get; set; } = ""; }
public class FinanceListInput { public Guid? BranchId { get; set; } public Guid? PatientId { get; set; } public Guid? ShiftId { get; set; } public DateTime? From { get; set; } public DateTime? To { get; set; } public int MaxResultCount { get; set; } = 100; }
public interface IVisitAppService : IApplicationService
{
    Task<VisitDto> GetAsync(Guid id);
    Task<VisitDto?> GetByAppointmentAsync(Guid appointmentId);
    Task<List<VisitServiceLookupDto>> GetServicesAsync(Guid branchId);
    Task<List<VisitMaterialLookupDto>> GetMaterialsAsync();
    Task<List<VisitStaffLookupDto>> GetStaffAsync(Guid branchId);
    Task<List<VisitDto>> GetListAsync(FinanceListInput input);
    Task<VisitDto> OpenAsync(Guid appointmentId);
    Task<VisitDto> UpdateAsync(Guid id, EditVisitInput input);
    Task<VisitDto> SetMaterialsAsync(Guid id, EditMaterialsInput input);
    Task<VisitDto> CloseAsync(Guid id, FinanceStampInput input);
    Task<VisitDto> CorrectAsync(Guid id, CorrectVisitInput input);
    Task<VisitDto> CancelAsync(Guid id, CancelVisitInput input);
}
public record VisitServiceLookupDto(Guid Id, string Name, long Price);
public record VisitMaterialLookupDto(Guid Id, string Name);
public record VisitStaffLookupDto(Guid Id, string Name, int Position);
public record CashRegisterDto(Guid Id, Guid BranchId, string Name);
public class CashShiftDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid CashRegisterId { get; set; }
    public string RegisterName { get; set; } = "";
    public string ConcurrencyStamp { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public CashShiftStatus Status { get; set; }
    public long OpeningBalance { get; set; }
    public long ExpectedCash { get; set; }
    public long? ClosingBalanceActual { get; set; }
    public long? Difference { get; set; }
}
public record PaymentDto(Guid Id, Guid BranchId, Guid PatientId, string PatientName, Guid? VisitId, Guid CashShiftId, PaymentMethod Method, long Amount, PaymentType Type, int State, Guid? RefundedPaymentId, string? Comment, DateTime CreationTime);
public record ExpenseCategoryDto(Guid Id, string Name, ExpenseCategoryType Type);
public record ExpenseDto(Guid Id, Guid BranchId, Guid CategoryId, string CategoryName, long Amount, DateTime PaidAt, Guid? CashShiftId, string? Counterparty, string? Comment);
public record CashOperationDto(Guid Id, Guid CashShiftId, CashOperationType Type, long Amount, string Comment, DateTime CreationTime);
public class CreateRegisterInput { public Guid BranchId { get; set; } [Required, StringLength(200)] public string Name { get; set; } = ""; }
public class CreateExpenseCategoryInput { [Required, StringLength(200)] public string Name { get; set; } = ""; public ExpenseCategoryType Type { get; set; } }
public class OpenShiftInput { public Guid CashRegisterId { get; set; } public long OpeningBalance { get; set; } }
public class CloseShiftInput : FinanceStampInput { public long Actual { get; set; } [StringLength(2000)] public string? Comment { get; set; } }
public class PaymentPartInput { public PaymentMethod Method { get; set; } public long Amount { get; set; } }
public class PayInput { public Guid ShiftId { get; set; } public Guid PatientId { get; set; } public Guid? VisitId { get; set; } public List<PaymentPartInput> Parts { get; set; } = []; [Required, StringLength(100)] public string IdempotencyKey { get; set; } = ""; [StringLength(2000)] public string? Comment { get; set; } }
public class RefundInput { public Guid ShiftId { get; set; } public long Amount { get; set; } [Required, StringLength(100)] public string IdempotencyKey { get; set; } = ""; [Required, StringLength(2000)] public string Reason { get; set; } = ""; }
public class ExpenseInput { public Guid BranchId { get; set; } public Guid CategoryId { get; set; } public long Amount { get; set; } public Guid? ShiftId { get; set; } [StringLength(200)] public string? Counterparty { get; set; } [StringLength(2000)] public string? Comment { get; set; } }
public class CashOperationInput { public Guid ShiftId { get; set; } public CashOperationType Type { get; set; } public long Amount { get; set; } [Required, StringLength(2000)] public string Comment { get; set; } = ""; }
public interface ICashAppService : IApplicationService
{
    Task<List<CashRegisterDto>> GetRegistersAsync(Guid branchId);
    Task<CashRegisterDto> CreateRegisterAsync(CreateRegisterInput input);
    Task<List<CashShiftDto>> GetShiftsAsync(Guid branchId);
    Task<CashShiftDto> OpenShiftAsync(OpenShiftInput input);
    Task<CashShiftDto> CloseShiftAsync(Guid id, CloseShiftInput input);
    Task<List<PaymentDto>> GetPaymentsAsync(FinanceListInput input);
    Task<List<PaymentDto>> PayAsync(PayInput input);
    Task<PaymentDto> RefundAsync(Guid id, RefundInput input);
    Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync();
    Task<ExpenseCategoryDto> CreateExpenseCategoryAsync(CreateExpenseCategoryInput input);
    Task<List<ExpenseDto>> GetExpensesAsync(FinanceListInput input);
    Task<ExpenseDto> CreateExpenseAsync(ExpenseInput input);
    Task<List<CashOperationDto>> GetOperationsAsync(Guid shiftId);
    Task<CashOperationDto> CreateOperationAsync(CashOperationInput input);
}
