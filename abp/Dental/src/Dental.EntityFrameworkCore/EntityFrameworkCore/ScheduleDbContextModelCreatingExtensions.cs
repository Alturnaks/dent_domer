using Dental.Schedule;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

public static class ScheduleDbContextModelCreatingExtensions
{
    public static void ConfigureSchedule(this ModelBuilder builder)
    {
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
