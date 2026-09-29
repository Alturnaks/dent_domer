using Dental.Domain.Scheduling;
using FluentValidation;

namespace Dental.Application.Schedule;

public sealed record DoctorScheduleDto(Guid Id, Guid MembershipId, Guid BranchId, int Weekday, TimeOnly StartTime, TimeOnly EndTime, Guid? ChairId, DateOnly ValidFrom, DateOnly? ValidTo);
public sealed record DoctorScheduleRequest(Guid MembershipId, Guid BranchId, int Weekday, TimeOnly StartTime, TimeOnly EndTime, Guid? ChairId, DateOnly? ValidFrom, DateOnly? ValidTo,
    AffectedAppointmentsAction? OnConflict = null);
/// <summary>Замена недельного шаблона врача в филиале целиком.</summary>
public sealed record DoctorWeekTemplateRequest(Guid MembershipId, Guid BranchId, DateOnly? ValidFrom, IReadOnlyList<DoctorWeekDay> Days,
    AffectedAppointmentsAction? OnConflict = null);
public sealed record DoctorWeekDay(int Weekday, TimeOnly StartTime, TimeOnly EndTime, Guid? ChairId);

public sealed record ScheduleExceptionDto(Guid Id, Guid MembershipId, Guid? BranchId, DateOnly DateFrom, DateOnly DateTo, ScheduleExceptionType Type, TimeOnly? StartTime, TimeOnly? EndTime, string? Comment);
public sealed record ScheduleExceptionRequest(Guid MembershipId, Guid? BranchId, DateOnly DateFrom, DateOnly DateTo, ScheduleExceptionType Type, TimeOnly? StartTime, TimeOnly? EndTime, string? Comment,
    AffectedAppointmentsAction? OnConflict = null);

public sealed record TimeBlockDto(Guid Id, Guid BranchId, Guid? DoctorId, Guid? ChairId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
public sealed record TimeBlockRequest(Guid BranchId, Guid? DoctorId, Guid? ChairId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason,
    AffectedAppointmentsAction? OnConflict = null);

public sealed record AppointmentServiceDto(Guid ServiceId, string Name, int DurationMin, long PlannedPrice, int Qty);
public sealed record AppointmentDto(
    Guid Id, Guid BranchId, Guid PatientId, string PatientName, string? PatientPhone, bool PatientIsVip, long PatientBalance,
    Guid DoctorId, string DoctorName, string? DoctorColor, Guid? ChairId, string? ChairName,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status, AppointmentSource Source, string? Comment,
    Guid? CancelReasonId, string? CancelComment, DateTimeOffset? ConfirmedAt, string? ConfirmedVia, Guid? VisitId, int Version,
    IReadOnlyList<AppointmentServiceDto> Services, long PlannedTotal);

public sealed record AppointmentServiceInput(Guid ServiceId, int? Qty);
public sealed record CreateAppointmentRequest(
    Guid BranchId, Guid PatientId, Guid DoctorId, Guid? ChairId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    IReadOnlyList<AppointmentServiceInput>? Services, AppointmentSource? Source, string? Comment, bool? Force);
public sealed record UpdateAppointmentRequest(string? Comment, IReadOnlyList<AppointmentServiceInput>? Services, Guid? ChairId, int? Version);
public sealed record MoveAppointmentRequest(DateTimeOffset StartsAt, DateTimeOffset? EndsAt, Guid? DoctorId, Guid? ChairId, Guid? ReasonId, bool? Force, int? Version);
public sealed record AppointmentStatusRequest(AppointmentStatus Status, Guid? ReasonId, string? Comment, int? Version);

public sealed record CalendarResource(Guid Id, string Title, string? Color, string Kind, string? Specialty);
public sealed record CalendarWorkingInterval(Guid DoctorId, DateTimeOffset Start, DateTimeOffset End, Guid? ChairId);
public sealed record CalendarResponse(
    IReadOnlyList<CalendarResource> Resources,
    IReadOnlyList<AppointmentDto> Appointments,
    IReadOnlyList<CalendarWorkingInterval> Working,
    IReadOnlyList<TimeBlockDto> Blocks,
    IReadOnlyList<ScheduleExceptionDto> Exceptions,
    string Timezone,
    int SlotMinutes);

public sealed record AvailableSlot(DateTimeOffset Start, DateTimeOffset End, Guid DoctorId);

public sealed record WaitlistDto(Guid Id, Guid BranchId, Guid PatientId, string PatientName, string? PatientPhone, Guid? DoctorId, string? DoctorName, Guid? ServiceId,
    string? ServiceName, DateOnly? PreferredFrom, DateOnly? PreferredTo, string? PreferredTimes, WaitlistStatus Status, string? Comment, DateTimeOffset CreatedAt);
public sealed record WaitlistRequest(Guid BranchId, Guid PatientId, Guid? DoctorId, Guid? ServiceId, DateOnly? PreferredFrom, DateOnly? PreferredTo,
    string? PreferredTimes, WaitlistStatus? Status, string? Comment);

public sealed class DoctorScheduleRequestValidator : AbstractValidator<DoctorScheduleRequest>
{
    public DoctorScheduleRequestValidator()
    {
        RuleFor(x => x.Weekday).InclusiveBetween(0, 6);
        RuleFor(x => x).Must(x => x.EndTime > x.StartTime).WithMessage("Окончание позже начала").WithName("endTime");
    }
}

public sealed class DoctorWeekTemplateRequestValidator : AbstractValidator<DoctorWeekTemplateRequest>
{
    public DoctorWeekTemplateRequestValidator()
    {
        RuleForEach(x => x.Days).ChildRules(d =>
        {
            d.RuleFor(x => x.Weekday).InclusiveBetween(0, 6);
            d.RuleFor(x => x).Must(x => x.EndTime > x.StartTime).WithMessage("Окончание позже начала").WithName("endTime");
        });
    }
}

public sealed class ScheduleExceptionRequestValidator : AbstractValidator<ScheduleExceptionRequest>
{
    public ScheduleExceptionRequestValidator()
    {
        RuleFor(x => x).Must(x => x.DateTo >= x.DateFrom).WithMessage("Дата окончания раньше начала").WithName("dateTo");
        RuleFor(x => x).Must(x => (x.StartTime is null) == (x.EndTime is null) && (x.StartTime is null || x.EndTime > x.StartTime))
            .WithMessage("Укажите интервал времени целиком").WithName("startTime");
        RuleFor(x => x).Must(x => x.Type != ScheduleExceptionType.ExtraShift || x.StartTime is not null).WithMessage("Для доп. смены укажите время").WithName("startTime");
    }
}

public sealed class TimeBlockRequestValidator : AbstractValidator<TimeBlockRequest>
{
    public TimeBlockRequestValidator() => RuleFor(x => x).Must(x => x.EndsAt > x.StartsAt).WithMessage("Окончание позже начала").WithName("endsAt");
}

public sealed class CreateAppointmentRequestValidator : AbstractValidator<CreateAppointmentRequest>
{
    public CreateAppointmentRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DoctorId).NotEmpty();
        RuleFor(x => x).Must(x => x.EndsAt is null || x.EndsAt > x.StartsAt).WithMessage("Окончание позже начала").WithName("endsAt");
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class AppointmentStatusRequestValidator : AbstractValidator<AppointmentStatusRequest>
{
    public AppointmentStatusRequestValidator() => RuleFor(x => x.Comment).MaximumLength(2000);
}

public sealed class WaitlistRequestValidator : AbstractValidator<WaitlistRequest>
{
    public WaitlistRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.PatientId).NotEmpty();
    }
}

/// <summary>Хук «пациент пришёл»: модуль визитов открывает визит (регистрируется в DI).</summary>
public interface IAppointmentArrivalHandler
{
    Task<Guid> OnArrivedAsync(Appointment appointment, CancellationToken ct);
}

/// <summary>Отправка напоминания пациенту (реализация — Infrastructure, провайдер сообщений).</summary>
public interface IReminderSender
{
    Task<bool> SendAsync(Appointment appointment, string templateType, CancellationToken ct);
}
