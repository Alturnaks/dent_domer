using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dental.Migrations
{
    /// <inheritdoc />
    public partial class Port_PatientsCatalogApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCancelReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCancelReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLeadSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLeadSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPriceLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPriceLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPriceLists_AppBranches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "AppBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppServiceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppServiceCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppServiceCategories_AppServiceCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AppServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppPatients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    Iin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PhoneExtra = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TagsRaw = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsVip = table.Column<bool>(type: "boolean", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPatients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPatients_AppLeadSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "AppLeadSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AppServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DurationMin = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppServices_AppServiceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AppServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppPatientBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<long>(type: "bigint", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPatientBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPatientBalances_AppPatients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "AppPatients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppPatientConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPatientConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPatientConsents_AppPatients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "AppPatients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppPriceListItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPriceListItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPriceListItems_AppPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "AppPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppPriceListItems_AppServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AppServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTechCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTechCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTechCards_AppServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "AppServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppTechCardItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TechCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTechCardItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTechCardItems_AppTechCards_TechCardId",
                        column: x => x.TechCardId,
                        principalTable: "AppTechCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppApprovalRequests_EntityId_Type",
                table: "AppApprovalRequests",
                columns: new[] { "EntityId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_AppApprovalRequests_TenantId_Status_CreationTime",
                table: "AppApprovalRequests",
                columns: new[] { "TenantId", "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCancelReasons_TenantId_Type",
                table: "AppCancelReasons",
                columns: new[] { "TenantId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLeadSources_TenantId_Name",
                table: "AppLeadSources",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_UserId_ReadAt_CreationTime",
                table: "AppNotifications",
                columns: new[] { "UserId", "ReadAt", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPatientBalances_PatientId",
                table: "AppPatientBalances",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPatientConsents_PatientId",
                table: "AppPatientConsents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_SourceId",
                table: "AppPatients",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_TenantId_CreationTime",
                table: "AppPatients",
                columns: new[] { "TenantId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_TenantId_Iin",
                table: "AppPatients",
                columns: new[] { "TenantId", "Iin" },
                unique: true,
                filter: "\"Iin\" IS NOT NULL AND \"IsDeleted\" = false AND \"MergedIntoId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_TenantId_LastName_FirstName",
                table: "AppPatients",
                columns: new[] { "TenantId", "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_TenantId_Phone",
                table: "AppPatients",
                columns: new[] { "TenantId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPatients_TenantId_PhoneExtra",
                table: "AppPatients",
                columns: new[] { "TenantId", "PhoneExtra" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPriceListItems_PriceListId_ServiceId",
                table: "AppPriceListItems",
                columns: new[] { "PriceListId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPriceListItems_ServiceId",
                table: "AppPriceListItems",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPriceLists_BranchId",
                table: "AppPriceLists",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPriceLists_TenantId_BranchId_ValidFrom",
                table: "AppPriceLists",
                columns: new[] { "TenantId", "BranchId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppServiceCategories_ParentId",
                table: "AppServiceCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppServiceCategories_TenantId_ParentId",
                table: "AppServiceCategories",
                columns: new[] { "TenantId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppServices_CategoryId",
                table: "AppServices",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AppServices_TenantId_Code",
                table: "AppServices",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppTechCardItems_ItemId",
                table: "AppTechCardItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTechCardItems_TechCardId",
                table: "AppTechCardItems",
                column: "TechCardId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTechCards_ServiceId_Version",
                table: "AppTechCards",
                columns: new[] { "ServiceId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppApprovalRequests");

            migrationBuilder.DropTable(
                name: "AppCancelReasons");

            migrationBuilder.DropTable(
                name: "AppNotifications");

            migrationBuilder.DropTable(
                name: "AppPatientBalances");

            migrationBuilder.DropTable(
                name: "AppPatientConsents");

            migrationBuilder.DropTable(
                name: "AppPriceListItems");

            migrationBuilder.DropTable(
                name: "AppTechCardItems");

            migrationBuilder.DropTable(
                name: "AppPatients");

            migrationBuilder.DropTable(
                name: "AppPriceLists");

            migrationBuilder.DropTable(
                name: "AppTechCards");

            migrationBuilder.DropTable(
                name: "AppLeadSources");

            migrationBuilder.DropTable(
                name: "AppServices");

            migrationBuilder.DropTable(
                name: "AppServiceCategories");
        }
    }
}
