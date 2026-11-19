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
using AriesMagicAppointmentSystem.ViewModels;
namespace AriesMagicAppointmentSystem.Controllers;

public sealed class PersonnelInviteInput
{
    [Required,EmailAddress,StringLength(254)] public string Email {get;set;}="";
    [Required,StringLength(100)] public string FullName {get;set;}="";
    [Required] public string Role {get;set;}="Staff";
}
public sealed record PersonnelRow(string Id,string FullName,string Email,string Role,string Status,DateTime? InvitedAtUtc=null);
public sealed record PersonnelPage(List<PersonnelRow> Accounts,string? Search=null,string? RoleFilter=null)
{
    public QueuePaginationViewModel Pagination {get;init;} = new(0,1,5,"Index",new());
    public bool HasEligibleAccounts {get;init;}
    public bool HasAccountsInSelectedRole {get;init;}
}
public sealed record StaffAccessRow(string Id,string FullName,string Email,string Status,bool CanResend,bool CanRevoke,bool CanDeactivate,bool CanReactivate);
public sealed record StaffAccessPage(List<StaffAccessRow> Accounts);
[Authorize(Roles="Owner"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PersonnelController(UserManager<ApplicationUser> users,PersonnelProvisioning provisioning,
    ApplicationDbContext db,IEmailService email,IOptions<PersonnelOptions> settings) : Controller
{
    private async Task<ApplicationUser> Actor()=>await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!)??throw new UnauthorizedAccessException();
    // The main table intentionally shows *only* verified, active accounts of the
    // three supported Identity roles; lifecycle accounts are managed separately.
    [HttpGet]
    public async Task<IActionResult> Index(string? search=null,string? roleFilter=null,int page=1)
    {
        var actor=await Actor();
        if(!await provisioning.CanManageAsync(actor,"Staff"))return Forbid();
        search=search?.Trim();
        if(search?.Length>100)search=search[..100];
        roleFilter=ApplicationRoles.IsActive(roleFilter??"")?roleFilter:"";
        var eligible=new List<(ApplicationUser User,string Role,string Status)>();
        foreach(var user in await users.Users.AsNoTracking()
            .Where(x=>x.IsActive&&x.EmailConfirmed)
            .OrderBy(x=>x.FullName).ThenBy(x=>x.Email).ThenBy(x=>x.Id).ToListAsync())
        {
            var roles=await users.GetRolesAsync(user);
            if(roles.Count!=1 || !ApplicationRoles.IsActive(roles[0]))continue;
            if(await provisioning.PendingAsync(user)!=null)continue;
            var status=user.LockoutEnd>DateTimeOffset.UtcNow?"Locked":"Active";
            eligible.Add((user,roles[0],status));
        }
        var scoped=eligible.Where(x=>string.IsNullOrEmpty(roleFilter)||x.Role==roleFilter).ToList();
        var filtered=scoped.Where(x=>string.IsNullOrWhiteSpace(search)
            || x.User.FullName.Contains(search,StringComparison.OrdinalIgnoreCase)
            || (x.User.Email?.Contains(search,StringComparison.OrdinalIgnoreCase)??false))
            .OrderBy(x=>x.User.FullName,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.User.Email,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.User.Id,StringComparer.Ordinal)
            .ToList();
        var filters=new Dictionary<string,string> {
            ["search"]=search??"",["roleFilter"]=roleFilter??""
        };
        var pagination=new QueuePaginationViewModel(filtered.Count,page,5,nameof(Index),filters);
        var visible=filtered.Skip(pagination.Skip).Take(pagination.PageSize).ToList();
        var ids=visible.Select(x=>x.User.Id).ToList();
        var invitedDates=await db.SystemActivities.AsNoTracking()
            .Where(x=>x.Type==SystemActivityType.UserCreated
                && x.AffectedRecordType=="ApplicationUser"
                && x.AffectedRecordId!=null && ids.Contains(x.AffectedRecordId)
                && x.Description=="Personnel enrollment issued; account inactive and role unassigned.")
            .GroupBy(x=>x.AffectedRecordId!)
            .Select(g=>new{Id=g.Key,IssuedAt=g.Min(x=>x.CreatedAt)})
            .ToDictionaryAsync(x=>x.Id,x=>(DateTime?)x.IssuedAt);
        var rows=visible.Select(x=>new PersonnelRow(x.User.Id,x.User.FullName,x.User.Email??"",
            x.Role,x.Status,invitedDates.GetValueOrDefault(x.User.Id))).ToList();
        // Deliberately do not query the Account Management Audit panel; the
        // security audit records and shared activity service are untouched.
        return View(new PersonnelPage(rows,search,roleFilter) {
            Pagination=pagination,HasEligibleAccounts=eligible.Count>0,
            HasAccountsInSelectedRole=scoped.Count>0
        });
    }

    // Separate authorized lifecycle workflow, not part of the active Personnel table.
    // Keeps previously supported Staff deactivation, reactivation, session revocation,
    // and invitation reissue/revocation accessible without exposing retired roles.
    [HttpGet]
    public async Task<IActionResult> Access()
    {
        var actor=await Actor();
        if(!await provisioning.CanManageAsync(actor,"Staff"))return Forbid();
        var items=new List<StaffAccessRow>();
        foreach(var user in await users.Users.AsNoTracking()
            .OrderBy(x=>x.FullName).ThenBy(x=>x.Id).ToListAsync())
        {
            var roles=await users.GetRolesAsync(user);
            var pending=await provisioning.PendingAsync(user);
            var enrolled=roles.Count==1&&roles[0]=="Staff"&&user.EmailConfirmed;
            var pendingStaff=roles.Count==0&&!user.IsActive&&pending?.Role=="Staff"&&pending.State=="Pending";
            if(!enrolled&&!pendingStaff)continue;
            var status=pendingStaff
                ? pending!.ExpiresUtc<=DateTime.UtcNow?"Pending (expired)":"Pending enrollment"
                : !user.IsActive?"Disabled"
                : user.LockoutEnd>DateTimeOffset.UtcNow?"Locked":"Active";
            items.Add(new StaffAccessRow(user.Id,user.FullName,user.Email??"",status,
                pendingStaff,pendingStaff,enrolled&&user.IsActive,enrolled&&!user.IsActive));
        }
        return View(new StaffAccessPage(items));
    }

    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("account")]
    public async Task<IActionResult> Invite(PersonnelInviteInput input)
    {
        if(input.Role!="Staff")return Forbid();
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
        return RedirectToAction(nameof(Access));
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
        return RedirectToAction(nameof(Access));
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
