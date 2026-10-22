using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AriesMagicAppointmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPostEventFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OverallRating = table.Column<int>(type: "int", nullable: false),
                    PerformanceRating = table.Column<int>(type: "int", nullable: false),
                    BookingRating = table.Column<int>(type: "int", nullable: false),
                    Experience = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Suggestions = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    PublicDisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TextConsent = table.Column<bool>(type: "bit", nullable: false),
                    NameConsent = table.Column<bool>(type: "bit", nullable: false),
                    MediaConsent = table.Column<bool>(type: "bit", nullable: false),
                    ConsentedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModerationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnpublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFeedbacks", x => x.Id);
                    table.CheckConstraint("CK_Feedback_Publication", "[Featured] = 0 OR ([TextConsent] = 1 AND [WithdrawnAtUtc] IS NULL AND [ModerationStatus] = 'Approved' AND LEN([Experience]) > 0)");
                    table.CheckConstraint("CK_Feedback_Ratings", "[OverallRating] BETWEEN 1 AND 5 AND [PerformanceRating] BETWEEN 1 AND 5 AND [BookingRating] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Feedback_Status", "[ModerationStatus] IN ('Pending','Approved','Private','Withdrawn')");
                    table.ForeignKey(
                        name: "FK_EventFeedbacks_AspNetUsers_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventFeedbacks_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FeedbackInvitations",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackInvitations", x => x.BookingId);
                    table.ForeignKey(
                        name: "FK_FeedbackInvitations_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FeedbackMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedbackId = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayFilename = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublicApproved = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackMedia", x => x.Id);
                    table.CheckConstraint("CK_FeedbackMedia_Public", "[PublicApproved] = 0 OR ([ProcessingStatus] = 'Ready' AND [ScannedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FeedbackMedia_EventFeedbacks_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "EventFeedbacks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FeedbackPublicationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedbackId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackPublicationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackPublicationEvents_EventFeedbacks_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "EventFeedbacks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbacks_BookingId",
                table: "EventFeedbacks",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbacks_ClientId_SubmittedAtUtc",
                table: "EventFeedbacks",
                columns: new[] { "ClientId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbacks_ModerationStatus_Featured",
                table: "EventFeedbacks",
                columns: new[] { "ModerationStatus", "Featured" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackMedia_FeedbackId",
                table: "FeedbackMedia",
                column: "FeedbackId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackMedia_StorageKey",
                table: "FeedbackMedia",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackPublicationEvents_FeedbackId_AtUtc",
                table: "FeedbackPublicationEvents",
                columns: new[] { "FeedbackId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackInvitations");

            migrationBuilder.DropTable(
                name: "FeedbackMedia");

            migrationBuilder.DropTable(
                name: "FeedbackPublicationEvents");

            migrationBuilder.DropTable(
                name: "EventFeedbacks");
        }
    }
}
