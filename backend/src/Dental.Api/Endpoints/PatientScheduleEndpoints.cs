using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Patients;
using Dental.Application.Permissions;
using Dental.Application.Schedule;
using Dental.Domain.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class PatientScheduleEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/patients").WithTags("Patients").RequireAuthorization();

        g.MapGet("", async (string? q, string? tag, [FromQuery(Name = "source")] Guid? source, bool? debtors, [FromQuery(Name = "not_visited_months")] int? notVisitedMonths,
                string? cursor, int? limit, PatientService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(q, tag, source, debtors, notVisitedMonths, cursor, limit ?? 50, ct)))
            .RequirePermission(Perm.Patients.View).WithName("ListPatients");
        g.MapPost("", async (PatientRequest r, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct)))
            .RequirePermission(Perm.Patients.Edit, Perm.Schedule.Manage).Validate<PatientRequest>().WithName("CreatePatient");
        g.MapGet("/duplicates", async (PatientService s, CancellationToken ct) => TypedResults.Ok(await s.DuplicatesAsync(ct)))
            .RequirePermission(Perm.Patients.Merge).WithName("PatientDuplicates");
        g.MapGet("/check-duplicates", async (string? phone, string? iin, [FromQuery(Name = "exclude_id")] Guid? excludeId, PatientService s, CancellationToken ct) =>
                TypedResults.Ok(await s.FindDuplicatesAsync(phone, iin, excludeId, ct)))
            .RequirePermission(Perm.Patients.View).WithName("CheckPatientDuplicates");
        g.MapGet("/{id:guid}", async (Guid id, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Perm.Patients.View).WithName("GetPatient");
        g.MapPatch("/{id:guid}", async (Guid id, PatientRequest r, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Patients.Edit).Validate<PatientRequest>().WithName("UpdatePatient");
        g.MapPost("/{id:guid}/merge", async (Guid id, MergePatientsRequest r, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.MergeAsync(id, r.DuplicateId, ct)))
            .RequirePermission(Perm.Patients.Merge).WithName("MergePatients");
        g.MapGet("/{id:guid}/visits", async (Guid id, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.VisitsAsync(id, ct)))
            .RequirePermission(Perm.Patients.View).WithName("PatientVisits");
        g.MapGet("/{id:guid}/payments", async (Guid id, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.PaymentsAsync(id, ct)))
            .RequirePermission(Perm.Patients.View).WithName("PatientPayments");
        g.MapGet("/{id:guid}/appointments", async (Guid id, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.AppointmentsAsync(id, ct)))
            .RequirePermission(Perm.Patients.View).WithName("PatientAppointments");
        g.MapGet("/{id:guid}/balance", async (Guid id, PatientService s, CancellationToken ct) => TypedResults.Ok(await s.BalanceAsync(id, ct)))
            .RequirePermission(Perm.Patients.View).WithName("PatientBalance");
        return app;
    }

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Schedule").RequireAuthorization();
        string[] view = [Perm.Schedule.ViewAll, Perm.Schedule.ViewOwn, Perm.Schedule.Manage];

        g.MapGet("/calendar", async ([FromQuery(Name = "branch_id")] Guid branchId, DateOnly from, DateOnly to, [FromQuery(Name = "doctor_ids")] Guid[]? doctorIds, string? view,
                ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CalendarAsync(branchId, from, to, doctorIds, view ?? "doctors", ct)))
            .RequirePermission(view).WithName("Calendar");
        g.MapGet("/slots/available", async ([FromQuery(Name = "branch_id")] Guid branchId, [FromQuery(Name = "doctor_id")] Guid? doctorId,
                [FromQuery(Name = "service_ids")] Guid[]? serviceIds, DateOnly date, ScheduleService s, CancellationToken ct) =>
                TypedResults.Ok(await s.AvailableSlotsAsync(branchId, doctorId, serviceIds, date, ct)))
            .RequirePermission(view).WithName("AvailableSlots");

        g.MapGet("/doctor-schedules", async ([FromQuery(Name = "branch_id")] Guid? branchId, [FromQuery(Name = "doctor_id")] Guid? doctorId, ScheduleService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListSchedulesAsync(branchId, doctorId, ct)))
            .RequirePermission(view).WithName("ListDoctorSchedules");
        g.MapPost("/doctor-schedules", async (DoctorScheduleRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateScheduleAsync(r, ct)))
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).Validate<DoctorScheduleRequest>().WithName("CreateDoctorSchedule");
        g.MapPut("/doctor-schedules/template", async (DoctorWeekTemplateRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.ReplaceWeekTemplateAsync(r, ct)))
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).Validate<DoctorWeekTemplateRequest>().WithName("ReplaceDoctorWeekTemplate");
        g.MapPatch("/doctor-schedules/{id:guid}", async (Guid id, DoctorScheduleRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateScheduleAsync(id, r, ct)))
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).Validate<DoctorScheduleRequest>().WithName("UpdateDoctorSchedule");
        g.MapDelete("/doctor-schedules/{id:guid}", async (Guid id, [FromQuery(Name = "on_conflict")] AffectedAppointmentsAction? onConflict, ScheduleService s, CancellationToken ct) =>
            { await s.DeleteScheduleAsync(id, onConflict, ct); return TypedResults.NoContent(); })
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).WithName("DeleteDoctorSchedule");

        g.MapGet("/schedule-exceptions", async ([FromQuery(Name = "doctor_id")] Guid? doctorId, DateOnly? from, DateOnly? to, ScheduleService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListExceptionsAsync(doctorId, from, to, ct)))
            .RequirePermission(view).WithName("ListScheduleExceptions");
        g.MapPost("/schedule-exceptions", async (ScheduleExceptionRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateExceptionAsync(r, ct)))
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).Validate<ScheduleExceptionRequest>().WithName("CreateScheduleException");
        g.MapDelete("/schedule-exceptions/{id:guid}", async (Guid id, [FromQuery(Name = "on_conflict")] AffectedAppointmentsAction? onConflict, ScheduleService s, CancellationToken ct) =>
            { await s.DeleteExceptionAsync(id, onConflict, ct); return TypedResults.NoContent(); })
            .RequirePermission(Perm.Schedule.DoctorSchedulesManage).WithName("DeleteScheduleException");

        g.MapGet("/time-blocks", async ([FromQuery(Name = "branch_id")] Guid branchId, DateTimeOffset from, DateTimeOffset to, ScheduleService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListBlocksAsync(branchId, from, to, ct)))
            .RequirePermission(view).WithName("ListTimeBlocks");
        g.MapPost("/time-blocks", async (TimeBlockRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateBlockAsync(r, ct)))
            .RequirePermission(Perm.Schedule.Manage, Perm.Schedule.DoctorSchedulesManage).Validate<TimeBlockRequest>().WithName("CreateTimeBlock");
        g.MapDelete("/time-blocks/{id:guid}", async (Guid id, ScheduleService s, CancellationToken ct) => { await s.DeleteBlockAsync(id, ct); return TypedResults.NoContent(); })
            .RequirePermission(Perm.Schedule.Manage, Perm.Schedule.DoctorSchedulesManage).WithName("DeleteTimeBlock");

        g.MapPost("/appointments", async (CreateAppointmentRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAppointmentAsync(r, ct)))
            .RequirePermission(Perm.Schedule.Manage).Validate<CreateAppointmentRequest>().WithName("CreateAppointment");
        g.MapGet("/appointments/{id:guid}", async (Guid id, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.GetAppointmentAsync(id, ct)))
            .RequirePermission(view).WithName("GetAppointment");
        g.MapPatch("/appointments/{id:guid}", async (Guid id, UpdateAppointmentRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateAppointmentAsync(id, r, ct)))
            .RequirePermission(Perm.Schedule.Manage).WithName("UpdateAppointment");
        g.MapPost("/appointments/{id:guid}/move", async (Guid id, MoveAppointmentRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.MoveAppointmentAsync(id, r, ct)))
            .RequirePermission(Perm.Schedule.Manage).WithName("MoveAppointment");
        g.MapPost("/appointments/{id:guid}/status", async (Guid id, AppointmentStatusRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.ChangeStatusAsync(id, r, ct)))
            .RequirePermission(Perm.Schedule.Manage, Perm.Visits.Complete).Validate<AppointmentStatusRequest>().WithName("ChangeAppointmentStatus");
        g.MapPost("/appointments/{id:guid}/remind", async (Guid id, ScheduleService s, CancellationToken ct) => TypedResults.Ok(new { sent = await s.RemindAsync(id, ct) }))
            .RequirePermission(Perm.Schedule.Manage).WithName("RemindAppointment");

        g.MapGet("/waitlist", async ([FromQuery(Name = "branch_id")] Guid? branchId, WaitlistStatus? status, ScheduleService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListWaitlistAsync(branchId, status, ct)))
            .RequirePermission(Perm.Schedule.Manage, Perm.Schedule.ViewAll).WithName("ListWaitlist");
        g.MapPost("/waitlist", async (WaitlistRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateWaitlistAsync(r, ct)))
            .RequirePermission(Perm.Schedule.Manage).Validate<WaitlistRequest>().WithName("CreateWaitlist");
        g.MapPatch("/waitlist/{id:guid}", async (Guid id, WaitlistRequest r, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateWaitlistAsync(id, r, ct)))
            .RequirePermission(Perm.Schedule.Manage).WithName("UpdateWaitlist");
        return app;
    }
}
