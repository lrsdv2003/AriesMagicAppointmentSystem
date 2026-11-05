using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Test executable only; never compiled/published into the application.
// Each loopback port forwards a real, separately authenticated synthetic Identity session.
// No passwords, cookie values, fixture sign-in route, or production connection are exposed.
static class InternalWorkspaceFixture
{
    public static async Task RunAsync(bool empty = false)
    {
        var name = "AriesWorkspaceVisual_" + Guid.NewGuid().ToString("N");
        var connection = @"Server=(localdb)\MSSQLLocalDB;Database=" + name + ";Integrated Security=True;TrustServerCertificate=True";
        var root = Path.GetFullPath(Path.Combine(".tmp-owner-build", name));
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await using var smtp = new SmtpFixture(); smtp.Start();
        var mapBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Staging", ContentRootPath=root });
        mapBuilder.Logging.ClearProviders();
        await using var map = mapBuilder.Build(); map.Urls.Add("http://127.0.0.1:0");
        map.MapGet("/search", () => Results.Json(Array.Empty<object>())); await map.StartAsync();
        await using var factory = new ReleaseFactory(connection,smtp.Port,new Uri(map.Urls.Single()),root);
        var hosts = new List<WebApplication>(); var clients = new List<HttpClient>();
        var state = Path.GetFullPath(Path.Combine(".tmp-owner-build/phase8", empty ? "empty-fixture.private.json" : "live-fixture.private.json"));
        try
        {
            if(!Regex.IsMatch(name,"^AriesWorkspaceVisual_[0-9a-f]{32}$") || db.Database.GetDbConnection().DataSource!=@"(localdb)\MSSQLLocalDB") throw new Exception("Unsafe fixture target.");
            await db.Database.MigrateAsync(); // Existing schema, applied only to this new GUID LocalDB test database.
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var accounts = new Dictionary<string,ApplicationUser>();
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            foreach(var role in new[]{"Staff","Admin","Owner","Client"})
            {
                if(!(await roles.RoleExistsAsync(role)) && !(await roles.CreateAsync(new IdentityRole(role))).Succeeded) throw new Exception("Fixture role creation failed.");
                var account = new ApplicationUser {UserName=role.ToLowerInvariant()+"@example.test",Email=role.ToLowerInvariant()+"@example.test",EmailConfirmed=true,FullName="Synthetic "+role,PhoneNumber="09123456789"};
                if(!(await users.CreateAsync(account,password)).Succeeded || !(await users.AddToRoleAsync(account,role)).Succeeded) throw new Exception("Fixture identity creation failed.");
                accounts[role] = account;
            }
            if(!empty) await SeedAsync(db,accounts,root);
            var effective = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetDbConnection();
            if(effective.DataSource!=@"(localdb)\MSSQLLocalDB" || effective.Database!=name) throw new Exception("Fixture isolation failed.");
            var urls = new Dictionary<string,string>();
            foreach(var (role,index) in new[]{"Staff","Admin","Owner","Client"}.Select((r,i)=>(r,i)))
            {
                var client = factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false,HandleCookies=true}); clients.Add(client);
                var login = await client.GetStringAsync("/Account/Login");
                var token = WebUtility.HtmlDecode(Regex.Match(login,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
                using var signedIn = await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string> {{"Email",accounts[role].Email!},{"Password",password},{"__RequestVerificationToken",token}}));
                if(signedIn.StatusCode!=HttpStatusCode.Redirect) throw new Exception("Synthetic Identity sign-in failed.");
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Development",ContentRootPath=root}); builder.Logging.ClearProviders();
                var proxy = builder.Build(); var url = "http://127.0.0.1:"+((empty?5320:5316)+index); proxy.Urls.Add(url); hosts.Add(proxy); urls[role]=url;
                proxy.Run(async context =>
                {
                    // The server-side CookieContainer belongs to this role only. Browser cookies
                    // are deliberately not forwarded between localhost ports/role workspaces.
                    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
                    if(context.Request.ContentLength>0){using var stream=new MemoryStream();await context.Request.Body.CopyToAsync(stream);request.Content=new ByteArrayContent(stream.ToArray());}
                    foreach(var header in context.Request.Headers){if(header.Key is "Host" or "Connection" or "Cookie")continue;if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray()))request.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());}
                    request.Headers.Host=context.Request.Host.Value;
                    using var response=await client.SendAsync(request);context.Response.StatusCode=(int)response.StatusCode;
                    foreach(var header in response.Headers.Concat(response.Content.Headers)){if(header.Key.Equals("Set-Cookie",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))continue;context.Response.Headers[header.Key]=header.Value.ToArray();}
                    await context.Response.Body.WriteAsync(await response.Content.ReadAsByteArrayAsync());
                });
                await proxy.StartAsync();
            }
            await File.WriteAllTextAsync(state,JsonSerializer.Serialize(new {Pid=Environment.ProcessId,Directory=root,Database=name,Urls=urls,Empty=empty,PaymentIds=await db.Payments.Select(p=>p.Id).ToListAsync(),BookingIds=await db.Bookings.Select(b=>b.Id).ToListAsync(),RescheduleIds=await db.RescheduleRequests.Select(r=>r.Id).ToListAsync()}));
            Console.WriteLine("Isolated synthetic workspaces ready: "+string.Join(", ",urls.Select(p=>p.Key+" "+p.Value))+". Stop marker: "+Path.Combine(root,"stop"));
            while(!File.Exists(Path.Combine(root,"stop"))) await Task.Delay(250);
        }
        finally
        {
            foreach(var host in hosts){await host.StopAsync();await host.DisposeAsync();}
            foreach(var client in clients) client.Dispose();
            if(!Regex.IsMatch(name,"^AriesWorkspaceVisual_[0-9a-f]{32}$")) throw new Exception("Unsafe fixture teardown target.");
            await db.Database.EnsureDeletedAsync();
            if(File.Exists(state)) File.Delete(state);
        }
    }

    static async Task SeedAsync(ApplicationDbContext db,Dictionary<string,ApplicationUser> users,string root)
    {
        await ServiceSeeder.SeedAsync(db); // Source-authoritative names/prices, intentionally no invented inclusions.
        var packages=await db.Services.OrderBy(s=>s.Price).ToListAsync();
        var client = new User {FullName="Synthetic Client",Email=users["Client"].Email!,Role="Client",PasswordHash="IDENTITY_MANAGED"};
        db.LegacyUsers.Add(client); await db.SaveChangesAsync();
        var proofRoot=Path.Combine(root,"financial-proofs"); Directory.CreateDirectory(proofRoot);
        File.Copy("tests/ReleaseIntegration/Fixtures/payment.png",Path.Combine(proofRoot,"synthetic-workspace.png"));
        Booking Make(int day,string status,int package=0) => new() {Client=client,ApplicationUserId=users["Client"].Id,ServiceId=packages[package].Id,PackageName=packages[package].Name,BasePrice=packages[package].Price,FinalPrice=packages[package].Price,EventDate=DateTime.Today.AddDays(day),StartTime=DateTime.Today.AddDays(day).AddHours(14),EndTime=DateTime.Today.AddDays(day).AddHours(17),Status=status,PartyVenue="Synthetic event venue, Rizal",ContactPerson="Synthetic Client",ContactNumber="09123456789",EventType="Birthday",PaxCount=50,IsServiceable=true,CreatedAt=DateTime.Now.AddHours(-day)};
        var pending=Enumerable.Range(1,5).Select(i=>Make(i+4,BookingStatus.Pending,i%3)).ToList(); db.Bookings.AddRange(pending);
        var confirmed=Enumerable.Range(1,4).Select(i=>Make(i*2-1,BookingStatus.Confirmed,i%3)).ToList(); db.Bookings.AddRange(confirmed);
        var payable=Enumerable.Range(1,5).Select(i=>Make(15+i,BookingStatus.AwaitingVerification,i%3)).ToList(); db.Bookings.AddRange(payable);
        var declined=Make(24,BookingStatus.Declined);db.Bookings.Add(declined);
        await db.SaveChangesAsync();
        foreach(var b in payable) db.Payments.Add(new(){Booking=b,Amount=2000,Status=PaymentStatus.Pending,ProofImagePath="synthetic-workspace.png",TransactionReference="SYNTHETIC-"+b.Id,OcrVerifications=[new(){VerificationPurpose=OcrVerificationPurposes.PaymentProof,ExtractedAmount=1900,VerificationResult=OcrVerificationResults.PartialMatch,WarningSummary="Synthetic advisory discrepancy; review the test image."}]});
        foreach(var b in confirmed) db.Payments.Add(new(){Booking=b,Amount=2000,Status=PaymentStatus.Verified,ProofImagePath="synthetic-workspace.png",VerifiedAt=DateTime.Now.AddDays(-1),VerifiedByUserId=users["Owner"].Id,VerifiedByUserName="Synthetic Owner"});
        foreach(var b in confirmed.Take(2)) db.RescheduleRequests.Add(new(){Booking=b,OriginalDate=b.EventDate,OriginalStartTime=b.StartTime,OriginalEndTime=b.EndTime,RequestedDate=b.EventDate.AddDays(14),RequestedStartTime=b.StartTime.AddDays(14),RequestedEndTime=b.EndTime.AddDays(14),Reason="Synthetic schedule conflict",Status=RescheduleRequestStatus.Pending});
        await db.SaveChangesAsync();
        foreach(var p in await db.Payments.Where(p=>p.Status==PaymentStatus.Verified).OrderBy(p=>p.Id).Take(2).ToListAsync()) db.RefundRequests.Add(new(){BookingId=p.BookingId,OriginalPaymentId=p.Id,Amount=2000,ClientReason="Synthetic refund review request",GCashAccountName="Synthetic Client",GCashNumber="09123456789",PaymentProofImagePath="synthetic-workspace.png",Status=RefundStatus.Pending});
        db.Conversations.Add(new(){ConversationType=ConversationTypes.ClientSupport,Title="Synthetic event planning",CreatedByUserId=users["Client"].Id,Participants=[new(){UserId=users["Client"].Id},new(){UserId=users["Staff"].Id}],Messages=[new(){SenderId=users["Client"].Id,MessageContent="Can we review the event schedule? Synthetic message for local testing."}]});
        db.Conversations.Add(new(){ConversationType=ConversationTypes.ClientSupport,Title="Synthetic venue details",CreatedByUserId=users["Client"].Id,Participants=[new(){UserId=users["Client"].Id},new(){UserId=users["Staff"].Id}],Messages=[new(){SenderId=users["Client"].Id,MessageContent="Please confirm the synthetic venue details."},new(){SenderId=users["Client"].Id,MessageContent="This is a second unread test message."}]});
        foreach(var role in new[]{"Staff","Admin","Owner"})
        {
            db.Notifications.Add(new(){UserId=users[role].Id,Title="Synthetic workspace update",Message="An isolated test item is ready for review.",Link="/Dashboard/"+role});
            db.SystemActivities.Add(new(){Type=role=="Staff"?SystemActivityType.BookingApproved:role=="Owner"?SystemActivityType.PaymentVerified:SystemActivityType.ServiceUpdated,Description="Synthetic local fixture activity",PerformedByUserId=users[role].Id,PerformedByUserName="Synthetic "+role});
        }
        db.BlockedDates.Add(new(){Date=DateTime.Today.AddDays(20),Reason="Synthetic blocked date"});
        await db.SaveChangesAsync();
    }
}
