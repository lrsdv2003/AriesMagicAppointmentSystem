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
        Booking booking, Payment payment, Service package, RefundRequest refund, BookingFinancialSummaryViewModel finance, Dictionary<string,RoleDashboardViewModel>? dashboards = null)
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
            ("Services","Details",displayPackage),
            ("Services","Create",new ServiceManageViewModel {Name="New package",Price=10000,DurationInHours=2}),
            ("Services","Edit",new ServiceManageViewModel { Id=displayPackage.Id, Name=displayPackage.Name, Price=displayPackage.Price, DurationInHours=displayPackage.DurationInHours,
                Inclusions=displayPackage.Inclusions.Select(i=>new ServiceInclusionInputViewModel { Id=i.Id, Name=i.Name, DeductionAmount=i.DeductionAmount, IsRemovable=i.IsRemovable }).ToList() }),
            ("Notifications","Index",notices)
        };
        var filterPages = new (string Controller,string Action,object Model)[] {
            ("Bookings","Index",new BookingManagementViewModel { AvailableServices = [package] }),
            ("Bookings","StaffIndex",new BookingManagementViewModel { AvailableServices = [package], BookingStatus = "AllRequests" }),
            ("Bookings","MyBookings",new[] {booking}),
            ("RescheduleRequests","Index",new RescheduleRequestIndexViewModel()),
            ("RescheduleRequests","Details",new RescheduleRequestDetailsViewModel {Request=new RescheduleRequest {Id=44,Booking=booking,RequestedDate=DateTime.Today.AddDays(5),RequestedStartTime=DateTime.Today.AddDays(5).AddHours(10),RequestedEndTime=DateTime.Today.AddDays(5).AddHours(12)},Availability=new() {IsAvailable=true}}),
            ("History","Index",new HistoryIndexViewModel { AvailableServices = [package], TotalPages = 2, TotalCount = 16, Bookings = [new HistoryRowViewModel { Id=1,BookingCode="BK-2026-001",ClientName="Fixture Client",PackageName="History Package",Venue="Fixture Venue",EventDate=DateTime.Today.AddDays(-2),StartTime=DateTime.Today.AddHours(10),EndTime=DateTime.Today.AddHours(12),PaymentStatus="Verified",BookingStatus="Completed" }] }),
            ("SystemActivity","Index",new SystemActivityIndexViewModel { Page = 1, PageSize = 20, TotalCount=21, TotalPages = 2, ActivityTypes=Enum.GetValues<SystemActivityType>().ToList(), Activities = [new SystemActivity {Type=SystemActivityType.PaymentVerified,PerformedByUserName="Test Owner",Description="Payment reviewed after proof comparison.",AffectedRecordType="Payment",AffectedRecordId="42",MetadataJson=System.Text.Json.JsonSerializer.Serialize(new { ActorRole="Owner" })}] }),
            ("TrashHistory","Index",new TrashHistoryIndexViewModel { TotalPages = 2, ArchivedPackages=[new Service {Id=55,Name="Archived Fixture",IsArchived=true}], Bookings = [new TrashHistoryRowViewModel {BookingCode="BK-2026-001",ClientName="Test Client",ArchiveDateRecorded=true,ArchivedAt=DateTime.UtcNow,ArchivedBy="Test Staff"}] }),
            ("Calendar","StaffIndex",new CalendarIndexViewModel { Bookings = Enumerable.Range(0,8).Select(i=>new Booking { Id = 990+i, Status = BookingStatus.Confirmed, EventDate = DateTime.Today.AddDays(i<6?2:1), StartTime = DateTime.Today.AddHours(10), EndTime = DateTime.Today.AddHours(12), PackageName = i%2==0?"Regression Package":"Other Package", PartyVenue = "Fixture Venue" }).ToList() }),
            ("History","Details",new HistoryDetailsViewModel {Booking=new Booking {Id=booking.Id,Client=booking.Client,Status=BookingStatus.Completed,EventType="Birthday",PartyVenue="Fixture Venue",PackageName=booking.PackageName,EventDate=booking.EventDate,StartTime=booking.StartTime,EndTime=booking.EndTime,FinalPrice=booking.FinalPrice,PaxCount=50},CompletedAt=DateTime.Today.AddDays(-2),BookingCode="BK-2026-001",AmountPaid=3000,RemainingBalance=7000,PaymentStatus="Verified",FinancialSummary=finance,Timeline=[new(){EventType="BookingCompleted",CreatedAt=DateTime.Today.AddDays(-2)},new(){EventType="PaymentVerified",Notes="Private financial marker",CreatedAt=DateTime.Today.AddDays(-3)}]}),
            ("Bookings","StaffDetails",booking),
            ("UserManagement","Index",Enumerable.Range(1,12).Select(i=>new ApplicationUser {Id="fixture-"+i,FullName="Test User "+i,Email="user"+i+"@example.test",EmailConfirmed=i%2==0,IsActive=i%3!=0}).ToArray()),
            ("UserManagement","Details",new ApplicationUser {Id="fixture-1",FullName="Test Staff",Email="staff@example.test",PhoneNumber="123456789",EmailConfirmed=true}),
            ("Calendar","Index",new CalendarIndexViewModel {Manage=new CalendarManageViewModel {MaxBookingsPerDay=3,BlockedDates=[new BlockedDate {Id=1,Date=DateTime.Today.AddDays(10),Reason="System maintenance"}]}}),
            ("TrashHistory","Details",new TrashHistoryDetailsViewModel {Trash=new TrashHistoryDetailViewModel {BookingCode="BK-2026-001",ClientName="Test Client",PackageName="Test Package",ReasonNotes="Request expired."}}),
            ("InternalProfile","Index",new InternalProfileViewModel {DisplayName="Test Internal User",Email="internal@example.test",Role="Staff",Status="Active",Verified=true,CreatedAt=DateTime.UtcNow.AddYears(-1),Personal=new() {FullName="Test Internal User",PhoneNumber="09123456789"},PasswordRules=["At least 6 characters."]}),
            ("Communications","Index",new CommunicationCenterViewModel { IsInternalUser = true })
        };
        pages = pages.Concat(filterPages).ToArray();
        if (dashboards != null)
            pages = pages.Concat(new[]{"Staff","Owner","Admin"}.Select(role => (Controller:"Dashboard",Action:role,Model:(object)dashboards[role]))).ToArray();
        var rendered = 0;
        foreach(var page in pages)
        {
            var defaultRole = page.Controller == "Dashboard" ? page.Action : page.Action == "Admin" || page.Controller == "Calendar" && page.Action == "Index" ? "Admin" : page.Action == "MyBookings" ? "Client" : page.Action is "StaffIndex" or "StaffDetails" || page.Controller == "RescheduleRequests" ? "Staff" : page.Controller is "UserManagement" or "SystemActivity" or "TrashHistory" ? "Admin" : "Owner";
            var roles = page.Controller == "History" ? new[] {"Owner","Staff"} : page.Controller == "InternalProfile" ? new[] { "Owner", "Staff", "Admin" } : page.Controller == "Services" && page.Action is "Index" or "Details" ? new[] { "Owner", "Staff", "Admin" } : page.Controller == "Communications" ? new[] { "Owner", "Admin" } : page.Controller == "Notifications" ? new[] { "Owner", "Client" } : new[] { defaultRole };
            foreach (var role in roles)
            {
            foreach(var state in page.Controller == "Dashboard" ? new[]{"populated","empty","error"} : page.Controller == "History" && page.Action == "Index" ? new[]{"populated","empty"} : new[]{"populated"})
            {
            object fixtureModel = page.Model;
            if(page.Controller=="History" && page.Action=="Index" && state=="empty") fixtureModel=new HistoryIndexViewModel {Filters=new() {Search="no matches"}};
            if(fixtureModel is InternalProfileViewModel account) account.Role=role;
            if(page.Controller == "Dashboard" && state != "populated") {
                var original=(RoleDashboardViewModel)page.Model;
                fixtureModel = state == "error" && role == "Owner" && dashboards != null
                    ? dashboards["OwnerFailure"]
                    : new RoleDashboardViewModel {RoleName=role,DisplayName="Fixture "+role,QuickActions=original.QuickActions,
                        Metrics=state=="empty" ? original.Metrics.Select(m=>new DashboardMetric(m.Label,m.Value.StartsWith("PHP")?"PHP 0.00":"0",m.Context)).ToList() : new(),
                        UnavailableSections=state=="error" ? new() {"requests","schedule","users","system overview","activity","messages"} : new()};
            }
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
            var data = new ViewDataDictionary(provider.GetRequiredService<IModelMetadataProvider>(),new ModelStateDictionary()) {Model=fixtureModel};
            data["FinancialSummary"]=finance; data["ExpectedReceiver"]="Aries Magic"; data["Filter"]="awaiting";
            data["PackageColors"] = new Dictionary<string,string> { ["Regression Package"] = "pink", ["Other Package"] = "blue" };
            data["BlockedDates"] = new[] { new { date=DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"), reason="Fixture blocked date" } };
            data["UserRoles"] = Enumerable.Range(1,12).ToDictionary(i=>"fixture-"+i,i=>new[]{"Client","Staff","Owner","Admin"}[i%4]);
            data["Roles"]="Staff";data["CanManage"]=true;
            using var writer = new StringWriter();
            var viewContext = new ViewContext(context,result.View,data,new TempDataDictionary(http,new EmptyTempData()),writer,new HtmlHelperOptions());
            await result.View.RenderAsync(viewContext);
            await File.WriteAllTextAsync(Path.Combine(output,page.Controller+"-"+page.Action+(role != defaultRole ? "-"+role : "")+(state=="populated" ? "" : "-"+state)+".html"),writer.ToString());
            rendered++;
            }
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
            (values.Count>0?"?"+string.Join("&",values.Select(v=>Uri.EscapeDataString(v.Key)+"="+Uri.EscapeDataString(v.Value?.ToString()??""))):"") + (string.IsNullOrEmpty(action.Fragment) ? "" : "#" + action.Fragment);
    }
    public string? Content(string? path) => path?.Replace("~/","/");
    public bool IsLocalUrl(string? url) => url?.StartsWith("/") == true;
    public string? Link(string? routeName,object? values) => RouteUrl(new UrlRouteContext { Values=values });
    public string? RouteUrl(UrlRouteContext route) {
        var values=new RouteValueDictionary(route.Values);
        return "/"+values.GetValueOrDefault("controller")+"/"+values.GetValueOrDefault("action");
    }
}
