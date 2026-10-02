using System;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Notifications;
using Dental.Staff;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;
[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreLoginAuditTests : DentalApplicationTestBase<DentalEntityFrameworkCoreTestModule>
{
    // Identity constructs these from SecurityLogInfo in the host; the fixture supplies the same persisted fields.
    private static IdentitySecurityLog Login(Guid tenant,Guid user,string browser,DateTime utc)
    {
        var log=(IdentitySecurityLog)Activator.CreateInstance(typeof(IdentitySecurityLog),nonPublic:true)!;
        void Set(string key,object value)=>typeof(IdentitySecurityLog).GetProperty(key)!.SetValue(log,value);
        Set("Id",Guid.NewGuid());Set("TenantId",tenant);Set("UserId",user);Set("Action","LoginSucceeded");Set("BrowserInfo",browser);Set("CreationTime",utc);
        return log;
    }
    [Fact]
    public async Task New_Device_Outside_Hours_Notifies_Once_And_Tenant_History_Is_Isolated()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var user=Guid.NewGuid();var owner=Guid.NewGuid();
        foreach(var tenant in new[]{first,second})
        {
            using var current=GetRequiredService<ICurrentTenant>().Change(tenant);
            await WithUnitOfWorkAsync(async()=>{
                var branch=Guid.NewGuid();await GetRequiredService<IRepository<Branch,Guid>>().InsertAsync(new(branch,tenant,"Audit test"));
                var doctor=new Employee(Guid.NewGuid(),tenant,user,"Doctor",StaffPosition.Doctor);doctor.SetBranches(false,[branch]);await GetRequiredService<IRepository<Employee,Guid>>().InsertAsync(doctor);
                var manager=new Employee(Guid.NewGuid(),tenant,owner,"Owner",StaffPosition.Owner);manager.SetBranches(true,[]);await GetRequiredService<IRepository<Employee,Guid>>().InsertAsync(manager);
            });
            await WithUnitOfWorkAsync(async()=>await GetRequiredService<IRepository<IdentitySecurityLog,Guid>>().InsertAsync(Login(tenant,user,"Fixture device",new(2026,10,3,21,0,0,DateTimeKind.Utc)),true));
            await WithUnitOfWorkAsync(async()=>await GetRequiredService<IRepository<IdentitySecurityLog,Guid>>().InsertAsync(Login(tenant,user,"Fixture device",new(2026,10,3,22,0,0,DateTimeKind.Utc)),true));
            await WithUnitOfWorkAsync(async()=>{
                var alerts=await GetRequiredService<IRepository<Notification,Guid>>().GetListAsync(n=>n.Type=="Suspicious"&&n.UserId==owner);
                alerts.Count.ShouldBe(1);alerts[0].TenantId.ShouldBe(tenant);alerts[0].Body.ShouldNotBeNull();alerts[0].Body!.ShouldNotContain("Fixture device");
            });
        }
    }
}
