using Dental.Domain.Audit;
using Dental.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dental.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations");
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Timezone).HasMaxLength(64);
        b.OwnsOne(x => x.Settings, s => s.ToJson());
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branches");
        b.OwnsOne(x => x.WorkingHours, w =>
        {
            w.ToJson();
            w.OwnsMany(x => x.Days);
        });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("rooms");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChairConfiguration : IEntityTypeConfiguration<Chair>
{
    public void Configure(EntityTypeBuilder<Chair> b)
    {
        b.ToTable("chairs");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.Phone).IsUnique();
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(20);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.OrganizationId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("password_reset_tokens");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        b.ToTable("user_devices");
        b.HasIndex(x => new { x.UserId, x.Fingerprint }).IsUnique();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships");
        b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Property(x => x.Color).HasMaxLength(16);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.OwnsOne(x => x.Limits, l =>
        {
            l.ToJson();
            l.Property(p => p.MaxDiscountPct).HasPrecision(5, 2);
        });
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> b)
    {
        b.ToTable("approval_requests");
        b.HasIndex(x => new { x.OrganizationId, x.Status, x.Type });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> b)
    {
        b.ToTable("document_counters");
        b.HasKey(x => new { x.OrganizationId, x.DocType });
        b.Property(x => x.DocType).HasMaxLength(40);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_log");
        // Партиционированная таблица: ключ партиционирования обязан входить в PK.
        b.HasKey(x => new { x.Id, x.CreatedAt });
        b.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => new { x.OrganizationId, x.IsSuspicious, x.CreatedAt });
        b.Property(x => x.Action).HasMaxLength(60);
        b.Property(x => x.EntityType).HasMaxLength(80);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
    }
}

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.ToTable("message_templates");
        b.HasIndex(x => new { x.OrganizationId, x.Type, x.Channel }).IsUnique();
    }
}

internal sealed class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessage>
{
    public void Configure(EntityTypeBuilder<OutgoingMessage> b)
    {
        b.ToTable("outgoing_messages");
        b.HasIndex(x => new { x.AppointmentId, x.Kind });
    }
}

internal sealed class ReportSubscriptionConfiguration : IEntityTypeConfiguration<ReportSubscription>
{
    public void Configure(EntityTypeBuilder<ReportSubscription> b) => b.ToTable("report_subscriptions");
}

internal sealed class SavedReportFilterConfiguration : IEntityTypeConfiguration<SavedReportFilter>
{
    public void Configure(EntityTypeBuilder<SavedReportFilter> b) => b.ToTable("saved_report_filters");
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasIndex(x => x.ProcessedAt);
    }
}

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("idempotency_keys");
        b.HasKey(x => new { x.OrganizationId, x.Key });
        b.Property(x => x.Key).HasMaxLength(200);
        b.HasIndex(x => x.ExpiresAt);
    }
}
