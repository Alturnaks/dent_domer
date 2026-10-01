using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Notifications;
using Dental.Permissions;
using Dental.Schedule;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
namespace Dental.Audit;
[Authorize(DentalPermissions.Audit.View)]
public class AuditAppService(IAuditStore store,AppointmentManager calendar,IRepository<Branch,Guid> branches,IRepository<Notification,Guid> notifications) : DentalAppService, IAuditAppService
{
    private async Task<AuditQuery> Query(AuditInput input)
    {
        if (CurrentTenant.Id == null) throw new AbpAuthorizationException(); if (input.To < input.From || input.To.DayNumber-input.From.DayNumber > 366) throw new UserFriendlyException("Укажите период до одного года.");
        var rows=await AsyncExecuter.ToListAsync(await BranchScope.ApplyAsync(await branches.GetQueryableAsync(),b => b.Id));if(input.BranchId != null){await BranchScope.EnsureCanAccessAsync(input.BranchId.Value);rows=rows.Where(b => b.Id==input.BranchId).ToList();}
        var tz=await calendar.GetTimezoneAsync();return new(CurrentTenant.Id.Value,rows.Select(b=>b.Id).ToArray(),input.BranchId==null&&(await BranchScope.GetAsync()).AllBranches&&await AuthorizationService.IsGrantedAsync(DentalPermissions.Org.SettingsManage),SlotCalculator.ToUtc(input.From.ToDateTime(TimeOnly.MinValue),tz),SlotCalculator.ToUtc(input.To.AddDays(1).ToDateTime(TimeOnly.MinValue),tz),Math.Max(0,input.SkipCount),Math.Clamp(input.MaxResultCount,1,200));
    }
    public async Task<AuditPage> GetAsync(AuditInput input) => await store.GetAsync(await Query(input));
    public async Task<List<SuspiciousDto>> GetSuspiciousAsync(AuditInput input)
    {
        var q=await Query(input);
        // Notifications are delivered only to users with entity/branch access; never read another user's inbox.
        var rows=await notifications.GetListAsync(n=>n.UserId==CurrentUser.Id && n.Type=="Suspicious" && n.CreationTime>=q.From && n.CreationTime<q.To);
        if (!q.Network)
        {
            var ids = new HashSet<Guid>();
            var visits = await LazyServiceProvider.LazyGetRequiredService<IRepository<Dental.Finance.Visit,Guid>>().GetListAsync(v=>q.BranchIds.Contains(v.BranchId)); foreach(var v in visits)ids.Add(v.Id);
            var shifts = await LazyServiceProvider.LazyGetRequiredService<IRepository<Dental.Finance.CashShift,Guid>>().GetListAsync(v=>q.BranchIds.Contains(v.BranchId));foreach(var v in shifts)ids.Add(v.Id);
            var payments = await LazyServiceProvider.LazyGetRequiredService<IRepository<Dental.Finance.Payment,Guid>>().GetListAsync(v=>q.BranchIds.Contains(v.BranchId));foreach(var v in payments)ids.Add(v.Id);
            var docs = await LazyServiceProvider.LazyGetRequiredService<IRepository<Dental.Inventory.StockDocument,Guid>>().GetListAsync(v=>v.BranchId!=null&&q.BranchIds.Contains(v.BranchId.Value));foreach(var v in docs)ids.Add(v.Id);
            rows=rows.Where(n=>n.EntityId!=null&&ids.Contains(n.EntityId.Value)).ToList();
        }
        return rows.GroupBy(n=>new {n.EntityType,n.EntityId,n.Title,n.Body}).Select(g=>g.OrderBy(n=>n.CreationTime).First()).OrderByDescending(n=>n.CreationTime).Take(q.Limit).Select(n=>new SuspiciousDto(n.Id,n.CreationTime,n.Title,n.Body,n.EntityType,n.EntityId)).ToList();
    }
}
