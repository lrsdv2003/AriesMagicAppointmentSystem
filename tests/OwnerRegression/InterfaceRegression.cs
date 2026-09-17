using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
static class InterfaceRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var owner = new ApplicationUser { Id="interface-owner", UserName="interface-owner", FullName="Fixture Owner" };
        var other = new ApplicationUser { Id="interface-other", UserName="interface-other", FullName="Fixture Other" };
        db.Users.AddRange(owner,other);
        var mine = new Notification { UserId=owner.Id, Title="Payment Verification", Message="Fixture", Link="/Payments/Verify/1" };
        var theirs = new Notification { UserId=other.Id, Title="Refund Request", Message="Fixture", Link="/Payments/RefundReview/2" };
        db.Notifications.AddRange(mine,theirs); await db.SaveChangesAsync();
        var http = new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.NameIdentifier,owner.Id),new Claim(ClaimTypes.Role,"Owner")},"Fixture")) };
        var notifications = new NotificationsController(db) { ControllerContext=new ControllerContext {HttpContext=http} };
        notifications.Url = new SnapshotUrlHelper(new ActionContext { HttpContext=http, RouteData=new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor=new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor() });
        check(await notifications.Open(theirs.Id) is NotFoundResult,"Cannot open another user's notification");
        var open = await notifications.Open(mine.Id);
        check(open is RedirectResult redirect && redirect.Url==mine.Link && mine.IsRead,"Open notification marks read and redirects to record");
        var firstRead = mine.ReadAt;
        await notifications.Open(mine.Id);
        check(mine.ReadAt==firstRead,"Opening again preserves original read timestamp");
        mine.IsRead=false;mine.ReadAt=null;await db.SaveChangesAsync();
        await notifications.MarkAllRead();db.ChangeTracker.Clear();
        check(await db.Notifications.Where(n=>n.UserId==owner.Id).AllAsync(n=>n.IsRead),"Mark all read updates own notifications");
        check(!await db.Notifications.Where(n=>n.Id==theirs.Id).Select(n=>n.IsRead).SingleAsync(),"Mark all read leaves other users unchanged");
        check(typeof(NotificationsController).GetMethod("MarkAllRead")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),"Mark all read requires anti-forgery");
        var service = new Service {Name="Editable Fixture",Price=500,DurationInHours=2,IsArchived=true};
        db.Services.Add(service);await db.SaveChangesAsync();
        var controller=new ServicesController(db,new SystemActivityService(db)) { ControllerContext=new ControllerContext {HttpContext=http} };
        var empty=new ServiceManageViewModel {Id=service.Id,Name=service.Name,Price=500,DurationInHours=2};
        check(await controller.Edit(service.Id,empty) is ViewResult && controller.ModelState.ContainsKey("Inclusions"),"Empty inclusions rejected with field error");
        controller.ModelState.Clear();
        var edited=new ServiceManageViewModel {Id=service.Id,Name=service.Name,Price=750,DurationInHours=3,Inclusions=[new(){Name="First",DeductionAmount=0,IsRemovable=false},new(){Name="Third retained",DeductionAmount=50,IsRemovable=true}]};
        check(await controller.Edit(service.Id,edited) is RedirectToActionResult,"Valid package changes save");
        db.ChangeTracker.Clear();
        var saved=await db.Services.Include(s=>s.Inclusions).SingleAsync(s=>s.Id==service.Id);
        check(saved.IsArchived && saved.Price==750 && saved.Inclusions.Count==2 && saved.Inclusions.Any(i=>i.Name=="Third retained"),"Saving preserves availability and all remaining inclusions");
        var invalid=new ServiceManageViewModel {Name="",Price=-1,DurationInHours=0};
        var errors=new List<ValidationResult>();
        check(!Validator.TryValidateObject(invalid,new ValidationContext(invalid),errors,true) && errors.Count>=4,"Name, price, duration and required inclusions validate");
        check(NotificationPresentation.From(theirs).Label=="REFUND","Refund notification category is explicit");
    }
}
