using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dental.Migrations
{
    /// <inheritdoc />
    public partial class ReportSubscriptionSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "AppReportSubscriptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "daily");

            migrationBuilder.AddColumn<string>(
                name: "GroupBy",
                table: "AppReportSubscriptions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<int>(
                name: "Weekday",
                table: "AppReportSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "AppReportSubscriptions");

            migrationBuilder.DropColumn(
                name: "GroupBy",
                table: "AppReportSubscriptions");

            migrationBuilder.DropColumn(
                name: "Weekday",
                table: "AppReportSubscriptions");
        }
    }
}
