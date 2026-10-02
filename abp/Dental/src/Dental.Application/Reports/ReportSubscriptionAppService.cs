using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Finance;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
namespace Dental.Reports;
[Authorize]
public class ReportSubscriptionAppService(IRepository<ReportSubscription,Guid> subscriptions,IReportsAppService reports,IFinanceLock financeLock) : DentalAppService,IReportSubscriptionAppService
{
    private static SubscriptionDto Map(ReportSubscription s) => new(s.Id,s.Code,s.BranchId,s.LocalTime,s.Channel,s.Enabled,s.LastSentAt,s.LastError,s.Frequency,s.Weekday,s.GroupBy);
    public async Task<List<SubscriptionDto>> GetListAsync() => (await subscriptions.GetListAsync(s=>s.UserId==CurrentUser.Id)).Select(Map).ToList();
    public async Task<SubscriptionDto> CreateAsync(SubscriptionInput input)
    {
        await financeLock.AcquireAsync(); if (!(await reports.GetCatalogAsync()).Any(r=>r.Code==input.Code)) throw new AbpAuthorizationException(); if(input.BranchId!=null)await BranchScope.EnsureCanAccessAsync(input.BranchId.Value);
        if(input.Channel is not ("internal" or "email"))throw new UserFriendlyException("Выберите уведомление в приложении или email."); if(input.Channel=="email"&&string.IsNullOrWhiteSpace(CurrentUser.Email))throw new UserFriendlyException("В профиле не указан email.");
        if(!ReportCatalog.Groupings(input.Code).Contains(input.GroupBy))throw new UserFriendlyException("Недопустимая группировка.");
        if(await subscriptions.AnyAsync(s=>s.UserId==CurrentUser.Id&&s.Code==input.Code&&s.BranchId==input.BranchId&&s.Channel==input.Channel&&s.Frequency==input.Frequency&&s.Weekday==input.Weekday&&s.GroupBy==input.GroupBy))throw new UserFriendlyException("Такая подписка уже существует.");
        var subscription=new ReportSubscription(GuidGenerator.Create(),CurrentTenant.Id,CurrentUser.GetId(),input.Code,input.BranchId,input.LocalTime,input.Channel,CurrentUser.Email);
        subscription.SetSchedule(input.Frequency,input.Weekday,input.GroupBy);
        return Map(await subscriptions.InsertAsync(subscription,true));
    }
    private async Task<ReportSubscription> Own(Guid id) {var s=await subscriptions.GetAsync(id);if(s.UserId!=CurrentUser.Id)throw new AbpAuthorizationException();return s;}
    public async Task SetEnabledAsync(Guid id,bool enabled) {await financeLock.AcquireAsync();var s=await Own(id);s.SetEnabled(enabled);await subscriptions.UpdateAsync(s,true);}
    public async Task DeleteAsync(Guid id) {await financeLock.AcquireAsync();var s=await Own(id);if(s.Code=="daily_summary"){s.SetEnabled(false);await subscriptions.UpdateAsync(s,true);}else await subscriptions.DeleteAsync(s,true);}
}
