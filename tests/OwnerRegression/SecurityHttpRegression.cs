using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

static class SecurityHttpRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        // Fixture authentication exists only in this executable, never in the application.
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PaymentsController).Assembly).AddControllersAsServices();
        builder.Services.AddSingleton(new PaymentsController(db, null!, null!, null!, null!, null!, new SystemActivityService(db), new ConfigurationBuilder().Build(), new PaymentFinancialService(db)));
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            foreach (var role in new[] { "Client", "Staff", "Admin", "Owner" })
            {
                client.DefaultRequestHeaders.Remove("X-Fixture-Role");
                client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
                foreach (var path in new[] { "/Payments/PaymentProof/0", "/Payments/RefundProof/0" })
                {
                    using var response = await client.GetAsync(path);
                    check(response.StatusCode == (role == "Owner" || role == "Client" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden), role + " HTTP proof authorization " + path);
                }
                foreach (var action in new[] { "Verify", "Reject", "ApproveRefund", "MarkAsRefunded" })
                {
                    using var response = await client.PostAsync("/Payments/" + action + "/0", new FormUrlEncodedContent([]));
                    check(response.StatusCode == (role == "Owner" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden), role + " HTTP financial POST authorization/CSRF " + action);
                }
            }
        }
        finally { await app.StopAsync(); }
    }
}

sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Fixture-Role"].ToString();
        if (role is not ("Client" or "Staff" or "Admin" or "Owner")) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "http-fixture"), new Claim(ClaimTypes.Role, role)], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
