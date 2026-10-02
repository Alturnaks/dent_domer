using System;
using Dental.Reports;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

public class ReportSubscriptionTests
{
    private static ReportSubscription Create() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"revenue",null,new(21,0),"internal",null);
    [Fact]
    public void Weekly_Delivery_Uses_Clinic_Weekday_Time_And_Deduplicates_The_Day()
    {
        var s=Create();s.SetSchedule("weekly",1,"branch");
        s.IsDue(new(2026,10,5),new(20,59)).ShouldBeFalse();
        s.IsDue(new(2026,10,5),new(21,0)).ShouldBeTrue();
        s.IsDue(new(2026,10,6),new(21,0)).ShouldBeFalse();
        s.Sent(new(2026,10,5),DateTime.UtcNow);
        s.IsDue(new(2026,10,5),new(23,0)).ShouldBeFalse();
        s.IsDue(new(2026,10,12),new(21,0)).ShouldBeTrue();
    }
    [Fact]
    public void Retry_Limit_Pauses_And_Explicit_Resume_Starts_A_Fresh_Attempt()
    {
        var s=Create();s.Failed("SMTP unavailable");s.Failed("SMTP unavailable");s.Enabled.ShouldBeTrue();s.Failed("SMTP unavailable");s.Enabled.ShouldBeFalse();
        s.SetEnabled(true);s.Failures.ShouldBe(0);s.LastError.ShouldBeNull();s.Failed("SMTP unavailable");s.Enabled.ShouldBeTrue();
    }
    [Theory]
    [InlineData("monthly",1)] [InlineData("weekly",7)] [InlineData("daily",-1)]
    public void Unsupported_Schedules_Are_Rejected(string frequency,int day)
    {Should.Throw<UserFriendlyException>(()=>Create().SetSchedule(frequency,day,"default"));}
}
