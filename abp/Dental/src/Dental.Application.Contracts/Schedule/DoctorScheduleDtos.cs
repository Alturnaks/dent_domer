using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Schedule;

public class DoctorScheduleDto : EntityDto<Guid>
{
    public Guid DoctorId { get; set; }
    public Guid BranchId { get; set; }
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid? ChairId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

public class DoctorWeekDayDto
{
    [Range(0, 6)] public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid? ChairId { get; set; }
}

public class ReplaceDoctorWeekDto
{
    public Guid DoctorId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly ValidFrom { get; set; }
    [Required, MaxLength(28)] public List<DoctorWeekDayDto> Days { get; set; } = [];
}

public class CreateScheduleExceptionDto
{
    public Guid DoctorId { get; set; }
    public Guid? BranchId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public ScheduleExceptionType Type { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    [StringLength(ScheduleConsts.MaxCommentLength)] public string? Comment { get; set; }
}

public class ScheduleExceptionDto : CreateScheduleExceptionDto
{
    public Guid Id { get; set; }
}

public class CreateTimeBlockDto
{
    public Guid BranchId { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? ChairId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    [StringLength(ScheduleConsts.MaxCommentLength)] public string? Reason { get; set; }
}

public class TimeBlockDto : CreateTimeBlockDto
{
    public Guid Id { get; set; }
}

public class WorkingIntervalDto
{
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}

public interface IDoctorScheduleAppService : IApplicationService
{
    Task<ListResultDto<DoctorScheduleDto>> GetListAsync(Guid branchId, Guid doctorId);
    Task<ListResultDto<DoctorScheduleDto>> ReplaceWeekAsync(ReplaceDoctorWeekDto input);
    Task<ListResultDto<ScheduleExceptionDto>> GetExceptionsAsync(Guid doctorId, DateOnly from, DateOnly to);
    Task<ScheduleExceptionDto> CreateExceptionAsync(CreateScheduleExceptionDto input);
    Task DeleteExceptionAsync(Guid id);
    Task<ListResultDto<TimeBlockDto>> GetBlocksAsync(Guid branchId, DateTimeOffset from, DateTimeOffset to);
    Task<TimeBlockDto> CreateBlockAsync(CreateTimeBlockDto input);
    Task DeleteBlockAsync(Guid id);
    Task<ListResultDto<WorkingIntervalDto>> GetWorkingIntervalsAsync(Guid branchId, Guid doctorId, DateOnly date);
}
