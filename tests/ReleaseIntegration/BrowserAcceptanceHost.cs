using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class BrowserAcceptanceHost
{
    static string RandomPassword() => "Aa!9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    static void Verify(bool ok,string name) {if(!ok)throw new Exception("FAILED: "+name);Console.WriteLine("ACCEPTANCE_PASS: "+name);}
    static async Task<int> RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var database="AriesBrowserAcceptance_"+Guid.NewGuid().ToString("N");
        var localInstance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!System.Text.RegularExpressions.Regex.IsMatch(localInstance,@"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))throw new InvalidOperationException("Invalid isolated SQL instance");
        var sql="Server=(localdb)\\"+localInstance+";Database="+database+";Integrated Security=True;TrustServerCertificate=True";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(sql).Options;
        var root=Path.Combine(Path.GetTempPath(),database);
        Directory.CreateDirectory(root);
        var handoff=Path.Combine(".tmp-owner-build","predefense-acceptance-20261115");
        Directory.CreateDirectory(handoff);
        var handoffFile=Path.Combine(handoff,"staging-info.json");
        var doneFile=Path.Combine(handoff,"browser-complete.json");
        if(File.Exists(handoffFile)||File.Exists(doneFile))throw new InvalidOperationException("Existing staging state: refuse overwrite");
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();
        smtp.Start();
        var mapBuilder=WebApplication.CreateBuilder();
        mapBuilder.Logging.ClearProviders();
        await using var map=mapBuilder.Build();
        map.Urls.Add("http://127.0.0.1:0");
        map.MapGet("/search",()=>Results.Json(new[]{new{lat="14.58125",lon="121.1258",display_name="Authorized synthetic venue, Philippines",addresstype="house",place_rank=30}}));
        await map.StartAsync();
        var mapUrl=new Uri(map.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        try
        {
            await db.Database.MigrateAsync();
            var normal=new ReleaseFactory(sql,smtp.Port,mapUrl,root);
            var ocrFailure=new ReleaseFactory(sql,smtp.Port,mapUrl,root,ocrOverride:new ThrowingOcrBoundary());
            await using(normal)
            await using(ocrFailure)
            {
                normal.UseKestrel(0);
                ocrFailure.UseKestrel(0);
                normal.StartServer();
                ocrFailure.StartServer();
                using var scope=normal.Services.CreateScope();
                await IdentitySeeder.SeedAsync(scope.ServiceProvider);
                var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var creds=new Dictionary<string,object>();
                var uid=new Dictionary<string,string>();
                foreach(var role in new[]{"Client","Staff","Owner"})
                {
                    var email="acceptance-"+role.ToLowerInvariant()+"@example.test";
                    var pw=RandomPassword();
                    var usr=new ApplicationUser{Email=email,UserName=email,FullName="Synthetic Acceptance "+role,PhoneNumber="09123456789",EmailConfirmed=true};
                    var created=await users.CreateAsync(usr,pw);
                    Verify(created.Succeeded,"generated "+role+" account created in isolated Identity SQL");
                    var assigned=await users.AddToRoleAsync(usr,role);
                    Verify(assigned.Succeeded,"generated "+role+" assigned appropriate role");
                    creds[role]=new{email,password=pw};
                    uid[role]=usr.Id;
                }
                var legacy=new User{FullName="Synthetic Browser Client",Email="acceptance-client@example.test",Role="Client",PasswordHash="IDENTITY_MANAGED"};
                var package=new Service{Name="Acceptance Fixture",Price=10000m,DurationInHours=1};
                db.LegacyUsers.Add(legacy);db.Services.Add(package);await db.SaveChangesAsync();
                Booking MakeBooking(int days,string status)=>new(){
                    ClientId=legacy.Id,ApplicationUserId=uid["Client"],ServiceId=package.Id,
                    PackageName=package.Name,BasePrice=10000,FinalPrice=10000,RequiredDownpayment=2000,
                    EventType="Company Events",PartyVenue="Synthetic acceptance venue, Philippines",
                    VenueLatitude=14.58125,VenueLongitude=121.1258,VenueSelectionMethod="Search",
                    ContactPerson="Synthetic Acceptance Client",ContactNumber="09123456789",
                    EventDate=DateTime.Today.AddDays(days),StartTime=DateTime.Today.AddDays(days).AddHours(10),
                    EndTime=DateTime.Today.AddDays(days).AddHours(11),Status=status,PaxCount=25,IsServiceable=true
                };
                var normalBooking=MakeBooking(75,BookingStatus.Pending);
                var failureBooking=MakeBooking(76,BookingStatus.Pending);
                db.Bookings.AddRange(normalBooking,failureBooking);await db.SaveChangesAsync();
                var state=new {
                    normalUrl=normal.ClientOptions.BaseAddress!.ToString().TrimEnd('/'),
                    failureUrl=ocrFailure.ClientOptions.BaseAddress!.ToString().TrimEnd('/'),
                    database,root,
                    bookings=new{normal=normalBooking.Id,ocrFailure=failureBooking.Id},
                    packageId=package.Id,
                    credentials=creds,
                    fixture=Path.GetFullPath("tests/ReleaseIntegration/Fixtures/payment.png"),
                    doneFile=Path.GetFullPath(doneFile)
                };
                await File.WriteAllTextAsync(handoffFile,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("STAGING_READY normal="+state.normalUrl+" failure="+state.failureUrl+" sql="+database);
                var deadline=DateTime.UtcNow.AddMinutes(8);
                while(!File.Exists(doneFile) && DateTime.UtcNow<deadline)await Task.Delay(250);
                Verify(File.Exists(doneFile),"browser runner completed before isolated staging shutdown");
                var browserState=JsonDocument.Parse(await File.ReadAllTextAsync(doneFile));
                Verify(browserState.RootElement.GetProperty("passed").GetBoolean(),"browser acceptance assertions completed");
                db.ChangeTracker.Clear();
                var normalPayment=await db.Payments.Include(p=>p.Booking).SingleOrDefaultAsync(p=>p.BookingId==normalBooking.Id);
                var failurePayment=await db.Payments.Include(p=>p.Booking).SingleOrDefaultAsync(p=>p.BookingId==failureBooking.Id);
                Verify(normalPayment!=null&&normalPayment.Status==PaymentStatus.Verified,"normal browser flow persisted Owner-verified payment");
                Verify(failurePayment!=null&&failurePayment.Status==PaymentStatus.Verified,"manual OCR-failure flow persisted authorized Owner review");
                Verify(normalPayment!.Booking!.Status==BookingStatus.Confirmed&&failurePayment!.Booking!.Status==BookingStatus.Confirmed,
                    "both browser flows ended in confirmed bookings");
                Verify(await db.OcrVerifications.AnyAsync(o=>o.PaymentId==normalPayment!.Id&&o.VerificationResult!=OcrVerificationResults.OcrFailed),
                    "native OCR processed normal browser receipt");
                Verify(await db.OcrVerifications.AnyAsync(o=>o.PaymentId==failurePayment!.Id&&o.VerificationResult==OcrVerificationResults.OcrFailed&&o.ExtractedAmount==null),
                    "OCR boundary exception retained honest failure state without invented amount");
                foreach(var pay in new[]{normalPayment,failurePayment})
                {
                    var physical=Path.Combine(root,"financial-proofs",pay!.ProofImagePath.Replace('/',Path.DirectorySeparatorChar));
                    Verify(File.Exists(physical)&&new FileInfo(physical).Length>0,"private evidence persisted: "+pay.Id);
                }
                foreach(var id in new[]{normalBooking.Id,failureBooking.Id})
                {
                    var summary=await new PaymentFinancialService(db).GetSummaryAsync(id);
                    Verify(summary.TotalVerifiedPayments==2000m&&summary.RemainingBalance==8000m,
                        "browser verified funds and remaining balance reconcile for booking "+id);
                }
                var finalEvidence=new{
                    source="Real Chromium → loopback Kestrel → ASP.NET Core Identity/MVC → disposable SQL and private files",
                    databaseIsolated=true,normalBooking=normalBooking.Id,failureBooking=failureBooking.Id,
                    normalPayment=normalPayment!.Id,failurePayment=failurePayment!.Id,
                    normalPaymentStatus=normalPayment.Status,failurePaymentStatus=failurePayment.Status,
                    privateFileCount=2,observedSmtpSinkMessages=smtp.Messages.Count,
                    completedAtUtc=DateTime.UtcNow
                };
                await File.WriteAllTextAsync(Path.Combine(handoff,"server-state-verified.json"),
                    JsonSerializer.Serialize(finalEvidence,new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("ACCEPTANCE_COMPLETE: REAL_CHROMIUM_TO_SQL_AND_MANUAL_OCR_REVIEW");
            }
            return 0;
        }
        finally
        {
            try{await db.Database.EnsureDeletedAsync();Console.WriteLine("GENERATED_SQL_DATABASE_REMOVED");}
            catch(Exception ex){Console.WriteLine("ISOLATED_SQL_CLEANUP_FAILED "+ex.GetType().Name);}
            try{if(Directory.Exists(root))Directory.Delete(root,true);}
            catch(Exception ex){Console.WriteLine("ISOLATED_FILES_CLEANUP_FAILED "+ex.GetType().Name);}
            try{if(File.Exists(handoffFile))File.Delete(handoffFile);}
            catch{}
        }
    }
    public static Task<int> ExecuteAsync()=>RunAsync();
}
