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
        var displayPackage = new Service { Id = package.Id, Name = package.Name, Price = package.Price, DurationInHours = package.DurationInHours,
            Inclusions = Enumerable.Range(1,5).Select(i => new ServiceInclusion { Id = i, Name = "Inclusion " + i, DeductionAmount = i * 100, IsRemovable = i != 1 }).ToList() };
        var notices = new[] {
            new Notification { Id=1, Title="Payment Verification Required", Message="Payment proof for BK-2026-001 is ready for review.", Link="/Payments/Verify/1", CreatedAt=DateTime.Now.AddMinutes(-5) },
            new Notification { Id=2, Title="New Refund Request", Message="Refund request RF-0002 needs review.", Link="/Payments/RefundReview/2", CreatedAt=DateTime.Now.AddHours(-2) },
            new Notification { Id=3, Title="Booking Confirmed", Message="Booking BK-2026-003 is confirmed.", Link="/Bookings/Details/3", IsRead=true, CreatedAt=DateTime.Now.AddDays(-1) },
            new Notification { Id=4, Title="System Update", Message="Your preferences were saved.", IsRead=true, CreatedAt=DateTime.Now.AddDays(-3) }
        };
        var pages = new (string Controller,string Action,object Model)[] {
            ("Payments","PendingVerification",new[] {payment}),
            ("Payments","Verify",payment),
            ("Payments","RefundRequests",new[] {refund}),
            ("Payments","RefundReview",refund),
            ("Reports","Index",reports),
            ("Services","Index",new[] {displayPackage}),
            ("Services","Edit",new ServiceManageViewModel { Id=displayPackage.Id, Name=displayPackage.Name, Price=displayPackage.Price, DurationInHours=displayPackage.DurationInHours,
                Inclusions=displayPackage.Inclusions.Select(i=>new ServiceInclusionInputViewModel { Id=i.Id, Name=i.Name, DeductionAmount=i.DeductionAmount, IsRemovable=i.IsRemovable }).ToList() }),
            ("Notifications","Index",notices)
        };
        var filterPages = new (string Controller,string Action,object Model)[] {
            ("Bookings","Index",new BookingManagementViewModel { AvailableServices = [package] }),
            ("Bookings","StaffIndex",new BookingManagementViewModel { AvailableServices = [package], BookingStatus = "AllRequests" }),
            ("Bookings","MyBookings",new[] {booking}),
            ("RescheduleRequests","Index",new RescheduleRequestIndexViewModel()),
            ("History","Index",new HistoryIndexViewModel { AvailableServices = [package], TotalPages = 2, TotalCount = 16, Bookings = [new HistoryRowViewModel { BookingCode = "BK-2026-001" }] }),
            ("SystemActivity","Index",new SystemActivityIndexViewModel { Page = 1, PageSize = 20, TotalPages = 2, Activities = [new SystemActivity()] }),
            ("TrashHistory","Index",new TrashHistoryIndexViewModel { TotalPages = 2, Bookings = [new TrashHistoryRowViewModel()] }),
            ("Calendar","StaffIndex",new CalendarIndexViewModel { Bookings = Enumerable.Range(0,8).Select(i=>new Booking { Id = 990+i, Status = BookingStatus.Confirmed, EventDate = DateTime.Today.AddDays(i<6?2:1), StartTime = DateTime.Today.AddHours(10), EndTime = DateTime.Today.AddHours(12), PackageName = i%2==0?"Regression Package":"Other Package", PartyVenue = "Fixture Venue" }).ToList() }),
            ("UserManagement","Index",Array.Empty<ApplicationUser>()),
            ("Communications","Index",new CommunicationCenterViewModel { IsInternalUser = true })
        };
        pages = pages.Concat(filterPages).ToArray();
        var rendered = 0;
        foreach(var page in pages)
        {
            var defaultRole = page.Action == "MyBookings" ? "Client" : page.Action == "StaffIndex" || page.Controller == "RescheduleRequests" ? "Staff" : page.Controller is "UserManagement" or "SystemActivity" or "TrashHistory" ? "Admin" : "Owner";
            var roles = page.Controller == "Services" && page.Action == "Index" ? new[] { "Owner", "Staff" } : page.Controller == "Notifications" ? new[] { "Owner", "Client" } : new[] { defaultRole };
            foreach (var role in roles)
            {
            var http = new DefaultHttpContext {
                RequestServices = provider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.Role,role),new Claim(ClaimTypes.Name,"Regression Owner")}, "Fixture"))
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
            data["PackageColors"] = new Dictionary<string,string> { ["Regression Package"] = "pink", ["Other Package"] = "blue" };
            data["BlockedDates"] = new[] { new { date=DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"), reason="Fixture blocked date" } };
            using var writer = new StringWriter();
            var viewContext = new ViewContext(context,result.View,data,new TempDataDictionary(http,new EmptyTempData()),writer,new HtmlHelperOptions());
            await result.View.RenderAsync(viewContext);
            await File.WriteAllTextAsync(Path.Combine(output,page.Controller+"-"+page.Action+(role != defaultRole ? "-"+role : "")+".html"),writer.ToString());
            rendered++;
            }
        }
        Console.WriteLine($"Rendered {rendered} role fixture pages to " + output);
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
