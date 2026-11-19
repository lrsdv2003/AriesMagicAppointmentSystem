using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Isolated, real authenticated ASP.NET Razor and GUID SQL LocalDB fixture.
// Does not connect to production, alter real records, or use real credentials.
internal static class StaffHistoryTableTests
{
    private static int checks;
    private static void Check(bool ok,string label)
    {
        if(!ok)throw new InvalidOperationException("FAILED Staff History: "+label);
        checks++;
        Console.WriteLine("PASS Staff History: "+label);
    }
    private static async Task<HttpClient> LoginAsync(ReleaseFactory app,string email,string password)
    {
        var client=app.CreateClient(new WebApplicationFactoryClientOptions
        {AllowAutoRedirect=false,HandleCookies=true});
        var html=await client.GetStringAsync("/Account/Login");
        var token=WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Check(token.Length>0,"real login antiforgery token present");
        using var response=await client.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>{
                ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=token
            }));
        Check(response.StatusCode==HttpStatusCode.Redirect,"real Identity login succeeds");
        return client;
    }
    private static string Table(string html)
    {
        var from=html.IndexOf("staff-history-table\"",StringComparison.Ordinal);
        if(from<0)throw new InvalidOperationException("Missing standardized History table");
        var start=html.LastIndexOf("<table",from,StringComparison.Ordinal);
        var end=html.IndexOf("</table>",from,StringComparison.Ordinal);
        if(start<0||end<0)throw new InvalidOperationException("Invalid history table");
        return html[start..(end+8)];
    }
    private static string OwnerTable(string html)
    {
        var from=html.IndexOf("owner-history-table\"",StringComparison.Ordinal);
        if(from<0)throw new InvalidOperationException("Missing standardized Owner History table");
        var start=html.LastIndexOf("<table",from,StringComparison.Ordinal);
        var end=html.IndexOf("</table>",from,StringComparison.Ordinal);
        if(start<0||end<0)throw new InvalidOperationException("Invalid Owner History table");
        return html[start..(end+8)];
    }
    private static List<int> DetailIds(string table) =>
        Regex.Matches(table,"href=\"[^\"]*?/History/Details/([0-9]+)[^\"]*\"")
            .Cast<Match>().Select(x=>int.Parse(x.Groups[1].Value)).ToList();
    private static string Code(int id,int year)=>$"BK-{year}-{id:D3}";

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Unsafe LocalDB instance");
        var dbName="AriesStaffHistory_"+Guid.NewGuid().ToString("N");
        Check(Regex.IsMatch(dbName,"^AriesStaffHistory_[0-9a-f]{32}$"),
            "isolated GUID LocalDB name");
        var conn=$@"Server=(localdb)\{instance};Database={dbName};Integrated Security=True;TrustServerCertificate=True";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(conn).Options;
        var root=Path.Combine(Path.GetTempPath(),dbName);
        Directory.CreateDirectory(root);
        var fixtures=Environment.GetEnvironmentVariable("ARIES_STAFF_HISTORY_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtures))
        {
            fixtures=Path.GetFullPath(fixtures);
            if(!fixtures.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture root must be under Temp");
            Directory.CreateDirectory(fixtures);
        }
        async Task Save(string name,string html)
        {
            if(fixtures!=null)
                await File.WriteAllTextAsync(Path.Combine(fixtures,name+".html"),html);
        }
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();
        smtp.Start();
        try
        {
            await db.Database.MigrateAsync();
            await using var app=new ReleaseFactory(conn,smtp.Port,new Uri("http://127.0.0.1:9"),root);
            using var scope=app.Services.CreateScope();
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            foreach(var role in new[]{"Staff","Owner","Client"})
                if(!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded,
                        "isolated "+role+" role created");
            async Task<ApplicationUser> Account(string role)
            {
                var email="history-"+role.ToLowerInvariant()+"@example.test";
                var user=new ApplicationUser{
                    Email=email,UserName=email,EmailConfirmed=true,
                    FullName="Synthetic "+role,PhoneNumber="09123456789"
                };
                Check((await users.CreateAsync(user,password)).Succeeded &&
                    (await users.AddToRoleAsync(user,role)).Succeeded,
                    "isolated "+role+" Identity");
                return user;
            }
            var staffAccount=await Account("Staff");
            var ownerAccount=await Account("Owner");
            var clientAccount=await Account("Client");
            var legacy=new User{
                FullName="Synthetic History Client",
                Email="legacy-history@example.test",Role="Client",
                PasswordHash="IDENTITY_MANAGED"
            };
            var services=new[]{
                new Service{Name="Synthetic History Premium",Price=8500,DurationInHours=3},
                new Service{Name="Synthetic History Deluxe",Price=10500,DurationInHours=3}
            };
            db.LegacyUsers.Add(legacy);db.Services.AddRange(services);
            await db.SaveChangesAsync();
            var creation=DateTime.Today.AddDays(-120);
            var bookings=Enumerable.Range(0,10).Select(i=>{
                var date=DateTime.Today.AddDays(-50+i*3);
                var service=services[i%2];
                return new Booking{
                    ClientId=legacy.Id,ApplicationUserId=clientAccount.Id,
                    ServiceId=service.Id,Service=service,
                    Status=BookingStatus.Declined,
                    CreatedAt=creation,ArchivedAt=date.AddDays(1),
                    EventDate=date,StartTime=date.AddHours(13),
                    EndTime=date.AddHours(16),
                    EventType="Birthday",Motif="Blue",
                    PartyVenue="Synthetic History Venue "+i+", Rizal",
                    ContactPerson="Synthetic History Client",ContactNumber="09123456789",
                    PackageName=service.Name,BasePrice=service.Price,FinalPrice=service.Price,
                    PaxCount=55,IsServiceable=true
                };
            }).ToList();
            db.Bookings.AddRange(bookings);
            await db.SaveChangesAsync();
            // History is read-only for the UI task. Seed the existing archived marker
            // so automatic first-visit archiving need not modify these fixtures.
            foreach(var booking in bookings)
                db.BookingTimelines.Add(new BookingTimeline{
                    BookingId=booking.Id,EventType=TimelineEventType.BookingArchived,
                    CreatedAt=booking.ArchivedAt!.Value,
                    Notes="Synthetic pre-existing archived marker"
                });
            await db.SaveChangesAsync();
            using var staff=await LoginAsync(app,staffAccount.Email!,password);
            using var owner=await LoginAsync(app,ownerAccount.Email!,password);
            using var client=await LoginAsync(app,clientAccount.Email!,password);
            using var anon=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            using(var denied=await client.GetAsync("/History/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"Client cannot open Staff History");
            using(var denied=await anon.GetAsync("/History/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"anonymous History access denied");
            using(var export=await staff.GetAsync("/History/ExportCsv"))
                Check(export.StatusCode==HttpStatusCode.NotFound,
                    "Retired History CSV export is not available to Staff");
            foreach(var n in new[]{0,1,2,3,4,5,6,10})
            {
                for(var i=0;i<bookings.Count;i++)
                    bookings[i].Status=i<n?BookingStatus.Completed:BookingStatus.Declined;
                await db.SaveChangesAsync();
                if(n==0)
                {
                    using(var ownerEmpty=await owner.GetAsync("/History/Index"))
                    {
                        var ownerHtml=await ownerEmpty.Content.ReadAsStringAsync();
                        var content=OwnerTable(ownerHtml);
                        Check(content.Contains("No booking history",StringComparison.Ordinal)
                              && content.Contains("No historical booking records are available.",StringComparison.Ordinal),
                              "Owner zero-record History shows the intended default in-table empty state");
                        await Save("owner-history-0",ownerHtml);
                    }
                    if(fixtures!=null)
                        await Save("owner-payment-0",await owner.GetStringAsync("/Payments/PendingVerification"));
                    using(var baseline=await owner.GetAsync("/History/Index?BookingStatus=Completed"))
                        Check(OwnerTable(await baseline.Content.ReadAsStringAsync())
                              .Contains("No booking history",StringComparison.Ordinal),
                              "Mandatory Completed filter alone is not treated as user-applied filtering");
                    using(var filtered=await owner.GetAsync("/History/Index?Search=no-match-in-history&BookingStatus=Completed"))
                        Check(OwnerTable(await filtered.Content.ReadAsStringAsync())
                              .Contains("No matching records",StringComparison.Ordinal),
                              "Owner filtered zero-result History shows the distinct matching-results empty state");
                }
                using var res=await staff.GetAsync("/History/Index");
                var html=await res.Content.ReadAsStringAsync();
                Check(res.StatusCode==HttpStatusCode.OK,
                    $"History loads for {n} eligible Completed bookings");
                var table=Table(html);
                var expected=bookings.Take(n)
                    .OrderByDescending(b=>b.ArchivedAt??b.EndTime)
                    .ThenByDescending(b=>b.Id)
                    .Take(5).Select(b=>b.Id).ToList();
                var links=DetailIds(table);
                Check(links.SequenceEqual(expected) && links.Distinct().Count()==links.Count,
                    $"stable newest-first unique history detail links {n}");
                Check(table.Contains("<colgroup>",StringComparison.Ordinal)
                    && Regex.Matches(table,"<th scope=\"col\"").Count==6
                    && html.Contains("staff-data-table-frame staff-history-table-frame",StringComparison.Ordinal),
                    $"shared six-column table/frame for {n}");
                Check(html.Contains(n+" matching record"+(n==1?"":"s"),StringComparison.Ordinal)
                    && html.Contains("Page 1 of "+Math.Max(1,(int)Math.Ceiling(n/5.0)),StringComparison.Ordinal),
                    $"correct total and five-record paging count {n}");
                Check(html.Contains("class=\"btn btn-outline-secondary aries-round3-refresh staff-history-refresh\"",StringComparison.Ordinal)
                    && html.Contains("title=\"Refresh history\"",StringComparison.Ordinal)
                    && html.Contains("class=\"visually-hidden\">Refresh</span>",StringComparison.Ordinal),
                    $"icon-only accessible Refresh link for Staff at {n}");
                if(n==0)
                    Check(table.Contains("No booking history",StringComparison.Ordinal)
                        && table.Contains("No historical booking records are available.",StringComparison.Ordinal)
                        && !table.Contains("No matching records",StringComparison.Ordinal),
                        "true empty state is distinct from no matches");
                else
                    Check(!table.Contains("staff-data-table-empty-row",StringComparison.Ordinal),
                        $"no fake/duplicated filler rows among {n} bookings");
                if(n<=5)
                    Check(!html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal)
                        && !html.Contains("aria-label=\"Previous page\"",StringComparison.Ordinal),
                        $"single page hides redundant controls at {n}");
                else
                    Check(html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal)
                        && html.Contains("page=2",StringComparison.OrdinalIgnoreCase),
                        $"pagination next page exists at {n}");
                await Save("staff-history-"+n,html);
            }
            foreach(var b in bookings)b.Status=BookingStatus.Completed;
            await db.SaveChangesAsync();
            using(var oldest=await staff.GetAsync("/History/Index?SortBy=Oldest"))
            {
                var html=await oldest.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(bookings.OrderBy(b=>b.ArchivedAt??b.EndTime)
                    .ThenBy(b=>b.Id).Take(5).Select(b=>b.Id)),
                    "Oldest event sorting uses existing archived timestamp and ID tie-breaker");
                await Save("staff-history-10-oldest",html);
            }
            foreach(var n in new[]{6,10})
            {
                for(var i=0;i<bookings.Count;i++)
                    bookings[i].Status=i<n?BookingStatus.Completed:BookingStatus.Declined;
                await db.SaveChangesAsync();
                using var page2=await staff.GetAsync("/History/Index?Page=2&SortBy=Oldest");
                var html=await page2.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(bookings.Take(n)
                    .OrderBy(b=>b.ArchivedAt??b.EndTime).ThenBy(b=>b.Id)
                    .Skip(5).Take(5).Select(b=>b.Id)),
                    $"page two correct no skipped/duplicated records at {n}");
                Check(html.Contains("Page 2 of 2",StringComparison.Ordinal) &&
                    html.Contains(n+" matching records",StringComparison.Ordinal),
                    $"page two count correct at {n}");
                await Save("staff-history-"+n+"-page2",html);
                using var clamp=await staff.GetAsync("/History/Index?Page=999&SortBy=Oldest");
                Check(DetailIds(Table(await clamp.Content.ReadAsStringAsync()))
                    .SequenceEqual(bookings.Take(n).OrderBy(b=>b.ArchivedAt??b.EndTime)
                        .ThenBy(b=>b.Id).Skip(5).Take(5).Select(b=>b.Id)),
                    $"out-of-range page clamped safely at {n}");
            }
            foreach(var b in bookings)b.Status=BookingStatus.Completed;
            await db.SaveChangesAsync();
            var sought=bookings[7];
            using(var byCode=await staff.GetAsync("/History/Index?Search="+
                Uri.EscapeDataString(Code(sought.Id,sought.CreatedAt.Year))))
                Check(DetailIds(Table(await byCode.Content.ReadAsStringAsync()))
                    .SequenceEqual(new[]{sought.Id}),"booking code search finds exact Completed history");
            using(var package=await staff.GetAsync("/History/Index?ServiceId="+services[0].Id))
            {
                var html=await package.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).Count==5 && html.Contains("5 matching records",StringComparison.Ordinal),
                    "package filter retains correct five-record result set");
                await Save("staff-history-10-filtered",html);
            }
            using(var noMatch=await staff.GetAsync("/History/Index?Search=never-exists-history-synthetic"))
            {
                var html=await noMatch.Content.ReadAsStringAsync();
                Check(Table(html).Contains("No matching records",StringComparison.Ordinal)
                    && html.Contains("Try adjusting your search or filters.",StringComparison.Ordinal)
                    && html.Contains("Clear filters",StringComparison.Ordinal)
                    && html.Contains("0 matching records",StringComparison.Ordinal),
                    "unsuccessful search gets contextual in-table empty state");
                await Save("staff-history-no-match",html);
            }
            using(var year=await staff.GetAsync("/History/Index?DateRange=Year&Year=1999"))
                Check(DetailIds(Table(await year.Content.ReadAsStringAsync())).Count==0,
                    "year filter excludes dates outside selected year");
            using(var staffSort=await staff.GetAsync("/History/Index?SortBy=ClientName"))
                Check(staffSort.StatusCode==HttpStatusCode.OK &&
                    DetailIds(Table(await staffSort.Content.ReadAsStringAsync()))
                      .SequenceEqual(bookings.OrderByDescending(b=>b.ArchivedAt??b.EndTime)
                        .ThenByDescending(b=>b.Id).Take(5).Select(b=>b.Id)),
                    "Staff cannot override unsupported ClientName sort");
            using(var detail=await staff.GetAsync("/History/Details/"+sought.Id))
                Check(detail.StatusCode==HttpStatusCode.OK,
                    "Staff History Details deep link opens correct historical booking");
            using(var ownerPage=await owner.GetAsync("/History/Index"))
            {
                var html=await ownerPage.Content.ReadAsStringAsync();
                var table=OwnerTable(html);
                Check(ownerPage.StatusCode==HttpStatusCode.OK
                    && html.Contains("staff-data-table-frame owner-queue-table-frame owner-history-table-frame",StringComparison.Ordinal)
                    && html.Contains("dashboard-panel owner-table-panel staff-data-table-panel",StringComparison.Ordinal)
                    && html.Contains("staff-data-table-scroll",StringComparison.Ordinal)
                    && html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal)
                    && table.Contains("owner-audit-table",StringComparison.Ordinal)
                    && table.Contains("<colgroup>",StringComparison.Ordinal)
                    && Regex.Matches(table,"<th scope=\"col\"").Count==6,
                    "Owner History uses the shared Verify Payment table shell with all six History columns");
                Check(DetailIds(table).Count==5 && html.Contains("10 matching records",StringComparison.Ordinal)
                    && html.Contains("Page 1 of 2",StringComparison.Ordinal),
                    "Owner History displays five results and accurate server pagination");
                Check(html.Contains("data-owner-history-refresh=\"true\"",StringComparison.Ordinal)
                    && html.Contains("aria-label=\"Refresh booking history\"",StringComparison.Ordinal)
                    && !html.Contains(">Refresh</span>",StringComparison.Ordinal)
                    && html.Contains("/js/owner-history.js",StringComparison.Ordinal),
                    "Owner Refresh is icon-only and provides in-place refresh behavior");
                var workspace=Regex.Match(html,@"<main\b[^>]*>.*?</main>",RegexOptions.Singleline);
                Check(workspace.Success
                    && !workspace.Value.Contains("href=\"/TrashHistory",StringComparison.Ordinal)
                    && html.Contains("href=\"/TrashHistory",StringComparison.Ordinal)
                    && !html.Contains("Completed events appear here.",StringComparison.Ordinal)
                    && !html.Contains("id=\"historyExport\"",StringComparison.Ordinal)
                    && !html.Contains("asp-action=\"Export",StringComparison.Ordinal),
                    "History omits the Archives control and retired export while the sidebar Archives link remains");
                await Save("owner-history-10",html);
            }
            using(var ownerPage2=await owner.GetAsync("/History/Index?Page=2&SortBy=Oldest"))
            {
                var html=await ownerPage2.Content.ReadAsStringAsync();
                var expected=bookings.OrderBy(b=>b.ArchivedAt??b.EndTime).ThenBy(b=>b.Id)
                    .Skip(5).Take(5).Select(b=>b.Id);
                Check(DetailIds(OwnerTable(html)).SequenceEqual(expected)
                    && html.Contains("Page 2 of 2",StringComparison.Ordinal),
                    "Owner History older-first second page has no missing or duplicate bookings");
            }
            using(var ownerSearch=await owner.GetAsync("/History/Index?Search="+
                Uri.EscapeDataString(Code(sought.Id,sought.CreatedAt.Year))+"&SortBy=Oldest"))
            {
                var html=await ownerSearch.Content.ReadAsStringAsync();
                Check(DetailIds(OwnerTable(html)).SequenceEqual(new[]{sought.Id})
                    && html.Contains("1 matching record",StringComparison.Ordinal)
                    && html.Contains("SortBy=Oldest",StringComparison.Ordinal)
                    && html.Contains("Search=",StringComparison.Ordinal),
                    "Owner search retains matching booking, sort selection and refresh query");
            }
            using(var ownerPackage=await owner.GetAsync("/History/Index?ServiceId="+services[0].Id))
            {
                var html=await ownerPackage.Content.ReadAsStringAsync();
                Check(DetailIds(OwnerTable(html)).Count==5
                    && html.Contains("5 matching records",StringComparison.Ordinal),
                    "Owner package filter maintains server-authoritative five-record result set");
            }
            using(var empty=await owner.GetAsync("/History/Index?Search=synthetic-owner-no-match"))
            {
                var html=await empty.Content.ReadAsStringAsync();
                Check(OwnerTable(html).Contains("No matching records",StringComparison.Ordinal)
                    && OwnerTable(html).Contains("Try adjusting your search or filters.",StringComparison.Ordinal),
                    "Owner empty search shows contextual in-table empty state");
                await Save("owner-history-no-match",html);
            }
            using(var ownerDetail=await owner.GetAsync("/History/Details/"+sought.Id))
                Check(ownerDetail.StatusCode==HttpStatusCode.OK,
                    "Owner History Details navigation remains available");
            foreach(var route in new[]{"/History/Print","/History/ExportCsv","/History/ExportPdf"})
            {
                using var ownerDenied=await owner.GetAsync(route);
                using var staffDenied=await staff.GetAsync(route);
                Check(ownerDenied.StatusCode==HttpStatusCode.NotFound
                    && staffDenied.StatusCode==HttpStatusCode.NotFound,
                    "Retired dedicated History report endpoint returns 404: "+route);
            }
            using(var report=await owner.GetAsync("/Reports/ExportCsv"))
                Check(report.StatusCode==HttpStatusCode.OK
                    && report.Content.Headers.ContentType?.MediaType=="text/csv",
                    "Unrelated authorized Reports CSV export remains functional");
            Check(await db.Bookings.AsNoTracking().CountAsync(b=>b.Status==BookingStatus.Completed)==10,
                "Staff History GETs do not change completed booking statuses");
            Check(await db.BookingTimelines.AsNoTracking().CountAsync(t=>
                t.EventType==TimelineEventType.BookingArchived)==10,
                "History GETs preserve pre-existing archival timeline markers");
            Check(await db.Payments.AsNoTracking().CountAsync()==0 &&
                await db.RefundRequests.AsNoTracking().CountAsync()==0,
                "History UI change does not create payment/refund entries");
            if (fixtures != null)
            {
                db.Payments.AddRange(bookings.Take(5).Select(booking => new Payment
                {
                    BookingId = booking.Id,
                    Amount = 2000m,
                    Status = PaymentStatus.Pending,
                    ProofImagePath = "synthetic-history-payment.png"
                }));
                await db.SaveChangesAsync();
                await Save("owner-payment-5", await owner.GetStringAsync("/Payments/PendingVerification"));
            }
            Console.WriteLine($"STAFF_HISTORY_INTEGRATION_PASS: {checks} isolated SQL/Razor checks");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
