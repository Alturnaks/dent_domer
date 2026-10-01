using System;
using Dental.Finance;
namespace Dental.Reports;
public class ReportSubscription : FinanceEntity
{
    protected ReportSubscription() { }
    public ReportSubscription(Guid id, Guid? tenant, Guid user, string code, Guid? branch, TimeOnly time, string channel, string? email) : base(id,tenant) { UserId=user; Code=code; BranchId=branch; LocalTime=time; Channel=channel; Email=email; }
    public Guid UserId { get; private set; }
    public string Code { get; private set; } = "";
    public Guid? BranchId { get; private set; }
    public TimeOnly LocalTime { get; private set; }
    public string Channel { get; private set; } = "internal";
    public string? Email { get; private set; }
    public bool Enabled { get; private set; } = true;
    public DateOnly? LastDate { get; private set; }
    public int Failures { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public void SetEnabled(bool value) => Enabled=value;
    public void Sent(DateOnly date,DateTime now) { LastDate=date;LastSentAt=now;Failures=0;LastError=null; }
    public void Failed(string error) { Failures++;LastError=error.Length>2000?error[..2000]:error;if(Failures>=3)Enabled=false; }
}
public class OperationsRun : FinanceEntity
{
    protected OperationsRun() { }
    public OperationsRun(Guid id,Guid? tenant,string key,DateOnly date) : base(id,tenant) { Key=key;Date=date; }
    public string Key { get; private set; } = "";
    public DateOnly Date { get; private set; }
}
