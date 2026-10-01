using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Schedule;

[Audited]
public class WaitlistEntry : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? DoctorId { get; private set; }
    public Guid? ServiceId { get; private set; }
    public DateOnly? PreferredFrom { get; private set; }
    public DateOnly? PreferredTo { get; private set; }
    public string? Comment { get; private set; }
    public WaitlistStatus Status { get; private set; }
    protected WaitlistEntry() { }
    public WaitlistEntry(Guid id, Guid? tenantId, Guid branchId, Guid patientId, Guid? doctorId, Guid? serviceId,
        DateOnly? from, DateOnly? to, string? comment) : base(id)
    {
        if (from != null && to != null && to < from) throw new BusinessException(DentalDomainErrorCodes.ScheduleInvalidRange);
        TenantId = tenantId; BranchId = branchId; PatientId = patientId; DoctorId = doctorId; ServiceId = serviceId;
        PreferredFrom = from; PreferredTo = to;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Check.Length(comment.Trim(), nameof(comment), ScheduleConsts.MaxCommentLength);
    }
    public void ChangeStatus(WaitlistStatus status)
    {
        if (!Enum.IsDefined(status) || Status is WaitlistStatus.Booked or WaitlistStatus.Cancelled)
            throw new BusinessException(DentalDomainErrorCodes.AppointmentInvalidStatus);
        Status = status;
    }
    public void MergePatient(Guid patientId) => PatientId = patientId;
}
