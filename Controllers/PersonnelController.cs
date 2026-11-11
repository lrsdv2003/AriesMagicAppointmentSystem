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
public sealed record PersonnelPage(List<PersonnelRow> Accounts,List<SystemActivity> Audit,bool Owner,bool CanInvite,string? Search=null,string? RoleFilter=null,string? StatusFilter=null);
public sealed class StaffProfileInput { [Required] public string Id {get;set;}=""; [Required,StringLength(100)] public string FullName {get;set;}=""; [Phone,StringLength(30)] public string? PhoneNumber {get;set;} }
[Authorize(Roles="Owner,Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PersonnelController(UserManager<ApplicationUser> users,PersonnelProvisioning provisioning,
    ApplicationDbContext db,IEmailService email,IOptions<PersonnelOptions> settings) : Controller
{
    private async Task<ApplicationUser> Actor()=>await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!)??throw new UnauthorizedAccessException();
    public async Task<IActionResult> Index(string? search=null,string? roleFilter=null,string? statusFilter=null)
    {
        var actor=await Actor();var owner=await users.IsInRoleAsync(actor,"Owner");
        if(!owner&&!await provisioning.CanManageAsync(actor,"Staff"))return Forbid();
        search=search?.Trim();if(search?.Length>100)search=search[..100];
        var rows=new List<PersonnelRow>();
        foreach(var user in await users.Users.OrderBy(x=>x.FullName).ToListAsync()) {
            var roles=await users.GetRolesAsync(user);var invite=await provisioning.PendingAsync(user);
            var role=roles.Contains("Owner")?"Owner":roles.Count==1?roles[0]:roles.Count==0?invite?.Role??"Unassigned":"Multiple (read-only)";
            if(!owner&&role!="Staff")continue;
            var status=invite!=null?invite.State+(invite.ExpiresUtc<=DateTime.UtcNow?" (expired)":""):!user.IsActive?"Disabled":user.LockoutEnd>DateTimeOffset.UtcNow?"Locked":!user.EmailConfirmed?"Unverified":"Active";
            if(!string.IsNullOrWhiteSpace(search)&&!user.FullName.Contains(search,StringComparison.OrdinalIgnoreCase)&&!(user.Email?.Contains(search,StringComparison.OrdinalIgnoreCase)??false))continue;
            if(!string.IsNullOrEmpty(roleFilter)&&role!=roleFilter)continue;
            if(!string.IsNullOrEmpty(statusFilter)&&!status.StartsWith(statusFilter,StringComparison.Ordinal))continue;
            rows.Add(new(user.Id,user.FullName,user.Email??"",role,status));
        }
        var audit=owner?await db.SystemActivities.AsNoTracking().Where(x=>x.AffectedRecordType=="ApplicationUser"&&x.Description.StartsWith("Personnel")).OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync():[];
        return View(new PersonnelPage(rows,audit,owner,true,search,roleFilter,statusFilter));
    }
    [HttpGet,Authorize(Roles="Owner")]
    public async Task<IActionResult> EditStaff(string id)
    {
        var user=await users.FindByIdAsync(id);if(user==null)return NotFound();
        if(!await provisioning.CanEditStaffProfileAsync(await Actor(),user))return Forbid();
        return View(new StaffProfileInput {Id=user.Id,FullName=user.FullName,PhoneNumber=user.PhoneNumber});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Owner")]
    public async Task<IActionResult> EditStaff(StaffProfileInput input)
    {
        if(!ModelState.IsValid)return View(input);
        try {await provisioning.UpdateStaffProfileAsync(await Actor(),input.Id,input.FullName,input.PhoneNumber);}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException){ModelState.AddModelError("","Staff profile could not be updated. Refresh and try again.");return View(input);}
        TempData["SuccessMessage"]="Staff profile updated. Login email and password were preserved.";
        return RedirectToAction(nameof(Index));
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
        catch(PersonnelActionException ex){TempData["ErrorMessage"]=ex.Reason switch {
            "AlreadyRegistered"=>"This email is already registered. Use Resend invitation for a pending account, or invite a different email.",
            "RoleNotConfigured"=>"Personnel roles are not initialized. Ask the system operator to verify the Staff role.",
            "InvalidOrigin"=>"The secure enrollment URL is not configured correctly. Contact the system operator.",
            _=>"Invitation could not be created. Refresh and check account status."
        };}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Invitation could not be created. Check whether the account already exists or the personnel setup is complete.";}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("account")]
    public async Task<IActionResult> Resend(string id)
    {
        try {_=Origin();var delivery=await provisioning.ReissueAsync(await Actor(),id);var user=await users.FindByIdAsync(id);await Deliver(delivery,user!.Email!);}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(PersonnelActionException ex){TempData["ErrorMessage"]=ex.Reason=="InvalidOrigin"?"The secure enrollment URL is not configured correctly. Contact the system operator.":"Invitation cannot be reissued for this account.";}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Invitation could not be reissued. Check pending state and configuration.";}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Manage(string id,string operation,string? role)
    {
        try {await provisioning.ManageAsync(await Actor(),id,operation,role);TempData["SuccessMessage"]="Personnel account updated. Previous sessions were revoked.";}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(PersonnelActionException ex){TempData["ErrorMessage"]=ex.Reason switch {
            "SelfManagement"=>"You cannot change your own personnel access from this page.",
            "NotManageable"=>"This account has no active personnel role or pending invitation. No changes were made.",
            _=>"Account action could not be completed. Refresh and try again."
        };}
        catch(InvalidOperationException){TempData["ErrorMessage"]="Account action refused. Refresh and check enrollment state.";}
        return RedirectToAction(nameof(Index));
    }
    private string Origin()
    {
        if(!Uri.TryCreate(settings.Value.PublicOrigin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||uri.AbsolutePath!="/"||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new PersonnelActionException("InvalidOrigin");
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
