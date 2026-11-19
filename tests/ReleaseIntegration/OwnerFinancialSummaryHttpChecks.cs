using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

internal static class OwnerFinancialSummaryHttpChecks
{
    private static async Task<(HttpStatusCode Status,string Html)> Get(HttpClient client,string url)
    {
        using var response=await client.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static string Table(string html)
    {
        var start=html.IndexOf("<table class=\"staff-data-table owner-audit-table owner-financial-records-table\"",StringComparison.Ordinal);
        var end=start<0?-1:html.IndexOf("</table>",start,StringComparison.Ordinal);
        if(start<0||end<start)throw new InvalidOperationException("Owner financial record table not rendered");
        return html[start..(end+8)];
    }
    private static List<string> DataRows(string table)
    {
        var body=Regex.Match(table,@"<tbody\b[^>]*>(.*?)</tbody>",RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body,@"<tr\b[^>]*>.*?</tr>",RegexOptions.Singleline)
            .Select(x=>x.Value)
            .Where(x=>!x.Contains("staff-data-table-empty-row",StringComparison.Ordinal))
            .ToList();
    }
    private static string BookingCode(Booking booking) => $"BK-{booking.CreatedAt.Year}-{booking.Id:D3}";
    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,Action<bool,string> check)
    {
        var unique="OwnerFinanceSummary"+Guid.NewGuid().ToString("N");
        var person=new User{FullName="Owner finance client "+unique,Email=unique+"@example.test",Role="Client"};
        var service=new Service{Name="Finance "+unique,Price=14000,DurationInHours=2};
        var day=DateTime.UtcNow.Date.AddDays(-180);
        var records=Enumerable.Range(0,8).Select(i=>{
            var eventDate=day.AddDays(i+16);
            return new Booking{
                Client=person,Service=service,PackageName=service.Name,
                Status=i%3==0?BookingStatus.Confirmed:i%3==1?BookingStatus.Completed:BookingStatus.Cancelled,
                EventDate=eventDate,StartTime=eventDate.AddHours(10),EndTime=eventDate.AddHours(12),
                CreatedAt=day.AddDays(i),EventType="Birthday",PartyVenue="Local financial summary venue",
                FinalPrice=12000m+i*175m,BasePrice=12000m+i*175m,RequiredDownpayment=2000m
            };
        }).ToList();
        db.Bookings.AddRange(records);
        await db.SaveChangesAsync();
        var payments=new List<Payment>{
            new(){Booking=records[0],Amount=3000.50m,Status=PaymentStatus.Verified,ProofImagePath="private:finance-test",PaymentMethod="GCash",UploadedAt=day.AddDays(1),VerifiedAt=day.AddDays(2)},
            new(){Booking=records[0],Amount=2000m,Status=PaymentStatus.Verified,ProofImagePath="private:finance-test",PaymentMethod="GCash",UploadedAt=day.AddDays(3),VerifiedAt=day.AddDays(4)},
            new(){Booking=records[1],Amount=1750.25m,Status=PaymentStatus.Verified,ProofImagePath="private:finance-test",PaymentMethod="GCash",UploadedAt=day.AddDays(4),VerifiedAt=day.AddDays(5)},
            new(){Booking=records[2],Amount=2750m,Status=PaymentStatus.Verified,ProofImagePath="private:finance-test",PaymentMethod="GCash",UploadedAt=day.AddDays(5),VerifiedAt=day.AddDays(6)},
            new(){Booking=records[6],Amount=1200m,Status=PaymentStatus.Pending,ProofImagePath="private:finance-test",PaymentMethod="GCash",UploadedAt=day.AddDays(6)}
        };
        db.Payments.AddRange(payments);
        await db.SaveChangesAsync();
        var refunds=new List<RefundRequest>{
            new(){Booking=records[0],OriginalPayment=payments[0],Amount=850m,ApprovedAmount=725.50m,Status=RefundStatus.Refunded,
                PaymentProofImagePath="private:finance-test",GCashAccountName="Test Account",GCashNumber="09123456789",RequestedAt=day.AddDays(8),RefundCompletedAt=day.AddDays(10)},
            new(){Booking=records[1],OriginalPayment=payments[2],Amount=500m,Status=RefundStatus.UnderReview,
                PaymentProofImagePath="private:finance-test",GCashAccountName="Test Account",GCashNumber="09123456789",RequestedAt=day.AddDays(9)}
        };
        db.RefundRequests.AddRange(refunds);
        await db.SaveChangesAsync();

        try
        {
            var summaryService=new PaymentFinancialService(db);
            var individual=await summaryService.GetSummariesAsync(records.Select(b=>b.Id));
            var eligible=records.Where(b=>b.Status is not (BookingStatus.Cancelled or BookingStatus.Declined or BookingStatus.Expired)).ToList();
            var gross=payments.Where(x=>x.Status==PaymentStatus.Verified).Sum(x=>x.Amount);
            var refunded=refunds.Where(x=>x.Status==RefundStatus.Refunded).Sum(x=>x.ApprovedAmount??x.Amount);
            var expectedValue=eligible.Sum(x=>x.FinalPrice);
            var outstanding=eligible.Sum(b=>Math.Max(0m,b.FinalPrice-
                payments.Where(x=>x.BookingId==b.Id&&x.Status==PaymentStatus.Verified).Sum(x=>x.Amount)));
            var model=(ReportDashboardViewModel)((ViewResult)await
                new ReportsController(db,summaryService).Index(null,null,null,null,service.Name,null)).Model!;
            check(model.TotalBookings==8 && model.TotalBookingValue==expectedValue &&
                  model.TotalCollectedRevenue==gross && model.RefundsIssued==refunded &&
                  model.NetCollectedRevenue==gross-refunded && model.OutstandingReceivables==outstanding,
                  "Authoritative report totals reconcile independently to 8 booking prices, verified receipts, refunded amount and remaining balances");
            check(model.BookingRecords.Count==5&&model.RecordsTotalCount==8&&
                  model.BookingFinancials.Count==8,
                  "All eight financial summaries remain calculated before five-record pagination");

            var url="/Reports/Index?package="+Uri.EscapeDataString(service.Name);
            var first=await Get(owner,url);
            var table=Table(first.Html);
            var rows=DataRows(table);
            check(first.Status==HttpStatusCode.OK&&rows.Count==5&&
                  first.Html.Contains("staff-data-table-frame owner-financial-records-frame",StringComparison.Ordinal)&&
                  first.Html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal)&&
                  first.Html.Contains("8 matching records",StringComparison.Ordinal)&&
                  first.Html.Contains("Page 1 of 2",StringComparison.Ordinal),
                  "Authenticated Owner report renders shared five-record financial table, full count and two-page pager");
            check(Regex.Matches(table,@"<th scope=""col""").Count==9&&
                  new[]{"Booking","Client","Package","Event Date","Booking Value","Collected","Outstanding","Status","Action"}
                    .SequenceEqual(Regex.Matches(table,@"<th scope=""col""[^>]*>([^<]+)</th>")
                      .Select(x=>x.Groups[1].Value))&&
                  table.Contains("owner-financial-amount",StringComparison.Ordinal)&&
                  !first.Html.Contains("class=\"workspace-page-heading\"",StringComparison.Ordinal),
                  "Nine original financial columns, monetary alignment and removal of redundant Owner workspace heading");
            check(first.Html.Contains(expectedValue.ToString("N2"),StringComparison.Ordinal)&&
                  first.Html.Contains(gross.ToString("N2"),StringComparison.Ordinal)&&
                  first.Html.Contains(refunded.ToString("N2"),StringComparison.Ordinal)&&
                  first.Html.Contains(outstanding.ToString("N2"),StringComparison.Ordinal)&&
                  first.Html.Contains((gross-refunded).ToString("N2"),StringComparison.Ordinal),
                  "Rendered full-scope financial cards retain exact booking value, receipts, refunds, outstanding and net amounts");
            check(rows.Count==5&&rows[0].Contains(BookingCode(records[7]),StringComparison.Ordinal)&&
                  rows[4].Contains(BookingCode(records[3]),StringComparison.Ordinal),
                  "Financial record table preserves event-date-descending order before pagination");
            check(first.Html.Contains("/Reports/ExportCsv",StringComparison.Ordinal)&&
                  first.Html.Contains("window.print()",StringComparison.Ordinal)&&
                  first.Html.Contains("/Bookings/Details/",StringComparison.Ordinal)&&
                  first.Html.Contains("revenueTrendChart",StringComparison.Ordinal)&&
                  first.Html.Contains("paymentStatusChart",StringComparison.Ordinal),
                  "CSV, Print, booking Details, revenue and payment charts remain accessible");
            var second=await Get(owner,url+"&page=2");
            var secondRows=DataRows(Table(second.Html));
            check(second.Status==HttpStatusCode.OK&&secondRows.Count==3&&
                  secondRows[0].Contains(BookingCode(records[2]),StringComparison.Ordinal)&&
                  secondRows[2].Contains(BookingCode(records[0]),StringComparison.Ordinal)&&
                  second.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Page two contains remaining three records in stable order without duplication");
            var selected=records[2];
            var single=await Get(owner,url+"&recordSearch="+Uri.EscapeDataString(BookingCode(selected)));
            check(single.Status==HttpStatusCode.OK&&DataRows(Table(single.Html)).Count==1&&
                  Table(single.Html).Contains(BookingCode(selected),StringComparison.Ordinal)&&
                  single.Html.Contains("1 matching record",StringComparison.Ordinal),
                  "Booking ID search returns the exact record while global report filter is retained");
            var filter=await Get(owner,url+"&recordStatus="+BookingStatus.Completed);
            check(filter.Status==HttpStatusCode.OK&&DataRows(Table(filter.Html)).Count==3&&
                  filter.Html.Contains("3 matching records",StringComparison.Ordinal),
                  "Record status filter updates table rows and counts correctly");
            var global=await Get(owner,url+"&bookingStatus="+BookingStatus.Cancelled);
            check(global.Status==HttpStatusCode.OK&&DataRows(Table(global.Html)).Count==2&&
                  global.Html.Contains("2 matching records",StringComparison.Ordinal),
                  "Global booking status filter retains its original financial report eligibility and counts");
            var range=records[5].EventDate.ToString("yyyy-MM-dd");
            var dated=await Get(owner,url+"&startDate="+range+"&endDate="+range);
            check(dated.Status==HttpStatusCode.OK&&DataRows(Table(dated.Html)).Count==1&&
                  Table(dated.Html).Contains(BookingCode(records[5]),StringComparison.Ordinal),
                  "Existing event date filtering remains authoritative");
            var clamped=await Get(owner,url+"&page=9999");
            check(clamped.Status==HttpStatusCode.OK&&DataRows(Table(clamped.Html)).Count==3&&
                  clamped.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Out-of-range page clamps after correct financial record filtering");
            var empty=await Get(owner,url+"&recordSearch=not-a-financial-record");
            check(empty.Status==HttpStatusCode.OK&&
                  Table(empty.Html).Contains("No matching financial records",StringComparison.Ordinal)&&
                  Table(empty.Html).Contains("Try adjusting your search or filters.",StringComparison.Ordinal)&&
                  DataRows(Table(empty.Html)).Count==0,
                  "Filtered no-result state appears inside the unchanged table frame");
            check(empty.Html.Contains(expectedValue.ToString("N2"),StringComparison.Ordinal)&&
                  empty.Html.Contains(gross.ToString("N2"),StringComparison.Ordinal)&&
                  empty.Html.Contains(outstanding.ToString("N2"),StringComparison.Ordinal)&&
                  empty.Html.Contains((gross-refunded).ToString("N2"),StringComparison.Ordinal),
                  "Table-only searches leave total booking value, verified revenue, refunds, and outstanding amounts unchanged");
            var finite=await Get(owner,url+"&recordStatus="+BookingStatus.Confirmed);
            check(finite.Status==HttpStatusCode.OK&&finite.Html.Contains(expectedValue.ToString("N2"),StringComparison.Ordinal)&&
                  finite.Html.Contains(gross.ToString("N2"),StringComparison.Ordinal),
                  "Record-only status filters do not alter independently calculated global financial metrics");
            var activeRow=rows.First(x=>x.Contains(BookingCode(records[7]),StringComparison.Ordinal));
            var expected=individual[records[7].Id];
            check(activeRow.Contains(expected.BookingTotal.ToString("N2"),StringComparison.Ordinal)&&
                  activeRow.Contains(expected.TotalVerifiedPayments.ToString("N2"),StringComparison.Ordinal)&&
                  activeRow.Contains(expected.RemainingBalance.ToString("N2"),StringComparison.Ordinal),
                  "Actual displayed monetary row values reconcile with authoritative per-booking financial service");
            using(var download=await owner.GetAsync("/Reports/ExportCsv?package="+Uri.EscapeDataString(service.Name)))
            {
                var csv=await download.Content.ReadAsStringAsync();
                check(download.StatusCode==HttpStatusCode.OK &&
                      download.Content.Headers.ContentType?.MediaType=="text/csv" &&
                      csv.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length==9 &&
                      csv.Contains(BookingCode(records[0]),StringComparison.Ordinal)==false,
                      "CSV export remains Owner-authorized, covers all eight matching records, and retains its original identifier format");
            }
            foreach(var (http,role) in new[]{(staff,"Staff"),(client,"Client")})
            {
                var denied=await Get(http,url);
                using var export=await http.GetAsync("/Reports/ExportCsv?package="+Uri.EscapeDataString(service.Name));
                check(denied.Status!=HttpStatusCode.OK&&export.StatusCode!=HttpStatusCode.OK,
                      role+" cannot access Owner Financial Summary or CSV via direct URL");
            }
            var persistence=await db.Bookings.AsNoTracking().Where(b=>b.ClientId==person.Id).ToListAsync();
            check(persistence.Count==8&&persistence.All(b=>b.ServiceId==service.Id)&&
                  await db.Payments.AsNoTracking().CountAsync(x=>records.Select(b=>b.Id).Contains(x.BookingId))==payments.Count&&
                  await db.RefundRequests.AsNoTracking().CountAsync(x=>records.Select(b=>b.Id).Contains(x.BookingId))==refunds.Count,
                  "Read-only reporting GETs preserve booking prices, verified payments, pending payment, and refund records");
        }
        finally
        {
            db.RefundRequests.RemoveRange(refunds);
            db.Payments.RemoveRange(payments);
            db.Bookings.RemoveRange(records);
            db.Services.Remove(service);
            db.LegacyUsers.Remove(person);
            await db.SaveChangesAsync();
        }
    }
}
