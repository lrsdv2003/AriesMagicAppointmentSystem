using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class StaffUnifiedBookingDetailsTests
{
    private static int checks;
    private static void Check(bool yes, string label)
    {
        if (!yes) throw new InvalidOperationException("FAILED Unified Staff Booking Details: " + label);
        checks++;
        Console.WriteLine("PASS Unified Staff Booking Details: " + label);
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(
        html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<HttpClient> Login(ReleaseFactory factory, string email, string password)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false
        });
        var login = await http.GetStringAsync("/Account/Login");
        Check(Token(login).Length > 0, "Identity login antiforgery token present");
        using var result = await http.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Email"]=email,["Password"]=password,
                ["__RequestVerificationToken"]=Token(login)
            }));
        Check(result.StatusCode == HttpStatusCode.Redirect &&
              !(result.Headers.Location?.OriginalString ?? "").StartsWith("/Account/VerifyEmail",
                  StringComparison.OrdinalIgnoreCase), "Identity authenticated session established");
        return http;
    }

    private static string DetailMarkup(string html)
    {
        var start = html.IndexOf("<div class=\"staff-booking-page staff-booking-details-page staff-unified-booking-details\">",
            StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Staff unified layout absent");
        return html[start..];
    }

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup", "false");
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled", "false");
        var localDb = Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE") ?? "MSSQLLocalDB";
        if (!Regex.IsMatch(localDb, "^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Only the isolated LocalDB instance is allowed.");
        var dbName = "AriesUnifiedStaffDetails_" + Guid.NewGuid().ToString("N");
        var connection = $@"Server=(localdb)\{localDb};Database={dbName};Integrated Security=True;TrustServerCertificate=True";
        var tempRoot = Path.Combine(Path.GetTempPath(), dbName);
        Directory.CreateDirectory(tempRoot);
        var fixtureRoot = Environment.GetEnvironmentVariable("ARIES_STAFF_DETAILS_FIXTURE_ROOT");
        if (!string.IsNullOrWhiteSpace(fixtureRoot))
        {
            fixtureRoot = Path.GetFullPath(fixtureRoot);
            if (!fixtureRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture files must remain in the Temp directory.");
            Directory.CreateDirectory(fixtureRoot);
        }
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await using var smtp = new SmtpFixture();
        smtp.Start();
        try
        {
            await db.Database.MigrateAsync();
            await using var factory = new ReleaseFactory(connection, smtp.Port,
                new Uri("http://127.0.0.1:9"), tempRoot);
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var password = "SyntheticBookingDetails2026!Safe";
            foreach (var role in new[] {"Client","Staff","Owner"})
                if (!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded, role + " isolated role created");
            var accounts = new Dictionary<string,ApplicationUser>();
            foreach (var role in new[] {"Client","Staff","Owner"})
            {
                var email = "unifieddetails-" + role.ToLowerInvariant() + "@example.test";
                var user = new ApplicationUser {UserName=email,Email=email,FullName="Fixture " + role,
                    PhoneNumber="09123456789",EmailConfirmed=true};
                Check((await users.CreateAsync(user,password)).Succeeded &&
                    (await users.AddToRoleAsync(user,role)).Succeeded, role + " account seeded");
                accounts[role] = user;
            }
            var legacy = new User {
                FullName="Fixture Events Client",Email="fixture-events@example.test",
                PasswordHash="IDENTITY_MANAGED",Role="Client"
            };
            var service = new Service {Name="Fixture Party Magic",Price=9000,DurationInHours=3};
            db.LegacyUsers.Add(legacy);db.Services.Add(service);
            await db.SaveChangesAsync();
            var future=DateTime.Today.AddDays(27);
            Booking Booking(string status,string venue) => new()
            {
                ClientId=legacy.Id,ApplicationUserId=accounts["Client"].Id,
                ServiceId=service.Id,Status=status,CreatedAt=DateTime.Now,
                EventDate=future,StartTime=future.AddHours(13),EndTime=future.AddHours(16),
                EventType="Birthday",Motif="Blue",PartyTheme="Under the Sea",
                ContactPerson="Fixture Point of Contact",ContactNumber="09123456789",
                PartyVenue=venue,VenueDirections="At the long entrance beside the main gate.",
                PackageName=service.Name,BasePrice=9000,FinalPrice=9800,
                TravelFee=800,RequiredDownpayment=2000,PaxCount=42,
                DistanceKm=21.5,ServiceZone="Zone 2",IsServiceable=true,
                RemovedInclusionsJson="[\"Sound system\"]"
            };
            var pending=Booking(BookingStatus.Pending,"Fixture Venue, Marikina City, Philippines");
            pending.InternalNotes="STAFF PRIVATE REVIEW NOTES";
            var location=Booking(BookingStatus.Pending,"Unverified Venue, Manila, Philippines");
            location.RequiresManualReview=true;
            location.TravelFeeStatus=VenueLocationPolicy.PendingReview;
            var confirmed=Booking(BookingStatus.Confirmed,"Verified Venue, Quezon City, Philippines");
            confirmed.Payments.Add(new Payment {
                Amount=2000,Status=PaymentStatus.Verified,PaymentMethod="GCash",
                ProofImagePath="/private-proofs/fixture-placeholder",
                VerifiedAt=DateTime.UtcNow.AddDays(-1),
                InitialVerificationAtUtc=DateTime.UtcNow.AddDays(-1)
            });
            db.Bookings.AddRange(pending,location,confirmed);
            await db.SaveChangesAsync();
            foreach (var booking in new[]{pending,location,confirmed})
            {
                db.BookingTimelines.Add(new BookingTimeline {
                    BookingId=booking.Id,CreatedAt=DateTime.Now.AddDays(-1),
                    EventType=TimelineEventType.BookingCreated,Notes="Timeline original"
                });
                db.BookingTimelines.Add(new BookingTimeline {
                    BookingId=booking.Id,CreatedAt=DateTime.Now,
                    EventType=TimelineEventType.BookingApproved,Notes="Timeline follow-up"
                });
            }
            await db.SaveChangesAsync();
            using var staff=await Login(factory, accounts["Staff"].Email!,password);
            using var owner=await Login(factory, accounts["Owner"].Email!,password);
            using var client=await Login(factory, accounts["Client"].Email!,password);
            using var anonymous=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            var headings=new[] {
                "Client Information","Event Information","Packing Information",
                "Venue Travel Overview","Client Notes"
            };
            foreach (var (name,booking) in new[] {
                ("pending",pending),("location-review",location),("confirmed",confirmed)
            })
            {
                using var response=await staff.GetAsync("/Bookings/Details/"+booking.Id);
                var html=await response.Content.ReadAsStringAsync();
                Check(response.StatusCode==HttpStatusCode.OK && html.Contains("staff-unified-booking-details"),
                    name+" Staff details served from authorized MVC handler");
                var view=DetailMarkup(html);
                var positions=headings.Select(h=>view.IndexOf("> "+h+"</h2>",StringComparison.Ordinal)).ToArray();
                Check(positions.All(p=>p>=0) && positions.SequenceEqual(positions.OrderBy(x=>x)),
                    name+" five required sections in identical order");
                Check(Regex.Matches(view,"<section class=\"dashboard-panel staff-detail-section").Count==5 &&
                    view.Contains("staff-unified-section-stack") &&
                    view.Contains("staff-final-booking-primary"),
                    name+" exactly five vertical information sections");
                var code=$"BK-{booking.CreatedAt.Year}-{booking.Id:D3}";
                Check(view.Contains("<h1>"+code+"</h1>") &&
                    !view.Contains("Staff workspace") && !view.Contains(">Booking Details</h1>") &&
                    !view.Contains(">Available Actions</h2>"),
                    name+" real booking code header without redundant page text");
                Check(view.Contains(booking.PartyVenue) && view.Contains("Fixture Events Client") &&
                    view.Contains(booking.PackageName) && view.Contains("Sound system") &&
                    view.Contains("At the long entrance beside the main gate.") &&
                    !view.Contains(">Selected Service</span>"),
                    name+" actual client/package/venue data retained without duplicate service");
                Check(view.Contains("staff-final-booking-timeline") &&
                    view.Contains("Booking Timeline") &&
                    view.IndexOf("Booking Created",StringComparison.Ordinal) <
                    view.IndexOf("Booking Approved",StringComparison.Ordinal),
                    name+" common right timeline and original chronological events");
                Check(view.Contains("No notes provided.") &&
                    !view.Contains("Revise confirmed event") &&
                    !view.Contains("Staff may propose a new venue or package."),
                    name+" Client notes fallback and redundant copy removed");
                Check(view.IndexOf("staff-unified-actions",StringComparison.Ordinal) >
                    view.IndexOf("Client Notes",StringComparison.Ordinal) &&
                    view.IndexOf("staff-final-booking-timeline",StringComparison.Ordinal) >
                    view.IndexOf("staff-unified-actions",StringComparison.Ordinal),
                    name+" actions after Client Notes and timeline ordered last on mobile");
                if (fixtureRoot != null)
                    await File.WriteAllTextAsync(Path.Combine(fixtureRoot,name+".html"),html);
                if (name=="pending")
                {
                    Check(view.Contains("id=\"approveBookingModal\"") &&
                        view.Contains("id=\"declineBookingModal\"") &&
                        view.Contains("staff-unified-approve") &&
                        view.Contains("js-booking-decision-form") &&
                        view.Contains("name=\"__RequestVerificationToken\"") &&
                        view.Contains("STAFF PRIVATE REVIEW NOTES") &&
                        view.Contains("staff-unified-private-notes"),
                        "pending approval, decline, private notes and antiforgery preserved");
                    Check(!view.Contains("STAFF PRIVATE REVIEW NOTES</span>") &&
                        view.IndexOf("STAFF PRIVATE REVIEW NOTES")>view.IndexOf("Client Notes"),
                        "private Staff notes remain separate from Client notes");
                }
                if (name=="location-review")
                    Check(view.Contains("asp-route-id") == false &&
                        view.Contains("Resolve the venue and travel quote") &&
                        view.Contains("name=\"latitude\"") && view.Contains("name=\"reviewReason\"") &&
                        view.Contains("staff-unified-approve") && view.Contains("disabled"),
                        "location-review form and disabled approval guard retained");
                if (name=="confirmed")
                    Check(!view.Contains("id=\"approveBookingModal\"") &&
                        !view.Contains("id=\"declineBookingModal\"") &&
                        view.Contains("Edit Booking Venue / Package") &&
                        view.Contains("/BookingChanges/Edit/") &&
                        view.Contains("staff-unified-actions"),
                        "confirmed revision navigation kept without pending action buttons");
            }
            using(var ownerView=await owner.GetAsync("/Bookings/Details/"+confirmed.Id))
                Check(ownerView.StatusCode==HttpStatusCode.OK &&
                    (await ownerView.Content.ReadAsStringAsync()).Contains("Booking Timeline",StringComparison.Ordinal),
                    "Owner retains authorized booking details and Booking Timeline");
            using(var clientView=await client.GetAsync("/Bookings/Details/"+pending.Id))
                Check(clientView.StatusCode==HttpStatusCode.OK &&
                    !(await clientView.Content.ReadAsStringAsync()).Contains("staff-unified-booking-details"),
                    "Client cannot use Staff booking details presentation");
            using(var anonymousView=await anonymous.GetAsync("/Bookings/Details/"+pending.Id))
                Check(anonymousView.StatusCode!=HttpStatusCode.OK,"Anonymous detail access denied");
            Check((await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==pending.Id)).Status==BookingStatus.Pending &&
                  (await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==confirmed.Id)).Status==BookingStatus.Confirmed &&
                  await db.BookingTimelines.CountAsync(t=>t.BookingId==pending.Id)==2,
                  "GET display does not mutate pending/confirmed statuses or timeline history");
            Console.WriteLine($"UNIFIED_STAFF_BOOKING_DETAILS_PASS: {checks} authenticated MVC/SQL checks");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot,true);
        }
    }
}
