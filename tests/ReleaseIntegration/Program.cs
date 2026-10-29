using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal static class ReleaseIntegrationRunner
{
    static int checks;
    static double restoreSeconds;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static string Password() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
    static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    static async Task<string> GetToken(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK || Token(html).Length == 0) throw new Exception("Token page failed: " + path + " HTTP " + (int)response.StatusCode);
        return Token(html);
    }
    static Task<HttpResponseMessage> Post(HttpClient client, string path, string token, params (string,string)[] values)
        => client.PostAsync(path, new FormUrlEncodedContent(values.Select(x => new KeyValuePair<string,string>(x.Item1,x.Item2)).Append(new("__RequestVerificationToken", token))));
    static async Task<HttpClient> Login(ReleaseFactory factory, string role, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await GetToken(client, "/Account/Login");
        using var response = await Post(client, "/Account/Login", token, ("Email", role.ToLowerInvariant()+"@example.test"), ("Password",password));
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.Contains(".AspNetCore.Identity.Application=")), role + " real Identity login issues cookie");
        return client;
    }
    static async Task Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--internal-workspaces-ui-fixture"})) { await InternalWorkspaceFixture.RunAsync(); return; }
        if(args.SequenceEqual(new[]{"--internal-workspaces-empty-fixture"})) { await InternalWorkspaceFixture.RunAsync(empty:true); return; }
        if(args.SequenceEqual(new[]{"--auth-ui-fixture"})) { await AuthBrowserFixture.RunAsync(); return; }
        if(args.SequenceEqual(new[]{"--booking-ui-fixture"})) { await BookingBrowserFixture.RunAsync(); return; }
        if(args.SequenceEqual(new[]{"--client-hub-ui-fixture"})) { await BookingBrowserFixture.RunAsync(hubPreview:true); return; }
        if(args.SequenceEqual(new[]{"--midnight-ui-fixture"})) { await BookingBrowserFixture.RunAsync(midnightPreview:true); return; }
        if(args.SequenceEqual(new[]{"--release-review-ui-fixture"})) { await BookingBrowserFixture.RunAsync(releaseReview:true); return; }
        if(args.SequenceEqual(new[]{"--manual-ui-preview"})) { await BookingBrowserFixture.RunAsync(manualPreview:true); return; }
        if(args.SequenceEqual(new[]{"--independent-findings"})) { await IndependentFindingsReview.RunAsync(); return; }
        if(args.SequenceEqual(new[]{"--reproduce-findings"})) { await IndependentFindingsReview.RunAsync(reproduce:true); return; }
        if(args.Length != 0) throw new ArgumentException("Unknown test mode.");
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled", "false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup", "false");
        var database = "AriesReleaseIntegration_" + Guid.NewGuid().ToString("N");
        var localInstance = Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE") ?? "MSSQLLocalDB";
        if (!Regex.IsMatch(localInstance, "^[A-Za-z][A-Za-z0-9_-]{0,63}$")) throw new Exception("Invalid local test instance name.");
        var connection = $"Server=(localdb)\\{localInstance};Database={database};Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        var root = Path.GetFullPath(Path.Combine(".tmp-owner-build", database));
        Directory.CreateDirectory(root);
        await using var db = new ApplicationDbContext(options);
        await using var smtp = new SmtpFixture(); smtp.Start();
        var mapBuilder = WebApplication.CreateBuilder(); mapBuilder.Logging.ClearProviders();
        await using var map = mapBuilder.Build(); map.Urls.Add("http://127.0.0.1:0");
        var mapFails = false;
        map.MapGet("/search", () => mapFails ? Microsoft.AspNetCore.Http.Results.StatusCode(503) : Microsoft.AspNetCore.Http.Results.Json(new[] { new { lat="14.58125", lon="121.1258", display_name="Isolated fixture venue, Philippines" } }));
        await map.StartAsync();
        var mapUrl = new Uri(map.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        try
        {
            Check(Environment.Version.Major == 10, "Integration process runs on .NET 10");
            await RecoveryRegressionTests.SchemaAsync(connection,smtp.Port,mapUrl,root,Check);
            await db.Database.MigrateAsync();
            Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Entire EF migration chain applies to an empty isolated SQL database");
            var script=await File.ReadAllTextAsync(".tmp-owner-build/release-migrations.sql");
            await db.Database.OpenConnectionAsync();
            try {
                for(var pass=0;pass<2;pass++) foreach(var batch in Regex.Split(script,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
                    if(!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            } finally {await db.Database.CloseConnectionAsync();}
            Check(!(await db.Database.GetPendingMigrationsAsync()).Any(),"Generated forward idempotent SQL script executes twice without schema drift");
            await using var factory = new ReleaseFactory(connection, smtp.Port, mapUrl, root);
            using var scope = factory.Services.CreateScope();
            using (var healthClient = factory.CreateClient())
            using (var health = await healthClient.GetAsync("/health"))
                Check(health.StatusCode == HttpStatusCode.OK && (await health.Content.ReadAsStringAsync()).Trim() == "Healthy",
                    "Real SQL health endpoint reports healthy after isolated schema validation");
            await IdentitySeeder.SeedAsync(scope.ServiceProvider);
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var passwords = new Dictionary<string,string>();
            foreach (var role in new[] {"Client","Staff","Admin","Owner"})
            {
                var user = new ApplicationUser { UserName=role.ToLowerInvariant()+"@example.test", Email=role.ToLowerInvariant()+"@example.test", FullName="Isolated "+role, EmailConfirmed=true, PhoneNumber="09123456789" };
                passwords[role] = Password();
                Check((await users.CreateAsync(user,passwords[role])).Succeeded && (await users.AddToRoleAsync(user,role)).Succeeded, "Provision generated test " + role);
            }
            var package = new Service {Name="Release fixture",Price=10000,DurationInHours=1}; db.Services.Add(package); await db.SaveChangesAsync();
            using var client = await Login(factory,"Client",passwords["Client"]);
            using var staff = await Login(factory,"Staff",passwords["Staff"]);
            using var admin = await Login(factory,"Admin",passwords["Admin"]);
            using var owner = await Login(factory,"Owner",passwords["Owner"]);
            var otherPassword=Password();
            var otherUser=new ApplicationUser{UserName="otherclient@example.test",Email="otherclient@example.test",EmailConfirmed=true,FullName="Other isolated Client"};
            Check((await users.CreateAsync(otherUser,otherPassword)).Succeeded&&(await users.AddToRoleAsync(otherUser,"Client")).Succeeded,"Provision unrelated Client with generated credentials");
            using var otherClient=await Login(factory,"OtherClient",otherPassword);
            foreach(var pair in new[]{(client,"/Bookings/MyBookings"),(staff,"/Dashboard/Staff"),(admin,"/Dashboard/Admin"),(owner,"/Dashboard/Owner")})
                Check((await pair.Item1.GetAsync(pair.Item2)).StatusCode==HttpStatusCode.OK,"Real cookie opens "+pair.Item2);
            var clientToken=await GetToken(client,"/Account/Login");
            var staffToken=await GetToken(staff,"/Dashboard/Staff");
            var ownerToken=await GetToken(owner,"/Dashboard/Owner");
            using (var support = await Post(client,"/Communications/StartSupport",clientToken))
                Check(support.StatusCode==HttpStatusCode.Redirect,"Client starts support through authenticated anti-forgery POST");
            var supportId=await db.Conversations.Where(c=>c.ConversationType==ConversationTypes.ClientSupport).Select(c=>c.Id).SingleAsync();
            var chatToken=await GetToken(client,"/Communications/Index?id="+supportId);
            using (var send = new HttpRequestMessage(HttpMethod.Post,"/Communications/SendMessage")) {
                send.Headers.Add("X-Requested-With","XMLHttpRequest");
                send.Content=new FormUrlEncodedContent(new Dictionary<string,string> {["conversationId"]=supportId.ToString(),["messageContent"]="Client fixture question",["requestType"]="Payment Concern",["actionLink"]="/Reports",["__RequestVerificationToken"]=chatToken});
                using var response=await client.SendAsync(send);
                Check(response.StatusCode==HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("\"ok\":true"),"Actual AJAX message endpoint accepts client message");
            }
            Check(await db.Messages.AnyAsync(m=>m.ConversationId==supportId&&m.MessageContent=="Client fixture question"&&m.MessageType==MessageTypes.Text&&m.ActionLink==null),"SQL persists client message without forged operational privileges");
            using (var unread=await staff.GetAsync("/Communications/UnreadCount"))
                Check((await unread.Content.ReadAsStringAsync()).Contains("\"count\":1"),"Staff sees an unread conversation after client sends");
            using (var reply=await Post(staff,"/Communications/SendMessage",staffToken,("conversationId",supportId.ToString()),("messageContent","Staff fixture reply")))
                Check(reply.StatusCode==HttpStatusCode.Redirect,"Staff reply persists through the existing endpoint");
            using (var inbox=await client.GetAsync("/Communications/Index?view=inbox")) {
                var html=await inbox.Content.ReadAsStringAsync();
                Check(inbox.StatusCode==HttpStatusCode.OK&&!html.Contains("id=\"commComposer\"")&&html.Contains("Aries Magic Support"),"Client inbox shows conversations without automatically opening a thread");
            }
            using (var unread=await client.GetAsync("/Communications/UnreadCount"))
                Check((await unread.Content.ReadAsStringAsync()).Contains("\"count\":1"),"Opening the inbox preserves an unread staff reply");
            using (var received=await client.GetAsync("/Communications/MessagesSince?id="+supportId)) {
                var json=await received.Content.ReadAsStringAsync();
                Check(received.StatusCode==HttpStatusCode.OK&&json.Contains("Client fixture question")&&json.Contains("Staff fixture reply"),"Client polling receives persisted messages from both participants");
            }
            using (var deniedChat=await otherClient.GetAsync("/Communications/MessagesSince?id="+supportId))
                Check(deniedChat.StatusCode==HttpStatusCode.Forbidden||deniedChat.StatusCode==HttpStatusCode.Redirect,"Unrelated client cannot poll private conversation");
            var otherChatToken=await GetToken(otherClient,"/Account/Login");
            using (var deniedSend=await Post(otherClient,"/Communications/SendMessage",otherChatToken,("conversationId",supportId.ToString()),("messageContent","Not a participant")))
                Check(deniedSend.StatusCode==HttpStatusCode.Forbidden||deniedSend.StatusCode==HttpStatusCode.Redirect,"Unrelated client cannot send into private conversation");
            using (var noCsrf=await client.PostAsync("/Communications/SendMessage",new FormUrlEncodedContent(new Dictionary<string,string>{{"conversationId",supportId.ToString()},{"messageContent","Missing token"}})))
                Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"Message sending still requires anti-forgery token");
            foreach(var denied in new[]{client,staff,admin}) {
                using var response=await Post(denied,"/Payments/Verify/1",clientToken);
                Check(response.StatusCode==HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("AccessDenied"),"Real cookie cannot bypass Owner-only payment verification");
            }
            var date=DateTime.Today.AddDays(40).ToString("yyyy-MM-dd");
            (string,string)[] bookingFields=[("EventType","Company Events"),("Motif","Blue"),("PartyTheme","Corporate"),("EventDate",date),("StartTime","09:00:00"),
                ("PartyVenue","Fixture venue"),("PaxCount","25"),("ContactPerson","Fixture Client"),("ContactNumber","09123456789"),("ServiceId",package.Id.ToString()),
                ("AcceptTerms","true"),("FinalPrice","0.01"),("TravelFee","-99999"),("VenueLatitude","90")];
            using(var response=await Post(client,"/Bookings/SubmitBooking",clientToken,bookingFields)) Check(response.StatusCode==HttpStatusCode.Redirect,"Client booking submitted over authenticated HTTP");
            var booking=await db.Bookings.AsNoTracking().SingleAsync();
            Check(booking.FinalPrice==10000 && booking.TravelFee==0 && booking.VenueLatitude==14.58125,"Database stores trusted server price and actual geocoding response");
            using(var response=await Post(staff,$"/Bookings/Approve/{booking.Id}",staffToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Staff approval HTTP completes");
            Check(await db.Bookings.AsNoTracking().AnyAsync(b=>b.Id==booking.Id&&b.Status==BookingStatus.AwaitingDownpayment),"Staff approval persists awaiting-downpayment status");
            using(var contextPage=await client.GetAsync($"/Payments/Upload?bookingId={booking.Id}")) {
                var html=await contextPage.Content.ReadAsStringAsync();var payload=Regex.Match(html,"id=\"clientBookingContext\">(.*?)</script>",RegexOptions.Singleline).Groups[1].Value;
                using var doc=System.Text.Json.JsonDocument.Parse(payload);var row=doc.RootElement.EnumerateArray().Single();
                Check(row.GetProperty("Id").GetInt32()==booking.Id&&row.GetProperty("Balance").GetDecimal()==10000m&&row.GetProperty("Verified").GetDecimal()==0m,"Concierge presentation reads authoritative own-booking financial values");
            }
            using(var contextPage=await otherClient.GetAsync($"/Payments/Upload?bookingId={booking.Id}"))Check(!(await contextPage.Content.ReadAsStringAsync()).Contains("id=\"clientBookingContext\""),"Concierge payment metadata does not expose an unrelated booking");
            using(var contextPage=await client.GetAsync($"/RescheduleRequests/Create?bookingId={booking.Id}")) {
                var html=await contextPage.Content.ReadAsStringAsync();var payload=Regex.Match(html,"id=\"clientBookingContext\">(.*?)</script>",RegexOptions.Singleline).Groups[1].Value;
                using var doc=System.Text.Json.JsonDocument.Parse(payload);var row=doc.RootElement.EnumerateArray().Single();
                Check(row.GetProperty("DurationHours").GetInt32()==package.DurationInHours&&row.GetProperty("Start").GetString()=="09:00","Concierge calendar receives actual own-booking duration and current time");
            }
            using(var contextPage=await otherClient.GetAsync($"/RescheduleRequests/Create?bookingId={booking.Id}"))Check(!(await contextPage.Content.ReadAsStringAsync()).Contains("id=\"clientBookingContext\""),"Concierge schedule metadata does not expose an unrelated booking");
            var png=await File.ReadAllBytesAsync("tests/ReleaseIntegration/Fixtures/payment.png");
            async Task<HttpResponseMessage> Upload(decimal amount) {
                var form=new MultipartFormDataContent(); form.Add(new StringContent(clientToken),"__RequestVerificationToken");
                form.Add(new StringContent("0"),"ExpectedLatestPaymentId");
                form.Add(new StringContent(booking.Id.ToString()),"BookingId");form.Add(new StringContent(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)),"Amount");form.Add(new StringContent("GCash"),"PaymentMethod");
                var image=new ByteArrayContent(png);image.Headers.ContentType=new("image/png");form.Add(image,"ProofImage","proof.png");
                return await client.PostAsync("/Payments/Upload",form);
            }
            using(var response=await Upload(1000)) Check(response.StatusCode==HttpStatusCode.OK && !await db.Payments.AnyAsync(),"Server rejects downpayment below PHP 2,000");
            using(var response=await Upload(2000)) Check(response.StatusCode==HttpStatusCode.Redirect,"Real multipart proof upload succeeds");
            var payment=await db.Payments.AsNoTracking().SingleAsync();
            using(var form=new MultipartFormDataContent()) {
                form.Add(new StringContent(clientToken),"__RequestVerificationToken"); form.Add(new StringContent(booking.Id.ToString()),"BookingId");
                form.Add(new StringContent("Isolated Client"),"GCashAccountName");form.Add(new StringContent("09123456789"),"GCashNumber");
                var image=new ByteArrayContent(png);image.Headers.ContentType=new("image/png");form.Add(image,"PaymentProofImage","refund.png");
                using var response=await client.PostAsync("/Payments/RequestRefund",form);
                Check(response.StatusCode==HttpStatusCode.Redirect,"Client submits eligible refund request using real HTTP proof");
            }
            var refund=await db.RefundRequests.AsNoTracking().SingleAsync();
            using(var response=await Post(owner,$"/Payments/ApproveRefund/{refund.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Unverified original receipt cannot yet authorize refund");
            Check(await db.RefundRequests.AsNoTracking().AnyAsync(x=>x.Id==refund.Id&&x.Status==RefundStatus.UnderReview),"Refund retains review state before original payment verification");
            Check(File.Exists(Path.Combine(root,"financial-proofs",payment.ProofImagePath.Replace('/',Path.DirectorySeparatorChar))),"Uploaded proof resides in configured private storage");
            Check(await db.OcrVerifications.AnyAsync(x=>x.PaymentId==payment.Id && x.ProcessingError==null && x.ExtractedAmount==2000),"Actual native OCR extracts PHP 2,000 from the generated receipt");
            using(var response=await otherClient.GetAsync($"/Bookings/Details/{booking.Id}")) Check(response.StatusCode==HttpStatusCode.NotFound,"Unrelated real Client cookie cannot read another booking by direct ID");
            using(var response=await otherClient.GetAsync($"/Payments/PaymentProof/{payment.Id}")) Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("AccessDenied"),"Unrelated real Client cookie cannot read another client's financial proof");
            using(var proof=await client.GetAsync($"/Payments/PaymentProof/{payment.Id}")) Check(proof.StatusCode==HttpStatusCode.OK && proof.Headers.CacheControl?.NoStore==true,"Own proof served through protected noncached HTTP route");
            using(var proof=await owner.GetAsync($"/Payments/PaymentProof/{payment.Id}")) Check(proof.StatusCode==HttpStatusCode.OK && proof.Headers.CacheControl?.NoStore==true && (await proof.Content.ReadAsByteArrayAsync()).SequenceEqual(png),"Real Owner cookie reads exact original private proof bytes through protected HTTP route");
            using(var proof=await staff.GetAsync($"/Payments/PaymentProof/{payment.Id}")) Check(proof.StatusCode==HttpStatusCode.Redirect,"Staff cookie denied private financial proof");
            using(var response=await Post(owner,$"/Payments/Verify/{payment.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Owner verification HTTP commits");
            Check(await db.Bookings.AsNoTracking().AnyAsync(b=>b.Id==booking.Id&&b.Status==BookingStatus.Confirmed),"Verified downpayment confirms booking in SQL");
            var finance=await new PaymentFinancialService(db).GetSummaryAsync(booking.Id);
            Check(finance.TotalVerifiedPayments==2000 && finance.RemainingBalance==8000,"Verified downpayment reduces outstanding balance exactly once");
            using(var response=await Post(owner,$"/Payments/Verify/{payment.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Duplicate verification safely redirects");
            Check((await new PaymentFinancialService(db).GetSummaryAsync(booking.Id)).TotalVerifiedPayments==2000,"Duplicate request does not reapply money");
            Check(await db.EmailOutboxMessages.CountAsync(x=>x.EventKey=="payment-verified:"+payment.Id)==1,"Committed verification atomically queues one confirmation email");
            using(var response=await Post(owner,$"/Payments/ApproveRefund/{refund.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Owner approves verified-receipt refund over HTTP");
            using(var form=new MultipartFormDataContent()) {
                form.Add(new StringContent(ownerToken),"__RequestVerificationToken");
                var image=new ByteArrayContent(png);image.Headers.ContentType=new("image/png");form.Add(image,"refundProofImage","confirmation.png");
                using var response=await owner.PostAsync($"/Payments/UploadRefundProof/{refund.Id}",form);
                Check(response.StatusCode==HttpStatusCode.Redirect,"Owner uploads private refund completion proof");
            }
            using(var response=await Post(owner,$"/Payments/MarkAsRefunded/{refund.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Owner completes reviewed refund over HTTP");
            var completed=await db.RefundRequests.AsNoTracking().SingleAsync(x=>x.Id==refund.Id);
            Check(completed.Status==RefundStatus.Refunded&&completed.ApprovedAmount==2000&&completed.RefundCompletedAt!=null,"Refund amount, final status and audit timestamps persist");
            using(var response=await Post(owner,$"/Payments/MarkAsRefunded/{refund.Id}",ownerToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Duplicate refund completion safely redirects");
            Check((await db.RefundRequests.AsNoTracking().SingleAsync(x=>x.Id==refund.Id)).RefundCompletedAt==completed.RefundCompletedAt,"Duplicate refund does not change original completion");
            using(var response=await Post(client,"/RescheduleRequests/Create",clientToken,("BookingId",booking.Id.ToString()),("RequestedDate",DateTime.Today.AddDays(45).ToString("yyyy-MM-dd")),("RequestedStartTime","11:00:00"),("Reason","Isolated schedule test")))
                Check(response.StatusCode==HttpStatusCode.Redirect,"Client reschedule request persists through HTTP");
            var reschedule=await db.RescheduleRequests.AsNoTracking().SingleAsync();
            using(var response=await Post(staff,$"/RescheduleRequests/Approve/{reschedule.Id}",staffToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Staff processes real reschedule request");
            Check((await db.Bookings.AsNoTracking().SingleAsync()).StartTime==reschedule.RequestedStartTime,"Rescheduling updates the single calendar/upcoming schedule record");
            // Decisions queue mail atomically; the dispatcher alone contacts SMTP.
            var mailBeforeOutbox=smtp.Messages.Count;
            await OutboxTests(factory,smtp,connection,mailBeforeOutbox);
            await ConcurrentReschedules(db,staff,staffToken,booking,package.Id);
            await ConcurrentRefunds(db,owner,ownerToken,booking.Id);
            await IntegrityChecks(db,booking.Id,payment.Id);
            await FinancialRollback(db,connection,smtp.Port,mapUrl,root,passwords["Owner"],booking,package.Id);
            var bookingsBeforeFailure=await db.Bookings.CountAsync();
            mapFails=true;
            using(var response=await Post(client,"/Bookings/SubmitBooking",clientToken,bookingFields)) Check(response.StatusCode==HttpStatusCode.OK && await db.Bookings.CountAsync()==bookingsBeforeFailure,"Real mapping 503 prevents booking with understandable validation and no partial data");
            mapFails=false;
            Check(await db.Notifications.AnyAsync(x=>x.UserId==booking.ApplicationUserId),"Client receives persisted workflow notifications");
            await FeedbackFeatureTests.RunAsync(connection,smtp.Port,mapUrl,root,passwords,otherPassword,Check);
            await RecoveryRegressionTests.ChatAsync(connection,Check);
            using(var response=await Post(staff,"/Account/Logout",staffToken)) Check(response.StatusCode==HttpStatusCode.Redirect,"Real logout expires authentication cookie");
            using(var response=await staff.GetAsync("/Dashboard/Staff")) Check(response.StatusCode==HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("Login"),"Logged-out cookie cannot reopen protected page");

            // A fresh application instance isolates rate-limit windows; it still uses real Identity.
            await AuthenticationRecovery(connection,smtp,mapUrl,root,passwords);
            await using(var expiredFactory=new ReleaseFactory(connection,smtp.Port,mapUrl,root,shortTicket:true)) {
                using var expired=await Login(expiredFactory,"Client",passwords["Client"]);await Task.Delay(100);
                using var response=await expired.GetAsync("/Bookings/MyBookings");
                Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("Login"),"Expired real Identity ticket is rejected by the application");
            }
            await RestoreTests(db,connection,database,root,smtp.Port,mapUrl,passwords["Client"]);
            await IndependentFindingsReview.RunAsync();
            await File.WriteAllTextAsync(".tmp-owner-build/release-evidence.json",System.Text.Json.JsonSerializer.Serialize(new {
                CheckedAtUtc=DateTime.UtcNow,Runtime=Environment.Version.ToString(),Checks=checks,SourceData="Synthetic isolated LocalDB",
                BackupVerified=true,RestoreSeconds=restoreSeconds,NativeOcrVerified=true,EmailTransport="Loopback SMTP, not Google",
                Authentication="Actual Identity cookies and MVC pipeline via TestServer",AzureTargetSelected=false,ProviderCredentialRevoked=false,
                ProductionVerified=false,StagingVerified=false,Verdict="NOT READY FOR PRODUCTION"
            },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"SUCCESS: {checks} release integration checks passed on {Environment.Version}.");
        }
        finally
        {
            await map.StopAsync();
            if(!Regex.IsMatch(database,"^AriesReleaseIntegration_[0-9a-f]{32}$")) throw new Exception("Unsafe cleanup target.");
            await db.Database.EnsureDeletedAsync();
        }
    }

    static async Task OutboxTests(ReleaseFactory factory,SmtpFixture smtp,string connection,int priorMessages)
    {
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue=scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        await using(var transaction=await db.Database.BeginTransactionAsync()) { await queue.EnqueueAsync("rollback-fixture","sink@example.test","Rollback","Never sent"); await transaction.RollbackAsync(); }
        db.ChangeTracker.Clear();
        Check(!await db.EmailOutboxMessages.AnyAsync(x=>x.EventKey=="rollback-fixture"),"SQL rollback removes queued email together with transaction");
        smtp.RejectAuthentication=true;
        var sanitized=false;
        try {await scope.ServiceProvider.GetRequiredService<IEmailService>().SendEmailAsync("sink@example.test","failure fixture","Isolated body");}
        catch(EmailDeliveryException error){sanitized=error.InnerException==null&&!error.Message.Contains("fixture");}
        Check(sanitized,"SMTP provider error is replaced by a secret-free exception without a raw inner response");
        var dispatcher=scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>();
        var queued=await db.EmailOutboxMessages.CountAsync(x=>x.DeliveredAtUtc==null&&x.FailedAtUtc==null);
        Check(queued==2,"Payment and reschedule confirmations are both queued, without false sent claims");
        Check(await dispatcher.DispatchAsync()==0,"Real SMTP authentication failure does not undo financial commit");
        Check(await db.EmailOutboxMessages.AnyAsync(x=>x.Attempts==1&&x.DeliveredAtUtc==null&&x.LastFailureType==nameof(EmailDeliveryException)),"SMTP failure is retained with sanitized bounded retry state");
        smtp.RejectAuthentication=false;
        await db.EmailOutboxMessages.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NextAttemptAtUtc,DateTime.UtcNow.AddMinutes(-1)));
        Check(await dispatcher.DispatchAsync()==queued && smtp.Messages.Count==priorMessages+queued,"Committed outbox retries through real loopback SMTP");
        Check(await dispatcher.DispatchAsync()==0 && smtp.Messages.Count==priorMessages+queued,"Delivered item is not sent again on ordinary retries");
    }

    static async Task AuthenticationRecovery(string connection,SmtpFixture smtp,Uri mapUrl,string root,Dictionary<string,string> passwords)
    {
        await using var factory=new ReleaseFactory(connection,smtp.Port,mapUrl,root);
        using(var galleryClient=factory.CreateClient(new(){AllowAutoRedirect=false})) {
            using var response=await galleryClient.GetAsync("/Home/Gallery");
            var gallery=await response.Content.ReadAsStringAsync();
            Check(response.StatusCode==HttpStatusCode.OK&&gallery.Contains("Moments that make"),"Anonymous HTTP Gallery serves the actual compiled editorial view");
            Check(Regex.Matches(gallery,"data-portfolio-src=").Count==7&&Regex.Matches(gallery,"data-portfolio-src=\"/images/client/").Count==7,"Gallery includes only seven curated public project photographs");
            Check(gallery.Contains("/Bookings/CreateStepOne")&&gallery.Contains("aria-current=\"page\" href=\"/Home/Gallery\""),"Gallery preserves booking route and current footer navigation");
            Check(gallery.Contains("Monarch / Rio Jones Photography")&&!gallery.Contains("/App_Data/")&&!gallery.Contains("/uploads/")&&!gallery.Contains("/FeedbackMedia/"),"Gallery preserves credit and does not expose private or uploaded media paths");
        }
        using var client=await Login(factory,"Client",passwords["Client"]);
        using var scope=factory.Services.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user=(await users.FindByEmailAsync("client@example.test"))!;
        var recovery=scope.ServiceProvider.GetRequiredService<AccountCredentialRecovery>();
        Check(!(await recovery.InvalidateAsync(user,false)).Succeeded,"Credential invalidation refuses an unverified recovery channel");
        Check((await recovery.InvalidateAsync(user,true)).Succeeded && !await users.CheckPasswordAsync(user,passwords["Client"]),"Authorized credential invalidation rejects old password and preserves recovery");
        using(var anonymous=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true})) {
            var token=await GetToken(anonymous,"/Account/ForgotPassword");
            using(var response=await Post(anonymous,"/Account/ForgotPassword",token,("Email","client@example.test"))) Check(response.StatusCode==HttpStatusCode.Redirect,"Actual password recovery sends through loopback SMTP");
            var message=smtp.Messages.Last();
            var link=WebUtility.HtmlDecode(Regex.Match(message.HtmlBody ?? "","href='([^']+)'",RegexOptions.IgnoreCase).Groups[1].Value);
            var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query);
            var resetToken=await GetToken(anonymous,new Uri(link).PathAndQuery);
            var replacement=Password();
            using(var response=await Post(anonymous,"/Account/ResetPassword",resetToken,("Email","client@example.test"),("Token",query["token"].ToString()),("NewPassword",replacement),("ConfirmPassword",replacement))) Check(response.StatusCode==HttpStatusCode.Redirect,"Real recovery token resets password through HTTP");
            passwords["Client"]=replacement;
        }
        using var fresh=await Login(factory,"Client",passwords["Client"]);
        // Force normal Identity security-stamp revalidation without a one-minute wait.
        factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>>().Value.ValidationInterval=TimeSpan.Zero;
        using(var response=await client.GetAsync("/Bookings/MyBookings")) Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("Login"),"Previously issued real cookie is rejected after credential invalidation");
        var staff=(await users.FindByEmailAsync("staff@example.test"))!;
        for(var i=0;i<5;i++) {
            using var bad=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});
            var token=await GetToken(bad,"/Account/Login");
            using var response=await Post(bad,"/Account/Login",token,("Email","staff@example.test"),("Password",Password()));
            Check(response.StatusCode==HttpStatusCode.OK,"Incorrect real password attempt rejected");
        }
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Entry(staff).ReloadAsync();
        var actual=await users.FindByIdAsync(staff.Id);
        Check(actual!=null && await users.IsLockedOutAsync(actual),"Five failed HTTP logins trigger Identity lockout");
    }

    static async Task ConcurrentReschedules(ApplicationDbContext db,HttpClient staff,string token,Booking original,int serviceId)
    {
        var target=DateTime.Today.AddDays(100);
        Booking Make(DateTime day,int hour) => new(){ClientId=original.ClientId,ApplicationUserId=original.ApplicationUserId,ServiceId=serviceId,
            EventDate=day,StartTime=day.AddHours(hour),EndTime=day.AddHours(hour+1),Status=BookingStatus.Confirmed,FinalPrice=10000};
        var first=Make(target,8);var second=Make(target,10);var third=Make(target.AddDays(1),8);var fourth=Make(target.AddDays(2),8);
        db.Bookings.AddRange(first,second,third,fourth);await db.SaveChangesAsync();
        RescheduleRequest Move(Booking b,int hour)=>new(){BookingId=b.Id,OriginalDate=b.EventDate,OriginalStartTime=b.StartTime,OriginalEndTime=b.EndTime,
            RequestedDate=target,RequestedStartTime=target.AddHours(hour),RequestedEndTime=target.AddHours(hour+1),Reason="Concurrent release fixture",Status=RescheduleRequestStatus.Pending};
        var a=Move(third,12);var b=Move(fourth,14);db.RescheduleRequests.AddRange(a,b);await db.SaveChangesAsync();
        var responses=await Task.WhenAll(Post(staff,$"/RescheduleRequests/Approve/{a.Id}",token),Post(staff,$"/RescheduleRequests/Approve/{b.Id}",token));
        foreach(var response in responses){Check(response.StatusCode==HttpStatusCode.Redirect||response.StatusCode==HttpStatusCode.Conflict,"Concurrent reschedule responds with a safe redirect or conflict");response.Dispose();}
        Check(await db.Bookings.AsNoTracking().CountAsync(x=>x.EventDate==target&&x.Status==BookingStatus.Confirmed)==3,"Concurrent real-cookie rescheduling preserves three confirmed bookings per day");
        Check(await db.RescheduleRequests.AsNoTracking().CountAsync(x=>(x.Id==a.Id||x.Id==b.Id)&&x.Status==RescheduleRequestStatus.Approved)==1,"Only one concurrent reschedule consumes the last daily slot");
    }

    static async Task IntegrityChecks(ApplicationDbContext db,int bookingId,int paymentId)
    {
        var invalidAmountRejected=false;
        try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Payments SET Amount=-1 WHERE Id={paymentId}"); }
        catch(SqlException error) when(error.Number==547){invalidAmountRejected=true;}
        Check(invalidAmountRejected,"SQL check constraint rejects negative financial amounts independently of controllers");
        var historyProtected=false;
        try {await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Bookings WHERE Id={bookingId}");}
        catch(SqlException error) when(error.Number==547){historyProtected=true;}
        Check(historyProtected&&await db.Payments.AnyAsync(x=>x.Id==paymentId),"SQL foreign keys prevent cascading removal of financial history");
        var uniqueEvent=false;
        var eventKey="payment-verified:"+paymentId;
        try {await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EmailOutboxMessages (EventKey,Recipient,Subject,HtmlBody,CreatedAtUtc,NextAttemptAtUtc,Attempts) VALUES ({eventKey},'sink@example.test','duplicate','fixture',SYSUTCDATETIME(),SYSUTCDATETIME(),0)");}
        catch(SqlException error) when(error.Number is 2601 or 2627){uniqueEvent=true;}
        Check(uniqueEvent,"SQL unique index rejects duplicate outbox event identity");
    }

    static async Task ConcurrentRefunds(ApplicationDbContext db,HttpClient owner,string token,int bookingId)
    {
        var payment=new Payment{BookingId=bookingId,Amount=2000,Status=PaymentStatus.Verified};db.Payments.Add(payment);await db.SaveChangesAsync();
        RefundRequest Make()=>new(){BookingId=bookingId,OriginalPaymentId=payment.Id,Amount=1500,Status=RefundStatus.UnderReview};
        var a=Make();var b=Make();db.RefundRequests.AddRange(a,b);await db.SaveChangesAsync();
        var responses=await Task.WhenAll(Post(owner,$"/Payments/ApproveRefund/{a.Id}",token),Post(owner,$"/Payments/ApproveRefund/{b.Id}",token));
        foreach(var response in responses){Check(response.StatusCode==HttpStatusCode.Redirect||response.StatusCode==HttpStatusCode.Conflict,"Concurrent refund review returns a safe redirect or conflict");response.Dispose();}
        var approved=await db.RefundRequests.AsNoTracking().Where(x=>x.OriginalPaymentId==payment.Id&&x.Status==RefundStatus.Approved).ToListAsync();
        Check(approved.Count==1&&approved.Sum(x=>x.ApprovedAmount??x.Amount)==1500,"Concurrent refund approvals cannot reserve more than the verified original receipt");
    }

    static async Task FinancialRollback(ApplicationDbContext db,string connection,int smtpPort,Uri mapUrl,string root,string password,Booking original,int serviceId)
    {
        var date=DateTime.Today.AddDays(160);
        var booking=new Booking{ClientId=original.ClientId,ApplicationUserId=original.ApplicationUserId,ServiceId=serviceId,EventDate=date,StartTime=date.AddHours(9),EndTime=date.AddHours(10),FinalPrice=10000,Status=BookingStatus.AwaitingVerification};
        var payment=new Payment{Booking=booking,Amount=2000,Status=PaymentStatus.Pending};db.Payments.Add(payment);await db.SaveChangesAsync();
        await using var factory=new ReleaseFactory(connection,smtpPort,mapUrl,root,failVerificationAudit:true);
        using var owner=await Login(factory,"Owner",password);var token=await GetToken(owner,"/Dashboard/Owner");
        owner.DefaultRequestHeaders.Add("X-Owner-Action","true");
        using(var response=await Post(owner,$"/Payments/Verify/{payment.Id}",token)) Check(response.StatusCode==HttpStatusCode.InternalServerError && !(await response.Content.ReadAsStringAsync()).Contains("Synthetic audit outage"),"Audit outage returns a generic financial error without internal details");
        Check(await db.Payments.AsNoTracking().AnyAsync(x=>x.Id==payment.Id&&x.Status==PaymentStatus.Pending)
            && await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==booking.Id&&x.Status==BookingStatus.AwaitingVerification),"Audit failure rolls back payment and booking status atomically");
        Check(!await db.EmailOutboxMessages.AnyAsync(x=>x.EventKey=="payment-verified:"+payment.Id)
            && !await db.BookingTimelines.AnyAsync(x=>x.BookingId==booking.Id&&x.EventType==TimelineEventType.PaymentVerified),"Failed financial transaction leaves no confirmation email or success timeline");
    }

    static async Task RestoreTests(ApplicationDbContext db,string connection,string database,string root,int smtpPort,Uri mapUrl,string clientPassword)
    {
        var restored=database+"_restored";
        var master=new SqlConnectionStringBuilder(connection){InitialCatalog="master"}.ConnectionString;
        await using var sql=new SqlConnection(master);await sql.OpenAsync();
        var backup=Path.Combine(root,"synthetic.bak");
        async Task Execute(string text,params (string,object)[] values) {using var cmd=new SqlCommand(text,sql){CommandTimeout=120};foreach(var pair in values)cmd.Parameters.AddWithValue(pair.Item1,pair.Item2);await cmd.ExecuteNonQueryAsync();}
        await Execute($"BACKUP DATABASE [{database}] TO DISK=@path WITH COPY_ONLY, CHECKSUM",("@path",backup));
        await Execute("RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM",("@path",backup));
        var watch=Stopwatch.StartNew();
        try {
            string? data=null,log=null;
            using(var cmd=new SqlCommand("RESTORE FILELISTONLY FROM DISK=@path",sql)) {cmd.Parameters.AddWithValue("@path",backup);using var reader=await cmd.ExecuteReaderAsync();while(await reader.ReadAsync()){if(reader["Type"].ToString()=="D")data=reader["LogicalName"].ToString();else if(reader["Type"].ToString()=="L")log=reader["LogicalName"].ToString();}}
            if(data==null||log==null)throw new Exception("Backup logical files missing.");
            await Execute($"IF DB_ID('{restored}') IS NOT NULL THROW 50000, 'Restore target already exists', 1; RESTORE DATABASE [{restored}] FROM DISK=@path WITH MOVE @data TO @mdf, MOVE @log TO @ldf, RECOVERY, CHECKSUM",
                ("@path",backup),("@data",data),("@log",log),("@mdf",Path.Combine(root,"restored.mdf")),("@ldf",Path.Combine(root,"restored_log.ldf")));
            await Execute($"DBCC CHECKDB ([{restored}]) WITH NO_INFOMSGS");
            var restoredConnection=new SqlConnectionStringBuilder(connection){InitialCatalog=restored}.ConnectionString;
            await using(var integrityConnection=new SqlConnection(restoredConnection)) {
                await integrityConnection.OpenAsync();
                using var command=new SqlCommand("DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS",integrityConnection);
                using var reader=await command.ExecuteReaderAsync();
                Check(!reader.HasRows,"Restored SQL database passes relational/check-constraint validation");
            }
            await using var restoredDb=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(restoredConnection).Options);
            Check(await restoredDb.Bookings.CountAsync()==await db.Bookings.CountAsync()&&await restoredDb.Payments.SumAsync(x=>x.Amount)==await db.Payments.SumAsync(x=>x.Amount),"Native SQL backup restores booking count and financial totals into a different database");
            Check(await restoredDb.RefundRequests.CountAsync()==await db.RefundRequests.CountAsync()
                && await restoredDb.BookingTimelines.CountAsync()==await db.BookingTimelines.CountAsync()
                && (await restoredDb.Database.GetAppliedMigrationsAsync()).SequenceEqual(await db.Database.GetAppliedMigrationsAsync()),"Restored refund, timeline and migration history match the source snapshot");
            await using(var factory=new ReleaseFactory(restoredConnection,smtpPort,mapUrl,root)) {using var client=await Login(factory,"Client",clientPassword);Check((await client.GetAsync("/Bookings/MyBookings")).StatusCode==HttpStatusCode.OK,"Actual application authenticates and reads restored SQL database");}
            watch.Stop();restoreSeconds=watch.Elapsed.TotalSeconds;Console.WriteLine($"RESTORE EVIDENCE: synthetic source only; CHECKSUM/VERIFYONLY/CHECKDB passed; recovery {restoreSeconds:F2} seconds.");
            await restoredDb.Database.EnsureDeletedAsync();
        }
        finally {
            if(!Regex.IsMatch(restored,"^AriesReleaseIntegration_[0-9a-f]{32}_restored$"))throw new Exception("Unsafe restore cleanup target.");
            await Execute($"IF DB_ID('{restored}') IS NOT NULL BEGIN ALTER DATABASE [{restored}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{restored}]; END");
        }
    }
}
