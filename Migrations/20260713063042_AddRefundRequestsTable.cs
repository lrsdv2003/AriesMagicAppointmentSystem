using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    public partial class AddRefundRequestsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefundRequests",
                columns: table => new
                {
                    Id = table.Column<int>(
                        type: "int",
                        nullable: false)
                        .Annotation(
                            "SqlServer:Identity",
                            "1, 1"),

                    BookingId = table.Column<int>(
                        type: "int",
                        nullable: false),

                    Amount = table.Column<decimal>(
                        type: "decimal(18,2)",
                        nullable: false),

                    GCashAccountName = table.Column<string>(
                        type: "nvarchar(100)",
                        maxLength: 100,
                        nullable: false),

                    GCashNumber = table.Column<string>(
                        type: "nvarchar(20)",
                        maxLength: 20,
                        nullable: false),

                    PaymentProofImagePath = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: false),

                    ClientReason = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true),

                    Status = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: false),

                    AdminRemarks = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: true),

                    RequestedAt = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: false),

                    ProcessedAt = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_RefundRequests",
                        x => x.Id);

                    table.ForeignKey(
                        name: "FK_RefundRequests_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequests_BookingId",
                table: "RefundRequests",
                column: "BookingId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefundRequests");
        }
    }
}