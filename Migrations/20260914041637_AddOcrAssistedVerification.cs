using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrAssistedVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedAmount",
                table: "RefundRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalPaymentId",
                table: "RefundRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundCompletedAt",
                table: "RefundRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundProofImagePath",
                table: "RefundRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReferenceNumber",
                table: "RefundRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserId",
                table: "RefundRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserName",
                table: "RefundRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Payments",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReviewerNote",
                table: "Payments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionReference",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByUserName",
                table: "Payments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OcrVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    RefundRequestId = table.Column<int>(type: "int", nullable: true),
                    VerificationPurpose = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ExtractedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExtractedReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExtractedSender = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExtractedReceiver = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExtractedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtractedTime = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ExtractedStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ExtractedPaymentMethod = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OcrConfidence = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    VerificationResult = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsDuplicateReference = table.Column<bool>(type: "bit", nullable: false),
                    DuplicatePaymentId = table.Column<int>(type: "int", nullable: true),
                    WarningSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrVerifications_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OcrVerifications_RefundRequests_RefundRequestId",
                        column: x => x.RefundRequestId,
                        principalTable: "RefundRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequests_OriginalPaymentId",
                table: "RefundRequests",
                column: "OriginalPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrVerifications_ExtractedReferenceNumber",
                table: "OcrVerifications",
                column: "ExtractedReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_OcrVerifications_PaymentId",
                table: "OcrVerifications",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrVerifications_RefundRequestId",
                table: "OcrVerifications",
                column: "RefundRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_RefundRequests_Payments_OriginalPaymentId",
                table: "RefundRequests",
                column: "OriginalPaymentId",
                principalTable: "Payments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefundRequests_Payments_OriginalPaymentId",
                table: "RefundRequests");

            migrationBuilder.DropTable(
                name: "OcrVerifications");

            migrationBuilder.DropIndex(
                name: "IX_RefundRequests_OriginalPaymentId",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedAmount",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "OriginalPaymentId",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "RefundCompletedAt",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "RefundProofImagePath",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "RefundReferenceNumber",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserName",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReviewerNote",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TransactionReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserName",
                table: "Payments");
        }
    }
}
