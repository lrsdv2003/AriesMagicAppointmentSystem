using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;
using AriesMagicAppointmentSystem.Data;
namespace AriesMagicAppointmentSystem.Services;

public static class FeedbackPolicy
{
    public static readonly TimeZoneInfo BusinessZone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
    public static DateTime BusinessNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessZone);
    public static bool Eligible(Booking b) => b.Status == BookingStatus.Completed && b.IsCompletedLocked
        && b.EventDate.Date < BusinessNow.Date && b.EndTime < BusinessNow;
    // Public content is plain text only. Reject contact details/links for moderator review.
    public static bool SafePublicText(string value) => !Regex.IsMatch(value,
        @"[<>]|https?://|www\.|[\w.+-]+@[\w.-]+\.[a-z]{2,}|(?:\+?\d[\s().-]*){7,}|(?:javascript|data):",
        RegexOptions.IgnoreCase|RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(100));
    public static IQueryable<EventFeedback> PublicQuery(ApplicationDbContext db) =>
        db.EventFeedbacks.AsNoTracking().Where(f => f.Featured && f.ModerationStatus=="Approved"
            && f.TextConsent && f.ConsentedAtUtc!=null && f.ReviewedAtUtc!=null && f.ReviewedBy!=null && f.PublishedAtUtc!=null && f.WithdrawnAtUtc==null && f.Experience!=""
            && f.Booking.Status==BookingStatus.Completed && f.Booking.IsCompletedLocked
            && f.Booking.EventDate < BusinessNow.Date && f.Booking.EndTime < BusinessNow);
    public static void Audit(ApplicationDbContext db,EventFeedback feedback,string actor,string action,string reason="")
    {
        feedback.UpdatedAtUtc=DateTime.UtcNow;
        feedback.PublicationEvents.Add(new FeedbackPublicationEvent {ActorId=actor,Action=action,Reason=reason});
        db.SystemActivities.Add(new SystemActivity {Type=SystemActivityType.FeedbackModerated,
            PerformedByUserId=actor,AffectedRecordType="Feedback",AffectedRecordId=feedback.Id.ToString(),
            Description="Feedback "+action,MetadataJson=System.Text.Json.JsonSerializer.Serialize(new {Action=action})});
    }
}
