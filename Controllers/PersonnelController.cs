using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace AriesMagicAppointmentSystem.Controllers;

public sealed class PersonnelInviteInput
{
    [Required,EmailAddress,StringLength(254)] public string Email {get;set;}="";
    [Required,StringLength(100)] public string FullName {get;set;}="";
    [Required] public string Role {get;set;}="Staff";
}
public sealed record PersonnelRow(string Id,string FullName,string Email,string Role,string Status);
public sealed record PersonnelPage(List<PersonnelRow> Accounts,List<SystemActivity> Audit,bool Owner,bool CanInvite);
[Authorize(Roles="Owner,Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PersonnelController(UserManager<ApplicationUser> users,PersonnelProvisioning provisioning,
    ApplicationDbContext db,IEmailService email,IOptions<PersonnelOptions> settings) : Controller
{
    private async Task<ApplicationUser> Actor()=>await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!)??throw new UnauthorizedAccessException();
    public async Task<IActionResult> Index()
    {
        var actor=await Actor();var owner=await users.IsInRoleAsync(actor,"Owner");
        if(!owner&&!await provisioning.CanManageAsync(actor,"Staff"))return Forbid();
        var rows=new List<PersonnelRow>();
        foreach(var user in await users.Users.OrderBy(x=>x.FullName).ToListAsync()) {
            var roles=await users.GetRolesAsync(user);var invite=await provisioning.PendingAsync(user);
            var role=roles.Count==1?roles[0]:invite?.Role;
            if(role is not ("Staff" or "Admin")||!owner&&role!="Staff")continue;
            rows.Add(new(user.Id,user.FullName,user.Email??"",role,invite!=null?invite.State+ (invite.ExpiresUtc<=DateTime.UtcNow?" (expired)":""):user.IsActive?"Active":"Disabled"));
        }
        var audit=owner?await db.SystemActivities.AsNoTracking().Where(x=>x.AffectedRecordType=="ApplicationUser"&&x.Description.StartsWith("Personnel")).OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync():[];
        return View(new PersonnelPage(rows,audit,owner,true));
    }
    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("account")]
    public async Task<IActionResult> Invite(PersonnelInviteInput input)
    {
        if(!ModelState.IsValid){TempData["ErrorMessage"]="Enter a valid name, email and permitted role.";return RedirectToAction(nameof(Index));}
        try {
            // Validate trusted destination before issuing; never derive enrollment links from Host headers.
            _=Origin();
            var delivery=await provisioning.InviteAsync(await Actor(),input.Email,input.FullName,input.Role);
            await Deliver(delivery,input.Email);
        } catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Invitation refused. Check account state, permissions and the configured enrollment origin.";}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("account")]
    public async Task<IActionResult> Resend(string id)
    {
        try {_=Origin();var delivery=await provisioning.ReissueAsync(await Actor(),id);var user=await users.FindByIdAsync(id);await Deliver(delivery,user!.Email!);}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Invitation could not be reissued. Check pending state and configuration.";}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Manage(string id,string operation,string? role)
    {
        try {await provisioning.ManageAsync(await Actor(),id,operation,role);TempData["SuccessMessage"]="Personnel account updated. Previous sessions were revoked.";}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Account action refused. Refresh and check enrollment state.";}
        return RedirectToAction(nameof(Index));
    }
    private string Origin()
    {
        if(!Uri.TryCreate(settings.Value.PublicOrigin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||uri.AbsolutePath!="/"||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new InvalidOperationException();
        return uri.GetLeftPart(UriPartial.Authority);
    }
    private async Task Deliver(EnrollmentDelivery delivery,string recipient)
    {
        var link=Origin()+"/PersonnelEnrollment/Accept#"+delivery.UserId+":"+delivery.Secret;
        try {await email.SendEmailAsync(recipient,"Your Aries Magic personnel enrollment",$"<p>You have been invited to Aries Magic. Choose your own password using this single-use, expiring link:</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Complete enrollment</a></p><p>If unexpected, ignore this message.</p>");TempData["SuccessMessage"]="Enrollment submitted to the email provider. Delivery is not yet confirmed. Account remains inactive until enrollment completes.";}
        catch {TempData["ErrorMessage"]="Email delivery is unavailable. Account remains inactive. An authorized operator must rotate and deliver the invitation through the documented private process.";}
    }
}

public sealed class PersonnelEnrollmentInput
{
    [Required,StringLength(64)] public string UserId {get;set;}="";
    [Required,StringLength(64,MinimumLength=64)] public string Token {get;set;}="";
    [Required,DataType(DataType.Password),StringLength(128,MinimumLength=12)] public string Password {get;set;}="";
    [Required,DataType(DataType.Password),Compare(nameof(Password)),Display(Name="Confirm password")] public string ConfirmPassword {get;set;}="";
}
[AllowAnonymous,EnableRateLimiting("account"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PersonnelEnrollmentController(PersonnelProvisioning provisioning):Controller
{
    [HttpGet] public IActionResult Accept(){Response.Headers["Referrer-Policy"]="no-referrer";return View(new PersonnelEnrollmentInput());}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Accept(PersonnelEnrollmentInput input)
    {
        Response.Headers["Referrer-Policy"]="no-referrer";
        if(ModelState.IsValid) {
            var result=await provisioning.EnrollAsync(input.UserId,input.Token,input.Password);
            if(result.Succeeded){TempData["SuccessMessage"]="Enrollment complete. Sign in with your verified email and new password.";return RedirectToAction("Login","Account");}
            ModelState.AddModelError("","Enrollment refused. Check password requirements or request a new invitation.");
        }
        // Never echo password or enrollment token into an HTML response.
        ModelState.Remove(nameof(input.Password));ModelState.Remove(nameof(input.ConfirmPassword));ModelState.Remove(nameof(input.Token));
        input.Password="";input.ConfirmPassword="";input.Token="";
        return View(input);
    }
}
