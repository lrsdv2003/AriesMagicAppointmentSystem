using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class BusinessAcceptanceHost
{
    static void Check(bool v,string label){if(!v)throw new Exception("FAILED: "+label);Console.WriteLine("BUSINESS_SQL_PASS: "+label);}
    public static async Task RunAsync(bool finalUi = false)
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var name="AriesBusinessAcceptance_"+Guid.NewGuid().ToString("N");
        var instance = Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE") ?? "MSSQLLocalDB";
        if (!Regex.IsMatch(instance, "^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Invalid LocalDB test instance.");
        var sql = $@"Server=(localdb)\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        var privateRoot=Path.Combine(Path.GetTempPath(),name);
        Directory.CreateDirectory(privateRoot);
        var evidence=Path.Combine(".tmp-owner-build",finalUi ? "final-ui-browser" : "predefense-business-20261115");
        Directory.CreateDirectory(evidence);
        var handoff=Path.Combine(evidence,"business-host.json");
        var done=Path.Combine(evidence,"business-done.json");
        if(File.Exists(handoff)||File.Exists(done))throw new InvalidOperationException("Previous business host state remains");
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(sql).Options;
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();smtp.Start();
        var mapBuilder=WebApplication.CreateBuilder();mapBuilder.Logging.ClearProviders();
        await using var map=mapBuilder.Build();
        map.Urls.Add("http://127.0.0.1:0");
        map.MapGet("/search",()=>Results.Json(new[]{new{lat="14.58125",lon="121.1258",display_name="Synthetic business venue, Philippines",addresstype="house",place_rank=30}}));
        await map.StartAsync();
        var mapUrl=new Uri(map.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        try
        {
            await db.Database.MigrateAsync();
            await using var host=new ReleaseFactory(sql,smtp.Port,mapUrl,privateRoot);
            host.UseKestrel(0);host.StartServer();
            using var scope=host.Services.CreateScope();
            await IdentitySeeder.SeedAsync(scope.ServiceProvider);
            var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var users=new Dictionary<string,ApplicationUser>();
            var passwords=new Dictionary<string,object>();
            foreach(var role in new[]{"Client","OtherClient","Staff","Owner"})
            {
                var roleClaim=role=="OtherClient"?"Client":role;
                var email="business-"+role.ToLowerInvariant()+"@example.test";
                var pass="Aa!9"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var appUser=new ApplicationUser{Email=email,UserName=email,FullName="Synthetic business "+role,EmailConfirmed=true,PhoneNumber="09123456789"};
                Check((await manager.CreateAsync(appUser,pass)).Succeeded,"generated "+role+" created");
                Check((await manager.AddToRoleAsync(appUser,roleClaim)).Succeeded,"generated "+role+" authorized");
                users[role]=appUser;passwords[role]=new{email,password=pass};
            }
            var package=new Service{Name="Synthetic Business Package",Price=10000,DurationInHours=1};
            var legacy=new User{FullName="Synthetic Business Client",Email=users["Client"].Email!,Role="Client",PasswordHash="IDENTITY_MANAGED"};
            var otherLegacy=new User{FullName="Synthetic Other Client",Email=users["OtherClient"].Email!,Role="Client",PasswordHash="IDENTITY_MANAGED"};
            db.Services.Add(package);db.LegacyUsers.AddRange(legacy,otherLegacy);await db.SaveChangesAsync();
            Booking NewBooking(int days,string status,string who="Client",int start=10)
            {
                var user=who=="OtherClient"?otherLegacy:legacy;
                return new(){ClientId=user.Id,ApplicationUserId=users[who].Id,ServiceId=package.Id,
                    PackageName=package.Name,BasePrice=10000,FinalPrice=10000,RequiredDownpayment=2000,
                    EventType="Company Events",PartyVenue="Synthetic business venue, Philippines",
                    VenueLatitude=14.58125,VenueLongitude=121.1258,VenueSelectionMethod="Search",
                    ContactPerson=user.FullName,ContactNumber="09123456789",PaxCount=25,IsServiceable=true,
                    EventDate=DateTime.Today.AddDays(days),StartTime=DateTime.Today.AddDays(days).AddHours(start),
                    EndTime=DateTime.Today.AddDays(days).AddHours(start+1),Status=status};
            }
            var decline=NewBooking(80,BookingStatus.Pending);
            var reschedule=NewBooking(85,BookingStatus.Confirmed);
            var cancel=NewBooking(89,BookingStatus.Confirmed);
            var overdue=NewBooking(90,BookingStatus.AwaitingDownpayment);
            var competing=NewBooking(85,BookingStatus.Pending,"OtherClient");
            db.Bookings.AddRange(decline,reschedule,cancel,overdue,competing);await db.SaveChangesAsync();
            var proofSource=Path.GetFullPath("tests/ReleaseIntegration/Fixtures/payment.png");
            var proofDir=Path.Combine(privateRoot,"financial-proofs","payments");Directory.CreateDirectory(proofDir);
            async Task Verified(Booking booking,DateTime anchorUtc)
            {
                var relative="payments/"+Guid.NewGuid().ToString("N")+".png";
                File.Copy(proofSource,Path.Combine(proofDir,relative["payments/".Length..]));
                db.Payments.Add(new(){
                    BookingId=booking.Id,Amount=2000,ExpectedAmountAtUpload=2000,Status=PaymentStatus.Verified,
                    PaymentMethod="GCash",ProofImagePath=relative,UploadedAt=anchorUtc,InitialVerificationAtUtc=anchorUtc,
                    VerifiedAt=anchorUtc,TransactionReference="STAGING"+Guid.NewGuid().ToString("N")[..12]});
                await db.SaveChangesAsync();
            }
            await Verified(reschedule,DateTime.UtcNow.AddHours(-30));
            await Verified(cancel,DateTime.UtcNow.AddHours(-1));
            db.BookingTimelines.Add(new(){BookingId=overdue.Id,EventType=TimelineEventType.PaymentWindowOpened,CreatedAt=DateTime.UtcNow.AddHours(-26),Notes="Isolated overdue fixture"});
            await db.SaveChangesAsync();
            var state=new{
                url=host.ClientOptions.BaseAddress!.ToString().TrimEnd('/'),database=name,privateRoot,packageId=package.Id,
                bookings=new{decline=decline.Id,reschedule=reschedule.Id,cancel=cancel.Id,overdue=overdue.Id,competing=competing.Id},
                requestedDate=DateTime.Today.AddDays(87).ToString("yyyy-MM-dd"),
                credentials=passwords,doneFile=Path.GetFullPath(done)};
            await File.WriteAllTextAsync(handoff,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("BUSINESS_BROWSER_HOST_READY "+state.url);
            var until=DateTime.UtcNow.AddMinutes(7);
            while(!File.Exists(done)&&DateTime.UtcNow<until)await Task.Delay(250);
            Check(File.Exists(done),"browser suite wrote completion state");
            var complete=JsonDocument.Parse(await File.ReadAllTextAsync(done));
            db.ChangeTracker.Clear();
            if (finalUi)
            {
                Check(complete.RootElement.GetProperty("passed").GetBoolean(), "Cross-role UI browser checks passed");
                var cancellation = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == cancel.Id);
                Check(cancellation.Status == BookingStatus.Cancelled
                    && cancellation.CancellationSource == BookingCancellationPolicy.OwnerCancellationSource
                    && !cancellation.RefundEligible
                    && await db.Payments.AnyAsync(p => p.BookingId == cancel.Id && p.Status == PaymentStatus.Verified)
                    && !await db.RefundRequests.AnyAsync(r => r.BookingId == cancel.Id),
                    "Browser cancellation persisted in SQL with its verified payment and no refund");
                return;
            }
            var all=await db.Bookings.AsNoTracking().Where(b=>new[]{decline.Id,reschedule.Id,cancel.Id,overdue.Id,competing.Id}.Contains(b.Id)).ToListAsync();
            var recap=new{
                browserPassed=complete.RootElement.GetProperty("passed").GetBoolean(),
                statuses=all.Select(b=>new{b.Id,b.Status,b.EventDate,b.StartTime,b.RefundEligible}).ToList(),
                refunds=await db.RefundRequests.AsNoTracking().Select(r=>new{r.BookingId,r.Status,r.Amount,r.ApprovedAmount}).ToListAsync(),
                reschedules=await db.RescheduleRequests.AsNoTracking().Select(r=>new{r.BookingId,r.Status,r.RequestedDate,r.RequestedStartTime}).ToListAsync(),
                messages=await db.Messages.AsNoTracking().CountAsync(),
                smtpSinkMessages=smtp.Messages.Count
            };
            await File.WriteAllTextAsync(Path.Combine(evidence,"business-sql-after-browser.json"),JsonSerializer.Serialize(recap,new JsonSerializerOptions{WriteIndented=true}));
            Check(recap.browserPassed,"Chromium business workflow assertions completed");
            Check(all.Single(b=>b.Id==decline.Id).Status==BookingStatus.Declined,"Staff declined booking through real browser");
            Check(all.Single(b=>b.Id==reschedule.Id).EventDate.Date==DateTime.Today.AddDays(87),"Client reschedule accepted by Staff changed actual SQL event date");
            Check(all.Single(b=>b.Id==cancel.Id).Status==BookingStatus.Cancelled&&all.Single(b=>b.Id==cancel.Id).RefundEligible,"Client cancellation recorded refund eligibility");
            Check(recap.refunds.Any(r=>r.BookingId==cancel.Id&&r.Status==RefundStatus.Refunded),"Owner refund completed for eligible cancelled booking");
            Check(all.Single(b=>b.Id==overdue.Id).Status==BookingStatus.Expired,"Expired payment window persisted appropriate terminal booking");
            Check(all.Single(b=>b.Id==competing.Id).Status!=BookingStatus.Confirmed,"Competing unverified slot never confirmed automatically");
            Check(recap.messages>=2,"Cross-role browser messages persisted in SQL");
            Check(await db.Bookings.AsNoTracking().AnyAsync(b=>b.ApplicationUserId==users["Client"].Id
                && b.Id!=decline.Id&&b.Id!=reschedule.Id&&b.Id!=cancel.Id&&b.Id!=overdue.Id
                && b.ContactPerson=="Synthetic Browser Client"&&b.Status==BookingStatus.Pending),
                "Client-created four-step browser booking persists a new pending SQL record");
            var updatedPackage=await db.Services.AsNoTracking().SingleAsync(s=>s.Id==package.Id);
            Check(updatedPackage.Name=="Synthetic Revised Package"&&updatedPackage.Price==12000,
                "Owner package edits persisted in isolated SQL");
            Check(await db.Bookings.AsNoTracking().Where(b=>b.Id==reschedule.Id||b.Id==cancel.Id)
                .AllAsync(b=>b.FinalPrice==10000&&b.PackageName=="Synthetic Business Package"),
                "Owner package edits did not rewrite historical booking totals and names");
            Console.WriteLine("BUSINESS_BROWSER_TO_SQL_COMPLETE");
        }
        finally
        {
            try{await db.Database.EnsureDeletedAsync();Console.WriteLine("GENERATED_BUSINESS_SQL_REMOVED");}catch(Exception e){Console.WriteLine("BUSINESS_SQL_CLEANUP_FAILED "+e.GetType().Name);}
            try{Directory.Delete(privateRoot,true);}catch{}
            try{File.Delete(handoff);}catch{}
        }
    }
}
