using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Staff Reschedule table contract: real authenticated MVC/Razor + fresh GUID
// SQL LocalDB only. Does not access production or mutate real booking records.
internal static class StaffRescheduleTableTests
{
    private static int checks;
    private static void Check(bool ok,string description)
    {
        if(!ok)throw new InvalidOperationException("FAILED Staff Reschedules: "+description);
        checks++;
        Console.WriteLine("PASS Staff Reschedules: "+description);
    }
    private static async Task<HttpClient> LoginAsync(ReleaseFactory factory,string email,string password)
    {
        var http=factory.CreateClient(new WebApplicationFactoryClientOptions
        {AllowAutoRedirect=false,HandleCookies=true});
        var login=await http.GetStringAsync("/Account/Login");
        var token=WebUtility.HtmlDecode(Regex.Match(login,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Check(token.Length>0,"antiforgery token exists");
        using var response=await http.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>{
                ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=token
            }));
        Check(response.StatusCode==HttpStatusCode.Redirect,"real Identity login succeeds");
        return http;
    }
    private static string Table(string html)
    {
        var start=html.IndexOf("class=\"table align-middle staff-reschedule-table staff-data-table\"",StringComparison.Ordinal);
        if(start<0)throw new InvalidOperationException("Staff table missing");
        var from=html.LastIndexOf("<table",start,StringComparison.Ordinal);
        var to=html.IndexOf("</table>",start,StringComparison.Ordinal);
        if(from<0||to<0)throw new InvalidOperationException("Table markup incomplete");
        return html[from..(to+8)];
    }
    private static List<int> DetailIds(string table) =>
        Regex.Matches(table,"href=\"[^\"]*?/RescheduleRequests/Details/([0-9]+)[^\"]*\"")
            .Cast<Match>().Select(x=>int.Parse(x.Groups[1].Value)).ToList();
    private static string SyntheticName(int i)=>"Synthetic Package "+i;
    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Unsafe LocalDB instance");
        var name="AriesStaffReschedule_"+Guid.NewGuid().ToString("N");
        Check(Regex.IsMatch(name,"^AriesStaffReschedule_[0-9a-f]{32}$"),"isolated GUID LocalDB name");
        var connection=$@"Server=(localdb)\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var root=Path.Combine(Path.GetTempPath(),name);
        Directory.CreateDirectory(root);
        var fixtureRoot=Environment.GetEnvironmentVariable("ARIES_STAFF_RESCHEDULE_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtureRoot))
        {
            fixtureRoot=Path.GetFullPath(fixtureRoot);
            if(!fixtureRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixtures must remain in temp directory");
            Directory.CreateDirectory(fixtureRoot);
        }
        async Task SaveFixture(string name,string html)
        {
            if(fixtureRoot!=null)
                await File.WriteAllTextAsync(Path.Combine(fixtureRoot,name+".html"),html);
        }
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();
        smtp.Start();
        try
        {
            await db.Database.MigrateAsync();
            await using var factory=new ReleaseFactory(connection,smtp.Port,
                new Uri("http://127.0.0.1:9"),root);
            using var scope=factory.Services.CreateScope();
            var roleManager=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            foreach(var role in new[]{"Staff","Owner","Client"})
                if(!await roleManager.RoleExistsAsync(role))
                    Check((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded,
                        "isolated "+role+" role");
            async Task<ApplicationUser> Account(string role)
            {
                var email="reschedule-"+role.ToLowerInvariant()+"@example.test";
                var user=new ApplicationUser{Email=email,UserName=email,EmailConfirmed=true,
                    FullName="Synthetic "+role,PhoneNumber="09123456789"};
                Check((await users.CreateAsync(user,password)).Succeeded &&
                    (await users.AddToRoleAsync(user,role)).Succeeded,
                    "isolated "+role+" identity");
                return user;
            }
            var staffAccount=await Account("Staff");
            var ownerAccount=await Account("Owner");
            var clientAccount=await Account("Client");
            var legacy=new User{FullName="Synthetic Calendar Client",
                Email="reschedule-client@example.test",Role="Client",
                PasswordHash="IDENTITY_MANAGED"};
            var services=new[]{
                new Service{Name="Synthetic Premium",Price=8500,DurationInHours=3},
                new Service{Name="Synthetic Deluxe",Price=9500,DurationInHours=3}
            };
            db.LegacyUsers.Add(legacy);db.Services.AddRange(services);
            await db.SaveChangesAsync();
            var date=DateTime.Today.AddDays(60);
            var bookings=Enumerable.Range(0,10).Select(i=>{
                var eventDay=date.AddDays(i*3);
                var service=services[i%2];
                return new Booking{
                    ClientId=legacy.Id, ApplicationUserId=clientAccount.Id, ServiceId=service.Id,
                    Service=service, Status=BookingStatus.Confirmed,
                    CreatedAt=DateTime.Today.AddDays(-4),
                    EventDate=eventDay, StartTime=eventDay.AddHours(13),
                    EndTime=eventDay.AddHours(16),
                    EventType="Birthday", Motif="Blue",
                    PartyVenue="Synthetic Reschedule Venue "+i+" in Rizal",
                    ContactPerson="Synthetic Client",ContactNumber="09123456789",
                    PackageName=service.Name,BasePrice=service.Price,FinalPrice=service.Price,
                    PaxCount=40,IsServiceable=true
                };
            }).ToList();
            db.Bookings.AddRange(bookings);await db.SaveChangesAsync();
            var requests=bookings.Select((booking,i)=>new RescheduleRequest{
                BookingId=booking.Id,Booking=booking,
                OriginalDate=booking.EventDate,
                OriginalStartTime=booking.StartTime,
                OriginalEndTime=booking.EndTime,
                RequestedDate=booking.EventDate.AddDays(1),
                RequestedStartTime=booking.StartTime.AddDays(1),
                RequestedEndTime=booking.EndTime.AddDays(1),
                CreatedAt=DateTime.Today.AddDays(-2).AddHours(10),
                Reason="Synthetic scenario "+i,
                Status=RescheduleRequestStatus.Approved
            }).ToList();
            db.RescheduleRequests.AddRange(requests);await db.SaveChangesAsync();
            using var staff=await LoginAsync(factory,staffAccount.Email!,password);
            using var owner=await LoginAsync(factory,ownerAccount.Email!,password);
            using var client=await LoginAsync(factory,clientAccount.Email!,password);
            using var anon=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            using(var denied=await client.GetAsync("/RescheduleRequests/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"Client cannot access Staff queue");
            using(var denied=await anon.GetAsync("/RescheduleRequests/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"anonymous cannot access Staff queue");
            foreach(var n in new[]{0,1,2,3,4,5,6,10})
            {
                for(var i=0;i<requests.Count;i++)
                    requests[i].Status=i<n?RescheduleRequestStatus.Pending:RescheduleRequestStatus.Approved;
                await db.SaveChangesAsync();
                using var response=await staff.GetAsync("/RescheduleRequests/Index");
                var html=await response.Content.ReadAsStringAsync();
                Check(response.StatusCode==HttpStatusCode.OK,
                    $"Staff Reschedule index loads with {n} pending requests");
                var table=Table(html);
                var ids=DetailIds(table);
                var expected=requests.Take(n).OrderByDescending(x=>x.CreatedAt)
                    .ThenByDescending(x=>x.Id).Take(5).Select(x=>x.Id).ToList();
                Check(ids.SequenceEqual(expected) && ids.Distinct().Count()==ids.Count,
                    $"five-row preview, unique newest-first links at {n}");
                Check(html.Contains(n+" matching record"+(n==1?"":"s"),StringComparison.Ordinal)
                    && html.Contains("Page 1 of "+Math.Max(1,(int)Math.Ceiling(n/5.0)),StringComparison.Ordinal),
                    $"accurate true count and page index at {n}");
                Check(html.Contains("staff-data-table-frame staff-reschedule-table-frame",StringComparison.Ordinal)
                    && table.Contains("<colgroup>",StringComparison.Ordinal)
                    && Regex.Matches(table,"<th scope=\"col\"").Count==7,
                    $"standard seven-column shared table at {n}");
                Check(!table.Contains("reschedule-availability",StringComparison.Ordinal)
                    && !table.Contains("class=\"reschedule-schedule",StringComparison.Ordinal)
                    && !html.Contains("class=\"staff-reschedule-cards\"",StringComparison.Ordinal),
                    $"no date highlights/availability markers or redundant cards at {n}");
                Check(html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal),
                    $"table capacity marker at {n}");
                if(n==0)
                    Check(table.Contains("No reschedule requests",StringComparison.Ordinal)
                        && table.Contains("There are no reschedule requests to display.",StringComparison.Ordinal)
                        && ids.Count==0,
                        "empty dataset shows genuine empty state, no filler");
                else
                    Check(!table.Contains("staff-data-table-empty-row",StringComparison.Ordinal)
                        && table.Contains("staff-reschedule-date-plain",StringComparison.Ordinal)
                        && table.Contains("status-badge status-pending",StringComparison.Ordinal),
                        $"plain dates but dedicated pending status still present for {n}");
                if(n<=5)
                    Check(!html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal)
                        && !html.Contains("aria-label=\"Previous page\"",StringComparison.Ordinal),
                        $"single page hides paging buttons at {n}");
                else
                    Check(html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal)
                        && html.Contains("page=2",StringComparison.OrdinalIgnoreCase),
                        $"multi-page navigation available at {n}");
                await SaveFixture("staff-reschedules-"+n,html);
            }
            foreach(var request in requests)request.Status=RescheduleRequestStatus.Pending;
            await db.SaveChangesAsync();
            using(var oldest=await staff.GetAsync("/RescheduleRequests/Index?sortBy=oldest"))
            {
                var html=await oldest.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(requests.OrderBy(x=>x.CreatedAt)
                    .ThenBy(x=>x.Id).Take(5).Select(x=>x.Id)),
                    "Oldest-first sort uses deterministic request date and ID");
                Check(html.Contains("Oldest first</option>",StringComparison.Ordinal),
                    "selected sort option rendered");
                await SaveFixture("staff-reschedules-10-oldest",html);
            }
            foreach(var n in new[]{6,10})
            {
                for(var i=0;i<requests.Count;i++)
                    requests[i].Status=i<n?RescheduleRequestStatus.Pending:RescheduleRequestStatus.Approved;
                await db.SaveChangesAsync();
                using var page2=await staff.GetAsync("/RescheduleRequests/Index?page=2&sortBy=oldest");
                var html=await page2.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(requests.Take(n)
                    .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id)
                    .Skip(5).Take(5).Select(x=>x.Id)),
                    $"page two retains sort and excludes already shown records at {n}");
                Check(html.Contains(n+" matching records",StringComparison.Ordinal)
                    && html.Contains("Page 2 of 2",StringComparison.Ordinal),
                    $"second-page count accurate at {n}");
                await SaveFixture("staff-reschedules-"+n+"-page2",html);
                using var clamp=await staff.GetAsync("/RescheduleRequests/Index?page=999&sortBy=oldest");
                Check(DetailIds(Table(await clamp.Content.ReadAsStringAsync()))
                    .SequenceEqual(requests.Take(n).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id)
                        .Skip(5).Take(5).Select(x=>x.Id)),
                    $"out-of-range page clamps correctly at {n}");
            }
            foreach(var request in requests)request.Status=RescheduleRequestStatus.Pending;
            await db.SaveChangesAsync();
            using(var search=await staff.GetAsync("/RescheduleRequests/Index?search=Synthetic%20Premium"))
            {
                var html=await search.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).Count==5 && html.Contains("5 matching records"),
                    "package search shows five of five matching pending requests");
                await SaveFixture("staff-reschedules-10-filtered",html);
            }
            using(var noMatch=await staff.GetAsync("/RescheduleRequests/Index?search=never-exists-synthetic"))
            {
                var html=await noMatch.Content.ReadAsStringAsync();
                Check(Table(html).Contains("No matching requests",StringComparison.Ordinal)
                    && html.Contains("Clear search",StringComparison.Ordinal)
                    && html.Contains("0 matching records",StringComparison.Ordinal),
                    "search returns contextual, resettable in-table empty state");
                await SaveFixture("staff-reschedules-no-match",html);
            }
            using(var requestedDate=await staff.GetAsync(
                "/RescheduleRequests/Index?requestedDate="+requests[4].RequestedDate.ToString("yyyy-MM-dd")))
            {
                var html=await requestedDate.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(new[]{requests[4].Id})
                    && html.Contains("1 matching record",StringComparison.Ordinal),
                    "requested-date filter finds only correct records");
            }
            using(var roleBypass=await staff.GetAsync("/RescheduleRequests/Index?status=Approved"))
                Check(DetailIds(Table(await roleBypass.Content.ReadAsStringAsync())).Count==5,
                    "Staff cannot override Pending-only request eligibility");
            using(var ownerHistory=await owner.GetAsync("/RescheduleRequests/Index?status=All"))
            {
                var html=await ownerHistory.Content.ReadAsStringAsync();
                Check(ownerHistory.StatusCode==HttpStatusCode.OK
                    && html.Contains("staff-reschedule-owner-list",StringComparison.Ordinal)
                    && html.Contains("reschedule-schedule",StringComparison.Ordinal)
                    && html.Contains("staff-reschedule-cards",StringComparison.Ordinal),
                    "Owner history retains previous visual date and mobile card markup");
            }
            using(var details=await staff.GetAsync("/RescheduleRequests/Details/"+requests[7].Id))
                Check(details.StatusCode==HttpStatusCode.OK,
                    "Staff Details remains accessible with availability review");
            Check((await db.RescheduleRequests.AsNoTracking()
                .CountAsync(x=>x.Status==RescheduleRequestStatus.Pending))==10,
                "view-only requests leave database statuses unchanged");
            Console.WriteLine($"STAFF_RESCHEDULE_INTEGRATION_PASS: {checks} isolated SQL/Razor checks");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
