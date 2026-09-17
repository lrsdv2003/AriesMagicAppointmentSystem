using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddRescheduleRequestAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientFacingReason",
                table: "RescheduleRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "RescheduleRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalDate",
                table: "RescheduleRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalEndTime",
                table: "RescheduleRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalStartTime",
                table: "RescheduleRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByName",
                table: "RescheduleRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserId",
                table: "RescheduleRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientFacingReason",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "OriginalDate",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "OriginalEndTime",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "OriginalStartTime",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedByName",
                table: "RescheduleRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "RescheduleRequests");
        }
    }
}
