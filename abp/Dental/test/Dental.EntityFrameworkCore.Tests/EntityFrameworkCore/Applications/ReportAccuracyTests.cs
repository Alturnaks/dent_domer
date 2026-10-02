using System;
using Dental.Audit;
using Dental.Branches;
using Dental.Finance;
using Shouldly;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;
public class ReportAccuracyTests
{
    [Fact]
    public void Splitting_The_Same_Material_Does_Not_Double_The_Norm()
    {
        var item=Guid.NewGuid();var row=Guid.NewGuid();
        var norms=MaterialNormAllocator.Allocate([(item,row,3m)],[(item,row,2m),(item,row,1m)]);
        norms.ShouldBe(new[]{2m,1m});
    }
    [Fact]
    public void Linked_And_Unlinked_Materials_Preserve_The_Total_Historical_Norm()
    {
        var item=Guid.NewGuid();var first=Guid.NewGuid();var second=Guid.NewGuid();
        var norms=MaterialNormAllocator.Allocate([(item,first,2m),(item,second,5m)],[(item,first,4m),(item,null,3m),(item,null,2m)]);
        norms.ShouldBe(new[]{2m,3m,2m});
    }
    [Fact]
    public void Manually_Added_Material_Without_A_Techcard_Does_Not_Invent_A_Norm()
    {MaterialNormAllocator.Allocate([],[(Guid.NewGuid(),null,4m)]).ShouldBe(new[]{0m});}
    [Theory]
    [InlineData(2026,10,5,8,59,true)] [InlineData(2026,10,5,9,0,false)]
    [InlineData(2026,10,5,19,59,false)] [InlineData(2026,10,5,20,0,true)]
    [InlineData(2026,10,4,12,0,true)] [InlineData(2026,10,3,15,0,true)]
    public void Suspicious_Login_Uses_The_Branch_Working_Day_Boundaries(int y,int m,int d,int h,int min,bool expected)
    {NewDeviceLoginHandler.OutsideHours(new Branch(Guid.NewGuid(),null,"Test"),new(y,m,d,h,min,0)).ShouldBe(expected);}
}
