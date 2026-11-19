using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Isolated real-SQL, real-Razor Staff booking-table contract tests.
// Uses only an auto-created GUID database on LocalDB; never a live connection.
internal static class StaffBookingsTableTests
{
    private static int checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("FAILED Staff Bookings: " + label);
        checks++;
        Console.WriteLine("PASS Staff Bookings: " + label);
    }

    private static async Task<HttpClient> LoginAsync(ReleaseFactory factory, string email, string password)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies=true, AllowAutoRedirect=false
        });
        var page = await http.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(page,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Check(token.Length > 0, "login form includes antiforgery token");
        using var response=await http.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=token
            }));
        Check(response.StatusCode==HttpStatusCode.Redirect,
            "real Identity authentication succeeds");
        return http;
    }

    private static string Table(string html)
    {
        var needle="<table class=\"table align-middle staff-booking-table staff-data-table\">";
        var start=html.IndexOf(needle,StringComparison.Ordinal);
        if (start<0) throw new InvalidOperationException("Missing Staff standardized table");
        var end=html.IndexOf("</table>",start,StringComparison.Ordinal);
        if (end<0) throw new InvalidOperationException("Unclosed Staff table");
        return html[start..(end+8)];
    }
    private static List<string> Codes(string table) =>
        Regex.Matches(table, "<strong class=\"staff-booking-code\">([^<]+)</strong>")
            .Cast<Match>().Select(m=>WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
    private static List<int> DetailIds(string table) =>
        Regex.Matches(table, "href=\"([^\"]*?/Bookings/Details/([0-9]+)[^\"]*)\"")
            .Cast<Match>().Select(m=>int.Parse(m.Groups[2].Value)).ToList();

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Invalid test instance");
        var database="AriesStaffBookings_"+Guid.NewGuid().ToString("N");
        Check(Regex.IsMatch(database,"^AriesStaffBookings_[0-9a-f]{32}$"),"safe GUID SQL database");
        var connection=$@"Server=(localdb)\{instance};Database={database};Integrated Security=True;TrustServerCertificate=True";
        var root=Path.Combine(Path.GetTempPath(),database);
        Directory.CreateDirectory(root);
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();
        smtp.Start();
        var fixtureRoot=Environment.GetEnvironmentVariable("ARIES_STAFF_BOOKINGS_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtureRoot))
        {
            fixtureRoot=Path.GetFullPath(fixtureRoot);
            if(!fixtureRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture HTML must be written under Temp");
            Directory.CreateDirectory(fixtureRoot);
        }
        async Task SaveFixture(string name,string html)
        {
            if(fixtureRoot != null)
                await File.WriteAllTextAsync(Path.Combine(fixtureRoot,name+".html"),html);
        }
        try
        {
            await db.Database.MigrateAsync();
            await using var factory=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:9"),root);
            using var scope=factory.Services.CreateScope();
            var roleManager=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            foreach(var role in new[]{"Staff","Owner","Client"})
            {
                if(!await roleManager.RoleExistsAsync(role))
                    Check((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded,"isolated "+role+" role created");
            }
            async Task<ApplicationUser> User(string role)
            {
                var name="bookings-"+role.ToLowerInvariant()+"@example.test";
                var user=new ApplicationUser{Email=name,UserName=name,EmailConfirmed=true,
                    FullName="Synthetic "+role,PhoneNumber="09123456789"};
                Check((await users.CreateAsync(user,password)).Succeeded
                    && (await users.AddToRoleAsync(user,role)).Succeeded,"isolated "+role+" identity");
                return user;
            }
            var staffAccount=await User("Staff");
            var ownerAccount=await User("Owner");
            var clientAccount=await User("Client");
            var legacy=new User{FullName="Synthetic Queue Client",
                Email="synthetic-queue@example.test",Role="Client",PasswordHash="IDENTITY_MANAGED"};
            var service=new Service{Name="Synthetic Magic Package",Price=8000,DurationInHours=3};
            db.LegacyUsers.Add(legacy);db.Services.Add(service);
            await db.SaveChangesAsync();
            var createdAt=DateTime.Today.AddDays(-2).AddHours(14);
            var bookings=Enumerable.Range(0,10).Select(i=>new Booking
            {
                ClientId=legacy.Id, ApplicationUserId=clientAccount.Id,
                ServiceId=service.Id, Status=BookingStatus.Declined,
                CreatedAt=createdAt, EventDate=DateTime.Today.AddDays(40+i),
                StartTime=DateTime.Today.AddDays(40+i).AddHours(13),
                EndTime=DateTime.Today.AddDays(40+i).AddHours(16),
                EventType="Birthday",Motif="Blue",
                PartyVenue="Synthetic Venue "+i+", Rizal",
                ContactPerson="Synthetic Client",ContactNumber="09123456789",
                PackageName=service.Name,BasePrice=service.Price,FinalPrice=service.Price,
                PaxCount=45,IsServiceable=true
            }).ToList();
            db.Bookings.AddRange(bookings);
            await db.SaveChangesAsync();

            using var staff=await LoginAsync(factory,staffAccount.Email!,password);
            using var owner=await LoginAsync(factory,ownerAccount.Email!,password);
            using var client=await LoginAsync(factory,clientAccount.Email!,password);
            using var anonymous=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            using(var denied=await client.GetAsync("/Bookings/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"Client cannot access Staff/Owner booking queue");
            using(var denied=await anonymous.GetAsync("/Bookings/Index"))
                Check(denied.StatusCode!=HttpStatusCode.OK,"anonymous booking queue blocked");

            foreach(var n in new[]{0,1,2,3,4,5,6,10})
            {
                for(var i=0;i<bookings.Count;i++)
                    bookings[i].Status=i<n?BookingStatus.Pending:BookingStatus.Declined;
                await db.SaveChangesAsync();
                using var response=await staff.GetAsync("/Bookings/Index");
                var html=await response.Content.ReadAsStringAsync();
                Check(response.StatusCode==HttpStatusCode.OK,$"Staff booking list status 200 for {n} records");
                var table=Table(html);
                Check(Regex.Matches(table,"<th scope=\"col\"").Count==6
                    && table.Contains("<colgroup>",StringComparison.Ordinal),
                    $"semantic six aligned columns at {n}");
                var ids=DetailIds(table);
                var codes=Codes(table);
                var wanted=bookings.Take(n).OrderByDescending(b=>b.CreatedAt)
                    .ThenByDescending(b=>b.Id).Take(5).Select(b=>b.Id).ToList();
                Check(ids.SequenceEqual(wanted) && ids.Count==Math.Min(n,5)
                    && ids.Distinct().Count()==ids.Count
                    && codes.Count==ids.Count,
                    $"visible unique stable newest-first booking rows {n} -> {Math.Min(n,5)}");
                Check(html.Contains(n+" matching record"+(n==1?"":"s"),StringComparison.Ordinal)
                    && html.Contains("Page 1 of "+Math.Max(1,(int)Math.Ceiling(n/5.0)),StringComparison.Ordinal),
                    $"true total, page count for {n}");
                Check(html.Contains("data-staff-table-capacity=\"5\"",StringComparison.Ordinal)
                    && html.Contains("class=\"staff-data-table-frame\"",StringComparison.Ordinal),
                    $"reusable five-row frame at {n}");
                if(n==0)
                    Check(table.Contains("No bookings found",StringComparison.Ordinal)
                        && !table.Contains("No matching bookings",StringComparison.Ordinal)
                        && !table.Contains("staff-booking-code",StringComparison.Ordinal),
                        "empty Staff queue uses dedicated table-row empty state, no filler bookings");
                else
                    Check(!table.Contains("staff-data-table-empty-row",StringComparison.Ordinal),
                        $"no dummy filler row when {n} real bookings exist");
                if(n<=5)
                    Check(!html.Contains("aria-label=\"Previous page\"",StringComparison.Ordinal)
                        && !html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal),
                        $"single page hides redundant navigation at {n}");
                else
                    Check(html.Contains("aria-label=\"Next page\"",StringComparison.Ordinal)
                        && html.Contains("page=2",StringComparison.OrdinalIgnoreCase),
                        $"multi-page next link present at {n}");
                await SaveFixture("staff-bookings-"+n,html);
            }
            for(var i=0;i<bookings.Count;i++)bookings[i].Status=BookingStatus.Pending;
            await db.SaveChangesAsync();

            using(var oldest=await staff.GetAsync("/Bookings/Index?sortBy=oldest"))
            {
                var html=await oldest.Content.ReadAsStringAsync();
                var ids=DetailIds(Table(html));
                var expected=bookings.OrderBy(b=>b.CreatedAt).ThenBy(b=>b.Id)
                    .Take(5).Select(b=>b.Id).ToList();
                Check(ids.SequenceEqual(expected),"Oldest sort uses ascending SQL tie-breaker");
                Check(html.Contains("<option value=\"oldest\" selected=\"selected\">Oldest first</option>",StringComparison.Ordinal)
                    || html.Contains("<option selected=\"selected\" value=\"oldest\">Oldest first</option>",StringComparison.Ordinal),
                    "Sort By selection remains visible");
            }
            foreach(var n in new[]{6,10})
            {
                for(var i=0;i<bookings.Count;i++)
                    bookings[i].Status=i<n?BookingStatus.Pending:BookingStatus.Declined;
                await db.SaveChangesAsync();
                using var page2=await staff.GetAsync("/Bookings/Index?page=2&sortBy=oldest");
                var html=await page2.Content.ReadAsStringAsync();
                var expected=bookings.Take(n).OrderBy(b=>b.CreatedAt).ThenBy(b=>b.Id)
                    .Skip(5).Take(5).Select(b=>b.Id);
                Check(DetailIds(Table(html)).SequenceEqual(expected),
                    $"page two oldest sort returns only correct {n-5} records");
                Check(html.Contains("Page 2 of 2",StringComparison.Ordinal)
                    && html.Contains(n+" matching records",StringComparison.Ordinal),
                    $"paging footer accurate on partial page {n}");
                await SaveFixture("staff-bookings-"+n+"-page2",html);
                using var overPage=await staff.GetAsync("/Bookings/Index?page=999&sortBy=oldest");
                Check(DetailIds(Table(await overPage.Content.ReadAsStringAsync())).SequenceEqual(expected),
                    $"out-of-range page clamps to final valid page at {n}");
            }
            for(var i=0;i<bookings.Count;i++)bookings[i].Status=BookingStatus.Pending;
            await db.SaveChangesAsync();
            var sought=bookings[7];
            var code=$"BK-{sought.CreatedAt.Year}-{sought.Id:D3}";
            using(var searched=await staff.GetAsync("/Bookings/Index?sortBy=oldest&search="+Uri.EscapeDataString(code)))
            {
                var html=await searched.Content.ReadAsStringAsync();
                Check(DetailIds(Table(html)).SequenceEqual(new[]{sought.Id})
                    && html.Contains("1 matching record",StringComparison.Ordinal),
                    "booking-code search composes with sorting and Staff eligibility");
            }
            using(var venueSearch=await staff.GetAsync("/Bookings/Index?search=Synthetic%20Venue%207"))
            {
                Check(DetailIds(Table(await venueSearch.Content.ReadAsStringAsync()))
                    .SequenceEqual(new[]{sought.Id}),"venue search filters only matching booking");
            }
            using(var noMatch=await staff.GetAsync("/Bookings/Index?search=no-matching-synthetic-booking"))
            {
                var html=await noMatch.Content.ReadAsStringAsync();
                Check(Table(html).Contains("No matching bookings",StringComparison.Ordinal)
                    && html.Contains("Clear search",StringComparison.Ordinal)
                    && html.Contains("0 matching records",StringComparison.Ordinal),
                    "unmatched search shows intentional resettable empty state");
                await SaveFixture("staff-bookings-no-match",html);
            }
            using(var wrongStatus=await staff.GetAsync("/Bookings/Index?bookingStatus=Confirmed"))
            {
                Check(DetailIds(Table(await wrongStatus.Content.ReadAsStringAsync())).Count==5,
                    "Staff list ignores legacy bookingStatus query; retains Pending-only scope");
            }
            using(var bookingDetails=await staff.GetAsync("/Bookings/Details/"+sought.Id))
                Check(bookingDetails.StatusCode==HttpStatusCode.OK,
                    "Staff booking Details link opens its correct record");
            using(var ownerList=await owner.GetAsync("/Bookings/Index"))
            {
                var html=await ownerList.Content.ReadAsStringAsync();
                Check(ownerList.StatusCode==HttpStatusCode.OK &&
                    html.Contains("booking-management-table",StringComparison.Ordinal),
                    "Owner booking list remains on its original view");
            }
            Console.WriteLine($"STAFF_BOOKINGS_INTEGRATION_PASS: {checks} checks on isolated LocalDB");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
