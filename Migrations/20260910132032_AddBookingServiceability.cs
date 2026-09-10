using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingServiceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DistanceKm",
                table: "Bookings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsServiceable",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresManualReview",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ServiceZone",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TravelFee",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<double>(
                name: "VenueLatitude",
                table: "Bookings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VenueLongitude",
                table: "Bookings",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsServiceable",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RequiresManualReview",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ServiceZone",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TravelFee",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueLatitude",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VenueLongitude",
                table: "Bookings");
        }
    }
}
