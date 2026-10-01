using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dental.Finance;
using Dental.Notifications;
using Dental.Purchasing;
using Dental.Reports;
using Dental.Schedule;
using Dental.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Volo.Abp.Settings;
using Dental.Settings;
using Dental.Inventory;
using IdentityUser=Volo.Abp.Identity.IdentityUser;
namespace Dental.Web.Reports;
public class OperationsWorker(IServiceScopeFactory scopes,ILogger<OperationsWorker> logger) : BackgroundService,IOperationsRunner
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5));
        while(await timer.WaitForNextTickAsync(token))
        {
            try
            {
                using var scope=scopes.CreateScope();var sp=scope.ServiceProvider;var uows=sp.GetRequiredService<IUnitOfWorkManager>();using var host=uows.Begin(requiresNew:true,isTransactional:false);var tenants=await sp.GetRequiredService<ITenantRepository>().GetListAsync(cancellationToken:token);await host.CompleteAsync(token);
                foreach(var tenant in tenants)
                {
                    try { await RunTenantAsync(tenant.Id,token); } catch(Exception ex) when(!token.IsCancellationRequested){logger.LogError(ex,"Operations failed for tenant {Tenant}",tenant.Id);}
                }
            }
            catch(Exception ex) when(!token.IsCancellationRequested){logger.LogError(ex,"Operations worker failed");}
        }
    }
    public async Task RunTenantAsync(Guid tenant,CancellationToken token=default)
    {
        using var scope=scopes.CreateScope();var sp=scope.ServiceProvider;using var current=sp.GetRequiredService<ICurrentTenant>().Change(tenant);using var uow=sp.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew:true,isTransactional:true);
        if(!await sp.GetRequiredService<IAppointmentStore>().TryLockTasksAsync(tenant))return;
        var tz=await sp.GetRequiredService<AppointmentManager>().GetTimezoneAsync();var now=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,tz);var today=DateOnly.FromDateTime(now);var time=TimeOnly.FromDateTime(now);
        var users=sp.GetRequiredService<IIdentityUserRepository>();var claims=sp.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();var principal=sp.GetRequiredService<ICurrentPrincipalAccessor>();
        var owners=await sp.GetRequiredService<IRepository<Employee,Guid>>().GetListAsync(e=>e.IsActive&&e.Position==StaffPosition.Owner);
        var subscriptions=sp.GetRequiredService<IRepository<ReportSubscription,Guid>>();var runs=sp.GetRequiredService<IRepository<OperationsRun,Guid>>();
        foreach(var owner in owners)
        {
            var user=await users.FindAsync(owner.UserId);if(user==null||!user.IsActive)continue;
            using var auth=principal.Change(await claims.CreateAsync(user));
            if(!await subscriptions.AnyAsync(s=>s.UserId==user.Id&&s.Code=="daily_summary"))
            {
                var configured=await sp.GetRequiredService<ISettingProvider>().GetOrNullAsync(DentalSettings.DailySummaryTime);var summaryTime=TimeOnly.TryParse(configured,out var parsed)?parsed:new TimeOnly(21,0);
                await subscriptions.InsertAsync(new(Guid.NewGuid(),tenant,user.Id,"daily_summary",null,summaryTime,"internal",user.Email),true);
            }
            if(time>=new TimeOnly(3,0)&&!await runs.AnyAsync(r=>r.Key=="replenishment"&&r.Date==today))
            {await sp.GetRequiredService<IPurchasingAppService>().GenerateAsync();await runs.InsertAsync(new(Guid.NewGuid(),tenant,"replenishment",today),true);}
        }
        if (time>=new TimeOnly(3,0)&&!await runs.AnyAsync(r=>r.Key=="expiry"&&r.Date==today))
        {
            var stock=await sp.GetRequiredService<IRepository<StockBalance,Guid>>().GetListAsync(b=>b.Qty>0&&b.BatchId!=null);var batchIds=stock.Select(b=>b.BatchId!.Value).Distinct().ToList();var expiry=today.AddDays(30);
            var batches=await sp.GetRequiredService<IRepository<Batch,Guid>>().GetListAsync(b=>batchIds.Contains(b.Id)&&b.ExpiresAt<=expiry);var warehouses=await sp.GetRequiredService<IRepository<Warehouse,Guid>>().GetListAsync();var notifications=sp.GetRequiredService<NotificationManager>();
            foreach(var warehouse in warehouses)
            {
                var ids=stock.Where(s=>s.WarehouseId==warehouse.Id).Select(s=>s.BatchId).ToHashSet();var expiring=batches.Where(b=>ids.Contains(b.Id)).ToList();if(expiring.Count==0)continue;
                await notifications.NotifyByPermissionAsync(Dental.Permissions.DentalPermissions.Inventory.View,"expiry","Сроки годности: "+warehouse.Name,string.Join("; ",expiring.Select(b=>(b.BatchNumber??b.Id.ToString())+" · "+b.ExpiresAt)),nameof(Warehouse),warehouse.Id,warehouse.BranchId,null);
            }
            await runs.InsertAsync(new(Guid.NewGuid(),tenant,"expiry",today),true);
        }
        var due=await subscriptions.GetListAsync(s=>s.Enabled&&s.LastDate!=today&&s.LocalTime<=time);
        foreach(var s in due)
        {
            var user=await users.FindAsync(s.UserId);if(user==null||!user.IsActive){s.SetEnabled(false);await subscriptions.UpdateAsync(s,true);continue;}
            using var auth=principal.Change(await claims.CreateAsync(user));
            try
            {
                var table=await sp.GetRequiredService<IReportsAppService>().GetAsync(new ReportInput{Code=s.Code,BranchId=s.BranchId,From=today,To=today});var body=new StringBuilder();body.AppendLine(string.Join(" | ",table.Columns.Select(c=>c.Name)));foreach(var row in table.Rows.Take(20))body.AppendLine(string.Join(" | ",row));
                var title=ReportCatalog.Find(s.Code)!.Name+" · "+today;
                if(s.Channel=="email")
                {
                    if(!sp.GetRequiredService<IConfiguration>().GetValue<bool>("Reports:EnableEmail"))throw new InvalidOperationException("Отправка email выключена. Настройте SMTP и Reports:EnableEmail.");
                    var sender=sp.GetRequiredService<IEmailSender>();if(sender is NullEmailSender)throw new InvalidOperationException("Используется демонстрационный отправитель email.");
                    await sender.SendAsync(user.Email,title,body.ToString(),isBodyHtml:false);
                }
                else await sp.GetRequiredService<NotificationManager>().NotifyAsync(user.Id,"report",title,body.ToString());
                s.Sent(today,DateTime.UtcNow);
            }
            catch(Exception ex){s.Failed(ex.Message);logger.LogWarning(ex,"Report subscription {Subscription} failed",s.Id);}
            await subscriptions.UpdateAsync(s,true);
        }
        await uow.CompleteAsync(token);
    }
}
