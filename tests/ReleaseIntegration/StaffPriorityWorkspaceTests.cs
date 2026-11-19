using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Isolated SQL Server LocalDB + real authenticated MVC/Razor HTML.
// No application data, production host, or production credentials are used.
internal static class StaffPriorityWorkspaceTests
{
    private static int checks;
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException("FAILED Staff Priority: " + description);
        checks++;
        Console.WriteLine("PASS Staff Priority: " + description);
    }

    private static async Task<HttpClient> LoginAsync(ReleaseFactory factory, string email, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Check(token.Length > 0, "real antiforgery token for isolated login");
        using var signIn = await client.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["Email"] = email, ["Password"] = password,
                ["__RequestVerificationToken"] = token
            }));
        Check(signIn.StatusCode == HttpStatusCode.Redirect, "Staff/Client signed in with real Identity cookie");
        return client;
    }

    private static string Panel(string html, string id, string? nextId)
    {
        var marker = "id=\"" + id + "\" role=\"tabpanel\"";
        var from = html.IndexOf(marker, StringComparison.Ordinal);
        if (from < 0) throw new InvalidOperationException("Missing tab panel " + id);
        var to = nextId == null
            ? html.IndexOf("<aside class=\"staff-round1-panel", from, StringComparison.Ordinal)
            : html.IndexOf("id=\"" + nextId + "\" role=\"tabpanel\"", from, StringComparison.Ordinal);
        if (to < from) throw new InvalidOperationException("Missing panel boundary " + id);
        return html[from..to];
    }

    private static int CountItems(string html) =>
        Regex.Matches(html, "class=\"staff-round1-task\"").Count;

    private static int TabTotal(string html, string category)
    {
        var pattern = "data-staff-tab=\"" + category + "\">[^<]*<span>([0-9,]+)</span>";
        var match = Regex.Match(html, pattern);
        if (!match.Success) throw new InvalidOperationException("Missing " + category + " tab count");
        return int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<int> Links(string panel, string route)
    {
        var ids = new List<int>();
        foreach (Match link in Regex.Matches(panel, "href=\"([^\"]+)\""))
        {
            var decoded = WebUtility.HtmlDecode(link.Groups[1].Value);
            if (!decoded.Contains(route, StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(decoded, @"(\d+)(?:[?#].*)?$");
            if (match.Success) ids.Add(int.Parse(match.Groups[1].Value));
        }
        return ids;
    }

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled", "false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup", "false");
        var database = "AriesStaffPriority_" + Guid.NewGuid().ToString("N");
        var instance = Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE") ?? "MSSQLLocalDB";
        if (!Regex.IsMatch(instance, "^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Unsafe LocalDB instance");
        var connection = $@"Server=(localdb)\{instance};Database={database};Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var root = Path.Combine(Path.GetTempPath(),database);
        Directory.CreateDirectory(root);
        await using var db = new ApplicationDbContext(options);
        await using var smtp = new SmtpFixture();
        smtp.Start();
        try
        {
            Check(Regex.IsMatch(database, "^AriesStaffPriority_[0-9a-f]{32}$"),
                "new GUID fixture database only");
            await db.Database.MigrateAsync();
            await using var factory = new ReleaseFactory(connection, smtp.Port,
                new Uri("http://127.0.0.1:9"), root);
            using var scope = factory.Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var role in new[] { "Client", "Staff" })
                if (!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded,
                        "isolated " + role + " role");
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            async Task<ApplicationUser> Account(string email, string role)
            {
                var user = new ApplicationUser { Email=email, UserName=email, EmailConfirmed=true,
                    FullName="Synthetic " + email, PhoneNumber="09123456789" };
                Check((await users.CreateAsync(user,password)).Succeeded
                    && (await users.AddToRoleAsync(user,role)).Succeeded,
                    "isolated " + role + " Identity account");
                return user;
            }
            var staffAccount = await Account("priority-staff@example.test", "Staff");
            var clients = new List<ApplicationUser>();
            for (var i=0;i<10;i++) clients.Add(await Account($"priority-client-{i}@example.test","Client"));
            var service = new Service { Name="Synthetic priority package", Price=9000, DurationInHours=3 };
            var legacy = new User { FullName="Synthetic Test Client", Email="legacy-priority@example.test",
                Role="Client", PasswordHash="IDENTITY_MANAGED" };
            db.Services.Add(service);
            db.LegacyUsers.Add(legacy);
            await db.SaveChangesAsync();
            Booking Booking(int i, string status) => new()
            {
                ClientId=legacy.Id, ApplicationUserId=clients[i%clients.Count].Id,
                ServiceId=service.Id, Service=service,
                Status=status, CreatedAt=DateTime.Today.AddHours(-5),
                EventDate=DateTime.Today.AddDays(40+i),
                StartTime=DateTime.Today.AddDays(40+i).AddHours(14),
                EndTime=DateTime.Today.AddDays(40+i).AddHours(17),
                EventType="Birthday", Motif="Blue", PartyVenue="Synthetic venue, Rizal",
                ContactPerson="Synthetic Client", ContactNumber="09123456789",
                PackageName=service.Name, BasePrice=service.Price, FinalPrice=service.Price,
                PaxCount=50, IsServiceable=true
            };
            var pending = Enumerable.Range(0,10).Select(i=>Booking(i, BookingStatus.Declined)).ToList();
            var rescheduleBookings = Enumerable.Range(0,10).Select(i=>Booking(i+20, BookingStatus.Confirmed)).ToList();
            db.Bookings.AddRange(pending);
            db.Bookings.AddRange(rescheduleBookings);
            await db.SaveChangesAsync();
            var reschedules = rescheduleBookings.Select((booking,i)=>new RescheduleRequest
            {
                BookingId=booking.Id, Booking=booking, OriginalDate=booking.EventDate,
                OriginalStartTime=booking.StartTime, OriginalEndTime=booking.EndTime,
                RequestedDate=booking.EventDate.AddDays(10),
                RequestedStartTime=booking.StartTime.AddDays(10),
                RequestedEndTime=booking.EndTime.AddDays(10),
                Reason="Synthetic reschedule " + i,
                CreatedAt=DateTime.Today.AddHours(-6), Status=RescheduleRequestStatus.Approved
            }).ToList();
            db.RescheduleRequests.AddRange(reschedules);
            await db.SaveChangesAsync();
            var conversations = new List<Conversation>();
            using var staff = await LoginAsync(factory,staffAccount.Email!,password);
            using var client = await LoginAsync(factory,clients[0].Email!,password);
            using (var denied = await client.GetAsync("/Dashboard/Staff"))
                Check(denied.StatusCode != HttpStatusCode.OK, "Client cannot open Staff dashboard");
            using (var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false}))
            using (var denied = await anonymous.GetAsync("/Dashboard/Staff"))
                Check(denied.StatusCode != HttpStatusCode.OK, "anonymous cannot open Staff dashboard");

            foreach (var n in new[] {0,1,4,5,6,10})
            {
                foreach (var (booking,i) in pending.Select((b,i)=>(b,i)))
                    booking.Status = i < n ? BookingStatus.Pending : BookingStatus.Declined;
                foreach (var (request,i) in reschedules.Select((r,i)=>(r,i)))
                    request.Status = i < n ? RescheduleRequestStatus.Pending : RescheduleRequestStatus.Approved;
                for (var i=conversations.Count;i<n;i++)
                {
                    var item = new Conversation {
                        ConversationType=ConversationTypes.ClientSupport,
                        SupportClientId=clients[i].Id, CreatedByUserId=clients[i].Id,
                        Title="Synthetic support " + i,
                        Participants=[
                            new ConversationParticipant {UserId=clients[i].Id},
                            new ConversationParticipant {
                                UserId=staffAccount.Id,
                                LastReadAt=i%2==0 ? null : DateTime.UtcNow.AddDays(1)
                            }],
                        Messages=[new Message {
                            SenderId=clients[i].Id, MessageContent="Synthetic question " + i,
                            SentAt=DateTime.UtcNow.AddMinutes(i/2) // deliberate timestamp ties
                        }]
                    };
                    db.Conversations.Add(item);
                    conversations.Add(item);
                }
                await db.SaveChangesAsync();
                using var response = await staff.GetAsync("/Dashboard/Staff");
                var html = await response.Content.ReadAsStringAsync();
                var fixturePath = Environment.GetEnvironmentVariable("ARIES_STAFF_PRIORITY_FIXTURE_HTML");
                if (n==10 && !string.IsNullOrWhiteSpace(fixturePath))
                {
                    var fullPath = Path.GetFullPath(fixturePath);
                    if (!fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("UI fixture output must be in temporary storage");
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await File.WriteAllTextAsync(fullPath,html);
                }
                Check(response.StatusCode==HttpStatusCode.OK, $"Staff dashboard renders at count {n}");
                Check(!html.Contains("Today's priorities",StringComparison.Ordinal)
                    && !html.Contains("Your top tasks, organized by next action.",StringComparison.Ordinal)
                    && !html.Contains("bi-lightning-charge",StringComparison.Ordinal),
                    $"Priority header cleanup at {n}");
                var messages = Panel(html,"staffTabMessages","staffTabBookings");
                var bookings = Panel(html,"staffTabBookings","staffTabReschedules");
                var resched = Panel(html,"staffTabReschedules",null);
                var visible = Math.Min(n,5);
                Check(TabTotal(html,"messages")==n && CountItems(messages)==visible,
                    $"Client Messages: total {n}, visible {visible}");
                Check(TabTotal(html,"bookings")==n && CountItems(bookings)==visible,
                    $"Booking Requests: total {n}, visible {visible}");
                Check(TabTotal(html,"reschedules")==n && CountItems(resched)==visible,
                    $"Reschedule Requests: total {n}, visible {visible}");
                var unread = Enumerable.Range(0,n).Count(i => i%2==0);
                Check(Regex.IsMatch(html, "<h2>Unread Client Messages</h2><strong>" + unread + "</strong>"),
                    $"Unread metric is independent of all-Client-Messages tab at {n}");
                if (n==0) {
                    Check(messages.Contains("No active Client conversations",StringComparison.Ordinal)
                        && bookings.Contains("No pending booking requests",StringComparison.Ordinal)
                        && resched.Contains("No pending reschedule requests",StringComparison.Ordinal),
                        "three correct empty states");
                }
                var bookingIds = Links(bookings,"/Bookings/Details/");
                var rescheduleIds = Links(resched,"/RescheduleRequests/Details/");
                var messageIds = Links(messages,"/Communications");
                var expectedBookings = pending.Take(n).OrderBy(b=>b.CreatedAt).ThenBy(b=>b.Id)
                    .Take(5).Select(b=>b.Id).ToList();
                var expectedReschedules = reschedules.Take(n).OrderBy(r=>r.CreatedAt).ThenBy(r=>r.Id)
                    .Take(5).Select(r=>r.Id).ToList();
                var expectedMessages = conversations.OrderByDescending(c=>c.Messages.Max(m=>m.SentAt))
                    .ThenByDescending(c=>c.Id).Take(5).Select(c=>c.Id).ToList();
                Check(bookingIds.SequenceEqual(expectedBookings) &&
                    bookingIds.Distinct().Count()==visible,
                    $"oldest-first booking links unique and correct at {n}");
                Check(rescheduleIds.SequenceEqual(expectedReschedules) &&
                    rescheduleIds.Distinct().Count()==visible,
                    $"oldest-first reschedule links unique and correct at {n}");
                Check(messageIds.SequenceEqual(expectedMessages) &&
                    messageIds.Distinct().Count()==visible,
                    $"latest-first Client message links unique and correct at {n}");
                if (n==5) {
                    using var bookingDetail = await staff.GetAsync("/Bookings/Details/"+bookingIds[4]);
                    using var rescheduleDetail = await staff.GetAsync("/RescheduleRequests/Details/"+rescheduleIds[4]);
                    Check(bookingDetail.StatusCode == HttpStatusCode.OK &&
                        rescheduleDetail.StatusCode == HttpStatusCode.OK,
                        "fifth booking and reschedule actions open real detail pages");
                }
            }
            // Change one category alone. Counts and previews of the other two must remain 10/5.
            for (var i=1;i<pending.Count;i++) pending[i].Status=BookingStatus.Declined;
            await db.SaveChangesAsync();
            var separated = await staff.GetStringAsync("/Dashboard/Staff");
            Check(TabTotal(separated,"bookings")==1 && CountItems(Panel(separated,"staffTabBookings","staffTabReschedules"))==1
                && TabTotal(separated,"messages")==10 && CountItems(Panel(separated,"staffTabMessages","staffTabBookings"))==5
                && TabTotal(separated,"reschedules")==10 && CountItems(Panel(separated,"staffTabReschedules",null))==5,
                "independent categories remain correct after isolated booking update");
            Console.WriteLine($"STAFF_PRIORITY_INTEGRATION_PASS: {checks} checks, isolated GUID LocalDB");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if (Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}
