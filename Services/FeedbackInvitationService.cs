using System.Data;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace AriesMagicAppointmentSystem.Services;

public static class FeedbackInvitationService
{
    public static async Task QueueAsync(ApplicationDbContext db)
    {
        var now=FeedbackPolicy.BusinessNow;var today=now.Date;
        var candidates=await db.Bookings.AsNoTracking().Where(b=>b.Status==BookingStatus.Completed&&b.IsCompletedLocked
            &&b.EventDate<today&&b.EndTime<now&&b.ApplicationUserId!=null
            &&!db.EventFeedbacks.Any(f=>f.BookingId==b.Id)&&!db.FeedbackInvitations.Any(i=>i.BookingId==b.Id))
            .Select(b=>new {b.Id,b.ApplicationUserId}).Take(50).ToListAsync();
        foreach(var booking in candidates) {
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if(await db.FeedbackInvitations.AnyAsync(i=>i.BookingId==booking.Id))continue;
            var invitation=new FeedbackInvitation {BookingId=booking.Id};
            var notification=new Notification {UserId=booking.ApplicationUserId!,Title="Share your experience",
                Message="Your event is complete. We'd love to hear about your experience.",Link="/Feedback/Create?bookingId="+booking.Id,CreatedAt=FeedbackPolicy.BusinessNow};
            db.FeedbackInvitations.Add(invitation);db.Notifications.Add(notification);
            try{await db.SaveChangesAsync();await transaction.CommitAsync();}
            catch(DbUpdateException ex) when(ex.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 1205) {
                await transaction.RollbackAsync();db.Entry(invitation).State=EntityState.Detached;db.Entry(notification).State=EntityState.Detached;
            }
        }
    }
}
