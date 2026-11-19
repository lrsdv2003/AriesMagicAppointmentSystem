using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.EntityFrameworkCore;

internal static class OwnerCancellationHttpChecks
{
    public static async Task RunAsync(HttpClient owner, HttpClient staff, HttpClient client, HttpClient anonymous,
        ApplicationDbContext db, Booking existing, Service package, Action<bool, string> check)
    {
        const string url = "/Bookings/CancelAsOwner";
        const string reason = "The performer is unavailable for this event.";
        var now = FeedbackPolicy.BusinessNow;
        var slot = now.Date.AddDays(3).AddHours(14);
        var actorId = await db.Users.Where(u => u.Email == "owner@example.test").Select(u => u.Id).SingleAsync();
        var staffId = await db.Users.Where(u => u.Email == "staff@example.test").Select(u => u.Id).SingleAsync();
        Booking MakeBooking(DateTime start, string status = BookingStatus.Confirmed) => new()
        {
            ApplicationUserId = existing.ApplicationUserId, ClientId = existing.ClientId, ServiceId = package.Id,
            PackageName = package.Name, EventType = "Company Events", PartyVenue = "Synthetic cancellation venue",
            EventDate = start.Date, StartTime = start, EndTime = start.AddHours(2), Status = status,
            BasePrice = 10000m, FinalPrice = 10000m, IsServiceable = true,
            TravelFeeStatus = VenueLocationPolicy.Calculated, ContactPerson = "Synthetic cancellation client"
        };
        var eligible = MakeBooking(slot);
        var competing = MakeBooking(slot, BookingStatus.AwaitingDownpayment);
        var ongoing = MakeBooking(now.AddMinutes(-20));
        var past = MakeBooking(now.AddDays(-3));
        var completed = MakeBooking(slot.AddDays(2), BookingStatus.Completed);
        var proofPending = MakeBooking(slot.AddDays(3));
        var refundPending = MakeBooking(slot.AddDays(4));
        var proposalPending = MakeBooking(slot.AddDays(5));
        var reschedulePending = MakeBooking(slot.AddDays(6));
        var terminal = MakeBooking(slot.AddDays(7), BookingStatus.Cancelled);
        db.Bookings.AddRange(eligible, competing, ongoing, past, completed, proofPending, refundPending,
            proposalPending, reschedulePending, terminal);
        await db.SaveChangesAsync();

        var verified = new Payment
        {
            BookingId = eligible.Id, Status = PaymentStatus.Verified, Amount = 2000,
            PaymentMethod = "GCash", ProofImagePath = "payments/owner-cancel-existing.png",
            InitialVerificationAtUtc = DateTime.UtcNow.AddHours(-2), TransactionReference = "OWNER-CANCEL-FIXTURE"
        };
        db.Payments.AddRange(verified, new Payment
        {
            BookingId = proofPending.Id, Status = PaymentStatus.AdditionalEvidenceRequired, Amount = 2000,
            PaymentMethod = "GCash", ProofImagePath = "payments/owner-cancel-pending.png"
        });
        db.RefundRequests.Add(new RefundRequest { BookingId = refundPending.Id, Status = RefundStatus.Approved });
        db.RescheduleRequests.Add(new RescheduleRequest
        {
            BookingId = reschedulePending.Id, RequestedDate = slot.Date,
            RequestedStartTime = slot, RequestedEndTime = slot.AddHours(2)
        });
        db.BookingTimelines.Add(new BookingTimeline
        {
            BookingId = proposalPending.Id, EventType = ConfirmedBookingChangePolicy.Proposed,
            Notes = JsonSerializer.Serialize(new ConfirmedBookingChangeProposal { BookingId = proposalPending.Id, Token = Guid.NewGuid() })
        });
        await db.SaveChangesAsync();

        string Details(Booking booking) => $"/Bookings/Details/{booking.Id}";
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(HttpClient http, Booking booking, string message = reason, string? token = null)
        {
            var fields = new Dictionary<string, string> { ["id"] = booking.Id.ToString(), ["reason"] = message, ["fromDashboard"] = "true" };
            if (token != null) fields.Add("__RequestVerificationToken", token);
            return await http.PostAsync(url, new FormUrlEncodedContent(fields));
        }

        var dashboard = await owner.GetStringAsync("/Dashboard/Owner");
        var modalId = $"ownerCancelBooking-{eligible.Id}";
        var ownerToken = Token(dashboard);
        check(dashboard.Contains($"data-bs-target=\"#{modalId}\"") && dashboard.Contains("Confirm Cancellation")
            && dashboard.Contains("Keep Booking") && dashboard.Contains("Philippine time") && ownerToken.Length > 0,
            "Upcoming Events renders the eligible Owner cancellation action, facts and protected confirmation modal");
        check((await owner.GetStringAsync(Details(eligible))).Contains($"id=\"{modalId}\""),
            "Owner booking details render the same cancellation action");
        var staffDetails = await staff.GetStringAsync(Details(eligible));
        var clientDetails = await client.GetStringAsync(Details(eligible));
        check(!staffDetails.Contains(modalId) && !clientDetails.Contains(modalId),
            "Staff and Client booking views exclude the Owner cancellation action");
        foreach (var (http, token, target) in new[]
        {
            (staff, Token(staffDetails), "AccessDenied"),
            (client, Token(clientDetails), "AccessDenied"),
            (anonymous, "", "Login")
        })
        {
            using var denied = await Post(http, eligible, token: token);
            check(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location?.ToString().Contains(target) == true,
                "Owner cancellation rejects non-Owner or unauthenticated POST");
        }
        using (var get = await owner.GetAsync(url + "?id=" + eligible.Id))
            check(get.StatusCode == HttpStatusCode.MethodNotAllowed, "Cancellation is POST-only");
        using (var csrf = await Post(owner, eligible))
            check(csrf.StatusCode == HttpStatusCode.BadRequest, "Owner cancellation validates antiforgery");
        foreach (var invalidReason in new[] { " ", "short", "********", new string('x', 1001) })
        {
            using var invalid = await Post(owner, eligible, invalidReason, ownerToken);
            check(invalid.StatusCode == HttpStatusCode.Redirect, "Owner cancellation rejects invalid reasons");
        }
        check(await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == eligible.Id && b.Status == BookingStatus.Confirmed),
            "Invalid submissions cannot alter the booking");

        foreach (var blocked in new[] { ongoing, past, completed, proofPending, refundPending, proposalPending, reschedulePending, terminal })
        {
            check(!(await owner.GetStringAsync(Details(blocked))).Contains($"id=\"ownerCancelBooking-{blocked.Id}\""),
                "Ineligible booking hides its Owner cancellation action");
            using var refused = await Post(owner, blocked, token: ownerToken);
            check(refused.StatusCode == HttpStatusCode.Redirect && !await db.Bookings.AsNoTracking()
                .AnyAsync(b => b.Id == blocked.Id && b.CancellationSource == BookingCancellationPolicy.OwnerCancellationSource),
                "Owner cancellation refuses started, completed, cancelled, or conflicting bookings");
        }
        var scheduleUrl = $"/Bookings/CheckScheduleAvailability?date={slot:yyyy-MM-dd}&startTime=14:00&serviceId={package.Id}";
        check((await client.GetStringAsync(scheduleUrl)).Contains("\"available\":false")
            && (await client.GetStringAsync(Details(competing))).Contains("This time is no longer available"),
            "Confirmed booking and its verified proof block the overlapping slot before cancellation");

        var attempts = await Task.WhenAll(Post(owner, eligible, token: ownerToken), Post(owner, eligible, token: ownerToken));
        using var first = attempts[0];
        using var second = attempts[1];
        using var retry = await Post(owner, eligible, token: ownerToken);
        check(attempts.Any(response => response.StatusCode == HttpStatusCode.Redirect
                && response.Headers.Location?.ToString() == "/Dashboard/Owner")
            && attempts.All(response => response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict)
            && retry.StatusCode == HttpStatusCode.Redirect, "Owner cancellation tolerates concurrent and later duplicate submissions");
        var cancelled = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == eligible.Id);
        check(cancelled.Status == BookingStatus.Cancelled && cancelled.CancellationPreviousStatus == BookingStatus.Confirmed
            && cancelled.CancelledAtUtc.HasValue && cancelled.CancelledByUserId == actorId && cancelled.CancellationActorRole == "Owner"
            && cancelled.CancellationSource == BookingCancellationPolicy.OwnerCancellationSource && cancelled.CancellationReason == reason,
            "Cancellation persists the actor, UTC time, reason, source and previous status");
        check(await db.Payments.AsNoTracking().AnyAsync(p => p.Id == verified.Id && p.Status == PaymentStatus.Verified
                && p.Amount == 2000 && p.ProofImagePath == verified.ProofImagePath && p.TransactionReference == verified.TransactionReference)
            && !cancelled.RefundEligible && !await db.RefundRequests.AnyAsync(r => r.BookingId == eligible.Id),
            "Cancellation leaves the verified payment intact and creates no refund eligibility, approval or transfer");
        check(await db.BookingTimelines.CountAsync(t => t.BookingId == eligible.Id && t.EventType == TimelineEventType.BookingCancelled
                && t.Notes!.Contains("Financial review required")) == 1
            && await db.SystemActivities.CountAsync(a => a.Type == SystemActivityType.BookingCancelled
                && a.AffectedRecordId == eligible.Id.ToString() && a.PerformedByUserId == actorId) == 1,
            "Owner cancellation has a single timeline entry and one business activity audit");
        var cancelledBookingUrl = Details(eligible);
        check(await db.Notifications.CountAsync(n => n.UserId == existing.ApplicationUserId && n.Link == cancelledBookingUrl
                && n.Message.Contains("Any refund is handled separately")) == 1
            && await db.Notifications.CountAsync(n => n.UserId == staffId && n.Link == cancelledBookingUrl
                && n.Title == "Confirmed booking cancelled") == 1, "Client and Staff receive one cancellation notification each");
        check(!(await owner.GetStringAsync("/Dashboard/Owner")).Contains(modalId)
            && (await owner.GetStringAsync(Details(eligible))).Contains("Verified payments remain on this cancelled booking"),
            "Cancelled booking leaves Upcoming Events and retains a financial review notice in its details");
        check((await client.GetStringAsync(scheduleUrl)).Contains("\"available\":true")
            && !(await client.GetStringAsync(Details(competing))).Contains("This time is no longer available"),
            "Cancellation releases both the confirmed slot and its retained payment lock");
        var paymentToken = Token(await client.GetStringAsync($"/Payments/Upload?bookingId={competing.Id}"));
        using var form = new MultipartFormDataContent();
        foreach (var (field, value) in new[] {
            ("__RequestVerificationToken", paymentToken), ("ExpectedLatestPaymentId", "0"), ("AcknowledgesPaymentTerms", "true"),
            ("BookingId", competing.Id.ToString()), ("Amount", "2000"), ("PaymentOption", "Minimum"), ("PaymentMethod", "GCash")
        })
            form.Add(new StringContent(value), field);
        var image = new ByteArrayContent(await File.ReadAllBytesAsync("tests/ReleaseIntegration/Fixtures/payment.png"));
        image.Headers.ContentType = new("image/png");
        form.Add(image, "ProofImage", "owner-cancellation-overlap.png");
        using var upload = await client.PostAsync("/Payments/Upload", form);
        check(upload.StatusCode == HttpStatusCode.Redirect
            && await db.Payments.AsNoTracking().AnyAsync(p => p.BookingId == competing.Id && p.Status == PaymentStatus.Pending)
            && !await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == competing.Id && b.Status == BookingStatus.Confirmed),
            "Released slot accepts the next eligible proof without confirming a second reservation automatically");
    }
}
