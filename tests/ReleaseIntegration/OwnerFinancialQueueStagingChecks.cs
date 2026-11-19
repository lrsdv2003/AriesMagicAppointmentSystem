using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

// Runs only inside ReleaseIntegration's disposable LocalDB/TestServer/SMTP fixtures.
// All requests use actual authenticated Owner/Staff/Client cookies; no production route or data.
internal static class OwnerFinancialQueueStagingChecks
{
    private static async Task<string> GetHtml(HttpClient client, string url, Action<bool,string> check, string label)
    {
        using var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        check(response.StatusCode == HttpStatusCode.OK, label + " returns HTTP 200");
        return html;
    }

    private static string Table(string html)
    {
        var match = Regex.Match(html, @"<table\b[^>]*class=""[^""]*\bowner-audit-table\b[^""]*""[^>]*>.*?</table>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Owner financial table markup was not rendered");
        return match.Value;
    }

    private static void CheckShell(string html, string queue, int count, Action<bool,string> check)
    {
        var table = Table(html);
        check(Regex.Matches(table, @"<th\b").Count == count, queue + " has the expected data column count");
        check(!html.Contains("owner-summary-row") && !html.Contains("workspace-page-heading"), queue + " omits redundant summary and heading");
        check(!table.Contains("Review Indicator") && !Regex.IsMatch(table, @"<th[^>]*>\s*Reason\s*</th>",RegexOptions.IgnoreCase),
            queue + " omits removed list-only columns");
        check(html.Contains("staff-data-table-frame") && html.Contains("data-staff-table-capacity=\"5\"") &&
              html.Contains("data-page-size=\"5\""), queue + " uses the shared five-row table and pager");
        check(html.Contains("data-owner-verification-sort") &&
              html.Contains("Newest to Oldest") && html.Contains("Oldest to Newest"),
            queue + " exposes exactly the two submission-date ordering labels");
        var sort=Regex.Match(html, @"<select\b[^>]*data-owner-verification-sort[^>]*>.*?</select>",RegexOptions.Singleline);
        check(sort.Success && Regex.Matches(sort.Value, @"<option\b").Count == 2, queue + " has two sort option values");
        check(html.Contains("data-queue-refresh") &&
            html.Contains(queue == "payment" ? "aria-label=\"Refresh payment verification records\"" : "aria-label=\"Refresh refund requests\""),
            queue + " has accessible icon-only refresh markup");
    }

    public static async Task EmptyAsync(HttpClient owner, HttpClient staff, HttpClient client, Action<bool,string> check)
    {
        foreach(var (path,name,columns,title,description) in new[]{
          ("/Payments/PendingVerification","payment",7,"No payment records","There are no payment submissions to review."),
          ("/Payments/RefundRequests","refund",6,"No refund requests","There are no refund requests to display.")
        })
        {
            var html=await GetHtml(owner,path,check,"Isolated Owner "+name+" empty queue");
            CheckShell(html,name,columns,check);
            check(Table(html).Contains(title) && Table(html).Contains(description),name+" empty dataset has the correct in-table message");
            foreach(var (other,role) in new[]{(staff,"Staff"),(client,"Client")})
            {
                using var response=await other.GetAsync(path);
                check(response.StatusCode==HttpStatusCode.Redirect &&
                      response.Headers.Location?.ToString().Contains("AccessDenied")==true,
                      role+" cookie cannot view Owner-only "+name+" queue");
            }
        }
    }

    public static async Task PaymentPendingAsync(HttpClient owner, ApplicationDbContext db,
         int paymentId, int bookingId, Action<bool,string> check)
    {
        var payment=await db.Payments.AsNoTracking().SingleAsync(x=>x.Id==paymentId);
        var year=await db.Bookings.Where(x=>x.Id==bookingId).Select(x=>x.CreatedAt.Year).SingleAsync();
        var code=$"BK-{year}-{bookingId:D3}";
        var url="/Payments/PendingVerification?filter=awaiting&q="+Uri.EscapeDataString(code)+
          "&paymentMethod=GCash&date="+payment.UploadedAt.ToString("yyyy-MM-dd")+
          "&sortBy=oldest&page=999&pageSize=500";
        var html=await GetHtml(owner,url,check,"Owner payment staging search");
        CheckShell(html,"payment",7,check);
        check(Table(html).Contains(code) && Table(html).Contains("/Payments/Verify/"+paymentId),
            "Actual Owner pending payment list resolves booking search and review link");
        check(html.Contains("sortBy=oldest") && html.Contains("filter=awaiting") &&
             html.Contains("paymentMethod=GCash") && html.Contains("data-queue-refresh"),
             "Payment refresh keeps sorting, status, and payment-method filters");
        check(html.Contains("Page 1 of 1") && !html.Contains("owner-summary-row"),
             "Payment page clamps invalid page numbers with shared five-record pagination");
        var unmatched=await GetHtml(owner,"/Payments/PendingVerification?q=staging-not-a-real-payment",
           check,"Owner payment filtered empty");
        check(Table(unmatched).Contains("No matching payments"),"Payment filtered empty is distinct from zero-record dataset");
        check(await db.Payments.AsNoTracking().AnyAsync(x=>x.Id==paymentId && x.Status==PaymentStatus.Pending),
            "Payment list GETs do not approve or mutate the pending proof");
    }

    public static async Task RefundPendingAsync(HttpClient owner, ApplicationDbContext db,
         int refundId, int bookingId, Action<bool,string> check)
    {
        var refund=await db.RefundRequests.AsNoTracking().SingleAsync(x=>x.Id==refundId);
        var year=await db.Bookings.Where(x=>x.Id==bookingId).Select(x=>x.CreatedAt.Year).SingleAsync();
        var code=$"BK-{year}-{bookingId:D3}";
        var url="/Payments/RefundRequests?filter=UnderReview&q="+Uri.EscapeDataString(code)+"&sortBy=oldest&page=900";
        var html=await GetHtml(owner,url,check,"Owner refund staging search");
        CheckShell(html,"refund",6,check);
        check(Table(html).Contains(code) && Table(html).Contains("/Payments/RefundReview/"+refundId),
            "Actual Owner pending refund list resolves booking and refund-review route");
        check(html.Contains("sortBy=oldest") && html.Contains("filter=UnderReview") &&
              html.Contains("Page 1 of 1"),
            "Refund sorting, filtering, refresh and pagination survive invalid page inputs");
        var none=await GetHtml(owner,"/Payments/RefundRequests?q=staging-not-a-real-refund",
            check,"Owner refund filtered empty");
        check(Table(none).Contains("No matching requests"),
            "Refund filtered empty is distinct from zero-record dataset");
        var details=await GetHtml(owner,"/Payments/RefundReview/"+refundId,check,"Owner refund review details");
        check(details.Contains("Reason") && details.Contains("Refund Request"),
            "Authorized review still displays the refund reason area");
        check(await db.RefundRequests.AsNoTracking().AnyAsync(x=>x.Id==refundId &&
          x.Status==refund.Status),"Refund list and review GETs do not change request status");
    }

    public static async Task RefundCompletedAsync(HttpClient owner, int refundId,
         Action<bool,string> check)
    {
        var html=await GetHtml(owner,"/Payments/RefundRequests?filter=Refunded&sortBy=newest",
            check,"Owner completed refund staging history");
        CheckShell(html,"refund",6,check);
        check(Table(html).Contains("COMPLETED") && Table(html).Contains("/Payments/RefundReview/"+refundId),
            "Completed refunds stay searchable and linked to authorized review");
    }
}
