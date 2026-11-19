using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

internal static class OwnerArchivesHttpChecks
{
    private static async Task<(HttpStatusCode Status,string Html)> Read(HttpClient client,string url)
    {
        using var response=await client.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static string Table(string html)
    {
        var start=html.IndexOf("<table class=\"staff-data-table owner-archives-table\"",StringComparison.Ordinal);
        var end=start<0?-1:html.IndexOf("</table>",start,StringComparison.Ordinal);
        if(start<0||end<0)throw new InvalidOperationException("Owner Archives five-column table not rendered");
        return html[start..(end+8)];
    }
    private static int Rows(string html)
    {
        var match=Regex.Match(html,@"<tbody\b[^>]*>(.*?)</tbody>",RegexOptions.Singleline);
        return Regex.Matches(match.Groups[1].Value,@"<tr\b(?![^>]*staff-data-table-empty-row)").Count;
    }
    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        ApplicationDbContext db,Action<bool,string> check)
    {
        var testClientName="OwnerArchivesHttp"+Guid.NewGuid().ToString("N");
        var person=new User{FullName=testClientName,Email=testClientName+"@example.test",Role="Client"};
        var historicalPackage=new Service{Name="Archive preserved package "+testClientName,
            Price=10000,DurationInHours=2,IsArchived=true};
        var baseUtc=DateTime.UtcNow.Date.AddDays(-170);
        var bookings=Enumerable.Range(0,8).Select(i=>{
            var when=baseUtc.AddDays(i+15);
            return new Booking{
                Client=person,Service=historicalPackage,PackageName=historicalPackage.Name,
                Status=i%3==0?BookingStatus.Cancelled:i%3==1?BookingStatus.Declined:BookingStatus.Expired,
                EventDate=when,StartTime=when.AddHours(10),EndTime=when.AddHours(12),
                CreatedAt=baseUtc.AddDays(i),ArchivedAt=i==0?null:baseUtc.AddDays(i+27),
                TrashReason=TrashReason.BookingRequestExpired,
                EventType="Birthday",PartyVenue="Isolated archived venue",FinalPrice=10000
            };
        }).ToList();
        db.Bookings.AddRange(bookings);
        await db.SaveChangesAsync();
        try
        {
            var url="/TrashHistory/Index?Search="+Uri.EscapeDataString(testClientName);
            var first=await Read(owner,url);
            var firstTable=Table(first.Html);
            check(first.Status==HttpStatusCode.OK &&
                  first.Html.Contains("staff-data-table-frame owner-archives-table-frame",StringComparison.Ordinal) &&
                  first.Html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal) &&
                  Rows(firstTable)==5 &&
                  first.Html.Contains("8 matching records",StringComparison.Ordinal) &&
                  first.Html.Contains("Page 1 of 2",StringComparison.Ordinal),
                  "Owner Archives renders five eligible archived records and accurate total count");
            check(Regex.Matches(firstTable,@"<th scope=""col"">").Count==5 &&
                  new[]{"Booking ID","Client Name","Date and Time","Archive Date","Final Status"}
                     .SequenceEqual(Regex.Matches(firstTable,@"<th scope=""col"">([^<]+)</th>")
                        .Select(m=>m.Groups[1].Value)) &&
                  !firstTable.Contains("Archived by",StringComparison.OrdinalIgnoreCase) &&
                  !firstTable.Contains("data-label=\"Actions\"",StringComparison.Ordinal) &&
                  !firstTable.Contains("data-label=\"Type\"",StringComparison.Ordinal),
                  "Archives shows precisely five requested column headings, with no actor/type/actions column");
            check(firstTable.Contains("10:00 AM",StringComparison.Ordinal) &&
                  firstTable.Contains("12:00 PM",StringComparison.Ordinal) &&
                  firstTable.Contains(bookings[7].EventDate.ToString("MMM d, yyyy"),StringComparison.Ordinal) &&
                  firstTable.Contains(bookings[7].ArchivedAt!.Value.ToString("MMM d, yyyy"),StringComparison.Ordinal),
                  "Event times, event date and recorded archive date are distinct authoritative booking values");
            check(!first.Html.Contains("Archived packages",StringComparison.OrdinalIgnoreCase) &&
                  !first.Html.Contains("Read-only records of cancelled, declined and expired requests.",StringComparison.Ordinal) &&
                  !first.Html.Contains("class=\"workspace-page-heading\"",StringComparison.Ordinal) &&
                  first.Html.Contains("data-owner-archives-refresh=\"true\"",StringComparison.Ordinal) &&
                  first.Html.Contains("aria-label=\"Refresh archived bookings\"",StringComparison.Ordinal) &&
                  first.Html.Contains("/js/owner-archives.js",StringComparison.Ordinal),
                  "Archived Packages, redundant header and Refresh label removed while icon-only Refresh is wired");
            check(Regex.Matches(first.Html,@"<select[^>]+id=""archiveSort""[^>]*>.*?</select>",
                     RegexOptions.Singleline).Cast<Match>().Single().Value.Contains("Newest to Oldest") &&
                  Regex.Matches(first.Html,@"<select[^>]+id=""archiveSort""[^>]*>.*?</select>",
                     RegexOptions.Singleline).Cast<Match>().Single().Value.Contains("Oldest to Newest") &&
                  Regex.Matches(Regex.Match(first.Html,@"<select[^>]+id=""archiveSort""[^>]*>(.*?)</select>",
                     RegexOptions.Singleline).Groups[1].Value,"<option").Count==2,
                  "Archive Sort By exposes exactly two authoritative date orderings");
            check(firstTable.IndexOf(bookings[7].Id.ToString("D3"),StringComparison.Ordinal) <
                  firstTable.IndexOf(bookings[3].Id.ToString("D3"),StringComparison.Ordinal),
                  "Default Archives page sorts by newest archive date before pagination");
            var second=await Read(owner,url+"&Page=2");
            var secondTable=Table(second.Html);
            check(second.Status==HttpStatusCode.OK && Rows(secondTable)==3 &&
                  secondTable.Contains($"BK-{bookings[0].CreatedAt.Year}-{bookings[0].Id:D3}",StringComparison.Ordinal) &&
                  !secondTable.Contains($"BK-{bookings[7].CreatedAt.Year}-{bookings[7].Id:D3}",StringComparison.Ordinal) &&
                  second.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Archive page two holds three remaining rows without repeating newest-page records");
            check(secondTable.Contains("Not recorded",StringComparison.Ordinal),
                  "ArchivedAt missing on historical row is shown as Not recorded rather than guessed");
            var oldest=await Read(owner,url+"&SortBy=oldest");
            var oldestTable=Table(oldest.Html);
            check(oldest.Status==HttpStatusCode.OK && Rows(oldestTable)==5 &&
                  oldestTable.IndexOf($"BK-{bookings[0].CreatedAt.Year}-{bookings[0].Id:D3}",StringComparison.Ordinal) <
                  oldestTable.IndexOf($"BK-{bookings[4].CreatedAt.Year}-{bookings[4].Id:D3}",StringComparison.Ordinal) &&
                  oldest.Html.Contains("SortBy=oldest",StringComparison.OrdinalIgnoreCase),
                  "Oldest-first archive order follows archive timestamps with existing missing-date fallback");
            var specificCode=$"BK-{bookings[3].CreatedAt.Year}-{bookings[3].Id:D3}";
            var searched=await Read(owner,"/TrashHistory/Index?Search="+specificCode);
            check(searched.Status==HttpStatusCode.OK && Rows(Table(searched.Html))==1 &&
                  Table(searched.Html).Contains(specificCode,StringComparison.Ordinal),
                  "Archived Booking ID search continues to locate the exact stored record");
            var selectedDate=bookings[4].ArchivedAt!.Value.ToString("yyyy-MM-dd");
            var dateFiltered=await Read(owner,url+"&DateFrom="+selectedDate+"&DateTo="+selectedDate);
            check(dateFiltered.Status==HttpStatusCode.OK && Rows(Table(dateFiltered.Html))==1 &&
                  Table(dateFiltered.Html).Contains($"BK-{bookings[4].CreatedAt.Year}-{bookings[4].Id:D3}",StringComparison.Ordinal),
                  "Archive date-range filter uses recorded archive timestamps before pagination");
            var invalidPage=await Read(owner,url+"&Page=9999");
            check(invalidPage.Status==HttpStatusCode.OK && Rows(Table(invalidPage.Html))==3 &&
                  invalidPage.Html.Contains("Page 2 of 2",StringComparison.Ordinal),
                  "Out-of-range archive pagination safely clamps after filtering");
            var empty=await Read(owner,url+"-no-match");
            check(empty.Status==HttpStatusCode.OK &&
                  Table(empty.Html).Contains("No matching archives",StringComparison.Ordinal) &&
                  Table(empty.Html).Contains("Try adjusting your search or sorting.",StringComparison.Ordinal) &&
                  Rows(Table(empty.Html))==0,
                  "Search returning zero archives displays dedicated filtered empty state");
            var direct=await Read(owner,"/TrashHistory/Details/"+bookings[6].Id);
            check(direct.Status==HttpStatusCode.OK &&
                  direct.Html.Contains($"BK-{bookings[6].CreatedAt.Year}-{bookings[6].Id:D3}",StringComparison.Ordinal),
                  "Owner read-only Details route used by History remains available after Actions removal");
            foreach(var (http,role) in new[]{(staff,"Staff"),(client,"Client")})
            {
                var denied=await Read(http,url);
                var deniedDetail=await Read(http,"/TrashHistory/Details/"+bookings[6].Id);
                check(denied.Status!=HttpStatusCode.OK && deniedDetail.Status!=HttpStatusCode.OK,
                      role+" cannot view Owner Archives Index or Details directly");
            }
            var refresh=await Read(owner,url+"&Page=2&SortBy=oldest");
            check(refresh.Status==HttpStatusCode.OK && Rows(Table(refresh.Html))==3 &&
                  refresh.Html.Contains("SortBy=oldest",StringComparison.OrdinalIgnoreCase),
                  "Refresh destination remains a read-only GET preserving search, page and date ordering");
            var persisted=await db.Bookings.AsNoTracking().Where(b=>b.ClientId==person.Id).ToListAsync();
            var archivedPackage=await db.Services.AsNoTracking().AnyAsync(s=>s.Id==historicalPackage.Id&&s.IsArchived);
            check(persisted.Count==8 && persisted.All(b=>b.ServiceId==historicalPackage.Id &&
                  b.Status is BookingStatus.Cancelled or BookingStatus.Declined or BookingStatus.Expired) &&
                  persisted.Count(b=>!b.ArchivedAt.HasValue)==1 && archivedPackage,
                  "Read-only Archives GETs preserve original statuses, archive dates and historical package relationships");
        }
        finally
        {
            db.Bookings.RemoveRange(bookings);
            db.Services.Remove(historicalPackage);
            db.LegacyUsers.Remove(person);
            await db.SaveChangesAsync();
        }
    }
}
