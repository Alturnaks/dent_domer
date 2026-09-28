using Dental.Domain.Audit;
using Dental.Domain.Cash;
using Dental.Domain.Catalog;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Payroll;
using Dental.Domain.Purchasing;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dental.Application.Common;

/// <summary>Порт к БД для use-case. Реализация — AppDbContext (EF Core + Npgsql) в Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Chair> Chairs { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<Role> Roles { get; }
    DbSet<ApprovalRequest> ApprovalRequests { get; }

    DbSet<Patient> Patients { get; }
    DbSet<PatientConsent> PatientConsents { get; }
    DbSet<PatientBalance> PatientBalances { get; }
    DbSet<LeadSource> LeadSources { get; }

    DbSet<ServiceCategory> ServiceCategories { get; }
    DbSet<Service> Services { get; }
    DbSet<PriceList> PriceLists { get; }
    DbSet<PriceListItem> PriceListItems { get; }
    DbSet<TechCard> TechCards { get; }
    DbSet<TechCardItem> TechCardItems { get; }

    DbSet<DoctorSchedule> DoctorSchedules { get; }
    DbSet<ScheduleException> ScheduleExceptions { get; }
    DbSet<TimeBlock> TimeBlocks { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AppointmentService> AppointmentServices { get; }
    DbSet<CancelReason> CancelReasons { get; }
    DbSet<WaitlistEntry> Waitlist { get; }

    DbSet<Visit> Visits { get; }
    DbSet<VisitItem> VisitItems { get; }
    DbSet<VisitMaterialUsage> VisitMaterialUsages { get; }

    DbSet<CashRegister> CashRegisters { get; }
    DbSet<CashShift> CashShifts { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<CashOperation> CashOperations { get; }

    DbSet<Warehouse> Warehouses { get; }
    DbSet<ItemCategory> ItemCategories { get; }
    DbSet<Item> Items { get; }
    DbSet<ItemUnit> ItemUnits { get; }
    DbSet<ItemStockLevel> ItemStockLevels { get; }
    DbSet<Batch> Batches { get; }
    DbSet<StockDocument> StockDocuments { get; }
    DbSet<StockDocumentLine> StockDocumentLines { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<WriteoffReason> WriteoffReasons { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<SupplierItem> SupplierItems { get; }
    DbSet<PurchaseRequest> PurchaseRequests { get; }
    DbSet<PurchaseRequestLine> PurchaseRequestLines { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<SupplierInvoice> SupplierInvoices { get; }

    DbSet<PayrollScheme> PayrollSchemes { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollEntry> PayrollEntries { get; }

    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<OutgoingMessage> OutgoingMessages { get; }
    DbSet<ReportSubscription> ReportSubscriptions { get; }
    DbSet<SavedReportFilter> SavedReportFilters { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
    DbSet<DocumentCounter> DocumentCounters { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Следующий номер документа по типу в рамках организации (в текущей транзакции).</summary>
    Task<long> NextNumberAsync(string docType, CancellationToken ct);
}

/// <summary>Явная запись бизнес-событий в журнал аудита (причина, подозрительность).</summary>
public interface IAuditService
{
    void Log(string entityType, Guid? entityId, string action, object? diff = null, string? reason = null,
        bool suspicious = false, Guid? branchId = null);
}

/// <summary>Уведомления пользователям (колокольчик).</summary>
public interface INotificationService
{
    /// <summary>Уведомить всех активных сотрудников организации, у которых есть право (или роль owner).</summary>
    Task NotifyByPermissionAsync(string permission, string type, string title, string? body, string? entityType, Guid? entityId,
        Guid? branchId, CancellationToken ct);

    Task NotifyOwnersAsync(string type, string title, string? body, string? entityType, Guid? entityId, CancellationToken ct);

    void Notify(Guid userId, string type, string title, string? body, string? entityType, Guid? entityId);
}

/// <summary>Отложенные события (outbox), обрабатываются после коммита.</summary>
public interface IOutbox
{
    void Enqueue(string type, object payload);
}

public static class OutboxTypes
{
    public const string AppointmentCancelled = "appointment.cancelled";
}
