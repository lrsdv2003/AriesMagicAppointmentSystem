using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    public partial class AddMissingBookingArchiveColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedStaffName",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalBookingId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashNotes",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrashReason",
                table: "Bookings",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "AssignedStaffName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OriginalBookingId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TrashNotes",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TrashReason",
                table: "Bookings");
        }
    }
}