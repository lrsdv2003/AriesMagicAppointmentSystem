using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment()) ProductionSafeLogging.Configure(builder.Services);
CertificateDataProtection.Configure(builder.Services, builder.Configuration, builder.Environment);

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddControllersWithViews(options => {
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<DatabaseConflictFilter>();
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

if (!builder.Environment.IsDevelopment())
{
    var connection = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection) || connection.Contains("Server=localhost", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection for production.");
    bool secureConnection;
    try {
        var sql = new SqlConnectionStringBuilder(connection);
        secureConnection = !sql.TrustServerCertificate && !string.IsNullOrWhiteSpace(sql.InitialCatalog)
            && !string.IsNullOrWhiteSpace(sql.DataSource)
            && !sql.Encrypt.ToString().Equals("Optional", StringComparison.OrdinalIgnoreCase)
            && !sql.Encrypt.ToString().Equals("False", StringComparison.OrdinalIgnoreCase)
            && !sql.DataSource.Contains("(localdb)", StringComparison.OrdinalIgnoreCase)
            && !sql.DataSource.Contains("localhost", StringComparison.OrdinalIgnoreCase);
    } catch (ArgumentException) {
        throw new InvalidOperationException("Invalid production SQL connection-string syntax. Connection values are omitted.");
    } catch { secureConnection = false; }
    if (!secureConnection) throw new InvalidOperationException("Production SQL requires an encrypted remote connection with certificate validation and an explicit database.");
    var hosts = builder.Configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Trim() == "*"))
        throw new InvalidOperationException("Configure explicit AllowedHosts for production.");
}
builder.Services.AddRazorPages();
builder.Services.AddScoped<ContractPdfService>();
builder.Services.AddScoped<IOcrVerificationService, OcrVerificationService>();
builder.Services.AddScoped<IPaymentFinancialService, PaymentFinancialService>();
builder.Services.AddOptions<VenueDistanceOptions>().Bind(builder.Configuration.GetSection("VenueDistance"))
    .Validate(o => double.IsFinite(o.BaseLatitude) && o.BaseLatitude >= -90 && o.BaseLatitude <= 90
        && double.IsFinite(o.BaseLongitude) && o.BaseLongitude >= -180 && o.BaseLongitude <= 180
        && double.IsFinite(o.FreeTravelRadiusKm) && o.FreeTravelRadiusKm >= 0
        && double.IsFinite(o.ManualReviewDistanceKm) && o.ManualReviewDistanceKm >= o.FreeTravelRadiusKm
        && double.IsFinite(o.MaximumServiceDistanceKm) && o.MaximumServiceDistanceKm >= o.ManualReviewDistanceKm
        && double.IsFinite(o.EstimatedTravelSpeedKph) && o.EstimatedTravelSpeedKph > 0 && o.TravelFeePerKm >= 0,
        "VenueDistance requires valid origin coordinates, ordered nonnegative radii, a positive speed and a nonnegative fee.")
    .ValidateOnStart();
builder.Services.AddScoped<IVenueDistanceService, VenueDistanceService>();
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client => client.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOptions<EmailSettings>().Bind(builder.Configuration.GetSection("EmailSettings"))
    .Validate(o => builder.Environment.IsDevelopment() || EmailConfiguration.IsValid(o),
        "Email provider configuration is invalid; values omitted.")
    .ValidateOnStart();
builder.Services.AddScoped<SmtpEmailService>();
builder.Services.AddHttpClient<HttpsEmailService>(c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddScoped<IEmailService>(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailSettings>>().Value.Provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
    ? sp.GetRequiredService<SmtpEmailService>() : sp.GetRequiredService<HttpsEmailService>());
builder.Services.AddScoped<OwnerEmailChangeService>();
builder.Services.AddScoped<EmailOutbox>();
builder.Services.AddScoped<EmailOutboxDispatcher>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IPasswordHasher<EmailVerification>, PasswordHasher<EmailVerification>>();

builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<ISystemActivityService, SystemActivityService>();
builder.Services.AddScoped<AccountCredentialRecovery>();
builder.Services.AddOptions<PersonnelOptions>().Bind(builder.Configuration.GetSection("Personnel"))
    .Validate(o=>o.InvitationHours is >=1 and <=48,"Personnel invitations must expire within 1 to 48 hours.").ValidateOnStart();
builder.Services.AddScoped<PersonnelProvisioning>();
builder.Services.AddOptions<FeedbackMediaOptions>().Bind(builder.Configuration.GetSection("FeedbackMedia"))
    .Validate(o=>o.MaxImages is >=1 and <=5 && o.MaxImageBytes is >=1 and <=10*1024*1024 && o.MaxVideoBytes is >=1024*1024 and <=25*1024*1024
        && o.MaxDimension is >=1 and <=6000 && o.MaxPixels is >=1 and <=16_000_000
        && o.ClamAvPort is >0 and <=65535,"Feedback media limits must stay within the supported safe bounds.").ValidateOnStart();
builder.Services.AddScoped<IFeedbackMalwareScanner,ClamAvFeedbackScanner>();
builder.Services.AddScoped<FeedbackMediaService>();
builder.Services.AddScoped<ITrashHistoryService, TrashHistoryService>();
builder.Services.AddScoped<IBookingExpirationService, BookingExpirationService>();
if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
{
    builder.Services.AddHostedService<BookingArchivingBackgroundService>();
    builder.Services.AddHostedService<InactiveAccountBackgroundService>();
    builder.Services.AddHostedService<BookingExpirationBackgroundService>();
    builder.Services.AddHostedService<EmailOutboxBackgroundService>();
    builder.Services.AddHostedService<FeedbackMediaWorker>();
}

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 12;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options => options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(ApplicationRoles.All).Build());

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context => {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if(context.Principal?.Identity?.IsAuthenticated!=true)return;
        var users=context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user=await users.GetUserAsync(context.Principal);
        if(user==null||!user.IsActive||!(await users.GetRolesAsync(user)).Any(ApplicationRoles.IsActive)){context.RejectPrincipal();await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);}
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

var app = builder.Build();
CertificateDataProtection.ValidateStartup(app.Services);
app.Logger.LogInformation("Selected application runtime {RuntimeVersion}; environment {EnvironmentName}.", Environment.Version, app.Environment.EnvironmentName);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
    if (PublicFileProtection.TryDeny(context)) return;
    // Legacy financial proofs must only be served through the authorized controller.
    if (context.Request.Path.StartsWithSegments("/uploads") &&
        (!context.Request.Path.StartsWithSegments("/uploads/profiles", out var profilePath)
        || profilePath.Value is not { } profileFile
        || profileFile.Length < 2
        || !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(profileFile).ToLowerInvariant())
        || profileFile[1..].Contains('/') || profileFile.Contains('\\') || profileFile.Contains("..", StringComparison.Ordinal)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
app.UseStatusCodePages("text/plain", "Request could not be completed (HTTP {0}).");
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health");

app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
// Explicit development initialization only; deployments manage schema separately.

// Explicit development initialization only; deployments manage schema separately.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<ApplicationDbContext>();

    if (app.Environment.IsDevelopment() &&
        builder.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
    {
        await dbContext.Database.MigrateAsync();
        await ServiceSeeder.SeedAsync(dbContext);
    }

    if (builder.Configuration.GetValue("Database:ValidateOnStartup", true))
    {
        await DatabaseStartupValidator.ValidateAsync(
            services.GetRequiredService<DbContextOptions<ApplicationDbContext>>(), app.Logger);
    }
}

// Separate development-only Client account initialization.
if (app.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("DevelopmentSeed:Enabled"))
{
    using var developmentSeedScope = app.Services.CreateScope();

    var seedServices = developmentSeedScope.ServiceProvider;

    await IdentitySeeder.SeedAsync(seedServices);
    await DevelopmentClientSeeder.SeedAsync(seedServices);
}


app.Run();

public partial class Program { }
