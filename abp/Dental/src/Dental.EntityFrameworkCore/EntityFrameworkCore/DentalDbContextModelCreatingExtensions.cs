using System;
using System.Collections.Generic;
using System.Text.Json;
using Dental.Branches;
using Dental.Roles;
using Dental.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

/// <summary>
/// Маппинг сущностей приложения. Модули добавляют сюда свой метод Configure{Module}(builder)
/// и вызывают его из DentalDbContext.OnModelCreating. Таблицы: префикс "App" + имя во множественном числе.
/// </summary>
public static class DentalDbContextModelCreatingExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void ConfigureDentalFoundation(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Branch>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "Branches", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(BranchConsts.MaxNameLength);
            b.Property(x => x.Address).HasMaxLength(BranchConsts.MaxAddressLength);
            b.Property(x => x.Phone).HasMaxLength(BranchConsts.MaxPhoneLength);
            b.Property(x => x.WorkingHours)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, Json),
                    v => JsonSerializer.Deserialize<List<WorkingDay>>(v, Json) ?? new List<WorkingDay>(),
                    new ValueComparer<List<WorkingDay>>(
                        (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                        v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<WorkingDay>>(JsonSerializer.Serialize(v, Json), Json)!));
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        builder.Entity<Room>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "Rooms", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(BranchConsts.MaxRoomNameLength);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.BranchId });
        });

        builder.Entity<Chair>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "Chairs", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(BranchConsts.MaxChairNameLength);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.BranchId });
        });

        builder.Entity<Employee>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "Employees", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.FullName).IsRequired().HasMaxLength(EmployeeConsts.MaxFullNameLength);
            b.Property(x => x.Phone).HasMaxLength(EmployeeConsts.MaxPhoneLength);
            b.Property(x => x.Specialty).HasMaxLength(EmployeeConsts.MaxSpecialtyLength);
            b.Property(x => x.Color).HasMaxLength(EmployeeConsts.MaxColorLength);
            b.Property(x => x.BranchIds).HasColumnType("uuid[]");
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        builder.Entity<RoleLimit>(b =>
        {
            b.ToTable(DentalConsts.DbTablePrefix + "RoleLimits", DentalConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.MaxDiscountPct).HasPrecision(5, 2);
            b.HasIndex(x => x.RoleId).IsUnique();
        });
    }
}
