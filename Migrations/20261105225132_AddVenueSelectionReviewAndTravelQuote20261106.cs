using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueSelectionReviewAndTravelQuote20261106 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TravelFeeStatus",
                table: "Bookings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TravelQuoteAcceptedAtUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueDirections",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueManualDetails",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueSelectionMethod",
                table: "Bookings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VenueStaffReviewedAtUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueVerificationStatus",
                table: "Bookings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TravelFeeStatus",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TravelQuoteAcceptedAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueDirections",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueManualDetails",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueSelectionMethod",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueStaffReviewedAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueVerificationStatus",
                table: "Bookings");
        }
    }
}
