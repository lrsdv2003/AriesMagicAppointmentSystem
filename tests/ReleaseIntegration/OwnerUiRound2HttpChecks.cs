using System.Net;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

static class OwnerUiRound2HttpChecks
{
    public static async Task RunAsync(HttpClient owner, HttpClient staff, HttpClient client, ApplicationDbContext db, int bookingId, int paymentId, int refundId, Action<bool,string> check)
    {
        var booking=await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==bookingId);
        var code=$"BK-{booking.CreatedAt.Year}-{booking.Id:D3}";
        async Task<string> Html(HttpClient http,string url)
        {
            using var response=await http.GetAsync(url);
            check(response.StatusCode==HttpStatusCode.OK,"Round 2 authenticated HTTP read: "+url.Split('?')[0]);
            return await response.Content.ReadAsStringAsync();
        }
        var calendar=await Html(owner,"/Calendar");
        check(calendar.Contains("href=\"/Calendar/Settings\"") && calendar.Contains("Calendar Settings") && !calendar.Contains("<span>Calendar settings</span>"),"Owner Calendar toolbar replaces separate Settings sidebar destination");
        var staffCalendar=await Html(staff,"/Calendar");
        check(!staffCalendar.Contains("href=\"/Calendar/Settings\""),"Staff Calendar does not expose Owner Settings control");
        var verified=await Html(owner,"/Payments/PendingVerification?filter=verified&q="+Uri.EscapeDataString(code)+"&page=999&pageSize=25");
        check(verified.Contains("name=\"pageSize\" value=\"25\"") && verified.Contains("data-queue-refresh") && verified.Contains(code) && verified.Contains("Page 1 of 1"),"Real payment GET binds code/filter/size, clamps paging and renders refresh");
        var returned=await Html(owner,"/Payments/RefundRequests?filter=Refunded&q="+Uri.EscapeDataString(code));
        check(returned.Contains(code) && returned.Contains("COMPLETED") && returned.Contains("owner-queue-pagination"),"Completed refund remains visible after cancellation through actual Owner queue");
        var history=await Html(owner,"/History?Search="+Uri.EscapeDataString(code));
        check(!history.Contains("aria-label=\"View Details for "+code+"\""),"Real completed History does not list cancelled booking");
        using(var legacy=await owner.GetAsync("/History/Details/"+bookingId))
            check(legacy.StatusCode==HttpStatusCode.Redirect && legacy.Headers.Location!.OriginalString.Contains("/TrashHistory/Details/"+bookingId),"Owner old History detail link redirects to authorized archive");
        var archived=await Html(owner,"/TrashHistory/Details/"+bookingId);
        check(archived.Contains("Booking details and transactions") && archived.Contains("Related refund requests"),"Archive detail preserves paths to transactions and refunds");
        foreach(var denied in new[]{staff,client}) foreach(var route in new[]{"/Payments/PendingVerification?page=2","/Payments/RefundRequests?page=2","/Calendar/Settings","/TrashHistory"})
        {
            using var response=await denied.GetAsync(route);
            check(response.StatusCode==HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("AccessDenied"),"Round 2 Owner route denies other real role cookies: "+route.Split('?')[0]);
        }
        var before=JsonSerializer.Serialize(new {Booking=await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==bookingId),Payment=await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==paymentId),Refund=await db.RefundRequests.AsNoTracking().SingleAsync(r=>r.Id==refundId)});
        await Html(owner,"/Payments/PendingVerification?filter=verified&q="+Uri.EscapeDataString(code));
        await Html(owner,"/Payments/RefundRequests?q="+Uri.EscapeDataString(code));
        var after=JsonSerializer.Serialize(new {Booking=await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==bookingId),Payment=await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==paymentId),Refund=await db.RefundRequests.AsNoTracking().SingleAsync(r=>r.Id==refundId)});
        check(before==after,"Manual queue refresh does not mutate booking, payment or refund SQL records");
    }
}
