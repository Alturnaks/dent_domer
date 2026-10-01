using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace Dental.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class DentalDbContext :
    AbpDbContext<DentalDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */
    public DbSet<Dental.Branches.Branch> Branches { get; set; }
    public DbSet<Dental.Branches.Room> Rooms { get; set; }
    public DbSet<Dental.Branches.Chair> Chairs { get; set; }
    public DbSet<Dental.Staff.Employee> Employees { get; set; }
    public DbSet<Dental.Roles.RoleLimit> RoleLimits { get; set; }

    // Пациенты, каталог, подтверждения, уведомления, справочники
    public DbSet<Dental.Patients.Patient> Patients { get; set; }
    public DbSet<Dental.Patients.PatientConsent> PatientConsents { get; set; }
    public DbSet<Dental.Patients.PatientBalance> PatientBalances { get; set; }
    public DbSet<Dental.Patients.LeadSource> LeadSources { get; set; }
    public DbSet<Dental.References.CancelReason> CancelReasons { get; set; }
    public DbSet<Dental.Catalog.ServiceCategory> ServiceCategories { get; set; }
    public DbSet<Dental.Catalog.ClinicService> ClinicServices { get; set; }
    public DbSet<Dental.Catalog.PriceList> PriceLists { get; set; }
    public DbSet<Dental.Catalog.PriceListItem> PriceListItems { get; set; }
    public DbSet<Dental.Catalog.TechCard> TechCards { get; set; }
    public DbSet<Dental.Catalog.TechCardItem> TechCardItems { get; set; }
    public DbSet<Dental.Approvals.ApprovalRequest> ApprovalRequests { get; set; }
    public DbSet<Dental.Notifications.Notification> Notifications { get; set; }

    public DbSet<Dental.Schedule.DoctorSchedule> DoctorSchedules { get; set; }
    public DbSet<Dental.Schedule.ScheduleException> ScheduleExceptions { get; set; }
    public DbSet<Dental.Schedule.TimeBlock> TimeBlocks { get; set; }
    public DbSet<Dental.Schedule.Appointment> Appointments { get; set; }
    public DbSet<Dental.Schedule.AppointmentLine> AppointmentLines { get; set; }
    public DbSet<Dental.Schedule.WaitlistEntry> WaitlistEntries { get; set; }
    public DbSet<Dental.Finance.Visit> Visits { get; set; }
    public DbSet<Dental.Finance.VisitItem> VisitItems { get; set; }
    public DbSet<Dental.Finance.VisitMaterial> VisitMaterials { get; set; }
    public DbSet<Dental.Finance.CashRegister> CashRegisters { get; set; }
    public DbSet<Dental.Finance.CashShift> CashShifts { get; set; }
    public DbSet<Dental.Finance.Payment> Payments { get; set; }
    public DbSet<Dental.Finance.ExpenseCategory> ExpenseCategories { get; set; }
    public DbSet<Dental.Finance.Expense> Expenses { get; set; }
    public DbSet<Dental.Finance.CashOperation> CashOperations { get; set; }

    // Склад
    public DbSet<Dental.Inventory.Warehouse> Warehouses { get; set; }
    public DbSet<Dental.Inventory.ItemCategory> ItemCategories { get; set; }
    public DbSet<Dental.Inventory.Item> Items { get; set; }
    public DbSet<Dental.Inventory.ItemUnit> ItemUnits { get; set; }
    public DbSet<Dental.Inventory.ItemStockLevel> ItemStockLevels { get; set; }
    public DbSet<Dental.Inventory.Batch> Batches { get; set; }
    public DbSet<Dental.Inventory.Supplier> Suppliers { get; set; }
    public DbSet<Dental.Inventory.SupplierItem> SupplierItems { get; set; }
    public DbSet<Dental.Inventory.WriteoffReason> WriteoffReasons { get; set; }
    public DbSet<Dental.Inventory.StockDocument> StockDocuments { get; set; }
    public DbSet<Dental.Inventory.StockDocumentLine> StockDocumentLines { get; set; }
    public DbSet<Dental.Inventory.StockMovement> StockMovements { get; set; }
    public DbSet<Dental.Inventory.StockBalance> StockBalances { get; set; }
    public DbSet<Dental.Inventory.DocumentCounter> DocumentCounters { get; set; }



    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public DentalDbContext(DbContextOptions<DentalDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();

        /* Configure your own tables/entities inside here */
        builder.ConfigureDentalFoundation();
        builder.ConfigurePatientsCatalogApprovals();
        builder.ConfigureInventory();
        builder.ConfigureSchedule();
        builder.ConfigureFinance();

        //builder.Entity<YourEntity>(b =>
        //{
        //    b.ToTable(DentalConsts.DbTablePrefix + "YourEntities", DentalConsts.DbSchema);
        //    b.ConfigureByConvention(); //auto configure for the base class props
        //    //...
        //});
    }
}
