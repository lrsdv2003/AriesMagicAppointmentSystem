using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;

sealed class ReleaseFactory(string connection, int smtpPort, Uri mapServer, string privateRoot, bool failVerificationAudit = false, bool shortTicket = false, bool cleanFeedbackMedia = false, bool authUi = false,
    Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? saveFault = null, IEmailService? emailOverride = null,
    AriesMagicAppointmentSystem.Models.SystemActivityType? failAuditType = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"] = connection, ["BackgroundJobs:Enabled"] = "false",
            ["Database:InitializeOnStartup"] = "false", ["EmailSettings:Host"] = "127.0.0.1", ["EmailSettings:Port"] = smtpPort.ToString(),
            ["EmailSettings:SenderEmail"] = "sender@example.test", ["EmailSettings:Username"] = "local-fixture",
            ["EmailSettings:Password"] = "generated-isolated-fixture", ["EmailSettings:EnableSsl"] = "false",
            ["PrivateStorage:RootPath"] = privateRoot
            , ["FeedbackMedia:Root"] = Path.Combine(Path.GetTempPath(),"AriesFeedbackIntegration",Path.GetFileName(privateRoot))
            , ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning"
        }));
        builder.ConfigureTestServices(services => {
            _ = authUi; // Preserve earlier named callers; every synthetic host must avoid the user profile key ring.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            if(cleanFeedbackMedia) {
                services.RemoveAll<IFeedbackMalwareScanner>();
                services.AddSingleton<IFeedbackMalwareScanner,FixtureCleanFeedbackScanner>();
            }
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => { o.UseSqlServer(connection); if(saveFault != null)o.AddInterceptors(saveFault); });
            if(emailOverride != null){services.RemoveAll<IEmailService>();services.AddSingleton(emailOverride);}
            if (shortTicket) services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, o => { o.ExpireTimeSpan=TimeSpan.FromMilliseconds(50);o.SlidingExpiration=false; });
            if (failVerificationAudit || failAuditType.HasValue) {
                services.RemoveAll<ISystemActivityService>();
                services.AddScoped<ISystemActivityService>(sp=>new FailingAudit(sp.GetRequiredService<ApplicationDbContext>(),failAuditType??AriesMagicAppointmentSystem.Models.SystemActivityType.PaymentVerified));
            }
            services.RemoveAll<IGeocodingService>();
            services.AddScoped<IGeocodingService>(_ => {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var service = new NominatimGeocodingService(client);
                client.BaseAddress = mapServer;
                return service;
            });
        });
    }
}
sealed class FixtureCleanFeedbackScanner : IFeedbackMalwareScanner
{
    public Task<MediaScan> ScanAsync(byte[] content,CancellationToken ct) => Task.FromResult(MediaScan.Clean);
}

sealed class FailingAudit(ApplicationDbContext db, AriesMagicAppointmentSystem.Models.SystemActivityType failOn=AriesMagicAppointmentSystem.Models.SystemActivityType.PaymentVerified) : ISystemActivityService
{
    readonly SystemActivityService inner = new(db);
    public Task LogAsync(AriesMagicAppointmentSystem.Models.SystemActivityType type,string description,string performedByUserId,string? performedByUserName=null,string? affectedRecordId=null,string? affectedRecordType=null,object? metadata=null)
        => type==failOn ? throw new InvalidOperationException("Synthetic audit outage") : inner.LogAsync(type,description,performedByUserId,performedByUserName,affectedRecordId,affectedRecordType,metadata);
    public Task<List<AriesMagicAppointmentSystem.Models.SystemActivity>> GetRecentAsync(int count=50)=>inner.GetRecentAsync(count);
    public Task<(List<AriesMagicAppointmentSystem.Models.SystemActivity> Items,int TotalCount)> GetPagedAsync(int page=1,int pageSize=20,AriesMagicAppointmentSystem.Models.SystemActivityType? type=null,DateTime? fromDate=null,DateTime? toDate=null,string? search=null)
        =>inner.GetPagedAsync(page,pageSize,type,fromDate,toDate,search);
}
