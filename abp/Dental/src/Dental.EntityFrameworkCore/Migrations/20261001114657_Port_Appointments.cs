using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dental.Migrations
{
    /// <inheritdoc />
    public partial class Port_Appointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAppointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChairId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancelReasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    MoveReasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reminder24hSentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reminder2hSentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ForcedOutsideSchedule = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppAppointments", x => x.Id);
                    table.CheckConstraint("CK_AppAppointments_Time", "\"EndsAt\" > \"StartsAt\"");
                });

            migrationBuilder.CreateTable(
                name: "AppWaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreferredFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PreferredTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppWaitlistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAppointmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PlannedPrice = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAppointmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAppointmentLines_AppAppointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "AppAppointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAppointmentLines_AppointmentId",
                table: "AppAppointmentLines",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAppointments_TenantId_BranchId_StartsAt",
                table: "AppAppointments",
                columns: new[] { "TenantId", "BranchId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAppointments_TenantId_PatientId_StartsAt",
                table: "AppAppointments",
                columns: new[] { "TenantId", "PatientId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppWaitlistEntries_TenantId_BranchId_Status",
                table: "AppWaitlistEntries",
                columns: new[] { "TenantId", "BranchId", "Status" });

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // UTC wall timestamps follow the existing ABP timestamp mapping. [start,end) permits adjacent appointments.
                migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
                migrationBuilder.Sql("""
                    ALTER TABLE "AppAppointments" ADD CONSTRAINT "EX_AppAppointments_Doctor"
                    EXCLUDE USING gist (
                        (COALESCE("TenantId", '00000000-0000-0000-0000-000000000000'::uuid)) WITH =,
                        "DoctorId" WITH =,
                        tsrange("StartsAt", "EndsAt", '[)') WITH &&
                    ) WHERE (NOT "IsDeleted" AND "Status" NOT IN (5, 6));
                    ALTER TABLE "AppAppointments" ADD CONSTRAINT "EX_AppAppointments_Chair"
                    EXCLUDE USING gist (
                        (COALESCE("TenantId", '00000000-0000-0000-0000-000000000000'::uuid)) WITH =,
                        "ChairId" WITH =,
                        tsrange("StartsAt", "EndsAt", '[)') WITH &&
                    ) WHERE (NOT "IsDeleted" AND "ChairId" IS NOT NULL AND "Status" NOT IN (5, 6));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAppointmentLines");

            migrationBuilder.DropTable(
                name: "AppWaitlistEntries");

            migrationBuilder.DropTable(
                name: "AppAppointments");
        }
    }
}
