// Local-only visual fixture: real Home controller/views, source-seeded synthetic catalog.
// No production host startup, secrets, workers, mail, uploads or financial actions.
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var root = Directory.GetCurrentDirectory();
var databaseName = "AriesClientVisual_" + Guid.NewGuid().ToString("N");
var connection = @"Server=(localdb)\MSSQLLocalDB;Database=" + databaseName + ";Trusted_Connection=True;TrustServerCertificate=True";
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
    ApplicationName = typeof(HomeController).Assembly.GetName().Name,
    ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot"), EnvironmentName = "ClientVisualFixture"
});
builder.Logging.ClearProviders();
builder.WebHost.UseStaticWebAssets(); // Test-only source-asset/bundle discovery.
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
await using var app = builder.Build();
app.Urls.Add("http://127.0.0.1:5295");
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store";
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) {
        context.Response.StatusCode = 405; return;
    }
    await next();
});
app.UseStaticFiles();
app.Use(async (context, next) => {
    // Generate real application URLs but execute only public Home views in this fixture.
    if (context.Request.Path != "/" && !context.Request.Path.StartsWithSegments("/Home")) {
        context.Response.StatusCode = 405; return;
    }
    await next();
});
app.MapControllerRoute("visual-home", "{controller=Home}/{action=Index}/{id?}");
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
try {
    await db.Database.EnsureCreatedAsync();
    await ServiceSeeder.SeedAsync(db);
    // Private test evidence, never a public diagnostics route or production catalog claim.
    var catalog = await db.Services.AsNoTracking().Where(s => !s.IsArchived).OrderBy(s => s.Price)
        .Select(s => new { s.Id, s.Name, s.Price, s.DurationInHours, s.Description,
            Inclusions = s.Inclusions.OrderBy(i => i.Id).Select(i => new { i.Id, i.Name }).ToList() }).ToListAsync();
    await File.WriteAllTextAsync(Path.Combine(root, ".tmp-owner-build", "client-visual-catalog.json"),
        System.Text.Json.JsonSerializer.Serialize(new { Source="Isolated SourceSeeder catalog; not production data", Packages=catalog }));
    var stopFile = Path.Combine(root, ".tmp-owner-build", $"client-visual-{Environment.ProcessId}.stop");
    _ = Task.Run(async () => {
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested) {
            if (File.Exists(stopFile)) { app.Lifetime.StopApplication(); break; }
            await Task.Delay(150);
        }
    });
    Console.WriteLine($"LOCAL VISUAL FIXTURE http://127.0.0.1:5295 — source-seeded test catalog; PID {Environment.ProcessId}. Stop marker: {stopFile}");
    await app.RunAsync();
}
finally { await db.Database.EnsureDeletedAsync(); }
