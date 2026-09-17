using System.Security.Claims;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class SnapshotRenderer
{
    public static async Task RenderAsync(DbContextOptions<ApplicationDbContext> options, ReportDashboardViewModel reports,
        Booking booking, Payment payment, Service package, RefundRequest refund, BookingFinancialSummaryViewModel finance)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(PaymentsController).Assembly.GetName().Name,
            ContentRootPath = Directory.GetCurrentDirectory(),
            WebRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
        });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PaymentsController).Assembly);
        builder.Services.AddScoped(_ => new ApplicationDbContext(options));
        builder.Services.AddSingleton<IUrlHelperFactory, SnapshotUrls>();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var output = Path.Combine(Path.GetTempPath(), "AriesOwnerSnapshots");
        Directory.CreateDirectory(output);
        var pages = new (string Controller,string Action,object Model)[] {
            ("Payments","PendingVerification",new[] {payment}),
            ("Payments","Verify",payment),
            ("Payments","RefundRequests",new[] {refund}),
            ("Payments","RefundReview",refund),
            ("Reports","Index",reports),
            ("Services","Index",new[] {package})
        };
        foreach(var page in pages)
        {
            var http = new DefaultHttpContext {
                RequestServices = provider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.Role,"Owner"),new Claim(ClaimTypes.Name,"Regression Owner")}, "Fixture"))
            };
            http.Request.Scheme = "http"; http.Request.Host = new HostString("owner.test");
            var route = new RouteData();
            route.Values["controller"] = page.Controller; route.Values["action"] = page.Action;
            var context = new ActionContext(http,route,new ActionDescriptor());
            var engine = provider.GetRequiredService<IRazorViewEngine>();
            var result = engine.GetView(null, "/Views/"+page.Controller+"/"+page.Action+".cshtml",true);
            if (!result.Success) throw new Exception("Could not find snapshot view");
            var data = new ViewDataDictionary(provider.GetRequiredService<IModelMetadataProvider>(),new ModelStateDictionary()) {Model=page.Model};
            data["FinancialSummary"]=finance; data["ExpectedReceiver"]="Aries Magic"; data["Filter"]="awaiting";
            using var writer = new StringWriter();
            var viewContext = new ViewContext(context,result.View,data,new TempDataDictionary(http,new EmptyTempData()),writer,new HtmlHelperOptions());
            await result.View.RenderAsync(viewContext);
            await File.WriteAllTextAsync(Path.Combine(output,page.Controller+"-"+page.Action+".html"),writer.ToString());
        }
        Console.WriteLine("Rendered 6 Owner fixture pages to " + output);
    }
}
sealed class SnapshotUrls : IUrlHelperFactory
{
    public IUrlHelper GetUrlHelper(ActionContext context) => new SnapshotUrlHelper(context);
}
sealed class SnapshotUrlHelper(ActionContext context) : IUrlHelper
{
    public ActionContext ActionContext => context;
    public string? Action(UrlActionContext action) {
        var values=new RouteValueDictionary(action.Values);
        return "/" + (action.Controller ?? context.RouteData.Values["controller"]) + "/" + (action.Action ?? context.RouteData.Values["action"]) +
            (values.Count>0?"?"+string.Join("&",values.Select(v=>Uri.EscapeDataString(v.Key)+"="+Uri.EscapeDataString(v.Value?.ToString()??""))):"");
    }
    public string? Content(string? path) => path?.Replace("~/","/");
    public bool IsLocalUrl(string? url) => url?.StartsWith("/") == true;
    public string? Link(string? routeName,object? values) => RouteUrl(new UrlRouteContext { Values=values });
    public string? RouteUrl(UrlRouteContext route) {
        var values=new RouteValueDictionary(route.Values);
        return "/"+values.GetValueOrDefault("controller")+"/"+values.GetValueOrDefault("action");
    }
}
