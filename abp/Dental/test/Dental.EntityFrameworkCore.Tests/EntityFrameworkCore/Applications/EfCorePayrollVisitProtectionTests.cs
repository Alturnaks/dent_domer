using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Branches;
using Dental.Catalog;
using Dental.Finance;
using Dental.Patients;
using Dental.Payroll;
using Dental.Staff;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;
namespace Dental.EntityFrameworkCore.Applications;
[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCorePayrollVisitProtectionTests : DentalApplicationTestBase<DentalEntityFrameworkCoreTestModule>
{
    [Fact]
    public async Task Approved_Payroll_Requires_Approval_Before_Visit_Correction_And_Keeps_The_Snapshot()
    {
        var user=Guid.NewGuid();using var principal=GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AbpClaimTypes.UserId,user.ToString()),new Claim(AbpClaimTypes.Role,"admin")],"test")));
        var branch=Guid.NewGuid();var doctor=Guid.NewGuid();var patient=Guid.NewGuid();var category=Guid.NewGuid();var service=Guid.NewGuid();var visitId=Guid.NewGuid();var periodId=Guid.NewGuid();var entryId=Guid.NewGuid();
        await WithUnitOfWorkAsync(async()=>{
            await GetRequiredService<IRepository<Branch,Guid>>().InsertAsync(new(branch,null,"Payroll test"));
            await GetRequiredService<IRepository<Patient,Guid>>().InsertAsync(new(patient,null,"Test","Patient"));
            var employee=new Employee(doctor,null,Guid.NewGuid(),"Test doctor",StaffPosition.Doctor);employee.SetBranches(true,[]);await GetRequiredService<IRepository<Employee,Guid>>().InsertAsync(employee);
            await GetRequiredService<IRepository<ServiceCategory,Guid>>().InsertAsync(new(category,null,"Payroll test"));
            await GetRequiredService<IRepository<ClinicService,Guid>>().InsertAsync(new(service,null,category,"PAYTEST","Service",30));
            var visit=new Visit(visitId,null,branch,patient,doctor,DateTime.UtcNow);visit.Items.Add(new(Guid.NewGuid(),null,visitId,service,doctor,1,10000,0));visit.Recalculate();await GetRequiredService<IRepository<Visit,Guid>>().InsertAsync(visit,true);
            var closed=await GetRequiredService<FinanceManager>().CloseVisitAsync(visitId,visit.ConcurrencyStamp);
            var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(closed.ClosedAt!.Value,TimeZoneInfo.FindSystemTimeZoneById("Asia/Almaty")));
            var period=new PayrollPeriod(periodId,null,branch,date,date);period.Approve(user,DateTime.UtcNow);await GetRequiredService<IRepository<PayrollPeriod,Guid>>().InsertAsync(period,true);
            var entry=new PayrollEntry(entryId,null,periodId,doctor);entry.Calculate(10000,0,3000,"[]");await GetRequiredService<IRepository<PayrollEntry,Guid>>().InsertAsync(entry,true);
        });
        Guid approvalId=Guid.Empty;
        await WithUnitOfWorkAsync(async()=>{
            var manager=GetRequiredService<FinanceManager>();var visit=await manager.GetVisitAsync(visitId);
            var pending=await manager.CorrectVisitAsync(visitId,visit.ConcurrencyStamp,[new(service,2,0,UnitPrice:10000)],[],"Payroll test correction");
            pending.Total.ShouldBe(10000);pending.ApprovalState.ShouldBe(VisitApprovalState.PendingCorrection);
            var request=(await GetRequiredService<IRepository<ApprovalRequest,Guid>>().GetListAsync(a=>a.EntityId==visitId)).Single();request.Type.ShouldBe(ApprovalTypes.PayrollPeriodChange);approvalId=request.Id;
        });
        await WithUnitOfWorkAsync(()=>GetRequiredService<ApprovalManager>().DecideAsync(approvalId,true,"Approved test correction"));
        await WithUnitOfWorkAsync(async()=>{
            var visit=await GetRequiredService<FinanceManager>().GetVisitAsync(visitId);visit.Total.ShouldBe(20000);visit.ApprovalState.ShouldBe(VisitApprovalState.None);
            var entry=await GetRequiredService<IRepository<PayrollEntry,Guid>>().GetAsync(entryId);entry.Total.ShouldBe(3000);entry.BaseRevenue.ShouldBe(10000);
            var balance=(await GetRequiredService<IRepository<PatientBalance,Guid>>().GetListAsync(b=>b.PatientId==patient)).Single();balance.Balance.ShouldBe(-20000);
        });
        await WithUnitOfWorkAsync(async()=>{
            var manager=GetRequiredService<FinanceManager>();var visit=await manager.GetVisitAsync(visitId);
            var pending=await manager.CancelVisitAsync(visitId,visit.ConcurrencyStamp,"Payroll test cancellation");pending.Status.ShouldBe(VisitStatus.Closed);
            approvalId=(await GetRequiredService<IRepository<ApprovalRequest,Guid>>().GetListAsync(a=>a.EntityId==visitId&&a.Status==ApprovalStatus.Pending)).Single().Id;
        });
        await WithUnitOfWorkAsync(()=>GetRequiredService<ApprovalManager>().DecideAsync(approvalId,true,"Approved cancellation"));
        await WithUnitOfWorkAsync(async()=>{
            (await GetRequiredService<FinanceManager>().GetVisitAsync(visitId)).Status.ShouldBe(VisitStatus.Cancelled);
            (await GetRequiredService<IRepository<PayrollEntry,Guid>>().GetAsync(entryId)).Total.ShouldBe(3000);
            (await GetRequiredService<IRepository<PatientBalance,Guid>>().GetListAsync(b=>b.PatientId==patient)).Single().Balance.ShouldBe(0);
        });
    }
}
