using Dental.Domain.Catalog;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dental.Infrastructure.Persistence.Configurations;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.ToTable("patients");
        b.Ignore(x => x.FullName);
        b.Property(x => x.Iin).HasMaxLength(12);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.PhoneExtra).HasMaxLength(20);
        b.HasIndex(x => new { x.OrganizationId, x.Iin }).IsUnique().HasFilter("iin IS NOT NULL AND deleted_at IS NULL AND merged_into_id IS NULL");
        b.HasIndex(x => new { x.OrganizationId, x.Phone });
        b.HasOne<LeadSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PatientConsentConfiguration : IEntityTypeConfiguration<PatientConsent>
{
    public void Configure(EntityTypeBuilder<PatientConsent> b)
    {
        b.ToTable("patient_consents");
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PatientBalanceConfiguration : IEntityTypeConfiguration<PatientBalance>
{
    public void Configure(EntityTypeBuilder<PatientBalance> b)
    {
        b.ToTable("patient_balances");
        b.HasIndex(x => x.PatientId).IsUnique();
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeadSourceConfiguration : IEntityTypeConfiguration<LeadSource>
{
    public void Configure(EntityTypeBuilder<LeadSource> b) => b.ToTable("lead_sources");
}

internal sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> b)
    {
        b.ToTable("service_categories");
        b.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("services");
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasIndex(x => new { x.OrganizationId, x.Code });
        b.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> b)
    {
        b.ToTable("price_lists");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> b)
    {
        b.ToTable("price_list_items");
        b.HasIndex(x => new { x.PriceListId, x.ServiceId }).IsUnique();
        b.HasOne<PriceList>().WithMany().HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TechCardConfiguration : IEntityTypeConfiguration<TechCard>
{
    public void Configure(EntityTypeBuilder<TechCard> b)
    {
        b.ToTable("tech_cards");
        b.HasIndex(x => new { x.ServiceId, x.Version }).IsUnique();
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.TechCardId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TechCardItemConfiguration : IEntityTypeConfiguration<TechCardItem>
{
    public void Configure(EntityTypeBuilder<TechCardItem> b)
    {
        b.ToTable("tech_card_items");
        b.Property(x => x.Quantity).HasPrecision(14, 3);
    }
}

internal sealed class DoctorScheduleConfiguration : IEntityTypeConfiguration<DoctorSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorSchedule> b)
    {
        b.ToTable("doctor_schedules");
        b.HasIndex(x => new { x.MembershipId, x.Weekday });
        b.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> b)
    {
        b.ToTable("schedule_exceptions");
        b.HasIndex(x => new { x.MembershipId, x.DateFrom, x.DateTo });
        b.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TimeBlockConfiguration : IEntityTypeConfiguration<TimeBlock>
{
    public void Configure(EntityTypeBuilder<TimeBlock> b)
    {
        b.ToTable("time_blocks");
        b.HasIndex(x => new { x.BranchId, x.StartsAt });
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments");
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => new { x.BranchId, x.StartsAt });
        b.HasIndex(x => new { x.DoctorId, x.StartsAt });
        b.HasIndex(x => new { x.PatientId, x.StartsAt });
        b.HasIndex(x => new { x.Status, x.StartsAt });
        b.HasMany(x => x.Services).WithOne().HasForeignKey(s => s.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Chair>().WithMany().HasForeignKey(x => x.ChairId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CancelReason>().WithMany().HasForeignKey(x => x.CancelReasonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentServiceConfiguration : IEntityTypeConfiguration<AppointmentService>
{
    public void Configure(EntityTypeBuilder<AppointmentService> b)
    {
        b.ToTable("appointment_services");
        b.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CancelReasonConfiguration : IEntityTypeConfiguration<CancelReason>
{
    public void Configure(EntityTypeBuilder<CancelReason> b) => b.ToTable("cancel_reasons");
}

internal sealed class WaitlistConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("waitlist");
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> b)
    {
        b.ToTable("visits");
        b.Ignore(x => x.Debt);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => new { x.BranchId, x.ClosedAt });
        b.HasIndex(x => new { x.DoctorId, x.ClosedAt });
        b.HasIndex(x => new { x.PatientId });
        b.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("appointment_id IS NOT NULL");
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.VisitId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Materials).WithOne().HasForeignKey(i => i.VisitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Membership>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VisitItemConfiguration : IEntityTypeConfiguration<VisitItem>
{
    public void Configure(EntityTypeBuilder<VisitItem> b)
    {
        b.ToTable("visit_items");
        b.Property(x => x.DiscountPct).HasPrecision(5, 2);
        b.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VisitMaterialUsageConfiguration : IEntityTypeConfiguration<VisitMaterialUsage>
{
    public void Configure(EntityTypeBuilder<VisitMaterialUsage> b)
    {
        b.ToTable("visit_material_usage");
        b.Property(x => x.Quantity).HasPrecision(14, 3);
        b.Property(x => x.NormQuantity).HasPrecision(14, 3);
    }
}
