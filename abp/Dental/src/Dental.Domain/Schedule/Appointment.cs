using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Schedule;

[Audited]
public class Appointment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid? ChairId { get; private set; }
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public AppointmentSource Source { get; private set; }
    public string? Comment { get; private set; }
    public Guid? CancelReasonId { get; private set; }
    public Guid? MoveReasonId { get; private set; }
    public string? CancelComment { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? Reminder24hSentAt { get; private set; }
    public DateTime? Reminder2hSentAt { get; private set; }
    public bool ForcedOutsideSchedule { get; private set; }
    public List<AppointmentLine> Services { get; private set; } = [];
    protected Appointment() { }
    public Appointment(Guid id, Guid? tenantId, Guid branchId, Guid patientId, Guid doctorId, Guid? chairId,
        DateTime start, DateTime end, AppointmentSource source, string? comment) : base(id)
    {
        TenantId = tenantId; BranchId = branchId; PatientId = patientId; Source = source;
        Move(doctorId, chairId, start, end, null, false); SetComment(comment);
    }
    public static bool IsActiveStatus(AppointmentStatus status) => status != AppointmentStatus.Cancelled && status != AppointmentStatus.NoShow;
    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to) => from == to || (from, to) switch
    {
        (AppointmentStatus.Scheduled, AppointmentStatus.Confirmed) => true,
        (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed or AppointmentStatus.NoShow, AppointmentStatus.Arrived) => true,
        (AppointmentStatus.Arrived, AppointmentStatus.InChair) => true,
        (AppointmentStatus.Arrived or AppointmentStatus.InChair, AppointmentStatus.Completed) => true,
        (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed, AppointmentStatus.Cancelled or AppointmentStatus.NoShow) => true,
        _ => false
    };
    public void Move(Guid doctorId, Guid? chairId, DateTime start, DateTime end, Guid? reasonId, bool forced)
    {
        if (Status != AppointmentStatus.Scheduled && Status != AppointmentStatus.Confirmed)
            throw new BusinessException(DentalDomainErrorCodes.AppointmentInvalidStatus);
        if (doctorId == Guid.Empty || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || end <= start || end - start > TimeSpan.FromDays(1))
            throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        DoctorId = doctorId; ChairId = chairId; StartsAt = start; EndsAt = end; MoveReasonId = reasonId; ForcedOutsideSchedule = forced;
        Reminder24hSentAt = null; Reminder2hSentAt = null;
    }
    public void SetComment(string? comment) => Comment = string.IsNullOrWhiteSpace(comment) ? null : Check.Length(comment.Trim(), nameof(comment), ScheduleConsts.MaxCommentLength);
    public void ReplaceServices(IEnumerable<AppointmentLine> lines)
    {
        if (Status != AppointmentStatus.Scheduled && Status != AppointmentStatus.Confirmed) throw new BusinessException(DentalDomainErrorCodes.AppointmentInvalidStatus);
        Services.Clear(); Services.AddRange(lines);
    }
    public void ChangeStatus(AppointmentStatus status, DateTime now, Guid? reasonId = null, string? comment = null)
    {
        if (!Enum.IsDefined(status) || !CanTransition(Status, status)) throw new BusinessException(DentalDomainErrorCodes.AppointmentInvalidStatus);
        if (Status == status) return;
        if (status == AppointmentStatus.Cancelled)
        {
            if (reasonId == null) throw new BusinessException(DentalDomainErrorCodes.AppointmentReasonRequired);
            CancelReasonId = reasonId;
            CancelComment = string.IsNullOrWhiteSpace(comment) ? null : Check.Length(comment, nameof(comment), ScheduleConsts.MaxCommentLength);
        }
        if (status == AppointmentStatus.Confirmed) ConfirmedAt = now;
        Status = status;
    }
    public void MarkReminder(int hours, DateTime now)
    {
        if (hours == 24) Reminder24hSentAt = now; else if (hours == 2) Reminder2hSentAt = now;
    }
    public void MergePatient(Guid patientId) => PatientId = patientId;
    public void CancelFromVisit(string reason)
    {
        if (Status != AppointmentStatus.Arrived && Status != AppointmentStatus.InChair && Status != AppointmentStatus.Completed)
            throw new BusinessException(DentalDomainErrorCodes.AppointmentInvalidStatus);
        CancelComment = Check.NotNullOrWhiteSpace(reason, nameof(reason), ScheduleConsts.MaxCommentLength);
        Status = AppointmentStatus.Cancelled;
    }
}

[Audited]
public class AppointmentLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid ServiceId { get; private set; }
    public int Qty { get; private set; }
    public int DurationMinutes { get; private set; }
    public long PlannedPrice { get; private set; }
    protected AppointmentLine() { }
    public AppointmentLine(Guid id, Guid? tenantId, Guid appointmentId, Guid serviceId, int qty, int duration, long price) : base(id)
    {
        if (qty < 1 || qty > 100 || duration < 1 || price < 0) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        TenantId = tenantId; AppointmentId = appointmentId; ServiceId = serviceId; Qty = qty; DurationMinutes = duration; PlannedPrice = price;
    }
}
