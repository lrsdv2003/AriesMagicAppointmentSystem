using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class ProtectFinancialHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Bookings_BookingId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_RefundRequests_Bookings_BookingId",
                table: "RefundRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefundRequests_ApprovedAmount",
                table: "RefundRequests",
                sql: "[ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [Amount])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefundRequests_PositiveAmount",
                table: "RefundRequests",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PositiveAmount",
                table: "Payments",
                sql: "[Amount] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_EventDate_StartTime_EndTime",
                table: "Bookings",
                columns: new[] { "EventDate", "StartTime", "EndTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Bookings_BookingId",
                table: "Payments",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RefundRequests_Bookings_BookingId",
                table: "RefundRequests",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Bookings_BookingId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_RefundRequests_Bookings_BookingId",
                table: "RefundRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefundRequests_ApprovedAmount",
                table: "RefundRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefundRequests_PositiveAmount",
                table: "RefundRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PositiveAmount",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_EventDate_StartTime_EndTime",
                table: "Bookings");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Bookings_BookingId",
                table: "Payments",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RefundRequests_Bookings_BookingId",
                table: "RefundRequests",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
