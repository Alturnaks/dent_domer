using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Dental.Schedule;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
namespace Dental.Reports;
[Authorize]
public class ReportsAppService(IReportStore store, AppointmentManager calendar, IRepository<Branch,Guid> branches) : DentalAppService, IReportsAppService
{
    public async Task<List<ReportDefinition>> GetCatalogAsync()
    { var result = new List<ReportDefinition>(); foreach(var r in ReportCatalog.All) if (await AuthorizationService.IsGrantedAsync(r.Permission) && (r.Code != "pnl" || await AuthorizationService.IsGrantedAsync(DentalPermissions.Reports.Finance))) result.Add(r); return result; }
    public async Task<ReportTable> GetAsync(ReportInput input)
    {
        var definition=ReportCatalog.Find(input.Code); if (definition == null) throw new UserFriendlyException("Отчёт не найден."); await AuthorizationService.CheckAsync(definition.Permission);
        if (input.Code is "pnl" or "daily_summary") await AuthorizationService.CheckAsync(DentalPermissions.Reports.Finance);
        if (CurrentTenant.Id == null) throw new AbpAuthorizationException();
        if (input.To < input.From || input.To.DayNumber-input.From.DayNumber > 730 || input.InactiveMonths is < 1 or > 120) throw new UserFriendlyException("Укажите период до двух лет и срок неактивности от 1 до 120 месяцев.");
        var allowed=await AsyncExecuter.ToListAsync(await BranchScope.ApplyAsync(await branches.GetQueryableAsync(),b => b.Id));
        if (input.BranchId != null) { await branches.GetAsync(input.BranchId.Value); await BranchScope.EnsureCanAccessAsync(input.BranchId.Value); allowed=allowed.Where(b => b.Id == input.BranchId).ToList(); }
        var employee=await BranchScope.GetCurrentEmployeeAsync(); var allDoctors=await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.ViewAll) || await AuthorizationService.IsGrantedAsync(DentalPermissions.Schedule.Manage);
        var doctor=input.DoctorId; if (!allDoctors) { if (doctor != null && doctor != employee?.Id) throw new AbpAuthorizationException(); doctor=employee?.Id ?? Guid.Empty; }
        var timezone=await calendar.GetTimezoneAsync();
        var q=new ReportQuery(input.Code,CurrentTenant.Id.Value,allowed.Select(b => b.Id).ToArray(),doctor,input.BranchId == null && await AuthorizationService.IsGrantedAsync(DentalPermissions.Reports.Network),SlotCalculator.ToUtc(input.From.ToDateTime(TimeOnly.MinValue),timezone),SlotCalculator.ToUtc(input.To.AddDays(1).ToDateTime(TimeOnly.MinValue),timezone),timezone.Id,input.InactiveMonths);
        return await store.ExecuteAsync(q);
    }
    public async Task<IRemoteStreamContent> ExportAsync(ReportInput input)
    {
        var result=await GetAsync(input);
        if (result.Truncated) throw new UserFriendlyException("Для экспорта сузьте период или выберите филиал: результат превышает 5000 строк.");
        if (input.Format is "xlsx" or "pdf")
        {
            var title=ReportCatalog.Find(input.Code)!.Name + " · " + input.From + " — " + input.To;
            var bytes=LazyServiceProvider.LazyGetRequiredService<IReportDocumentRenderer>().Render(result,title,input.Format);
            return new RemoteStreamContent(new MemoryStream(bytes),input.Code+"."+input.Format,input.Format=="pdf"?"application/pdf":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        if (input.Format != "csv") throw new UserFriendlyException("Поддерживаются XLSX, PDF и CSV.");
        var b=new StringBuilder("\uFEFF");
        static string Escape(object? value) { var s=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture) ?? ""; if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
        b.AppendLine(string.Join(";",result.Columns.Select(c => Escape(c.Name)))); foreach(var row in result.Rows) b.AppendLine(string.Join(";",row.Select(Escape)));
        return new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(b.ToString())),input.Code+".csv","text/csv; charset=utf-8");
    }
}
