using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrReceiptReviewReliability20261106 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedAmountAtUpload",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmountReliability",
                table: "OcrVerifications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDuplicateImage",
                table: "OcrVerifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProofSha256",
                table: "OcrVerifications",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OcrVerifications_ProofSha256",
                table: "OcrVerifications",
                column: "ProofSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OcrVerifications_ProofSha256",
                table: "OcrVerifications");

            migrationBuilder.DropColumn(
                name: "ExpectedAmountAtUpload",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AmountReliability",
                table: "OcrVerifications");

            migrationBuilder.DropColumn(
                name: "IsDuplicateImage",
                table: "OcrVerifications");

            migrationBuilder.DropColumn(
                name: "ProofSha256",
                table: "OcrVerifications");
        }
    }
}
