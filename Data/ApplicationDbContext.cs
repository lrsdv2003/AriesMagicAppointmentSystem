using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> LegacyUsers { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<BookingTimeline> BookingTimelines { get; set; }
        public DbSet<RescheduleRequest> RescheduleRequests { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<BlockedDate> BlockedDates { get; set; }
        public DbSet<DateBookingLimit> DateBookingLimits { get; set; }
        public DbSet<ServiceInclusion> ServiceInclusions { get; set; }
        public DbSet<RefundRequest> RefundRequests { get; set; }
        public DbSet<OcrVerification> OcrVerifications { get; set; }
        public DbSet<SystemActivity> SystemActivities { get; set; }
        public DbSet<TrashHistory> TrashHistories { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<ConversationParticipant> ConversationParticipants { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<EmailVerification> EmailVerifications { get; set; }
        public DbSet<EmailOutboxMessage> EmailOutboxMessages { get; set; }
        public DbSet<EventFeedback> EventFeedbacks { get; set; }
        public DbSet<FeedbackMedia> FeedbackMedia { get; set; }
        public DbSet<FeedbackPublicationEvent> FeedbackPublicationEvents { get; set; }
        public DbSet<FeedbackInvitation> FeedbackInvitations { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<EventFeedback>().HasIndex(f=>f.BookingId).IsUnique();
            builder.Entity<EventFeedback>().HasIndex(f=>new {f.ClientId,f.SubmittedAtUtc});
            builder.Entity<EventFeedback>().HasIndex(f=>new {f.ModerationStatus,f.Featured});
            builder.Entity<EventFeedback>().HasOne(f=>f.Booking).WithMany().HasForeignKey(f=>f.BookingId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<EventFeedback>().HasOne<ApplicationUser>().WithMany().HasForeignKey(f=>f.ClientId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<EventFeedback>().ToTable(t=>{
                t.HasCheckConstraint("CK_Feedback_Ratings","[OverallRating] BETWEEN 1 AND 5 AND [PerformanceRating] BETWEEN 1 AND 5 AND [BookingRating] BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_Feedback_Publication","[Featured] = 0 OR ([TextConsent] = 1 AND [WithdrawnAtUtc] IS NULL AND [ModerationStatus] = 'Approved' AND LEN([Experience]) > 0)");
                t.HasCheckConstraint("CK_Feedback_Status","[ModerationStatus] IN ('Pending','Approved','Private','Withdrawn')");
            });
            builder.Entity<FeedbackMedia>().HasOne(m=>m.Feedback).WithMany(f=>f.Media).HasForeignKey(m=>m.FeedbackId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<FeedbackMedia>().HasIndex(m=>m.StorageKey).IsUnique();
            builder.Entity<FeedbackMedia>().ToTable(t=>t.HasCheckConstraint("CK_FeedbackMedia_Public","[PublicApproved] = 0 OR ([ProcessingStatus] = 'Ready' AND [ScannedAtUtc] IS NOT NULL)"));
            builder.Entity<FeedbackPublicationEvent>().HasOne(e=>e.Feedback).WithMany(f=>f.PublicationEvents).HasForeignKey(e=>e.FeedbackId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<FeedbackPublicationEvent>().HasIndex(e=>new {e.FeedbackId,e.AtUtc});
            builder.Entity<FeedbackInvitation>().HasKey(i=>i.BookingId);
            builder.Entity<FeedbackInvitation>().HasOne(i=>i.Booking).WithMany().HasForeignKey(i=>i.BookingId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<EmailOutboxMessage>().HasIndex(x => x.EventKey).IsUnique();
            builder.Entity<EmailOutboxMessage>().HasIndex(x => new { x.DeliveredAtUtc, x.FailedAtUtc, x.NextAttemptAtUtc });
            builder.Entity<Payment>().ToTable(t => t.HasCheckConstraint("CK_Payments_PositiveAmount", "[Amount] > 0"));
            builder.Entity<RefundRequest>().ToTable(t => {
                t.HasCheckConstraint("CK_RefundRequests_PositiveAmount", "[Amount] > 0");
                t.HasCheckConstraint("CK_RefundRequests_ApprovedAmount", "[ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [Amount])");
            });
            builder.Entity<Payment>().HasOne(p => p.Booking).WithMany(b => b.Payments).HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<RefundRequest>().HasOne(r => r.Booking).WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<Booking>().HasIndex(b => new { b.EventDate, b.StartTime, b.EndTime });

            builder.Entity<User>().ToTable("Users");

            // Notifications belong to ASP.NET Identity users (string IDs), not legacy Users (int IDs).
            builder.Entity<Notification>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Conversation>()
                .HasOne(c => c.Booking)
                .WithMany()
                .HasForeignKey(c => c.BookingId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Conversation>()
                .HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ConversationParticipant>()
                .HasIndex(p => new { p.ConversationId, p.UserId })
                .IsUnique();

            builder.Entity<ConversationParticipant>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Message>()
                .HasIndex(m => new { m.ConversationId, m.SentAt });

            builder.Entity<Conversation>()
                .HasIndex(c => c.UpdatedAt);

            builder.Entity<EmailVerification>()
                .HasOne(v => v.User)
                .WithOne()
                .HasForeignKey<EmailVerification>(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EmailVerification>()
                .HasIndex(v => v.UserId)
                .IsUnique();

            builder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
            builder.Entity<RefundRequest>().Property(r => r.Amount).HasPrecision(18, 2);
            builder.Entity<RefundRequest>().Property(r => r.ApprovedAmount).HasPrecision(18, 2);
            builder.Entity<OcrVerification>().Property(o => o.ExtractedAmount).HasPrecision(18, 2);
            builder.Entity<OcrVerification>().Property(o => o.OcrConfidence).HasPrecision(5, 1);

            builder.Entity<RefundRequest>()
                .HasOne(r => r.OriginalPayment)
                .WithMany()
                .HasForeignKey(r => r.OriginalPaymentId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<OcrVerification>()
                .HasOne(o => o.Payment)
                .WithMany(p => p.OcrVerifications)
                .HasForeignKey(o => o.PaymentId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<OcrVerification>()
                .HasOne(o => o.RefundRequest)
                .WithMany(r => r.OcrVerifications)
                .HasForeignKey(o => o.RefundRequestId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<OcrVerification>()
                .HasIndex(o => o.ExtractedReferenceNumber);
            builder.Entity<Booking>().Property(b => b.BasePrice).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.FinalPrice).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.RequiredDownpayment).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.TravelFee).HasPrecision(18, 2);
            builder.Entity<Service>().Property(s => s.Price).HasPrecision(18, 2);
            builder.Entity<ServiceInclusion>().Property(s => s.DeductionAmount).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.BasePrice).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.FinalPrice).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.RequiredDownpayment).HasPrecision(18, 2);
        }
    }
}
