using Dental.Domain.Common;

namespace Dental.Domain.Organizations;

public enum OrganizationType { ClinicNetwork, Lab, Supplier, Platform }

/// <summary>Организация (арендатор). Сама не ITenantEntity: её id и есть tenant.</summary>
[Audited]
public class Organization : Entity, IHasTimestamps
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public OrganizationType Type { get; set; } = OrganizationType.ClinicNetwork;
    public string Timezone { get; set; } = "Asia/Almaty";
    public string Currency { get; set; } = Money.DefaultCurrency;
    public OrganizationSettings Settings { get; set; } = new();
    public string? LogoUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class OrganizationSettings
{
    public int SlotMinutes { get; set; } = 15;
    public TimeOnly DefaultOpen { get; set; } = new(9, 0);
    public TimeOnly DefaultClose { get; set; } = new(20, 0);
    public int NoShowAfterMinutes { get; set; } = 60;
    public bool Reminder24h { get; set; } = true;
    public bool Reminder2h { get; set; } = true;
    public bool AllowNegativeStock { get; set; } = true;
    /// <summary>База зарплаты: только полностью оплаченные визиты (true) или все закрытые.</summary>
    public bool PayrollOnlyPaidVisits { get; set; }
    /// <summary>Порог суммы заказа поставщику, выше которого нужно подтверждение (тиыны).</summary>
    public long PurchaseOrderApprovalThreshold { get; set; } = 50_000_000;
    /// <summary>Подозрительное ручное списание дороже (тиыны).</summary>
    public long SuspiciousWriteoffAmount { get; set; } = 5_000_000;
    /// <summary>Подозрительное расхождение инвентаризации дороже (тиыны).</summary>
    public long SuspiciousInventoryDiffAmount { get; set; } = 2_000_000;
    public TimeOnly DailySummaryTime { get; set; } = new(21, 0);
}

[Audited]
public class Branch : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public WorkingHours WorkingHours { get; set; } = WorkingHours.Default();
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Часы работы по дням недели (0 = воскресенье … 6 = суббота, как DayOfWeek).</summary>
public class WorkingHours
{
    public List<WorkingDay> Days { get; set; } = [];

    public static WorkingHours Default() => new()
    {
        Days = Enumerable.Range(0, 7).Select(d => new WorkingDay
        {
            DayOfWeek = d,
            IsWorking = d != 0,
            Open = new TimeOnly(9, 0),
            Close = d == 6 ? new TimeOnly(15, 0) : new TimeOnly(20, 0),
        }).ToList(),
    };

    public WorkingDay? For(DayOfWeek day) => Days.FirstOrDefault(d => d.DayOfWeek == (int)day);
}

public class WorkingDay
{
    public int DayOfWeek { get; set; }
    public bool IsWorking { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}

[Audited]
public class Room : TenantEntity, ISoftDeletable
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset? DeletedAt { get; set; }
}

[Audited]
public class Chair : TenantEntity, ISoftDeletable
{
    public Guid BranchId { get; set; }
    public Guid? RoomId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Глобальная учётная запись. Не принадлежит организации.</summary>
public class User : Entity, IHasTimestamps
{
    public string? Email { get; set; }
    [Sensitive] public string? Phone { get; set; }
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public Guid? LastOrganizationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Refresh-токен (хранится хэш). Глобальная таблица.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
}

/// <summary>Известные устройства пользователя (для события «вход с нового устройства»).</summary>
public class UserDevice : Entity
{
    public Guid UserId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public enum StaffPosition { Owner, Admin, SeniorAdmin, Doctor, Assistant, Storekeeper, Cashier, Other }

[Audited]
public class Membership : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public bool AllBranches { get; set; }
    public List<Guid> BranchIds { get; set; } = [];
    public StaffPosition Position { get; set; } = StaffPosition.Other;
    public string? Specialty { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? FiredAt { get; set; }

    public bool HasBranch(Guid branchId) => AllBranches || BranchIds.Contains(branchId);
}

[Audited]
public class Role : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = "";
    /// <summary>Код пресета (owner, senior_admin, admin, doctor) или custom.</summary>
    public string Code { get; set; } = "custom";
    public bool IsPreset { get; set; }
    public List<string> Permissions { get; set; } = [];
    public RoleLimits Limits { get; set; } = new();
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Лимиты роли. null = без ограничения.</summary>
public class RoleLimits
{
    public decimal? MaxDiscountPct { get; set; }
    public long? MaxWriteoffAmount { get; set; }
    public long? MaxRefundAmount { get; set; }
    public bool CanEditClosedShiftVisits { get; set; }

    public bool AllowsDiscount(decimal pct) => MaxDiscountPct is null || pct <= MaxDiscountPct.Value;
    public bool AllowsWriteoff(long amount) => MaxWriteoffAmount is null || amount <= MaxWriteoffAmount.Value;
    public bool AllowsRefund(long amount) => MaxRefundAmount is null || amount <= MaxRefundAmount.Value;
}

public enum ApprovalType { Discount, Writeoff, Refund, ClosedVisitEdit, PurchaseOrder, TransferShortage, PayrollPeriodChange }
public enum ApprovalStatus { Pending, Approved, Rejected }

[Audited]
public class ApprovalRequest : TenantEntity
{
    public Guid? BranchId { get; set; }
    public ApprovalType Type { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    /// <summary>Сериализованный JSON с контекстом (было/стало, суммы).</summary>
    public string Payload { get; set; } = "{}";
    /// <summary>Сумма/процент, по которой проверяется лимит подтверждающего.</summary>
    public decimal Amount { get; set; }
    public string? Summary { get; set; }
    public Guid RequestedBy { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Счётчики нумерации документов (ПРХ-000123 …).</summary>
public class DocumentCounter : ITenantEntity
{
    public Guid OrganizationId { get; set; }
    public string DocType { get; set; } = "";
    public long Value { get; set; }
}

/// <summary>Токен сброса пароля (хранится хэш). Глобальная таблица.</summary>
public class PasswordResetToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
