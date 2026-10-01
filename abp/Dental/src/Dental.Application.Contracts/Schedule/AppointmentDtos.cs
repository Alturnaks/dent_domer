using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Schedule;

public class AppointmentDto : EntityDto<Guid>
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public string? PatientPhone { get; set; }
    public Guid DoctorId { get; set; }
    public string DoctorName { get; set; } = "";
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public AppointmentStatus Status { get; set; }
    public AppointmentSource Source { get; set; }
    public string? Comment { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public bool ForcedOutsideSchedule { get; set; }
    public long PlannedTotal { get; set; }
    public List<AppointmentServiceDto> Services { get; set; } = [];
}
public class AppointmentServiceDto
{
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public int DurationMinutes { get; set; }
    public long PlannedPrice { get; set; }
}
public class AppointmentServiceInput
{
    public Guid ServiceId { get; set; }
    [Range(1, 100)] public int Qty { get; set; } = 1;
}
public class CreateAppointmentDto
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public AppointmentSource Source { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
    public bool Force { get; set; }
    public List<AppointmentServiceInput> Services { get; set; } = [];
}
public class MoveAppointmentDto
{
    [Required] public string ConcurrencyStamp { get; set; } = "";
    public Guid DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public Guid? ReasonId { get; set; }
    public bool Force { get; set; }
}
public class ChangeAppointmentStatusDto
{
    [Required] public string ConcurrencyStamp { get; set; } = "";
    public AppointmentStatus Status { get; set; }
    public Guid? ReasonId { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
}
public class CalendarInput
{
    public Guid BranchId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public Guid? DoctorId { get; set; }
}
public class CalendarResourceDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}
public class CalendarWorkingDto : WorkingIntervalDto { public Guid DoctorId { get; set; } }
public class CalendarDto
{
    public List<CalendarResourceDto> Doctors { get; set; } = [];
    public List<AppointmentDto> Appointments { get; set; } = [];
    public List<CalendarWorkingDto> Working { get; set; } = [];
    public List<TimeBlockDto> Blocks { get; set; } = [];
    public string Timezone { get; set; } = "";
    public int SlotMinutes { get; set; }
}
public class AvailableSlotsInput
{
    public Guid BranchId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateOnly Date { get; set; }
    [Range(5, 1440)] public int DurationMinutes { get; set; } = 30;
}
public class CreateWaitlistDto
{
    public Guid BranchId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? ServiceId { get; set; }
    public DateOnly? PreferredFrom { get; set; }
    public DateOnly? PreferredTo { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
}
public class WaitlistDto : EntityDto<Guid>
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public Guid? DoctorId { get; set; }
    public Guid? ServiceId { get; set; }
    public DateOnly? PreferredFrom { get; set; }
    public DateOnly? PreferredTo { get; set; }
    public WaitlistStatus Status { get; set; }
    public string? Comment { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
}
public class ChangeWaitlistStatusDto
{
    [Required] public string ConcurrencyStamp { get; set; } = "";
    public WaitlistStatus Status { get; set; }
}
public interface IAppointmentAppService : IApplicationService
{
    Task<CalendarDto> GetCalendarAsync(CalendarInput input);
    Task<AppointmentDto> GetAsync(Guid id);
    Task<ListResultDto<AppointmentDto>> GetPatientAppointmentsAsync(Guid patientId);
    Task<AppointmentDto> CreateAsync(CreateAppointmentDto input);
    Task<AppointmentDto> MoveAsync(Guid id, MoveAppointmentDto input);
    Task<AppointmentDto> ChangeStatusAsync(Guid id, ChangeAppointmentStatusDto input);
    Task<ListResultDto<WorkingIntervalDto>> GetAvailableSlotsAsync(AvailableSlotsInput input);
    Task<bool> RemindAsync(Guid id);
    Task<ListResultDto<WaitlistDto>> GetWaitlistAsync(Guid branchId);
    Task<WaitlistDto> CreateWaitlistAsync(CreateWaitlistDto input);
    Task ChangeWaitlistStatusAsync(Guid id, ChangeWaitlistStatusDto input);
}
