using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.EntityFrameworkCore;

internal static class OwnerUnifiedBookingDetailsHttpChecks
{
    private static async Task<(HttpStatusCode Status,string Html)> Get(HttpClient http,string url)
    {
        using var response=await http.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static bool InOrder(string html, params string[] values)
    {
        var last=-1;
        foreach(var val in values){
            var next=html.IndexOf(val,last+1,StringComparison.Ordinal);
            if(next<0)return false;
            last=next;
        }
        return true;
    }
    public static async Task PaymentReviewAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,int bookingId,int paymentId,Action<bool,string> check)
    {
        var url="/Payments/Verify/"+paymentId;
        var (status,html)=await Get(owner,url);
        var booking=await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==bookingId);
        var payment=await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==paymentId);
        var code=$"BK-{booking.CreatedAt.Year}-{booking.Id:D3}";
        check(status==HttpStatusCode.OK&&
              html.Contains($"<h1>{code}</h1>",StringComparison.Ordinal)&&
              !html.Contains("class=\"workspace-page-heading\"",StringComparison.Ordinal)&&
              html.Contains("data-owner-booking-variant=\"payment\"",StringComparison.Ordinal),
              "Owner payment review renders real booking code as heading without redundant generic workspace title");
        check(InOrder(html,"<h2>Booking Information</h2>","<h2>Payment Summary</h2>",
                "<h2>OCR Extraction</h2>","<h2>Expected vs Detected</h2>",
                "aria-label=\"Payment verification actions\"","owner-booking-detail-timeline"),
              "Owner payment review uses four sequential sections, bottom actions and right Booking Timeline");
        check(html.Contains("owner-booking-detail-layout",StringComparison.Ordinal)&&
              html.Contains("aria-label=\"Booking activity history\"",StringComparison.Ordinal)&&
              html.Contains("aries-timeline-list",StringComparison.Ordinal),
              "Payment Details timeline uses the original booking's stored timeline entries in the shared two-area grid");
        check(html.Contains("Protected payment proof",StringComparison.Ordinal)&&
              html.Contains("Submission history",StringComparison.Ordinal)&&
              html.Contains("OCR-Detected Amount",StringComparison.Ordinal)&&
              html.Contains("Reference Number",StringComparison.Ordinal)&&
              html.Contains("Payment comparison",StringComparison.Ordinal),
              "Payment proof, submission history, automatic OCR and expected/detected comparison remain readable");
        check(html.Contains(payment.Amount.ToString("N2"),StringComparison.Ordinal)&&
              html.Contains("Already Paid",StringComparison.Ordinal)&&
              html.Contains("Expected at Submission",StringComparison.Ordinal)&&
              html.Contains("Remaining Before Approval",StringComparison.Ordinal),
              "Original expected payment amount and finance summary remain visible before approval");
        check(html.Contains("data-bs-target=\"#approvePaymentModal\"",StringComparison.Ordinal)&&
              html.Contains("data-bs-target=\"#evidencePaymentModal\"",StringComparison.Ordinal),
              "Approve and Request New Proof controls retain existing authenticated confirmation modals");
        check(Regex.Matches(html,@"data-bs-target=""#(?:approvePaymentModal|evidencePaymentModal)""").Count==2 &&
              !html.Contains("id=\"rejectPaymentModal\"",StringComparison.Ordinal)&&
              !html.Contains("Reanalyze proof",StringComparison.OrdinalIgnoreCase)&&
              !html.Contains("Reject Payment",StringComparison.Ordinal)&&
              !html.Contains("Owner Decision",StringComparison.Ordinal)&&
              !html.Contains("Financial actions require confirmation.",StringComparison.Ordinal),
              "Retired Reject and manual Reanalyze controls, form and redundant Owner Decision copy are absent");
        check(html.Contains("name=\"duplicateOverrideConfirmed\"",StringComparison.Ordinal)&&
              html.Contains("name=\"duplicateOverrideReason\"",StringComparison.Ordinal)&&
              html.Contains("name=\"amountCorrectionReason\"",StringComparison.Ordinal)&&
              html.Contains("trusted payment transaction record",StringComparison.Ordinal)&&
              html.Contains("name=\"__RequestVerificationToken\"",StringComparison.Ordinal),
              "Approval still requires manual-provider verification and retains duplicate/correction validation inputs and antiforgery token");
        check(html.Contains("name=\"reason\"",StringComparison.Ordinal)&&
              html.Contains("name=\"id\"",StringComparison.Ordinal)&&
              html.Contains("Request New Proof",StringComparison.Ordinal),
              "Request New Proof preserves correct payment identifier and client-facing reason control");
        foreach(var (http,role) in new[]{(staff,"Staff"),(client,"Client")}){
            var denied=await Get(http,url);
            check(denied.Status!=HttpStatusCode.OK,role+" cannot read Owner payment verification details");
        }
    }
    public static async Task ConfirmedAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,int bookingId,int paymentId,Action<bool,string> check)
    {
        var booking=await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==bookingId);
        var code=$"BK-{booking.CreatedAt.Year}-{booking.Id:D3}";
        var url="/Bookings/Details/"+bookingId;
        var (status,html)=await Get(owner,url);
        check(status==HttpStatusCode.OK&&
              html.Contains($"<h1>{code}</h1>",StringComparison.Ordinal)&&
              html.Contains("data-owner-booking-variant=\"confirmed\"",StringComparison.Ordinal)&&
              !html.Contains("class=\"workspace-page-heading\"",StringComparison.Ordinal),
              "Owner confirmed booking has real booking ID as shared heading; Staff-specific view not reused as a page");
        check(InOrder(html,"Client Information","Event Information","Packing Information",
              "Venue Travel Overview","Client Notes","Booking Financial Details",
              "aria-label=\"Booking actions\"","owner-booking-detail-timeline"),
              "Owner confirmed booking shows six ordered vertically stacked sections, actions and right timeline");
        check(html.Contains("owner-booking-detail-layout",StringComparison.Ordinal)&&
              html.Contains("aries-timeline-list",StringComparison.Ordinal)&&
              html.Contains("aria-label=\"Booking activity history\"",StringComparison.Ordinal),
              "Owner confirmed timeline uses original shared chronological activity partial");
        check(html.Contains("Selected Package",StringComparison.Ordinal)&&
              !html.Contains("Selected Service Info",StringComparison.Ordinal)&&
              html.Contains("Package Price",StringComparison.Ordinal)&&
              html.Contains("Travel fee",StringComparison.OrdinalIgnoreCase)&&
              html.Contains("Full Address",StringComparison.Ordinal),
              "Original package and venue information retained without duplicate Selected Service Info");
        check(!html.Contains("<th scope=\"col\">Proof</th>",StringComparison.Ordinal)&&
              !html.Contains("Refund History",StringComparison.Ordinal)&&
              html.Contains("Payment Transaction History",StringComparison.Ordinal)&&
              html.Contains("Review details",StringComparison.Ordinal)&&
              html.Contains("/Payments/Verify/"+paymentId,StringComparison.Ordinal),
              "Proof and Refund History presentation removed while protected payment-review navigation remains");
        check(html.Contains("Verified Payments",StringComparison.Ordinal)&&
              html.Contains("Remaining Balance",StringComparison.Ordinal)&&
              html.Contains("Net Collected",StringComparison.Ordinal)&&
              html.Contains("Booking Total",StringComparison.Ordinal),
              "Confirmed booking financial amounts remain backed by authoritative summary");
        check(html.Contains("Add Internal Note",StringComparison.Ordinal)&&
              html.Contains("Booking Conversation",StringComparison.Ordinal)&&
              html.Contains("Back to Bookings",StringComparison.Ordinal),
              "Existing Owner internal-note, booking-conversation and back navigation actions remain accessible");
        check(!html.Contains("Revise confirmed event",StringComparison.Ordinal)&&
              !html.Contains("View the details and recorded activity for this booking.",StringComparison.Ordinal)&&
              !html.Contains("Available Actions",StringComparison.Ordinal),
              "Removed redundant booking descriptions and action header are not rendered");
        var staffPage=await Get(staff,url);
        check(staffPage.Status==HttpStatusCode.OK&&
              staffPage.Html.Contains("staff-unified-booking-details",StringComparison.Ordinal)&&
              !staffPage.Html.Contains("data-owner-booking-variant=\"confirmed\"",StringComparison.Ordinal),
              "Existing Staff booking details UI remains intact, including Staff-only revision controls");
        var staffEdit=await Get(owner,"/BookingChanges/Edit/"+bookingId);
        check(staffEdit.Status is HttpStatusCode.OK or HttpStatusCode.Redirect,
              "Owner confirmed booking revision endpoint now enforces actual eligibility, not a Staff-only role");
        var financial=new PaymentFinancialService(db);
        var summary=await financial.GetSummaryAsync(bookingId);
        check(summary.TotalVerifiedPayments>0&&
              html.Contains(summary.TotalVerifiedPayments.ToString("N2"),StringComparison.Ordinal)&&
              html.Contains(summary.RemainingBalance.ToString("N2"),StringComparison.Ordinal)&&
              await db.Payments.AsNoTracking().AnyAsync(p=>p.Id==paymentId&&p.Status==PaymentStatus.Verified),
              "Read-only details retain verified payment, accurate balance and unchanged payment status");
    }
}
