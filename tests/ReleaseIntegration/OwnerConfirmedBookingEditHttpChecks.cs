using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.EntityFrameworkCore;

internal static class OwnerConfirmedBookingEditHttpChecks
{
    static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    static Task<HttpResponseMessage> Post(HttpClient http,string url,string token,
        params (string Name,string Value)[] values)
    {
        var pairs=values.Select(v=>new KeyValuePair<string,string>(v.Name,v.Value))
            .Append(new("__RequestVerificationToken",token));
        return http.PostAsync(url,new FormUrlEncodedContent(pairs));
    }

    static async Task<(HttpStatusCode Status,string Html)> Get(HttpClient http,string url)
    {
        using var response=await http.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }

    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,Action<bool,string> check)
    {
        var nonce=Guid.NewGuid().ToString("N");
        var person=new User{FullName="Revision Client "+nonce,Email="revision."+nonce+"@example.test",Role="Client"};
        var existing=new Service{Name="Existing Rev "+nonce,Price=10000m,DurationInHours=2};
        var higher=new Service{Name="Higher Rev "+nonce,Price=13000m,DurationInHours=2};
        var lower=new Service{Name="Lower Rev "+nonce,Price=1800m,DurationInHours=2};
        var accountId=await db.Users.Where(x=>x.Email=="client@example.test").Select(x=>x.Id).SingleAsync();
        var at=DateTime.UtcNow.Date.AddDays(170).AddHours(14);
        var booking=new Booking{
            Client=person,ApplicationUserId=accountId,Service=existing,PackageName=existing.Name,
            EventType="Birthday",PartyVenue="Test Hall near Ortigas, Philippines",
            EventDate=at.Date,StartTime=at,EndTime=at.AddHours(2),
            CreatedAt=DateTime.UtcNow,FinalPrice=10000m,BasePrice=10000m,TravelFee=0m,
            Status=BookingStatus.Confirmed,RequiredDownpayment=2000m,
            VenueLatitude=14.58125,VenueLongitude=121.1258,
            IsServiceable=true
        };
        db.Services.AddRange(existing,higher,lower);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var payment=new Payment{
            BookingId=booking.Id,Amount=2000m,Status=PaymentStatus.Verified,
            UploadedAt=DateTime.UtcNow.AddDays(-1),VerifiedAt=DateTime.UtcNow,
            InitialVerificationAtUtc=DateTime.UtcNow.AddHours(-3),
            PaymentMethod="GCash",ProofImagePath="private:revision-regression",TransactionReference="REV-"+nonce
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        try
        {
            string editUrl="/BookingChanges/Edit/"+booking.Id;
            string bookingUrl="/Bookings/Details/"+booking.Id;
            var page=await Get(owner,editUrl);
            check(page.Status==HttpStatusCode.OK&&
                  page.Html.Contains("owner-confirmed-change-page",StringComparison.Ordinal)&&
                  page.Html.Contains($"BK-{booking.CreatedAt.Year}-{booking.Id:D3}",StringComparison.Ordinal)&&
                  Token(page.Html).Length>10,
                "Owner can open authorized edit form with booking identity and anti-forgery token");
            var source=page.Html;
            check(!source.Contains("Staff workspace",StringComparison.Ordinal)&&
                  !source.Contains("Edit Confirmed Booking",StringComparison.Ordinal)&&
                  !source.Contains("Propose package or venue changes",StringComparison.Ordinal)&&
                  !source.Contains("The booking will stay confirmed",StringComparison.Ordinal)&&
                  !source.Contains("Payment safety",StringComparison.Ordinal)&&
                  !source.Contains("name=\"Reason\"",StringComparison.Ordinal)&&
                  !source.Contains("Staff verification",StringComparison.OrdinalIgnoreCase),
                "Redundant headings, Reason/Staff Verification controls and permanent Payment Safety card are removed");
            check(source.Contains("name=\"ServiceId\"",StringComparison.Ordinal)&&
                  source.Contains("name=\"Venue\"",StringComparison.Ordinal)&&
                  source.Contains("name=\"Latitude\"",StringComparison.Ordinal)&&
                  source.Contains("name=\"Longitude\"",StringComparison.Ordinal)&&
                  source.Contains("Send proposal to Client",StringComparison.Ordinal)&&
                  source.Contains("Current confirmed booking",StringComparison.Ordinal),
                "Package, venue, map-pin inputs, pricing context and proposal action preserved");
            var staffPage=await Get(staff,editUrl);
            check(staffPage.Status==HttpStatusCode.OK&&Token(staffPage.Html).Length>10,
                "Staff retains original booking revision access alongside Owner");
            var denied=await Get(client,editUrl);
            check(denied.Status!=HttpStatusCode.OK,"Client cannot open Owner/Staff revision form");
            var ownerDetails=await Get(owner,bookingUrl);
            check(ownerDetails.Status==HttpStatusCode.OK&&
                  ownerDetails.Html.Contains("Edit Booking Venue / Package",StringComparison.Ordinal)&&
                  ownerDetails.Html.Contains("/BookingChanges/Edit",StringComparison.Ordinal),
                "Eligible Owner booking details provide actual edit-navigation entrypoint");
            using(var noToken=await owner.PostAsync("/BookingChanges/Propose",
                new FormUrlEncodedContent(new[]{
                    new KeyValuePair<string,string>("BookingId",booking.Id.ToString()),
                    new("ServiceId",higher.Id.ToString()),new("Venue",booking.PartyVenue)
                })))
                check(noToken.StatusCode==HttpStatusCode.BadRequest,
                    "Owner POST still requires valid anti-forgery token");
            var token=Token(source);
            using(var unauthorized=await Post(client,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),("Venue",booking.PartyVenue)))
                check(unauthorized.StatusCode!=HttpStatusCode.OK&&unauthorized.StatusCode!=HttpStatusCode.Redirect ||
                    unauthorized.Headers.Location?.ToString().Contains("AccessDenied",StringComparison.OrdinalIgnoreCase)==true,
                    "Client cannot POST a confirmed booking revision");
            using(var invalidPackage=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId","999999"),("Venue",booking.PartyVenue)))
                check(invalidPackage.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                      "Invalid package is rejected without writing proposal");
            using(var malformedVenue=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),("Venue","x")))
                check(malformedVenue.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                      "Invalid venue is rejected by server validation");
            using(var low=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",lower.Id.ToString()),("Venue",booking.PartyVenue)))
                check(low.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                      "Revised total below verified payment remains blocked despite Payment Safety UI removal");
            using(var noCoordinates=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",existing.Id.ToString()),
                ("Venue","Some entirely different event venue in the Philippines")))
                check(noCoordinates.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                      "Changed venue without reviewed pin remains rejected");

            using(var proposed=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),
                ("Venue",booking.PartyVenue)))
                check(proposed.StatusCode==HttpStatusCode.Redirect,
                      "Owner submits eligible package revision without any Reason/Staff Verification values");
            var pending=await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id);
            var original=await db.Bookings.AsNoTracking().SingleAsync(x=>x.Id==booking.Id);
            check(pending!=null&&pending.Reason==string.Empty&&pending.NewFinalPrice==13000m&&
                  pending.NewRemainingBalance==11000m&&pending.VerifiedPaymentsAtProposal==2000m&&
                  original.ServiceId==existing.Id&&original.FinalPrice==10000m&&
                  original.Status==BookingStatus.Confirmed,
                  "New quote uses authoritative catalog price, has no Reason, preserves confirmed booking until Client acceptance");
            check(await db.Notifications.AnyAsync(n=>n.UserId==accountId&&
                    n.Title=="Review your confirmed booking change"&&n.Link==bookingUrl),
                  "Client is notified of pending Owner revision");
            var clientPage=await Get(client,bookingUrl);
            check(clientPage.Status==HttpStatusCode.OK&&
                  clientPage.Html.Contains("Accept revised booking",StringComparison.Ordinal)&&
                  clientPage.Html.Contains("Decline changes",StringComparison.Ordinal)&&
                  clientPage.Html.Contains("13,000.00",StringComparison.Ordinal),
                "Client reviews actual proposed quotation before explicit acceptance");
            var clientToken=Token(clientPage.Html);
            check(clientToken.Length>10,"Client acceptance and decline forms retain anti-forgery token");
            using(var duplicate=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),("Venue",booking.PartyVenue)))
                check(duplicate.StatusCode==HttpStatusCode.Redirect&&
                      (await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id))?.Token==pending!.Token,
                    "Duplicate proposal cannot overwrite an existing pending revision");
            using(var decline=await Post(client,"/BookingChanges/Reject",clientToken,
                ("id",booking.Id.ToString()),("token",pending!.Token.ToString("D"))))
                check(decline.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                      "Client may decline and resolve a proposed revision");
            check(await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==booking.Id&&
                    x.ServiceId==existing.Id&&x.FinalPrice==10000m),
                  "Declined proposal leaves original booking and verified payments unchanged");
            using(var renewed=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),("Venue",booking.PartyVenue)))
                check(renewed.StatusCode==HttpStatusCode.Redirect,
                      "A fresh proposal may be submitted after Client decline");
            var fresh=await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id);
            check(fresh!=null&&fresh.Token!=pending!.Token,
                  "New proposal has distinct token and keeps previous resolved audit timeline");
            using(var wrongToken=await Post(client,"/BookingChanges/Accept",clientToken,
                ("id",booking.Id.ToString()),("token",pending.Token.ToString("D")),("acceptChange","true")))
                check(wrongToken.StatusCode==HttpStatusCode.Redirect&&
                      await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==booking.Id&&x.ServiceId==existing.Id),
                      "Stale acceptance token cannot mutate confirmed booking");
            using(var noConsent=await Post(client,"/BookingChanges/Accept",clientToken,
                ("id",booking.Id.ToString()),("token",fresh!.Token.ToString("D"))))
                check(noConsent.StatusCode==HttpStatusCode.Redirect&&
                      await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==booking.Id&&x.ServiceId==existing.Id),
                      "Client change acceptance requires explicit checkbox consent");
            using(var accepted=await Post(client,"/BookingChanges/Accept",clientToken,
                ("id",booking.Id.ToString()),("token",fresh!.Token.ToString("D")),("acceptChange","true")))
                check(accepted.StatusCode==HttpStatusCode.Redirect,
                      "Client can explicitly accept the pending revised package quotation");
            var final=await db.Bookings.AsNoTracking().SingleAsync(x=>x.Id==booking.Id);
            check(final.ServiceId==higher.Id&&final.PackageName==higher.Name&&
                  final.FinalPrice==13000m&&final.BasePrice==13000m&&
                  final.Status==BookingStatus.Confirmed&&final.EndTime==at.AddHours(2)&&
                  await db.Payments.AsNoTracking().AnyAsync(x=>x.Id==payment.Id&&
                    x.Amount==2000m&&x.Status==PaymentStatus.Verified),
                "Accepted revision updates package/total once; schedule/status/verified payment unchanged");
            check(await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null&&
                  await db.BookingTimelines.CountAsync(t=>t.BookingId==booking.Id&&
                    t.EventType==ConfirmedBookingChangePolicy.Proposed)==2&&
                  await db.BookingTimelines.CountAsync(t=>t.BookingId==booking.Id&&
                    t.EventType==ConfirmedBookingChangePolicy.Accepted)==1&&
                  await db.BookingTimelines.CountAsync(t=>t.BookingId==booking.Id&&
                    t.EventType==ConfirmedBookingChangePolicy.Rejected)==1,
                "Append-only proposal, Client decline and acceptance audit events remain intact");
            using(var replay=await Post(client,"/BookingChanges/Accept",clientToken,
                ("id",booking.Id.ToString()),("token",fresh.Token.ToString("D")),("acceptChange","true")))
                check(replay.StatusCode==HttpStatusCode.Redirect&&
                      await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==booking.Id&&x.FinalPrice==13000m),
                    "Already-applied proposal cannot apply a second time");
            // Confirm business-rule locks still guard the simplified UI.
            await db.Entry(booking).ReloadAsync();
            var waitingPayment=new Payment{
                BookingId=booking.Id,Amount=400m,Status=PaymentStatus.Pending,
                PaymentMethod="GCash",ProofImagePath="private:pending-approval-test",UploadedAt=DateTime.UtcNow
            };
            db.Payments.Add(waitingPayment);
            await db.SaveChangesAsync();
            var blockedPayment=await Get(owner,editUrl);
            check(blockedPayment.Status==HttpStatusCode.Redirect,
                "Active Owner payment verification blocks confirmed-booking editing");
            db.Payments.Remove(waitingPayment);
            await db.SaveChangesAsync();

            var waitingRefund=new RefundRequest{
                BookingId=booking.Id,OriginalPaymentId=payment.Id,Amount=500m,
                Status=RefundStatus.Pending,GCashAccountName="Refund Fixture",
                GCashNumber="09123456789",PaymentProofImagePath="private:refund-review-test"
            };
            db.RefundRequests.Add(waitingRefund);
            await db.SaveChangesAsync();
            var blockedRefund=await Get(owner,editUrl);
            check(blockedRefund.Status==HttpStatusCode.Redirect,
                "Active refund requests block confirmed-booking editing");
            db.RefundRequests.Remove(waitingRefund);
            await db.SaveChangesAsync();

            var waitingReschedule=new RescheduleRequest{
                BookingId=booking.Id,RequestedDate=at.Date.AddDays(4),
                RequestedStartTime=at.AddDays(4),RequestedEndTime=at.AddDays(4).AddHours(2),
                Reason="Test changing schedule",Status=RescheduleRequestStatus.Pending
            };
            db.RescheduleRequests.Add(waitingReschedule);
            await db.SaveChangesAsync();
            var blockedReschedule=await Get(owner,editUrl);
            check(blockedReschedule.Status==HttpStatusCode.Redirect,
                "Active reschedule review blocks confirmed-booking editing");
            db.RescheduleRequests.Remove(waitingReschedule);
            await db.SaveChangesAsync();

            const string newVenue="Verified new venue near Pasig, Philippines";
            using(var venueSubmitted=await Post(owner,"/BookingChanges/Propose",token,
                ("BookingId",booking.Id.ToString()),("ServiceId",higher.Id.ToString()),
                ("Venue",newVenue),("Latitude","14.60125"),("Longitude","121.1258")))
                check(venueSubmitted.StatusCode==HttpStatusCode.Redirect,
                    "Owner venue revision submission retains authenticated proposal process");
            var venuePending=await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id);
            check(venuePending!=null&&venuePending.VenueChanged&&venuePending.NewVenue==newVenue&&
                  venuePending.NewLatitude==14.60125&&
                  venuePending.NewFinalPrice==13000m+venuePending.NewTravelFee &&
                  venuePending.NewFinalPrice>=2000m,
                "Venue change recalculates server-authoritative travel quote without changing paid booking");
            var unchangedVenue=await db.Bookings.AsNoTracking().SingleAsync(x=>x.Id==booking.Id);
            check(unchangedVenue.PartyVenue!=newVenue&&unchangedVenue.FinalPrice==13000m,
                "Venue and quote stay unchanged on confirmed booking until explicit Client acceptance");
            using(var venueDeclined=await Post(client,"/BookingChanges/Reject",clientToken,
                ("id",booking.Id.ToString()),("token",venuePending!.Token.ToString("D"))))
                check(venueDeclined.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                    "Client can decline venue quote without modifying confirmed venue or verified payment");

            // Owner/Staff can withdraw submitted proposals, but cannot delete their audit trail.
            using(var staffProposes=await Post(staff,"/BookingChanges/Propose",Token(staffPage.Html),
                ("BookingId",booking.Id.ToString()),("ServiceId",existing.Id.ToString()),
                ("Venue",booking.PartyVenue)))
                check(staffProposes.StatusCode==HttpStatusCode.Redirect,
                    "Staff retains package-proposal workflow without obsolete Reason field");
            var staffPending=await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id);
            check(staffPending!=null,"Staff proposal remains pending for Client review");
            using(var ownerWithdraws=await Post(owner,"/BookingChanges/Withdraw",token,
                ("id",booking.Id.ToString()),("token",staffPending!.Token.ToString("D"))))
                check(ownerWithdraws.StatusCode==HttpStatusCode.Redirect&&
                      await ConfirmedBookingChangePolicy.PendingAsync(db,booking.Id)==null,
                    "Owner may withdraw an existing revision without changing accepted booking");

            await db.Entry(booking).ReloadAsync(); // HTTP updates used an independent EF context.
        }
        finally
        {
            db.BookingTimelines.RemoveRange(await db.BookingTimelines.Where(t=>t.BookingId==booking.Id).ToListAsync());
            db.Notifications.RemoveRange(await db.Notifications.Where(n=>n.Link=="/Bookings/Details/"+booking.Id).ToListAsync());
            db.Payments.Remove(payment);
            db.Bookings.Remove(booking);
            db.Services.RemoveRange(existing,higher,lower);
            db.LegacyUsers.Remove(person);
            await db.SaveChangesAsync();
        }
    }
}
