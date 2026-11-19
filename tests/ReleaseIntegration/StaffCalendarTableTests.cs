using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// GUID LocalDB, real authenticated Razor, no live credentials or production DB.
internal static class StaffCalendarTableTests
{
    private static int checks;
    private static void Check(bool ok, string name)
    {
        if(!ok)throw new InvalidOperationException("FAILED Staff Calendar: "+name);
        checks++;
        Console.WriteLine("PASS Staff Calendar: "+name);
    }

    private static async Task<HttpClient> Login(ReleaseFactory app,string email,string password)
    {
        var client=app.CreateClient(new WebApplicationFactoryClientOptions
        {AllowAutoRedirect=false,HandleCookies=true});
        var html=await client.GetStringAsync("/Account/Login");
        var token=WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Check(token.Length>0,"login token exists");
        using var response=await client.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>{
                ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=token
            }));
        Check(response.StatusCode==HttpStatusCode.Redirect,"real Identity cookie established");
        return client;
    }

    private static string ExtractTable(string html)
    {
        var begin=html.IndexOf("id=\"upcomingTable\"",StringComparison.Ordinal);
        if(begin<0)throw new InvalidOperationException("Missing Upcoming Booking List table");
        var left=html.LastIndexOf("<table",begin,StringComparison.Ordinal);
        var end=html.IndexOf("</table>",begin,StringComparison.Ordinal);
        if(left<0||end<0)throw new InvalidOperationException("Invalid table markup");
        return html[left..(end+8)];
    }

    private static List<int> DetailIds(string html) =>
        Regex.Matches(html,"href=\"([^\"]*?/Bookings/Details/([0-9]+)[^\"]*)\"")
            .Cast<Match>().Select(x=>int.Parse(x.Groups[2].Value)).ToList();

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Unsafe SQL instance");
        var database="AriesStaffCalendar_"+Guid.NewGuid().ToString("N");
        Check(Regex.IsMatch(database,"^AriesStaffCalendar_[0-9a-f]{32}$"),"isolated GUID LocalDB");
        var conn=$@"Server=(localdb)\{instance};Database={database};Integrated Security=True;TrustServerCertificate=True";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(conn).Options;
        var directory=Path.Combine(Path.GetTempPath(),database);
        Directory.CreateDirectory(directory);
        var fixtureDirectory=Environment.GetEnvironmentVariable("ARIES_STAFF_CALENDAR_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            fixtureDirectory=Path.GetFullPath(fixtureDirectory);
            if(!fixtureDirectory.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixtures must be in temporary storage");
            Directory.CreateDirectory(fixtureDirectory);
        }
        async Task Fixture(string title,string html)
        {
            if(fixtureDirectory!=null)
                await File.WriteAllTextAsync(Path.Combine(fixtureDirectory,title+".html"),html);
        }
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();
        smtp.Start();
        try
        {
            await db.Database.MigrateAsync();
            await using var app=new ReleaseFactory(conn,smtp.Port,new Uri("http://127.0.0.1:9"),directory);
            using var scope=app.Services.CreateScope();
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            foreach(var role in new[]{"Staff","Owner","Client"})
                if(!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded,"isolated role "+role);
            async Task<ApplicationUser> Account(string role)
            {
                var email="staff-calendar-"+role.ToLowerInvariant()+"@example.test";
                var account=new ApplicationUser{Email=email,UserName=email,EmailConfirmed=true,
                    FullName="Synthetic "+role,PhoneNumber="09123456789"};
                Check((await users.CreateAsync(account,password)).Succeeded &&
                    (await users.AddToRoleAsync(account,role)).Succeeded,"isolated "+role+" identity");
                return account;
            }
            var staffAccount=await Account("Staff");
            var ownerAccount=await Account("Owner");
            var clientAccount=await Account("Client");
            var legacy=new User{FullName="Synthetic Calendar Client",
                Email="calendar@example.test",Role="Client",PasswordHash="IDENTITY_MANAGED"};
            var premium=new Service{Name="Premium Magic",Price=8500,DurationInHours=3};
            var deluxe=new Service{Name="Deluxe Magic",Price=10500,DurationInHours=3};
            var unusedPackage=new Service{Name="Unused Magic Package",Price=10500,DurationInHours=3};
            db.LegacyUsers.Add(legacy);
            db.Services.AddRange(premium,deluxe,unusedPackage);
            await db.SaveChangesAsync();
            var future=DateTime.Today.AddDays(42);
            var bookings=Enumerable.Range(0,10).Select(i=>{
                var date=future.AddDays(i/2);
                var service=i%2==0?premium:deluxe;
                return new Booking
                {
                    ClientId=legacy.Id,ApplicationUserId=clientAccount.Id,
                    ServiceId=service.Id,Service=service,
                    PackageName=service.Name,Status=BookingStatus.Declined,
                    CreatedAt=DateTime.Today.AddDays(-4),
                    EventDate=date,StartTime=date.AddHours(13),
                    EndTime=date.AddHours(16),
                    EventType="Birthday",Motif="Blue",
                    PartyVenue="Synthetic Venue "+i+" in Rizal",
                    ContactPerson="Synthetic Client",ContactNumber="09123456789",
                    BasePrice=service.Price,FinalPrice=service.Price,
                    PaxCount=55,IsServiceable=true
                };
            }).ToList();
            db.Bookings.AddRange(bookings);
            await db.SaveChangesAsync();
            using var staff=await Login(app,staffAccount.Email!,password);
            using var owner=await Login(app,ownerAccount.Email!,password);
            using var client=await Login(app,clientAccount.Email!,password);
            using var anon=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            using(var denied=await client.GetAsync("/Calendar/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"Client Calendar access denied");
            using(var denied=await anon.GetAsync("/Calendar/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"anonymous Calendar access denied");

            foreach(var n in new[]{0,1,2,3,4,5,6,10})
            {
                for(var i=0;i<bookings.Count;i++)
                    bookings[i].Status=i<n?BookingStatus.Confirmed:BookingStatus.Declined;
                await db.SaveChangesAsync();
                using var res=await staff.GetAsync("/Calendar/Index");
                var html=await res.Content.ReadAsStringAsync();
                Check(res.StatusCode==HttpStatusCode.OK,$"Staff Calendar renders for {n} future bookings");
                var table=ExtractTable(html);
                Check(html.Contains("staff-upcoming-table-frame",StringComparison.Ordinal) &&
                    table.Contains("staff-data-table",StringComparison.Ordinal) &&
                    table.Contains("<colgroup>",StringComparison.Ordinal) &&
                    Regex.Matches(table,"<th scope=\"col\"").Count==6,
                    $"shared semantic six-column table for {n}");
                var rows=Regex.Matches(table,"<tr class=\"js-upcoming-row\"").Count;
                Check(rows==n,$"{n} eligible booking rows present for client-side paging");
                Check(!html.Contains("id=\"upcomingCards\"",StringComparison.Ordinal),
                    $"Staff does not render redundant mobile booking cards at {n}");
                var ids=DetailIds(table);
                var expected=bookings.Take(n).OrderBy(b=>b.EventDate)
                    .ThenBy(b=>b.StartTime).ThenBy(b=>b.Id).Select(b=>b.Id).ToList();
                Check(ids.SequenceEqual(expected) && ids.Distinct().Count()==n,
                    $"stable date/time/id ordering, unique detail links for {n}");
                Check(html.Contains(n+" matching record"+(n==1?"":"s"),StringComparison.Ordinal),
                    $"count {n} reflects complete upcoming dataset");
                Check(html.Contains("id=\"staffUpcomingPrev\"",StringComparison.Ordinal) &&
                    html.Contains("id=\"staffUpcomingNext\"",StringComparison.Ordinal) &&
                    html.Contains("id=\"staffUpcomingPageInfo\"",StringComparison.Ordinal),
                    "existing pagination controls retained");
                Check(html.Contains("id=\"calPrev\"",StringComparison.Ordinal) &&
                    html.Contains("id=\"calToday\"",StringComparison.Ordinal) &&
                    html.Contains("id=\"calNext\"",StringComparison.Ordinal) &&
                    html.Contains("id=\"calendarPackageChips\"",StringComparison.Ordinal),
                    "calendar navigation and original legend controls retained");
                Check(table.Contains("id=\"upcomingFilteredEmpty\"",StringComparison.Ordinal) &&
                    table.Contains("No upcoming bookings",StringComparison.Ordinal),
                    "in-table empty state is available for selected package/empty dataset");
                Check(!html.Contains("Calendar Settings",StringComparison.Ordinal),
                    "Staff does not receive Owner Calendar Settings");
                await Fixture("staff-calendar-"+n,html);
                using var ownerResponse=await owner.GetAsync("/Calendar/Index");
                var ownerHtml=await ownerResponse.Content.ReadAsStringAsync();
                Check(ownerResponse.StatusCode==HttpStatusCode.OK &&
                    ownerHtml.Contains("staff-upcoming-table-frame",StringComparison.Ordinal) &&
                    ownerHtml.Contains("staff-data-table-scroll",StringComparison.Ordinal) &&
                    ownerHtml.Contains("class=\"table staff-upcoming-table staff-data-table\"",StringComparison.Ordinal) &&
                    !ownerHtml.Contains("id=\"upcomingCards\"",StringComparison.Ordinal),
                    $"Owner uses the unified Staff table for {n} upcoming bookings");
                Check(ExtractTable(ownerHtml)==table &&
                    DetailIds(ExtractTable(ownerHtml)).SequenceEqual(expected) &&
                    ownerHtml.Contains(n+" matching record"+(n==1?"":"s"),StringComparison.Ordinal),
                    $"Owner has identical Staff table markup, ordered links and accurate counts at {n}");
                Check(ownerHtml.Contains("Calendar Settings",StringComparison.Ordinal) &&
                    ownerHtml.Contains("/Calendar/Settings",StringComparison.Ordinal) &&
                    ownerHtml.Contains("staff-final-booking-modal",StringComparison.Ordinal) &&
                    ownerHtml.Contains("staff-final-preview-group",StringComparison.Ordinal) &&
                    !ownerHtml.Contains("staff-preview-grid",StringComparison.Ordinal),
                    $"Owner preserves Settings while using the grouped Staff preview at {n}");
                Check(ownerHtml.Contains("lib/fullcalendar/index.global.min.js",StringComparison.Ordinal) &&
                    !ownerHtml.Contains("cdn.jsdelivr.net/npm/fullcalendar",StringComparison.Ordinal) &&
                    ownerHtml.Contains("fixedWeekCount: true",StringComparison.Ordinal) &&
                    ownerHtml.Contains("staff-final-day-booking",StringComparison.Ordinal),
                    $"Owner has Staff month geometry, click targets and local FullCalendar at {n}");
                await Fixture("owner-calendar-"+n,ownerHtml);
            }
            for(var i=0;i<bookings.Count;i++)bookings[i].Status=BookingStatus.Confirmed;
            await db.SaveChangesAsync();
            using(var settings=await owner.GetAsync("/Calendar/Settings"))
                Check(settings.StatusCode==HttpStatusCode.OK,
                    "Owner keeps Calendar Settings access");
            using(var deniedSettings=await staff.GetAsync("/Calendar/Settings"))
                Check(deniedSettings.StatusCode!=HttpStatusCode.OK,
                    "Staff remains forbidden from Owner Calendar Settings");
            using(var single=await staff.GetAsync("/Calendar/GetReservationsByDate?date="+future.ToString("yyyy-MM-dd")))
            {
                var json=await single.Content.ReadAsStringAsync();
                Check(single.StatusCode==HttpStatusCode.OK &&
                    json.Contains(bookings[0].Id.ToString(),StringComparison.Ordinal) &&
                    json.Contains(bookings[1].Id.ToString(),StringComparison.Ordinal),
                    "date-specific event preview still fetches confirmed bookings");
            }
            using(var details=await staff.GetAsync("/Bookings/Details/"+bookings[8].Id))
                Check(details.StatusCode==HttpStatusCode.OK,
                    "upcoming booking Details action resolves booking 9");
            Console.WriteLine($"STAFF_CALENDAR_INTEGRATION_PASS: {checks} checks on isolated GUID LocalDB");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
