using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    public partial class CanonicalClientSupportAndMessageContext20261108 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Other historical schema upgrades have their own separate migrations.
            // This migration only adds canonical conversation ownership and message booking context.
            migrationBuilder.AddColumn<string>(
                name: "SupportClientId",
                table: "Conversations",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedBookingId",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_SupportClientId",
                table: "Conversations",
                column: "SupportClientId",
                unique: true,
                filter: "[SupportClientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RelatedBookingId",
                table: "Messages",
                column: "RelatedBookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_AspNetUsers_SupportClientId",
                table: "Conversations",
                column: "SupportClientId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Bookings_RelatedBookingId",
                table: "Messages",
                column: "RelatedBookingId",
                principalTable: "Bookings",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Bookings_RelatedBookingId", table: "Messages");
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_AspNetUsers_SupportClientId", table: "Conversations");
            migrationBuilder.DropIndex(
                name: "IX_Messages_RelatedBookingId", table: "Messages");
            migrationBuilder.DropIndex(
                name: "IX_Conversations_SupportClientId", table: "Conversations");
            migrationBuilder.DropColumn(name: "RelatedBookingId", table: "Messages");
            migrationBuilder.DropColumn(name: "SupportClientId", table: "Conversations");
        }
    }
}
