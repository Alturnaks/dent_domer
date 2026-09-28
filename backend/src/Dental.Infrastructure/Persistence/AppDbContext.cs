using System.Linq.Expressions;
using System.Reflection;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Domain.Cash;
using Dental.Domain.Catalog;
using Dental.Domain.Common;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Payroll;
using Dental.Domain.Purchasing;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace Dental.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : DbContext(options), IAppDbContext
{
    /// <summary>Текущая организация для глобальных фильтров (параметризуется EF на каждый запрос).</summary>
    public Guid CurrentOrganizationId => tenant.OrganizationId ?? Guid.Empty;

    public ITenantContext Tenant => tenant;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Chair> Chairs => Set<Chair>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientConsent> PatientConsents => Set<PatientConsent>();
    public DbSet<PatientBalance> PatientBalances => Set<PatientBalance>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<TechCard> TechCards => Set<TechCard>();
    public DbSet<TechCardItem> TechCardItems => Set<TechCardItem>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<TimeBlock> TimeBlocks => Set<TimeBlock>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentService> AppointmentServices => Set<AppointmentService>();
    public DbSet<CancelReason> CancelReasons => Set<CancelReason>();
    public DbSet<WaitlistEntry> Waitlist => Set<WaitlistEntry>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitItem> VisitItems => Set<VisitItem>();
    public DbSet<VisitMaterialUsage> VisitMaterialUsages => Set<VisitMaterialUsage>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<CashOperation> CashOperations => Set<CashOperation>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemUnit> ItemUnits => Set<ItemUnit>();
    public DbSet<ItemStockLevel> ItemStockLevels => Set<ItemStockLevel>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<WriteoffReason> WriteoffReasons => Set<WriteoffReason>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierItem> SupplierItems => Set<SupplierItem>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseRequestLine> PurchaseRequestLines => Set<PurchaseRequestLine>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<PayrollScheme> PayrollSchemes => Set<PayrollScheme>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollEntry> PayrollEntries => Set<PayrollEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<OutgoingMessage> OutgoingMessages => Set<OutgoingMessage>();
    public DbSet<ReportSubscription> ReportSubscriptions => Set<ReportSubscription>();
    public DbSet<SavedReportFilter> SavedReportFilters => Set<SavedReportFilter>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public async Task<long> NextNumberAsync(string docType, CancellationToken ct)
    {
        var orgId = tenant.RequiredOrganizationId;
        var result = await Database.SqlQuery<long>($"""
            INSERT INTO document_counters (organization_id, doc_type, value) VALUES ({orgId}, {docType}, 1)
            ON CONFLICT (organization_id, doc_type) DO UPDATE SET value = document_counters.value + 1
            RETURNING value AS "Value"
            """).ToListAsync(ct);
        return result[0];
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (entityType.IsOwned()) continue;

            if (typeof(ITenantEntity).IsAssignableFrom(clr))
            {
                typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
                modelBuilder.Entity(clr).HasIndex(nameof(ITenantEntity.OrganizationId));
            }

            if (typeof(IVersioned).IsAssignableFrom(clr))
            {
                modelBuilder.Entity(clr).Property(nameof(IVersioned.Version)).IsConcurrencyToken();
            }

            foreach (var prop in entityType.GetProperties().Where(p => p.ClrType == typeof(string)))
            {
                if (JsonColumns.Contains(prop.Name))
                {
                    prop.SetColumnType("jsonb");
                    prop.SetMaxLength(null);
                }
                else if (TextColumns.Contains(prop.Name))
                {
                    prop.SetColumnType("text");
                    prop.SetMaxLength(null);
                }
            }
        }
    }

    private static readonly HashSet<string> JsonColumns = ["Payload", "Diff", "Details", "Params", "PreferredTimes"];
    private static readonly HashSet<string> TextColumns = ["Text", "Body", "Notes", "ResponseBody", "Error", "UserAgent", "Comment", "CancelComment", "Summary", "Address"];

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantEntity
    {
        Expression<Func<T, bool>> filter = e => e.OrganizationId == CurrentOrganizationId;
        modelBuilder.Entity<T>().HasQueryFilter(filter);
    }
}
