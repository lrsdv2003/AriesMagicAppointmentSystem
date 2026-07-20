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
        public DbSet<SystemActivity> SystemActivities { get; set; }
        public DbSet<TrashHistory> TrashHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<User>().ToTable("Users");

            // Notifications belong to ASP.NET Identity users (string IDs), not legacy Users (int IDs).
            builder.Entity<Notification>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
            builder.Entity<RefundRequest>().Property(r => r.Amount).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.BasePrice).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.FinalPrice).HasPrecision(18, 2);
            builder.Entity<Booking>().Property(b => b.RequiredDownpayment).HasPrecision(18, 2);
            builder.Entity<Service>().Property(s => s.Price).HasPrecision(18, 2);
            builder.Entity<ServiceInclusion>().Property(s => s.DeductionAmount).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.BasePrice).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.FinalPrice).HasPrecision(18, 2);
            builder.Entity<TrashHistory>().Property(t => t.RequiredDownpayment).HasPrecision(18, 2);
        }
    }
}
