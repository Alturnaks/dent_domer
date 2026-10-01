using Dental.Schedule;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

public static class ScheduleDbContextModelCreatingExtensions
{
    public static void ConfigureSchedule(this ModelBuilder builder)
    {
        builder.Entity<Appointment>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "Appointments", DentalConsts.DbSchema, t =>
                t.HasCheckConstraint("CK_AppAppointments_Time", "\"EndsAt\" > \"StartsAt\""));
            b.ConfigureByConvention();
            b.Property(a => a.Comment).HasMaxLength(ScheduleConsts.MaxCommentLength);
            b.Property(a => a.CancelComment).HasMaxLength(ScheduleConsts.MaxCommentLength);
            b.HasMany(a => a.Services).WithOne().HasForeignKey(l => l.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(a => new { a.TenantId, a.BranchId, a.StartsAt });
            b.HasIndex(a => new { a.TenantId, a.PatientId, a.StartsAt });
        });
        builder.Entity<AppointmentLine>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "AppointmentLines", DentalConsts.DbSchema);
            b.ConfigureByConvention();
        });
        builder.Entity<WaitlistEntry>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "WaitlistEntries", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(a => a.Comment).HasMaxLength(ScheduleConsts.MaxCommentLength);
            b.HasIndex(a => new { a.TenantId, a.BranchId, a.Status });
        });
        builder.Entity<DoctorSchedule>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "DoctorSchedules", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(s => new { s.TenantId, s.BranchId, s.DoctorId, s.Weekday });
        });
        builder.Entity<ScheduleException>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "ScheduleExceptions", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(s => s.Comment).HasMaxLength(ScheduleConsts.MaxCommentLength);
            b.HasIndex(s => new { s.TenantId, s.DoctorId, s.DateFrom, s.DateTo });
        });
        builder.Entity<TimeBlock>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "TimeBlocks", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(s => s.Reason).HasMaxLength(ScheduleConsts.MaxCommentLength);
            b.HasIndex(s => new { s.TenantId, s.BranchId, s.StartsAt });
        });
    }
}
