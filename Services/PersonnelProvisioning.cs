using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AriesMagicAppointmentSystem.Services;

public sealed class PersonnelOptions
{
    public bool AdminMayManageStaff { get; set; }
    public string? PublicOrigin { get; set; }
    public int InvitationHours { get; set; } = 24;
}
public sealed record PersonnelInvitation(string Role,string Digest,DateTime ExpiresUtc,string State,string Issuer);
public sealed record EnrollmentDelivery(string UserId,string Secret);
public sealed class PersonnelProvisioning(ApplicationDbContext db,UserManager<ApplicationUser> users,RoleManager<IdentityRole> roleManager,
    ISystemActivityService audit,IOptions<PersonnelOptions> configuration)
{
    public const string Provider="AriesPersonnel";
    public const string TokenName="Enrollment";
    private PersonnelOptions Options=>configuration.Value;
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(IdentityResult result){if(!result.Succeeded)throw new InvalidOperationException("Identity operation refused. Check account state and password policy.");}
    private async Task LockAsync()=>await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sp_getapplock @Resource=N'AriesPersonnelProvisioning',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000; IF @r<0 THROW 50000,'Personnel operation busy',1;");
    public async Task<PersonnelInvitation?> PendingAsync(ApplicationUser user)
    {
        var value=await db.UserTokens.AsNoTracking().Where(x=>x.UserId==user.Id&&x.LoginProvider==Provider&&x.Name==TokenName).Select(x=>x.Value).SingleOrDefaultAsync();
        return value==null?null:JsonSerializer.Deserialize<PersonnelInvitation>(value);
    }
    public async Task<bool> CanManageAsync(ApplicationUser actor,string role)
    {
        await db.Entry(actor).ReloadAsync();
        return actor.IsActive&&actor.EmailConfirmed&&
            (await users.IsInRoleAsync(actor,"Owner")&&role is "Admin" or "Staff" ||
             Options.AdminMayManageStaff&&role=="Staff"&&await users.IsInRoleAsync(actor,"Admin")&&!await users.IsInRoleAsync(actor,"Staff"));
    }
    public async Task<EnrollmentDelivery> InviteAsync(ApplicationUser? actor,string email,string name,string role,bool bootstrap=false)
    {
        email=email.Trim();name=name.Trim();
        if(!new EmailAddressAttribute().IsValid(email)||email.Length>254||name.Length is <1 or >100)throw new InvalidOperationException("Valid email and name required.");
        await using var tx=await db.Database.BeginTransactionAsync();await LockAsync();
        if(bootstrap) {
            // No HTTP caller may select this mode. A pending Owner reserves the bootstrap as well.
            if(actor!=null||role!="Owner"||await users.Users.AnyAsync()||await db.Bookings.AnyAsync()||await db.Payments.AnyAsync())throw new InvalidOperationException("Owner bootstrap requires a fresh, empty database.");
        } else if(actor==null||!await CanManageAsync(actor,role))throw new UnauthorizedAccessException();
        if(await users.FindByEmailAsync(email)!=null)throw new InvalidOperationException("Email already registered; existing accounts cannot be promoted through enrollment.");
        if(!await roleManager.RoleExistsAsync(role))throw new InvalidOperationException("Initialize Identity roles using the operator tool first.");
        var user=new ApplicationUser {UserName=email,Email=email,FullName=name,EmailConfirmed=false,IsActive=false};
        Require(await users.CreateAsync(user));
        var delivery=await IssueAsync(user,role,actor?.Id??"operator-bootstrap");
        await audit.LogAsync(SystemActivityType.UserCreated,"Personnel enrollment issued; account inactive and role unassigned.",actor?.Id??"operator-bootstrap",affectedRecordId:user.Id,affectedRecordType:"ApplicationUser");
        await tx.CommitAsync();return delivery;
    }
    private async Task<EnrollmentDelivery> IssueAsync(ApplicationUser user,string role,string issuer)
    {
        var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var state=new PersonnelInvitation(role,Hash(secret),DateTime.UtcNow.AddHours(Options.InvitationHours),"Pending",issuer);
        Require(await users.SetAuthenticationTokenAsync(user,Provider,TokenName,JsonSerializer.Serialize(state)));
        Require(await users.UpdateSecurityStampAsync(user));return new(user.Id,secret);
    }
    public async Task<EnrollmentDelivery> ReissueAsync(ApplicationUser? actor,string id,bool operatorOwner=false)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await LockAsync();
        var user=await users.FindByIdAsync(id)??throw new InvalidOperationException("Account unavailable.");
        await db.Entry(user).ReloadAsync();
        var pending=await PendingAsync(user)??throw new InvalidOperationException("No pending invitation.");
        if(pending.State!="Pending"||user.IsActive||await users.HasPasswordAsync(user)|| (await users.GetRolesAsync(user)).Count!=0)throw new InvalidOperationException("Account not pending enrollment.");
        if(operatorOwner){if(actor!=null||pending.Role!="Owner")throw new UnauthorizedAccessException();}
        else if(actor==null||!await CanManageAsync(actor,pending.Role))throw new UnauthorizedAccessException();
        var delivery=await IssueAsync(user,pending.Role,actor?.Id??"operator-bootstrap");
        await audit.LogAsync(SystemActivityType.UserUpdated,"Personnel invitation rotated; previous link invalidated.",actor?.Id??"operator-bootstrap",affectedRecordId:id,affectedRecordType:"ApplicationUser");
        await tx.CommitAsync();return delivery;
    }
    public async Task<IdentityResult> EnrollAsync(string id,string secret,string password)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await LockAsync();
        var user=await users.FindByIdAsync(id);if(user!=null)await db.Entry(user).ReloadAsync();var pending=user==null?null:await PendingAsync(user);
        if(user==null||pending==null||pending.State!="Pending"||pending.ExpiresUtc<=DateTime.UtcNow||secret.Length!=64||
           !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)),Encoding.ASCII.GetBytes(pending.Digest))||
           user.IsActive||await users.HasPasswordAsync(user)||(await users.GetRolesAsync(user)).Count!=0)
            return IdentityResult.Failed(new IdentityError {Code="EnrollmentUnavailable",Description="Enrollment is invalid, expired or already used. Request a new invitation."});
        if(pending.Role is not ("Owner" or "Admin" or "Staff"))throw new InvalidOperationException("Invalid enrollment role.");
        if(pending.Role=="Owner"&&(await users.GetUsersInRoleAsync("Owner")).Count!=0)throw new InvalidOperationException("An Owner already exists.");
        if(pending.Role!="Owner") {
            var issuer=await users.FindByIdAsync(pending.Issuer);
            if(issuer==null||!await CanManageAsync(issuer,pending.Role))return IdentityResult.Failed(new IdentityError {Description="Invitation issuer is no longer authorized."});
        }
        var result=await users.AddPasswordAsync(user,password);if(!result.Succeeded)return result;
        // Possession of the email-delivered link verifies this exact invited mailbox. Operator delivery requires independent mailbox verification.
        var confirmation=await users.GenerateEmailConfirmationTokenAsync(user);
        Require(await users.ConfirmEmailAsync(user,confirmation));
        Require(await users.AddToRoleAsync(user,pending.Role));user.IsActive=true;Require(await users.UpdateAsync(user));
        Require(await users.RemoveAuthenticationTokenAsync(user,Provider,TokenName));Require(await users.UpdateSecurityStampAsync(user));
        await audit.LogAsync(SystemActivityType.RoleAssigned,"Personnel completed verified enrollment and password setup.",user.Id,affectedRecordId:user.Id,affectedRecordType:"ApplicationUser");
        await tx.CommitAsync();return IdentityResult.Success;
    }
    public async Task ManageAsync(ApplicationUser actor,string id,string action,string? role=null)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await LockAsync();
        var user=await users.FindByIdAsync(id)??throw new InvalidOperationException("Account unavailable.");
        await db.Entry(user).ReloadAsync();
        var roles=await users.GetRolesAsync(user);var invitation=await PendingAsync(user);
        var current=roles.Count==1?roles[0]:roles.Count==0&&invitation?.State=="Pending"?invitation.Role:null;
        if(id==actor.Id||current==null||!await CanManageAsync(actor,current))throw new UnauthorizedAccessException();
        switch(action) {
            case "Revoke":
                if(invitation?.State!="Pending"||roles.Count!=0)throw new InvalidOperationException("Not pending.");
                Require(await users.SetAuthenticationTokenAsync(user,Provider,TokenName,JsonSerializer.Serialize(invitation with {State="Revoked",Digest=""})));break;
            case "Deactivate":
                user.IsActive=false;Require(await users.UpdateAsync(user));
                if(invitation?.State=="Pending")Require(await users.SetAuthenticationTokenAsync(user,Provider,TokenName,JsonSerializer.Serialize(invitation with {State="Revoked",Digest=""})));
                break;
            case "Reactivate":
                if(!user.EmailConfirmed||!await users.HasPasswordAsync(user)||roles.Count!=1)throw new InvalidOperationException("Complete enrollment first.");
                user.IsActive=true;Require(await users.UpdateAsync(user));break;
            case "RevokeSessions": break;
            case "Role":
                if(role is not ("Admin" or "Staff")||roles.Count!=1||!await users.IsInRoleAsync(actor,"Owner")||!await CanManageAsync(actor,role))throw new UnauthorizedAccessException();
                Require(await users.RemoveFromRoleAsync(user,current));Require(await users.AddToRoleAsync(user,role));break;
            default:throw new InvalidOperationException("Unsupported account action.");
        }
        Require(await users.UpdateSecurityStampAsync(user));
        await audit.LogAsync(SystemActivityType.UserUpdated,"Personnel action: "+action,actor.Id,affectedRecordId:id,affectedRecordType:"ApplicationUser",
            metadata:new {PreviousRole=current,NewRole=action=="Role"?role:current,Active=user.IsActive});
        await tx.CommitAsync();
    }
}
