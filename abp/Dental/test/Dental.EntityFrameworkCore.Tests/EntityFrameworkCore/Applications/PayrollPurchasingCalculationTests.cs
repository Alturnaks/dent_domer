using System;
using Dental.Payroll;
using Dental.Purchasing;
using Shouldly;
using Volo.Abp;
using Xunit;
namespace Dental.EntityFrameworkCore.Applications;
public class PayrollPurchasingCalculationTests
{
    [Theory]
    [InlineData(PayrollSchemeType.PercentRevenue,10001,1000,2,3000)]
    [InlineData(PayrollSchemeType.PercentRevenueMinusMaterials,10001,1000,2,2700)]
    [InlineData(PayrollSchemeType.PercentRevenueMinusMaterials,1000,2000,2,0)]
    [InlineData(PayrollSchemeType.FixedPlusPercent,10001,1000,2,8000)]
    [InlineData(PayrollSchemeType.PerShift,10001,1000,2,14000)]
    public void Schemes_Round_Minor_Units_And_Exclude_Negative_Margin(PayrollSchemeType type,long revenue,long materials,int days,long expected)
    { var scheme=new PayrollScheme(Guid.NewGuid(),null,Guid.NewGuid(),type,30,5000,7000,new DateOnly(2026,1,1));PayrollCalculator.Accrue(scheme,revenue,materials,days).ShouldBe(expected); }
    [Fact]
    public void Half_Minor_Unit_Rounds_Away_From_Zero()
    {var s=new PayrollScheme(Guid.NewGuid(),null,Guid.NewGuid(),PayrollSchemeType.PercentRevenue,50,0,0,new(2026,1,1));PayrollCalculator.Accrue(s,101,0,1).ShouldBe(51);}
    [Fact]
    public void Approved_Period_And_Invalid_Transitions_Are_Protected()
    {var p=new PayrollPeriod(Guid.NewGuid(),null,Guid.NewGuid(),new(2026,1,1),new(2026,1,31));Should.Throw<UserFriendlyException>(()=>p.MarkPaid(DateTime.UtcNow));p.Approve(Guid.NewGuid(),DateTime.UtcNow);Should.Throw<UserFriendlyException>(()=>p.EnsureDraft());p.MarkPaid(DateTime.UtcNow);p.Status.ShouldBe(PayrollPeriodStatus.Paid);Should.Throw<UserFriendlyException>(()=>p.MarkPaid(DateTime.UtcNow));}
    [Fact]
    public void Adjustment_Cannot_Make_Pay_Negative_And_Requires_Reason()
    {var e=new PayrollEntry(Guid.NewGuid(),null,Guid.NewGuid(),Guid.NewGuid());e.Calculate(10000,0,3000,"[]");Should.Throw<UserFriendlyException>(()=>e.Adjust(0,3001,"test"));Should.Throw<UserFriendlyException>(()=>e.Adjust(100,0," "));e.Adjust(1000,500,"Премия");e.Total.ShouldBe(3500);}
    [Theory]
    [InlineData(8,10,20,2,3,7)] [InlineData(10,10,20,0,0,0)] [InlineData(-2,10,20,0,0,22)] [InlineData(1,10,20,10,20,0)]
    public void Replenishment_Subtracts_Transit_And_Outstanding_Orders(decimal stock,decimal min,decimal optimal,decimal transit,decimal ordered,decimal expected)
    { Replenishment.SuggestedQty(stock,min,optimal,transit,ordered).ShouldBe(expected); }
    [Fact]
    public void Receipt_Updates_Order_Status_Without_Changing_Requested_Quantity()
    {var o=new PurchaseOrder(Guid.NewGuid(),null,"1",Guid.NewGuid(),Guid.NewGuid(),null,null,null);var l=new PurchaseOrderLine(Guid.NewGuid(),null,o.Id,Guid.NewGuid(),10,101);o.Lines.Add(l);o.Recalculate();o.Total.ShouldBe(1010);o.Status=PurchaseOrderStatus.Sent;l.ReceivedQty=4;o.RecalculateStatus();o.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);l.ReceivedQty=10;o.RecalculateStatus();o.Status.ShouldBe(PurchaseOrderStatus.Received);l.ReceivedQty=0;o.RecalculateStatus();o.Status.ShouldBe(PurchaseOrderStatus.Sent);}
}
