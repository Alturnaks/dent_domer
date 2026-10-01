using Dental.Approvals;
using Dental.Branches;
using Dental.Catalog;
using Dental.Notifications;
using Dental.Patients;
using Dental.References;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

/// <summary>Маппинг модулей: пациенты, справочники, каталог (услуги/прайсы/техкарты), подтверждения, уведомления.</summary>
public static class DentalPatientsCatalogModelCreatingExtensions
{
    public static void ConfigurePatientsCatalogApprovals(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
        const string p = DentalConsts.DbTablePrefix;
        var schema = DentalConsts.DbSchema;

        builder.Entity<Patient>(b =>
        {
            b.ToTable(p + "Patients", schema);
            b.ConfigureByConvention();
            b.Ignore(x => x.FullName);
            b.Ignore(x => x.Tags);
            b.Property(x => x.LastName).IsRequired().HasMaxLength(PatientConsts.MaxNameLength);
            b.Property(x => x.FirstName).IsRequired().HasMaxLength(PatientConsts.MaxNameLength);
            b.Property(x => x.MiddleName).HasMaxLength(PatientConsts.MaxNameLength);
            b.Property(x => x.Iin).HasMaxLength(PatientConsts.MaxIinLength);
            b.Property(x => x.Phone).HasMaxLength(PatientConsts.MaxPhoneLength);
            b.Property(x => x.PhoneExtra).HasMaxLength(PatientConsts.MaxPhoneLength);
            b.Property(x => x.Email).HasMaxLength(PatientConsts.MaxEmailLength);
            b.Property(x => x.Address).HasMaxLength(PatientConsts.MaxAddressLength);
            b.Property(x => x.Notes).HasMaxLength(PatientConsts.MaxNotesLength);
            b.Property(x => x.TagsRaw).IsRequired().HasMaxLength(PatientConsts.MaxTagsLength);
            b.HasOne<LeadSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.Phone });
            b.HasIndex(x => new { x.TenantId, x.PhoneExtra });
            // ИИН уникален среди живых карточек (у слитого дубля ИИН обнуляется).
            b.HasIndex(x => new { x.TenantId, x.Iin }).IsUnique().HasFilter("\"Iin\" IS NOT NULL AND \"IsDeleted\" = false AND \"MergedIntoId\" IS NULL");
            b.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
            b.HasIndex(x => new { x.TenantId, x.CreationTime });
        });

        builder.Entity<PatientConsent>(b =>
        {
            b.ToTable(p + "PatientConsents", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired().HasMaxLength(PatientConsts.MaxConsentTypeLength);
            b.Property(x => x.FileUrl).HasMaxLength(PatientConsts.MaxFileUrlLength);
            b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.PatientId);
        });

        builder.Entity<PatientBalance>(b =>
        {
            b.ToTable(p + "PatientBalances", schema);
            b.ConfigureByConvention();
            b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.PatientId).IsUnique();
        });

        builder.Entity<LeadSource>(b =>
        {
            b.ToTable(p + "LeadSources", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(PatientConsts.MaxLeadSourceNameLength);
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        builder.Entity<CancelReason>(b =>
        {
            b.ToTable(p + "CancelReasons", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(ReferenceConsts.MaxNameLength);
            b.HasIndex(x => new { x.TenantId, x.Type });
        });

        builder.Entity<ServiceCategory>(b =>
        {
            b.ToTable(p + "ServiceCategories", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(CatalogConsts.MaxCategoryNameLength);
            b.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.ParentId });
        });

        builder.Entity<ClinicService>(b =>
        {
            b.ToTable(p + "Services", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(CatalogConsts.MaxServiceCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CatalogConsts.MaxServiceNameLength);
            b.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => x.CategoryId);
        });

        builder.Entity<PriceList>(b =>
        {
            b.ToTable(p + "PriceLists", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(CatalogConsts.MaxPriceListNameLength);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PriceListId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Items).AutoInclude();
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.ValidFrom });
        });

        builder.Entity<PriceListItem>(b =>
        {
            b.ToTable(p + "PriceListItems", schema);
            b.ConfigureByConvention();
            b.HasOne<ClinicService>().WithMany().HasForeignKey(x => x.ServiceId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.PriceListId, x.ServiceId }).IsUnique();
        });

        builder.Entity<TechCard>(b =>
        {
            b.ToTable(p + "TechCards", schema);
            b.ConfigureByConvention();
            b.HasOne<ClinicService>().WithMany().HasForeignKey(x => x.ServiceId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TechCardId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Items).AutoInclude();
            b.HasIndex(x => new { x.ServiceId, x.Version }).IsUnique();
        });

        builder.Entity<TechCardItem>(b =>
        {
            b.ToTable(p + "TechCardItems", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Quantity).HasPrecision(18, 4);
            // ItemId — номенклатура склада (модуль Inventory); FK добавит модуль склада.
            b.HasIndex(x => x.ItemId);
        });

        builder.Entity<ApprovalRequest>(b =>
        {
            b.ToTable(p + "ApprovalRequests", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired().HasMaxLength(ApprovalConsts.MaxTypeLength);
            b.Property(x => x.EntityType).IsRequired().HasMaxLength(ApprovalConsts.MaxEntityTypeLength);
            b.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Summary).HasMaxLength(ApprovalConsts.MaxSummaryLength);
            b.Property(x => x.Comment).HasMaxLength(ApprovalConsts.MaxCommentLength);
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreationTime });
            b.HasIndex(x => new { x.EntityId, x.Type });
        });

        builder.Entity<Notification>(b =>
        {
            b.ToTable(p + "Notifications", schema);
            b.ConfigureByConvention();
            b.Property(x => x.Type).IsRequired().HasMaxLength(NotificationConsts.MaxTypeLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(NotificationConsts.MaxTitleLength);
            b.Property(x => x.Body).HasMaxLength(NotificationConsts.MaxBodyLength);
            b.Property(x => x.EntityType).HasMaxLength(NotificationConsts.MaxEntityTypeLength);
            b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreationTime });
        });
    }
}
