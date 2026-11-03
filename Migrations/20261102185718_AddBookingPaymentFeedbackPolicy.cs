using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPaymentFeedbackPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EligibilityReason",
                table: "RefundRequests",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialVerificationAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "FeedbackMedia",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "FeedbackMedia",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAtUtc",
                table: "FeedbackMedia",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentVersion",
                table: "EventFeedbacks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharingPreference",
                table: "EventFeedbacks",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedTermsVersion",
                table: "Bookings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationActorRole",
                table: "Bookings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancellationPaymentId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationPreviousStatus",
                table: "Bookings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationSource",
                table: "Bookings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundEligibilityPaymentId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundEligibilityReason",
                table: "Bookings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RefundEligible",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAtUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EligibilityReason",
                table: "RefundRequests");

            migrationBuilder.DropColumn(
                name: "InitialVerificationAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "FeedbackMedia");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "FeedbackMedia");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAtUtc",
                table: "FeedbackMedia");

            migrationBuilder.DropColumn(
                name: "ConsentVersion",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "SharingPreference",
                table: "EventFeedbacks");

            migrationBuilder.DropColumn(
                name: "AcceptedTermsVersion",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationActorRole",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationPaymentId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationPreviousStatus",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationSource",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundEligibilityPaymentId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundEligibilityReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundEligible",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAtUtc",
                table: "Bookings");
        }
    }
}
