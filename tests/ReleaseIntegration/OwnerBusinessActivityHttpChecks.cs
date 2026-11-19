using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

internal static class OwnerBusinessActivityHttpChecks
{
    private static async Task<(HttpStatusCode Status,string Html)> Read(HttpClient http,string url)
    {
        using var response=await http.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static string Table(string html)
    {
        var index=html.IndexOf("owner-activity-table-frame",StringComparison.Ordinal);
        if(index<0)throw new InvalidOperationException("Owner Business Activity table was not rendered");
        var start=html.IndexOf("<table",index,StringComparison.Ordinal);
        var end=start<0?-1:html.IndexOf("</table>",start,StringComparison.Ordinal);
        if(start<0||end<start)throw new InvalidOperationException("Malformed Business Activity table markup");
        return html[start..(end+8)];
    }
    private static int Rows(string markup)
    {
        var match=Regex.Match(markup,@"<tbody\b[^>]*>(.*?)</tbody>",RegexOptions.Singleline);
        return Regex.Matches(match.Groups[1].Value,@"<tr\b(?![^>]*staff-data-table-empty-row)").Count;
    }
    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,Action<bool,string> check)
    {
        var initialCount=await db.SystemActivities.CountAsync();
        var target="owner-activity-qa-"+Guid.NewGuid().ToString("N");
        var baseUtc=DateTime.UtcNow.Date.AddDays(-29);
        var fullDescription="Complete audit details preserved in the activity list: "+new string('A',260)+".";
        var rows=Enumerable.Range(0,8).Select(i=>new SystemActivity{
            Type=i%2==0?SystemActivityType.PaymentVerified:SystemActivityType.BookingApproved,
            Description=i==7?fullDescription:"Business audit event description "+i,
            PerformedByUserId="isolated-owner-activity-actor",PerformedByUserName="AuditActor"+i.ToString("D2"),
            AffectedRecordId=target,AffectedRecordType="Booking",
            CreatedAt=baseUtc.AddDays(i),
            MetadataJson="{\"ActorRole\":\"Owner\",\"Trace\":\"keep-activity-target-metadata\"}"
        }).ToList();
        var excluded=new SystemActivity{
            Type=SystemActivityType.LoginFailed,Description="Technical audit record stays stored",
            PerformedByUserId="isolated-owner-activity-actor",PerformedByUserName="TechnicalAuditActor",
            AffectedRecordId=target,AffectedRecordType="Account",
            CreatedAt=baseUtc.AddDays(9),MetadataJson="{\"ActorRole\":\"Owner\"}"
        };
        db.SystemActivities.AddRange(rows);
        db.SystemActivities.Add(excluded);
        await db.SaveChangesAsync();
        try
        {
            var query="/SystemActivity/Index?search="+Uri.EscapeDataString(target);
            var first=await Read(owner,query);
            var table=Table(first.Html);
            check(first.Status==HttpStatusCode.OK &&
                  first.Html.Contains("staff-data-table-frame owner-activity-table-frame",StringComparison.Ordinal) &&
                  first.Html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal) &&
                  Rows(table)==5 &&
                  first.Html.Contains("8 matching records",StringComparison.Ordinal) &&
                  first.Html.Contains("Page 1 of 2",StringComparison.Ordinal),
                  "Business Activity renders five real records per page and accurate totals using shared Staff table");
            check(Regex.Matches(table,@"<th scope=""col"">").Count==6 &&
                  table.Contains("Date &amp; time (UTC)",StringComparison.Ordinal) &&
                  !table.Contains("data-label=\"Target\"",StringComparison.Ordinal) &&
                  !table.Contains(">Target</th>",StringComparison.Ordinal) &&
                  !table.Contains("View Details",StringComparison.Ordinal) &&
                  !table.Contains("<summary",StringComparison.Ordinal),
                  "Table removes Target and View Details but retains six correctly named audit columns");
            check(!table.Contains(target,StringComparison.Ordinal) &&
                  table.Contains(fullDescription,StringComparison.Ordinal) &&
                  table.Contains("owner-activity-description",StringComparison.Ordinal),
                  "Target is not displayed, while full long activity descriptions remain directly readable");
            check(table.IndexOf("AuditActor07",StringComparison.Ordinal) <
                  table.IndexOf("AuditActor03",StringComparison.Ordinal) &&
                  !table.Contains("TechnicalAuditActor",StringComparison.Ordinal),
                  "Newest-first chronology and existing business-only visibility remain authoritative");
            check(Regex.IsMatch(table,@"datetime=""[^""]+Z""") &&
                  !first.Html.Contains("Dates and times are UTC.",StringComparison.Ordinal) &&
                  !first.Html.Contains("class=\"workspace-page-heading\"",StringComparison.Ordinal),
                  "UTC semantic timestamps are preserved without the redundant heading or UTC description");
            check(first.Html.Contains("name=\"type\"",StringComparison.Ordinal) &&
                  first.Html.Contains("name=\"fromDate\"",StringComparison.Ordinal) &&
                  first.Html.Contains("name=\"toDate\"",StringComparison.Ordinal) &&
                  first.Html.Contains("name=\"sortBy\"",StringComparison.Ordinal) &&
                  first.Html.Contains("name=\"search\"",StringComparison.Ordinal),
                  "Existing search, type/date filtering and sorting controls remain available");
            var second=await Read(owner,query+"&page=2");
            var secondTable=Table(second.Html);
            check(second.Status==HttpStatusCode.OK && Rows(secondTable)==3 &&
                  secondTable.Contains("AuditActor00",StringComparison.Ordinal) &&
                  !secondTable.Contains("AuditActor07",StringComparison.Ordinal) &&
                  second.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Second activity page shows exactly three remaining records with no repeated first-page rows");
            var oldest=await Read(owner,query+"&sortBy=oldest");
            var oldestTable=Table(oldest.Html);
            check(oldest.Status==HttpStatusCode.OK && Rows(oldestTable)==5 &&
                  oldestTable.IndexOf("AuditActor00",StringComparison.Ordinal) <
                  oldestTable.IndexOf("AuditActor04",StringComparison.Ordinal) &&
                  oldest.Html.Contains("sortBy=oldest",StringComparison.OrdinalIgnoreCase),
                  "Oldest-first sort maintains chronological ordering and preserves page navigation state");
            var type=await Read(owner,query+"&type="+((int)SystemActivityType.PaymentVerified));
            check(type.Status==HttpStatusCode.OK && Rows(Table(type.Html))==4 &&
                  type.Html.Contains("4 matching records",StringComparison.Ordinal),
                  "Activity type filtering precedes pagination and updates totals");
            var date=baseUtc.AddDays(3).ToString("yyyy-MM-dd");
            var range=await Read(owner,query+"&fromDate="+date+"&toDate="+date);
            check(range.Status==HttpStatusCode.OK && Rows(Table(range.Html))==1 &&
                  Table(range.Html).Contains("AuditActor03",StringComparison.Ordinal),
                  "UTC date filter continues to retrieve the correct original activity");
            var userSearch=await Read(owner,"/SystemActivity/Index?search=AuditActor06");
            check(userSearch.Status==HttpStatusCode.OK &&
                  Table(userSearch.Html).Contains("AuditActor06",StringComparison.Ordinal) &&
                  Rows(Table(userSearch.Html))==1,
                  "Searching activity actor names still works after Target column removal");
            var clamped=await Read(owner,query+"&page=9999");
            check(clamped.Status==HttpStatusCode.OK && Rows(Table(clamped.Html))==3 &&
                  clamped.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Out-of-range activity page clamps after server-side filtering");
            var empty=await Read(owner,"/SystemActivity/Index?search="+Uri.EscapeDataString(target+"-no-match"));
            check(empty.Status==HttpStatusCode.OK && Table(empty.Html).Contains("No matching activity",StringComparison.Ordinal) &&
                  Table(empty.Html).Contains("Try adjusting your search or filters.",StringComparison.Ordinal) &&
                  Rows(Table(empty.Html))==0,
                  "Actual filtered no-result request renders the dedicated in-table empty state");
            foreach(var (http,role) in new[]{(staff,"Staff"),(client,"Client")})
            {
                var denied=await Read(http,query);
                check(denied.Status!=HttpStatusCode.OK,role+" cannot access Owner Business Activity through real login cookie");
            }
            var noDetail=await Read(owner,"/SystemActivity/Details/"+rows[0].Id);
            check(noDetail.Status==HttpStatusCode.NotFound,
                "No dedicated activity Details endpoint remains reachable by direct URL");
            var persisted=await db.SystemActivities.AsNoTracking().Where(x=>x.AffectedRecordId==target).ToListAsync();
            check(persisted.Count==9 &&
                  persisted.All(x=>!string.IsNullOrWhiteSpace(x.MetadataJson)) &&
                  persisted.Any(x=>x.Type==SystemActivityType.LoginFailed) &&
                  persisted.Where(x=>x.Type!=SystemActivityType.LoginFailed).All(x=>
                      x.AffectedRecordType=="Booking" && x.CreatedAt>=baseUtc),
                  "Business-only table does not mutate or delete technical audit events, Target metadata or timestamps");
        }
        finally
        {
            db.SystemActivities.RemoveRange(rows);
            db.SystemActivities.Remove(excluded);
            await db.SaveChangesAsync();
        }
        check(await db.SystemActivities.CountAsync()==initialCount,
            "Business Activity HTTP fixtures are removed without disturbing existing audit records");
    }
}
