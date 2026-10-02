using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Notifications;
using Dental.Settings;
using Dental.Staff;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;

namespace Dental.Audit;

/// <summary>ABP security logs are the device history; raw user agent and IP are never sent in notifications.</summary>
public class NewDeviceLoginHandler(IRepository<IdentitySecurityLog,Guid> logs,IRepository<Employee,Guid> employees,IRepository<Branch,Guid> branches,NotificationManager notifications,ICurrentTenant tenant,ISettingProvider settings)
    : ILocalEventHandler<EntityCreatedEventData<IdentitySecurityLog>>,ITransientDependency
{
    public static bool OutsideHours(Branch branch,DateTime local)
    {
        var day=branch.For(local.DayOfWeek);var time=TimeOnly.FromDateTime(local);
        return day==null||!day.IsWorking||time<day.Open||time>=day.Close;
    }
    public async Task HandleEventAsync(EntityCreatedEventData<IdentitySecurityLog> eventData)
    {
        var log=eventData.Entity;
        if(log.Action!="LoginSucceeded"||log.TenantId==null||log.UserId==null||string.IsNullOrWhiteSpace(log.BrowserInfo))return;
        using var current=tenant.Change(log.TenantId);
        if(await logs.AnyAsync(x=>x.Id!=log.Id&&x.UserId==log.UserId&&x.TenantId==log.TenantId&&x.Action=="LoginSucceeded"&&x.BrowserInfo==log.BrowserInfo))return;
        var employee=await employees.FirstOrDefaultAsync(e=>e.UserId==log.UserId&&e.IsActive);if(employee==null)return;
        var accessible=await branches.GetListAsync(b=>b.IsActive&&(employee.AllBranches||employee.BranchIds.Contains(b.Id)));
        if(accessible.Count==0)return;
        var timezone=TimeZoneInfo.FindSystemTimeZoneById(await settings.GetOrNullAsync(DentalSettings.Timezone)??"Asia/Almaty");
        var local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(log.CreationTime,DateTimeKind.Utc),timezone);
        if(accessible.Any(b=>!OutsideHours(b,local)))return;
        var owners=await employees.GetListAsync(e=>e.IsActive&&e.Position==StaffPosition.Owner);
        foreach(var owner in owners)await notifications.NotifyAsync(owner.UserId,"Suspicious","Вход с нового устройства вне рабочего времени",employee.FullName+" · "+local.ToString("dd.MM.yyyy HH:mm"),nameof(Employee),employee.Id);
    }
}
