using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Catalog;
using Dental.Inventory;
using Dental.Patients;
using Dental.Permissions;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Dental.Finance;

[Authorize]
public class VisitAppService(FinanceManager manager, IRepository<Visit, Guid> visits, IRepository<Patient, Guid> patients, IRepository<Employee, Guid> employees, IRepository<ClinicService, Guid> services, IRepository<Item, Guid> materials, IRepository<PatientBalance, Guid> balances) : DentalAppService, IVisitAppService
{
    private static List<VisitLineData> Lines(EditVisitInput input) => input.Items.Select(i => new VisitLineData(i.ServiceId, i.Qty, i.DiscountPct, i.DoctorId, i.UnitPrice, i.ToothNumbers)).ToList();
    private static List<MaterialData>? Materials(List<MaterialInput>? input) => input?.Select(m => new MaterialData(m.ItemId, m.Quantity, m.VisitItemId)).ToList();
    public async Task<VisitDto> GetAsync(Guid id) => await MapAsync(await manager.GetVisitAsync(id));
    public async Task<VisitDto?> GetByAppointmentAsync(Guid appointmentId)
    { var v = await visits.FirstOrDefaultAsync(v => v.AppointmentId == appointmentId && v.Status != VisitStatus.Cancelled); return v == null ? null : await GetAsync(v.Id); }
    private async Task CanEditAsync()
    { if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Visits.EditOpen) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Visits.EditClosed) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Visits.Complete)) throw new AbpAuthorizationException(); }
    public async Task<List<VisitServiceLookupDto>> GetServicesAsync(Guid branchId)
    { await CanEditAsync(); await BranchScope.EnsureCanAccessAsync(branchId); var rows = await services.GetListAsync(s => s.IsActive); var price = await LazyServiceProvider.LazyGetRequiredService<IPriceResolver>().ResolveAsync(branchId, rows.Select(s => s.Id).ToList()); return rows.Where(s => price.ContainsKey(s.Id)).Select(s => new VisitServiceLookupDto(s.Id, s.Name, price[s.Id])).ToList(); }
    public async Task<List<VisitMaterialLookupDto>> GetMaterialsAsync()
    { await CanEditAsync(); return (await materials.GetListAsync(i => i.IsActive)).OrderBy(i => i.Name).Select(i => new VisitMaterialLookupDto(i.Id, i.Name)).ToList(); }
    public async Task<List<VisitStaffLookupDto>> GetStaffAsync(Guid branchId)
    { await CanEditAsync(); await BranchScope.EnsureCanAccessAsync(branchId); return (await employees.GetListAsync(e => e.IsActive && (e.AllBranches || e.BranchIds.Contains(branchId)) && (e.Position == StaffPosition.Doctor || e.Position == StaffPosition.Assistant))).Select(e => new VisitStaffLookupDto(e.Id, e.FullName, (int)e.Position)).ToList(); }
    public async Task<List<VisitDto>> GetListAsync(FinanceListInput input)
    {
        var q = await BranchScope.ApplyAsync(await visits.GetQueryableAsync(), v => v.BranchId);
        if (input.BranchId != null) q = q.Where(v => v.BranchId == input.BranchId);
        if (input.PatientId != null) q = q.Where(v => v.PatientId == input.PatientId);
        if (input.From != null) q = q.Where(v => v.OpenedAt >= input.From);
        if (input.To != null) q = q.Where(v => v.OpenedAt < input.To);
        if (!await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.Manage) && !await AuthorizationService.IsGrantedAsync(DentalPermissions.Cash.PaymentCreate))
        { var own = (await BranchScope.GetCurrentEmployeeAsync())?.Id; if (own == null) throw new AbpAuthorizationException(); q = q.Where(v => v.DoctorId == own); }
        var ids = await AsyncExecuter.ToListAsync(q.OrderByDescending(v => v.OpenedAt).Take(Math.Clamp(input.MaxResultCount, 1, 200)).Select(v => v.Id));
        var result = new List<VisitDto>(); foreach (var id in ids) result.Add(await GetAsync(id)); return result;
    }
    [Authorize(DentalPermissions.Schedule.Manage)]
    public async Task<VisitDto> OpenAsync(Guid appointmentId) => await MapAsync(await manager.OpenFromAppointmentAsync(appointmentId));
    public async Task<VisitDto> UpdateAsync(Guid id, EditVisitInput input) => await MapAsync(await manager.ReplaceOpenItemsAsync(id, input.ConcurrencyStamp, Lines(input), input.AssistantId));
    public async Task<VisitDto> SetMaterialsAsync(Guid id, EditMaterialsInput input) => await MapAsync(await manager.SetMaterialsAsync(id, input.ConcurrencyStamp, Materials(input.Materials)!));
    public async Task<VisitDto> CloseAsync(Guid id, FinanceStampInput input) => await MapAsync(await manager.CloseVisitAsync(id, input.ConcurrencyStamp));
    public async Task<VisitDto> CorrectAsync(Guid id, CorrectVisitInput input) => await MapAsync(await manager.CorrectVisitAsync(id, input.ConcurrencyStamp, Lines(input), Materials(input.Materials), input.Reason));
    public async Task<VisitDto> CancelAsync(Guid id, CancelVisitInput input) => await MapAsync(await manager.CancelVisitAsync(id, input.ConcurrencyStamp, input.Reason));
    private async Task<VisitDto> MapAsync(Visit v)
    {
        var serviceIds = v.ActiveItems.Select(i => i.ServiceId).Distinct().ToList(); var materialIds = v.ActiveMaterials.Select(m => m.ItemId).Distinct().ToList();
        var serviceNames = (await services.GetListAsync(s => serviceIds.Contains(s.Id))).ToDictionary(s => s.Id, s => s.Name);
        var materialNames = (await materials.GetListAsync(s => materialIds.Contains(s.Id))).ToDictionary(s => s.Id, s => s.Name);
        return new VisitDto { Id = v.Id, ConcurrencyStamp = v.ConcurrencyStamp, BranchId = v.BranchId, PatientId = v.PatientId, PatientName = (await patients.GetAsync(v.PatientId)).FullName, DoctorId = v.DoctorId, DoctorName = (await employees.GetAsync(v.DoctorId)).FullName, AppointmentId = v.AppointmentId, AssistantId = v.AssistantId, Status = v.Status, ApprovalState = v.ApprovalState, OpenedAt = v.OpenedAt, ClosedAt = v.ClosedAt, Subtotal = v.Subtotal, DiscountTotal = v.DiscountTotal, Total = v.Total, PaidTotal = v.PaidTotal, Debt = v.Debt, PatientBalance = (await balances.FirstOrDefaultAsync(b => b.PatientId == v.PatientId))?.Balance ?? 0,
            Items = v.ActiveItems.Select(i => new VisitItemDto(i.Id, i.ServiceId, serviceNames.GetValueOrDefault(i.ServiceId, ""), i.DoctorId, i.Qty, i.UnitPrice, i.DiscountPct, i.DiscountAmount, i.Total, i.ToothNumbers)).ToList(),
            Materials = v.ActiveMaterials.Select(m => new VisitMaterialDto(m.Id, m.VisitItemId, m.ItemId, materialNames.GetValueOrDefault(m.ItemId, ""), m.Quantity, m.NormQuantity, m.BatchId, m.Cost)).ToList() };
    }
}

