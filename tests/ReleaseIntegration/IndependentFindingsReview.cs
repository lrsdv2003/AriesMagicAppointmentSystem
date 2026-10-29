using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Actual MVC/Identity/SQL requests. No production URL, configurable SQL target or fixture auth headers.
static class IndependentFindingsReview
{
    public static async Task RunAsync(bool reproduce=false)
    {
        var results=new List<object>();var failures=0;var checks=0;
        void Check(bool value,string name){checks++;results.Add(new {name,passed=value});if(!value)failures++;Console.WriteLine((value?"PASS: ":"MISSING SAFEGUARD: ")+name);if(!value&&!reproduce)throw new Exception("Independent findings check failed: "+name);}
        PolicyChecks(Check);
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))throw new Exception("Invalid isolated instance name.");
        var name="AriesFindings_"+Guid.NewGuid().ToString("N");
        var connection=@"Server=(localdb)\"+instance+";Database="+name+";Integrated Security=True;TrustServerCertificate=True";
        var root=Path.GetFullPath(Path.Combine(".tmp-owner-build",name));Directory.CreateDirectory(root);
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();smtp.Start();
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();await using var map=builder.Build();map.Urls.Add("http://127.0.0.1:0");
        map.MapGet("/search",(string? q)=>q=="unresolved"?Array.Empty<object>():new object[]{new {lat="14.58125",lon="121.1258",display_name="Synthetic findings venue, Philippines"}});
        await map.StartAsync();var mapUrl=new Uri(map.Urls.Single());
        await using var factory=new ReleaseFactory(connection,smtp.Port,mapUrl,root);
        var passwords=new Dictionary<string,string>();var identities=new Dictionary<string,string>();
        async Task<HttpClient> Login(ReleaseFactory f,string role){var c=f.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,HandleCookies=true});var t=await Token(c,"/Account/Login");using var r=await Post(c,"/Account/Login",t,("Email",role+"@example.test"),("Password",passwords[role]));if(r.StatusCode!=HttpStatusCode.Redirect)throw new Exception("Isolated sign-in failed.");return c;}
        try{
            await db.Database.MigrateAsync();
            using(var scope=factory.Services.CreateScope()){
                var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach(var role in new[]{"Client","Staff","Owner","Admin"})if(!await roles.RoleExistsAsync(role))await roles.CreateAsync(new(role));
                foreach(var item in new[]{("client","Client"),("other","Client"),("fresh","Client"),("staff","Staff"),("owner","Owner"),("admin","Admin")}){
                    var p=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));var u=new ApplicationUser{UserName=item.Item1+"@example.test",Email=item.Item1+"@example.test",EmailConfirmed=true,FullName="Synthetic "+item.Item1,PhoneNumber="09123456789"};
                    if(!(await users.CreateAsync(u,p)).Succeeded||!(await users.AddToRoleAsync(u,item.Item2)).Succeeded)throw new Exception("Isolated identity seed failed.");passwords[item.Item1]=p;identities[item.Item1]=u.Id;
                }
            }
            var package=new Service{Name="Synthetic findings package",Price=19800,DurationInHours=3};
            var legacy=new User{FullName="Synthetic client",Email="client@example.test",Role="Client",PasswordHash="IDENTITY_MANAGED"};db.AddRange(package,legacy);await db.SaveChangesAsync();
            Booking Booking(int offset,string status,string user="client",int hour=10)=>new(){ClientId=legacy.Id,ApplicationUserId=identities[user],ServiceId=package.Id,PackageName=package.Name,BasePrice=package.Price,FinalPrice=package.Price,RequiredDownpayment=2000,EventDate=DateTime.Today.AddDays(offset),StartTime=DateTime.Today.AddDays(offset).AddHours(hour),EndTime=DateTime.Today.AddDays(offset).AddHours(hour+3),Status=status,PartyVenue="Synthetic findings venue, Philippines",VenueLatitude=14.58125,VenueLongitude=121.1258,ContactPerson="Synthetic client",ContactNumber="09123456789",EventType="Company Events",Motif="Navy",PaxCount=20,IsServiceable=true};
            async Task<Booking> AddBooking(int offset,string status,string user="client",int hour=10){var b=Booking(offset,status,user,hour);db.Bookings.Add(b);await db.SaveChangesAsync();return b;}
            using var client=await Login(factory,"client");using var other=await Login(factory,"other");using var staff=await Login(factory,"staff");using var owner=await Login(factory,"owner");
            var token=await Token(client,"/Communications");var otherToken=await Token(other,"/Communications");var staffToken=await Token(staff,"/Communications");var ownerToken=await Token(owner,"/Communications");
            var png=await File.ReadAllBytesAsync("tests/ReleaseIntegration/Fixtures/payment.png");
            async Task<HttpResponseMessage> Upload(HttpClient c,Booking b,string csrf,int version=0,decimal amount=2000,byte[]? bytes=null,string filename="proof.png"){
                using var f=new MultipartFormDataContent();f.Add(new StringContent(csrf),"__RequestVerificationToken");f.Add(new StringContent(b.Id.ToString()),"BookingId");f.Add(new StringContent(version.ToString()),"ExpectedLatestPaymentId");f.Add(new StringContent(amount.ToString(CultureInfo.InvariantCulture)),"Amount");f.Add(new StringContent("GCash"),"PaymentMethod");var file=new ByteArrayContent(bytes??png);file.Headers.ContentType=new("image/png");f.Add(file,"ProofImage",filename);return await c.PostAsync("/Payments/Upload",f);
            }
            var installment=await AddBooking(80,BookingStatus.AwaitingDownpayment);
            using(var r=await Upload(client,installment,token))Check(r.StatusCode==HttpStatusCode.Redirect,"PAY-03 submitted proof remains pending");
            var first=await db.Payments.AsNoTracking().SingleAsync(p=>p.BookingId==installment.Id);
            Check(first.Status==PaymentStatus.Pending&&(await new PaymentFinancialService(db).GetSummaryAsync(installment.Id)).TotalVerifiedPayments==0,"PAY-06 pending proof does not reduce verified balance");
            using(var r=await Upload(client,installment,token))Check(await db.Payments.CountAsync(p=>p.BookingId==installment.Id)==1,"PAY-04 repeated pending POST cannot create a second record");
            using(var r=await Post(owner,"/Payments/Verify/"+first.Id,ownerToken))Check(r.StatusCode==HttpStatusCode.Redirect,"PAY-03 actual Owner can review synthetic proof");
            using(var r=await Upload(client,installment,token))Check(await db.Payments.CountAsync(p=>p.BookingId==installment.Id)==1,"PAY-04 stale POST after approval cannot become a new installment");
            var newest=await db.Payments.Where(p=>p.BookingId==installment.Id).MaxAsync(p=>p.Id);
            if(reproduce)await db.Payments.Where(p=>p.BookingId==installment.Id&&p.Status==PaymentStatus.Pending).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Status,PaymentStatus.Rejected));
            using(var r=await Upload(client,installment,token,newest))Check(r.StatusCode==HttpStatusCode.Redirect,"PAY-04 fresh installment intent remains allowed");
            var replacement=await db.Payments.AsNoTracking().Where(p=>p.BookingId==installment.Id&&p.Status==PaymentStatus.Pending).SingleAsync();
            using(var r=await Post(owner,"/Payments/RequestAdditionalEvidence/"+replacement.Id,ownerToken,("reason","Synthetic clearer-proof request")))Check(r.StatusCode==HttpStatusCode.Redirect,"PAY-03 Owner requests replacement evidence");
            using(var form=new MultipartFormDataContent()){
                form.Add(new StringContent(token),"__RequestVerificationToken");var image=new ByteArrayContent(png);image.Headers.ContentType=new("image/png");form.Add(image,"proofImage","replacement.png");using var r=await client.PostAsync("/Payments/ReplaceProof/"+replacement.Id,form);
                Check(r.StatusCode==HttpStatusCode.Redirect&&await db.Payments.CountAsync(p=>p.BookingId==installment.Id)==(reproduce?3:2),"PAY-04 authorized replacement preserves the existing payment record");
            }
            Check((await new PaymentFinancialService(db).GetSummaryAsync(installment.Id)).TotalVerifiedPayments==2000,"PAY-06 pending installment and replacement never double-count verified funds");
            var paymentCount=await db.Payments.CountAsync();using(var r=await Upload(other,installment,otherToken))Check(await db.Payments.CountAsync()==paymentCount,"PAY-04 unrelated Client cannot submit another client's proof");

            var badUpload=await AddBooking(81,BookingStatus.AwaitingDownpayment);
            using(var r=await Upload(client,badUpload,token,bytes:new byte[5*1024*1024+1]))Check(!await db.Payments.AnyAsync(p=>p.BookingId==badUpload.Id),"PAY-02 server rejects oversized images");
            using(var r=await Upload(client,badUpload,token,filename:"proof.pdf"))Check(!await db.Payments.AnyAsync(p=>p.BookingId==badUpload.Id),"PAY-02 server rejects unsupported extensions");
            using(var r=await Upload(client,badUpload,token,bytes:png[..24]))Check(!await db.Payments.AnyAsync(p=>p.BookingId==badUpload.Id),"PAY-01 signature-only truncated image is rejected");
            using(var image=new SkiaSharp.SKBitmap(32,32)){
                image.Erase(SkiaSharp.SKColors.Navy);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
                using var r=await Upload(client,badUpload,token,bytes:data.ToArray());
                Check(await db.Payments.AnyAsync(p=>p.BookingId==badUpload.Id&&p.Status==PaymentStatus.Pending),"PAY-01 safe nonreceipt is accepted for manual Owner review, never verified automatically");
            }
            var raced=await AddBooking(82,BookingStatus.AwaitingDownpayment);using var secondSession=await Login(factory,"client");var secondToken=await Token(secondSession,"/Communications");
            // A fresh host retains the real upload limiter with an unused window: both requests
            // must reach SQL, rather than accidentally proving only rate-limit rejection.
            await using var raceFactory=new ReleaseFactory(connection,smtp.Port,mapUrl,root);
            using var raceClientA=await Login(raceFactory,"client");using var raceClientB=await Login(raceFactory,"client");
            var raceTokenA=await Token(raceClientA,"/Communications");var raceTokenB=await Token(raceClientB,"/Communications");
            var uploads=await Task.WhenAll(Upload(raceClientA,raced,raceTokenA),Upload(raceClientB,raced,raceTokenB));foreach(var r in uploads){Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict or HttpStatusCode.OK,"PAY-04 concurrent uploads reach the real SQL-coordinated path");r.Dispose();}
            Check(await db.Payments.CountAsync(p=>p.BookingId==raced.Id)==1,"PAY-04 SQL persists one payment for concurrent submissions");

            var notice=new Notification{UserId=identities["client"],Title="Synthetic notice",Message="Synthetic only",Link="/Bookings/MyBookings"};db.Notifications.Add(notice);await db.SaveChangesAsync();
            using(var r=await client.GetAsync("/Notifications/Open/"+notice.Id))Check(!(await db.Notifications.AsNoTracking().SingleAsync(n=>n.Id==notice.Id)).IsRead,"SEC-01 notification GET does not mutate read state");
            using(var r=await client.PostAsync("/Notifications/Open/"+notice.Id,new FormUrlEncodedContent([])))Check(r.StatusCode==HttpStatusCode.BadRequest,"SEC-01 notification POST without antiforgery is rejected");
            using(var r=await Post(client,"/Notifications/Open/"+notice.Id,"invalid"))Check(r.StatusCode==HttpStatusCode.BadRequest,"SEC-01 invalid antiforgery is rejected");
            using(var r=await Post(other,"/Notifications/Open/"+notice.Id,otherToken))Check(r.StatusCode==HttpStatusCode.NotFound,"DASH-03 unrelated Client cannot mark a notification read");
            using(var r=await Post(client,"/Notifications/Open/"+notice.Id,token))Check(r.StatusCode==HttpStatusCode.Redirect,"DASH-03 valid owned notification POST navigates");
            var readAt=(await db.Notifications.AsNoTracking().SingleAsync(n=>n.Id==notice.Id)).ReadAt;
            using(var r=await Post(secondSession,"/Notifications/Open/"+notice.Id,secondToken))Check(readAt!=null&&(await db.Notifications.AsNoTracking().SingleAsync(n=>n.Id==notice.Id)).ReadAt==readAt,"DASH-03 read state persists across sessions and repeated opens");

            var previous=await AddBooking(90,BookingStatus.Confirmed,"other",8);previous.PartyVenue="PRIVATE_OTHER_CLIENT_VENUE_SENTINEL";previous.VenueLatitude=14.5;previous.VenueLongitude=121.1;await db.SaveChangesAsync();
            using(var r=await Post(client,"/Bookings/CheckVenueServiceability",token,("partyVenue","Fixture"),("eventDate",previous.EventDate.ToString("yyyy-MM-dd")),("startTime","16:00"))){var text=await r.Content.ReadAsStringAsync();Check(r.IsSuccessStatusCode&&!text.Contains(previous.PartyVenue)&&!text.Contains("\"originLatitude\":14.5")&&!text.Contains("\"originLongitude\":121.1"),"SEC-02 venue response does not disclose another Client's previous event location");using var data=JsonDocument.Parse(text);Check(data.RootElement.GetProperty("usesPreviousEventLocation").GetBoolean()&&data.RootElement.GetProperty("distanceKm").GetDouble()>0,"VEN-03 previous-event routing and computed distance remain active");}

            (string,string)[] bookingFields=[("EventType","Company Events"),("Motif","Navy"),("PartyTheme","Synthetic"),("EventDate",DateTime.Today.AddDays(100).ToString("yyyy-MM-dd")),("StartTime","10:00"),("PartyVenue","Synthetic venue"),("PaxCount","20"),("ContactPerson","Synthetic fresh Client"),("ContactNumber","09123456789"),("ServiceId",package.Id.ToString()),("AcceptTerms","true")];
            await using(var broken=new ReleaseFactory(connection,smtp.Port,mapUrl,root,saveFault:new FailBookingNotificationSave())){
                using var fresh=await Login(broken,"fresh");var freshToken=await Token(fresh,"/Communications");var count=await db.Bookings.CountAsync();var clients=await db.LegacyUsers.CountAsync();
                using(var r=await Post(fresh,"/Bookings/SubmitBooking",freshToken,bookingFields))Check((int)r.StatusCode>=400,"DB-02 injected server notification-save failure is observed");
                Check(await db.Bookings.CountAsync()==count&&await db.LegacyUsers.CountAsync()==clients,"DB-02 server failure rolls back booking, legacy row, timeline and notifications together");
            }
            var auditBooking=await AddBooking(101,BookingStatus.Pending);
            await using(var broken=new ReleaseFactory(connection,smtp.Port,mapUrl,root,failAuditType:SystemActivityType.BookingApproved)){
                using var reviewer=await Login(broken,"staff");var t=await Token(reviewer,"/Communications");using var r=await Post(reviewer,"/Bookings/Approve/"+auditBooking.Id,t);
                Check((await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==auditBooking.Id)).Status==BookingStatus.Pending&&!await db.BookingTimelines.AnyAsync(x=>x.BookingId==auditBooking.Id&&x.EventType==TimelineEventType.BookingApproved),"AUD-01 failed booking audit rolls back the decision and timeline");
            }
            var emailBooking=await AddBooking(102,BookingStatus.Confirmed);var req=new RescheduleRequest{BookingId=emailBooking.Id,RequestedDate=emailBooking.EventDate.AddDays(1),RequestedStartTime=emailBooking.StartTime.AddDays(1),RequestedEndTime=emailBooking.EndTime.AddDays(1),Reason="Synthetic SMTP failure",Status=RescheduleRequestStatus.Pending};db.RescheduleRequests.Add(req);await db.SaveChangesAsync();
            var failingEmail=new FindingsEmailFailure();
            await using(var emailFactory=new ReleaseFactory(connection,smtp.Port,mapUrl,root,emailOverride:failingEmail)){
                using var reviewer=await Login(emailFactory,"staff");var t=await Token(reviewer,"/Communications");using var r=await Post(reviewer,"/RescheduleRequests/Approve/"+req.Id,t);
                Check(r.StatusCode==HttpStatusCode.Redirect&&failingEmail.Calls==0,"EXT-01 reschedule commits without synchronously contacting unavailable SMTP");
                Check(await db.EmailOutboxMessages.AnyAsync(x=>x.EventKey=="reschedule-approved:"+req.Id),"EXT-01 reschedule decision atomically queues recoverable email");
                foreach(var error in new Exception[]{new TimeoutException("PRIVATE_SMTP_SENTINEL"),new AuthenticationException("PRIVATE_SMTP_SENTINEL"),new IOException("PRIVATE_SMTP_SENTINEL")}){
                    failingEmail.Failure=error;await db.EmailOutboxMessages.Where(x=>x.EventKey=="reschedule-approved:"+req.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NextAttemptAtUtc,DateTime.UtcNow.AddMinutes(-1)));
                    using var scope=emailFactory.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchAsync();
                }
                Check(await db.RescheduleRequests.AsNoTracking().AnyAsync(x=>x.Id==req.Id&&x.Status==RescheduleRequestStatus.Approved),"EXT-01 timeout/authentication/transport failures do not undo committed schedule");
                if(!reproduce){
                    var queued=await db.EmailOutboxMessages.AsNoTracking().SingleAsync(x=>x.EventKey=="reschedule-approved:"+req.Id);
                    Check(queued.Attempts==3&&queued.DeliveredAtUtc==null&&queued.LastFailureType==nameof(IOException),"EXT-01 failed email retains safe retry metadata, without sent-success state");
                    failingEmail.Failure=null;await db.EmailOutboxMessages.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NextAttemptAtUtc,DateTime.UtcNow.AddMinutes(-1)));
                    using var scope=emailFactory.Services.CreateScope();var dispatcher=scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>();await dispatcher.DispatchAsync();var calls=failingEmail.Calls;
                    Check(await dispatcher.DispatchAsync()==0&&failingEmail.Calls==calls,"EXT-01 successful recovery does not resend delivered messages");
                    Check(await db.SystemActivities.AnyAsync(x=>x.Type==SystemActivityType.RescheduleApproved&&x.AffectedRecordId==req.Id.ToString()&&x.PerformedByUserId==identities["staff"]&&x.CreatedAt>=DateTime.UtcNow.AddMinutes(-5)&&x.MetadataJson!.Contains("PreviousStatus")),"AUD-01 reschedule audit persists actor, status transition and UTC timestamp");
                }
            }
            var repeatDate=DateTime.Today.AddDays(104).ToString("yyyy-MM-dd");
            var duplicateFields=bookingFields.Select(v=>v.Item1=="EventDate"?(v.Item1,repeatDate):v).ToArray();
            var beforeRetry=await db.Bookings.CountAsync();
            using(var r=await Post(client,"/Bookings/SubmitBooking",token,duplicateFields))Check(r.StatusCode==HttpStatusCode.Redirect,"DB-03 initial synthetic request persists");
            using(var r=await Post(secondSession,"/Bookings/SubmitBooking",secondToken,duplicateFields))Check(await db.Bookings.CountAsync()==beforeRetry+1,"DB-03 exact booking POST retry does not create a duplicate active request");

            if(!reproduce){
                var duplicateId=await db.Bookings.Where(b=>b.ApplicationUserId==identities["client"]&&b.EventDate==DateTime.Parse(repeatDate)).Select(b=>b.Id).SingleAsync();
                using(var r=await Post(staff,"/Bookings/Approve/"+duplicateId,staffToken))Check(r.StatusCode==HttpStatusCode.Redirect,"DB-03 Staff decision remains available after idempotent booking creation");
                using(var r=await Post(client,"/Bookings/SubmitBooking",token,duplicateFields))Check(await db.Bookings.CountAsync()==beforeRetry+1,"DB-03 exact booking replay after Staff approval returns the existing active request");
                await db.Services.Where(s=>s.Id==package.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Price,19801m));
                using(var r=await Post(client,"/Bookings/SubmitBooking",token,duplicateFields))Check(await db.Bookings.CountAsync()==beforeRetry+1&&await db.Bookings.AsNoTracking().AnyAsync(b=>b.Id==duplicateId&&b.FinalPrice==19800),"DB-03 catalog price change does not reinterpret a replay or rewrite the accepted price");
                await db.Services.Where(s=>s.Id==package.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Price,19800m));
                var concurrentDate=DateTime.Today.AddDays(130).ToString("yyyy-MM-dd");var concurrentFields=bookingFields.Select(v=>v.Item1=="EventDate"?(v.Item1,concurrentDate):v).ToArray();
                var submissions=await Task.WhenAll(Post(client,"/Bookings/SubmitBooking",token,concurrentFields),Post(secondSession,"/Bookings/SubmitBooking",secondToken,concurrentFields));foreach(var r in submissions){Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict,"DB-03 simultaneous identical booking requests return a controlled outcome");r.Dispose();}
                Check(await db.Bookings.AsNoTracking().CountAsync(b=>b.ApplicationUserId==identities["client"]&&b.EventDate==DateTime.Parse(concurrentDate))==1,"DB-03 SQL persists one canonical request after concurrent sessions");
                var decision=await AddBooking(105,BookingStatus.Pending);using var staffSecond=await Login(factory,"staff");var staffSecondToken=await Token(staffSecond,"/Communications");
                var decisions=await Task.WhenAll(Post(staff,"/Bookings/Approve/"+decision.Id,staffToken),Post(staffSecond,"/Bookings/Decline/"+decision.Id,staffSecondToken));foreach(var r in decisions){Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict,"DB-03 concurrent approval/decline returns controlled outcome");r.Dispose();}
                Check(await db.BookingTimelines.CountAsync(t=>t.BookingId==decision.Id&&(t.EventType==TimelineEventType.BookingApproved||t.EventType==TimelineEventType.BookingDeclined))==1,"DB-03 concurrent Staff decisions persist only one transition");
                Check(await db.SystemActivities.CountAsync(a=>a.AffectedRecordType=="Booking"&&a.AffectedRecordId==decision.Id.ToString()&&(a.Type==SystemActivityType.BookingApproved||a.Type==SystemActivityType.BookingDeclined))==1,"AUD-01 concurrent decision has exactly one audit event");

                var rejectBooking=await AddBooking(106,BookingStatus.Confirmed);var rejectRequest=new RescheduleRequest{BookingId=rejectBooking.Id,RequestedDate=rejectBooking.EventDate.AddDays(1),RequestedStartTime=rejectBooking.StartTime.AddDays(1),RequestedEndTime=rejectBooking.EndTime.AddDays(1),Reason="Synthetic rejection case",Status=RescheduleRequestStatus.Pending};db.RescheduleRequests.Add(rejectRequest);await db.SaveChangesAsync();
                using(var r=await Post(staff,"/RescheduleRequests/Reject/"+rejectRequest.Id,staffToken,("clientFacingReason","Synthetic scheduling decision")))Check(r.StatusCode==HttpStatusCode.Redirect,"EXT-01 rejection commits without synchronous SMTP");
                Check(await db.EmailOutboxMessages.AnyAsync(e=>e.EventKey=="reschedule-rejected:"+rejectRequest.Id)&&await db.Bookings.AsNoTracking().AnyAsync(b=>b.Id==rejectBooking.Id&&b.StartTime==rejectBooking.StartTime),"AUD-01 rejected reschedule preserves schedule and atomically queues its own email");

                var target=DateTime.Today.AddDays(120);var occupiedA=await AddBooking(120,BookingStatus.Confirmed,hour:0);var occupiedB=await AddBooking(120,BookingStatus.Confirmed,hour:6);var candidateA=await AddBooking(120,BookingStatus.AwaitingVerification,hour:12);var candidateB=await AddBooking(120,BookingStatus.AwaitingVerification,"other",18);
                var pa=new Payment{BookingId=candidateA.Id,Amount=2000,Status=PaymentStatus.Pending,ProofImagePath=first.ProofImagePath};var pb=new Payment{BookingId=candidateB.Id,Amount=2000,Status=PaymentStatus.Pending,ProofImagePath=first.ProofImagePath};db.Payments.AddRange(pa,pb);await db.SaveChangesAsync();
                using var ownerSecond=await Login(factory,"owner");var ownerSecondToken=await Token(ownerSecond,"/Communications");var confirms=await Task.WhenAll(Post(owner,"/Payments/Verify/"+pa.Id,ownerToken),Post(ownerSecond,"/Payments/Verify/"+pb.Id,ownerSecondToken));foreach(var r in confirms){Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict,"DB-03 simultaneous final-capacity confirmations have controlled responses");r.Dispose();}
                Check(await db.Bookings.AsNoTracking().CountAsync(b=>b.EventDate==target&&b.Status==BookingStatus.Confirmed)==3,"DB-03 actual SQL preserves maximum three confirmed bookings under payment race");
                Check(await db.Payments.AsNoTracking().CountAsync(p=>(p.Id==pa.Id||p.Id==pb.Id)&&p.Status==PaymentStatus.Verified)==1,"PAY-06 only the winning capacity confirmation verifies funds");
                var buffered=await AddBooking(125,BookingStatus.Confirmed,"other",10);
                foreach(var time in new[]{"13:59","14:00"}){
                    using var r=await client.GetAsync($"/Bookings/CheckScheduleAvailability?date={buffered.EventDate:yyyy-MM-dd}&startTime={time}&serviceId={package.Id}");using var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                    Check(json.RootElement.GetProperty("available").GetBoolean()==(time=="14:00"),"VEN-07 actual package duration and exact one-hour buffer: "+time);
                }
                using(var r=await Post(client,"/Bookings/CheckVenueServiceability",token,("partyVenue","unresolved")))Check(r.StatusCode==HttpStatusCode.UnprocessableEntity,"VEN-02 unresolved geocoding never becomes a verified location");

                var supportBefore=await db.Conversations.CountAsync();using(var r=await Post(client,"/Communications/StartSupport",token))Check(r.StatusCode==HttpStatusCode.Redirect,"MSG-01 authenticated Client starts support normally");
                var support=await db.Conversations.AsNoTracking().Where(c=>c.CreatedByUserId==identities["client"]).OrderByDescending(c=>c.Id).FirstAsync();
                using(var r=await other.GetAsync("/Communications/MessagesSince?id="+support.Id))Check(Denied(r),"MSG-01 direct conversation ID tampering is denied");
                var messages=await db.Messages.CountAsync();using(var r=await Post(other,"/Communications/SendMessage",otherToken,("conversationId",support.Id.ToString()),("messageContent","Synthetic unauthorized message")))Check(Denied(r)&&await db.Messages.CountAsync()==messages,"MSG-01 message POST enforces membership independently of UI");
                using(var r=await Post(client,"/Communications/SendMessage",token,("conversationId",support.Id.ToString()),("messageContent","<script>INDEPENDENT_XSS_SENTINEL</script>")))Check(r.StatusCode==HttpStatusCode.Redirect,"SEC-01 safe synthetic markup is accepted as text");
                using(var r=await client.GetAsync("/Communications?id="+support.Id)){var html=await r.Content.ReadAsStringAsync();Check(!html.Contains("<script>INDEPENDENT_XSS_SENTINEL")&&html.Contains("&lt;script&gt;INDEPENDENT_XSS_SENTINEL"),"SEC-01 actual Razor output encodes message markup");}
                using(var r=await other.GetAsync("/Bookings/Details/"+installment.Id))Check(r.StatusCode==HttpStatusCode.NotFound,"SEC-02 direct booking ID is scoped to its Client");
                using(var r=await other.GetAsync("/Payments/PaymentProof/"+first.Id))Check(Denied(r),"SEC-02 direct private proof ID is scoped to its Client");
                using(var r=await staff.GetAsync("/Payments/PaymentProof/"+first.Id))Check(Denied(r),"SEC-02 Staff cannot bypass Owner-only proof access");
                using(var r=await client.GetAsync("/Payments/PaymentProof/"+first.Id))Check(r.IsSuccessStatusCode&&r.Headers.CacheControl?.NoStore==true,"SEC-02 own proof remains private and noncached");
            }

            var evidenceDirectory=Path.Combine(".tmp-owner-build","findings-review");
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory,reproduce?"additional-reproduction.json":"targeted-results.json"),JsonSerializer.Serialize(new {checks,failures,mode=reproduce?"Before-fix reproduction":"Regression",results,ProductionContacted=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine(reproduce?$"REPRODUCTION: {failures} missing safeguards observed across {checks} checks. No production contact.":$"SUCCESS: {checks} independent findings checks passed. Isolated SQL only.");
        }finally{await map.StopAsync();if(!Regex.IsMatch(name,"^AriesFindings_[0-9a-f]{32}$"))throw new Exception("Unsafe test teardown.");await db.Database.EnsureDeletedAsync();}
    }
    static string ExtractToken(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    static async Task<string> Token(HttpClient c,string path){using var r=await c.GetAsync(path);var t=ExtractToken(await r.Content.ReadAsStringAsync());if(!r.IsSuccessStatusCode||t.Length==0)throw new Exception("Isolated token page failed.");return t;}
    static Task<HttpResponseMessage> Post(HttpClient c,string path,string token,params (string,string)[] values)=>c.PostAsync(path,new FormUrlEncodedContent(values.Select(v=>new KeyValuePair<string,string>(v.Item1,v.Item2)).Append(new("__RequestVerificationToken",token))));
    static bool Denied(HttpResponseMessage r)=>r.StatusCode==HttpStatusCode.Forbidden||(r.StatusCode==HttpStatusCode.Redirect&&r.Headers.Location!.ToString().Contains("AccessDenied"));
    static void PolicyChecks(Action<bool,string> check)
    {
        var options=new VenueDistanceOptions{BaseLatitude=14.58125,BaseLongitude=121.1258};var service=new VenueDistanceService(Microsoft.Extensions.Options.Options.Create(options));
        foreach(var pair in new[]{(double.NaN,121d),(14d,double.NaN),(double.PositiveInfinity,121d),(91d,121d),(-91d,121d),(14d,181d),(14d,-181d),(121.1258,14.58125)})check(!service.IsValidCoordinate(pair.Item1,pair.Item2),"VEN-02 invalid/nonfinite/out-of-range/reversed coordinates are rejected");
        check(service.Calculate(options.BaseLatitude,options.BaseLongitude).DistanceKm==0,"VEN-03 configured origin has zero distance to itself");
        check(Math.Abs(service.Calculate(0,0,1,0).DistanceKm-111.20)<.02,"VEN-03 one equatorial degree is about 111.20 kilometres");
        var ph=service.Calculate(14.5995,120.9842,14.676,121.0437);check(ph.DistanceKm>9&&ph.DistanceKm<12,"VEN-03 known Manila/Quezon coordinate pair has plausible straight-line distance, not road-distance proof");
        foreach(var d in new[]{24.999,25d,25.001,49.999,50d,50.001,79.999,80d,80.001}){
            var lat=d/6371.0088*180/Math.PI;var r=service.Calculate(0,0,lat,0);
            check(r.IsServiceable==(d<=80),"VEN-04 inside/on/outside service boundary: "+d.ToString(CultureInfo.InvariantCulture));
            check(r.RequiresManualReview==(d>50&&d<=80),"VEN-06 existing review-zone boundary: "+d.ToString(CultureInfo.InvariantCulture));
            check(r.TravelFee==(d>80?0:Math.Round(Math.Max(0,(decimal)d-25)*15,2,MidpointRounding.AwayFromZero)),"VEN-05 source fee threshold and decimal rounding: "+d.ToString(CultureInfo.InvariantCulture));
        }
        foreach(var item in new[]{(19500m,(decimal?)19637,90m,"Possible overpayment"),(19500m,(decimal?)19000,90m,"Possible underpayment"),(19500m,(decimal?)19500,90m,"Match"),(19500m,(decimal?)null,90m,"Not Detected"),(19500m,(decimal?)19637,20m,"Uncertain OCR")})check(OcrComparison.AmountLabel(item.Item1,item.Item2,item.Item3)==item.Item4,"PAY-05 advisory classification: "+item.Item4);
        var finance=new PaymentFinancialService(null!);var booking=new Booking{Id=1,FinalPrice=19800};var p=new[]{new Payment{Id=1,Amount=2000.01m,Status=PaymentStatus.Verified},new Payment{Id=2,Amount=999.99m,Status=PaymentStatus.Verified},new Payment{Id=3,Amount=1234.56m,Status=PaymentStatus.Pending},new Payment{Id=4,Amount=500,Status=PaymentStatus.Rejected},new Payment{Id=5,Amount=100,Status=PaymentStatus.AdditionalEvidenceRequired}};
        var f=finance.Calculate(booking,p,new[]{new RefundRequest{Status=RefundStatus.Approved,Amount=55},new RefundRequest{Status=RefundStatus.Refunded,Amount=100.50m}});
        check(f.TotalVerifiedPayments==3000m&&f.RemainingBalance==16800m&&f.NetCollected==2899.50m,"PAY-06 decimals, partial installments and completed-only refunds reconcile");
        check(finance.Calculate(booking,new[]{new Payment{Amount=19800,Status=PaymentStatus.Verified}},Array.Empty<RefundRequest>()).IsFullyPaid,"PAY-06 verified full payment alone reaches fully paid");
    }
}
sealed class FailBookingNotificationSave : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default){if(data.Context!.ChangeTracker.Entries<Notification>().Any(x=>x.State==EntityState.Added&&x.Entity.Title=="New Booking Submitted"))throw new InvalidOperationException("Synthetic notification persistence failure");return ValueTask.FromResult(result);}
}
sealed class FindingsEmailFailure : IEmailService
{
    public int Calls {get;private set;}
    public Exception? Failure {get;set;}=new TimeoutException("PRIVATE_SMTP_SENTINEL");
    public Task SendEmailAsync(string to,string subject,string html){Calls++;if(Failure!=null)throw Failure;return Task.CompletedTask;}
    public Task SendEmailWithAttachmentAsync(string to,string subject,string html,byte[] attachment,string attachmentName){Calls++;if(Failure!=null)throw Failure;return Task.CompletedTask;}
}
