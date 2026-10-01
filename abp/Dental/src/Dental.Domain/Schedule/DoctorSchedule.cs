using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Schedule;

[Audited]
public class DoctorSchedule : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid BranchId { get; private set; }
    public int Weekday { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public Guid? ChairId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }

    protected DoctorSchedule() { }

    public DoctorSchedule(Guid id, Guid? tenantId, Guid doctorId, Guid branchId, int weekday,
        TimeOnly startTime, TimeOnly endTime, Guid? chairId, DateOnly validFrom, DateOnly? validTo = null) : base(id)
    {
        if (doctorId == Guid.Empty || branchId == Guid.Empty || weekday is < 0 or > 6 || endTime <= startTime || validTo < validFrom)
            throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        TenantId = tenantId;
        DoctorId = doctorId;
        BranchId = branchId;
        Weekday = weekday;
        StartTime = startTime;
        EndTime = endTime;
        ChairId = chairId;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public bool AppliesTo(DateOnly date) => (int)date.DayOfWeek == Weekday && date >= ValidFrom && (ValidTo == null || date <= ValidTo);

    // Retain historical shifts when replacing a weekly template.
    public void CloseBefore(DateOnly date) => ValidTo = date.AddDays(-1);
}

[Audited]
public class ScheduleException : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateOnly DateFrom { get; private set; }
    public DateOnly DateTo { get; private set; }
    public ScheduleExceptionType Type { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public string? Comment { get; private set; }

    protected ScheduleException() { }

    public ScheduleException(Guid id, Guid? tenantId, Guid doctorId, Guid? branchId, DateOnly from, DateOnly to,
        ScheduleExceptionType type, TimeOnly? start, TimeOnly? end, string? comment) : base(id)
    {
        if (doctorId == Guid.Empty || to < from || !Enum.IsDefined(type) || (start == null) != (end == null)
            || (start != null && end <= start) || (type == ScheduleExceptionType.ExtraShift && start == null))
            throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        TenantId = tenantId; DoctorId = doctorId; BranchId = branchId;
        DateFrom = from; DateTo = to; Type = type; StartTime = start; EndTime = end;
        Comment = Check.Length(comment?.Trim(), nameof(comment), ScheduleConsts.MaxCommentLength);
    }

    public bool Covers(DateOnly date) => date >= DateFrom && date <= DateTo;
}

[Audited]
public class TimeBlock : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? DoctorId { get; private set; }
    public Guid? ChairId { get; private set; }
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public string? Reason { get; private set; }

    protected TimeBlock() { }

    public TimeBlock(Guid id, Guid? tenantId, Guid branchId, Guid? doctorId, Guid? chairId, DateTime start, DateTime end, string? reason) : base(id)
    {
        if (branchId == Guid.Empty || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || end <= start)
            throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        TenantId = tenantId; BranchId = branchId; DoctorId = doctorId; ChairId = chairId;
        StartsAt = start; EndsAt = end;
        Reason = Check.Length(reason?.Trim(), nameof(reason), ScheduleConsts.MaxCommentLength);
    }
}
