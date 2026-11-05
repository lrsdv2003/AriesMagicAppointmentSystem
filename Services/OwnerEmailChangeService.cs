using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace AriesMagicAppointmentSystem.Services;
public sealed record PendingOwnerEmail(string Email,string Hash,DateTime ExpiresUtc,DateTime SentUtc);
public sealed class OwnerEmailChangeService(UserManager<ApplicationUser> users,ApplicationDbContext db,IEmailService email,IOptions<PersonnelOptions> origin)
{
    const string Provider="AriesOwnerEmail",Name="Pending";
    async Task Lock(ApplicationUser user) {
        _ = await db.Database.SqlQuery<int>($"SELECT 1 AS Value FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={user.Id}").ToListAsync();
        await db.Entry(user).ReloadAsync();
    }
    static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<PendingOwnerEmail?> Pending(ApplicationUser user){var value=await db.UserTokens.AsNoTracking().Where(x=>x.UserId==user.Id&&x.LoginProvider==Provider&&x.Name==Name).Select(x=>x.Value).SingleOrDefaultAsync();return value==null?null:JsonSerializer.Deserialize<PendingOwnerEmail>(value);}
    public async Task Audit(ApplicationUser user,string outcome){db.SystemActivities.Add(new(){Type=SystemActivityType.UserUpdated,Description="Owner email change: "+outcome,PerformedByUserId=user.Id,AffectedRecordId=user.Id,AffectedRecordType="ApplicationUser"});await db.SaveChangesAsync();}
    public async Task Request(ApplicationUser user,string address)
    {
        if(!user.IsActive||!await users.IsInRoleAsync(user,"Owner"))throw new InvalidOperationException();
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await Lock(user);
        if(!user.IsActive||!await users.IsInRoleAsync(user,"Owner"))throw new InvalidOperationException();
        var pending=await Pending(user);if(pending!=null&&pending.SentUtc>DateTime.UtcNow.AddMinutes(-2))throw new InvalidOperationException();
        var duplicate=await users.FindByEmailAsync(address);if(duplicate!=null)throw new InvalidOperationException();
        if(!Uri.TryCreate(origin.Value.PublicOrigin,UriKind.Absolute,out var root)||root.Scheme!="https"||root.AbsolutePath!="/"||root.Query!=""||root.Fragment!=""||root.UserInfo!="")throw new InvalidOperationException();
        var token=await users.GenerateChangeEmailTokenAsync(user,address);
        var link=root.GetLeftPart(UriPartial.Authority)+"/OwnerEmail/Confirm#"+Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var result=await users.SetAuthenticationTokenAsync(user,Provider,Name,JsonSerializer.Serialize(new PendingOwnerEmail(address,Hash(token),DateTime.UtcNow.AddMinutes(30),DateTime.UtcNow)));
        if(!result.Succeeded)throw new InvalidOperationException();
        try {await email.SendEmailAsync(address,"Verify your Aries Magic email change",$"<h1>Aries d’ Magic Artist</h1><p>Your current login remains active until you confirm.</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Verify email change</a></p><p>This link expires in 30 minutes. Sign in with your existing Owner account to confirm.</p>");await Audit(user,"submission accepted; delivery unconfirmed");await transaction.CommitAsync();}
        catch {await transaction.RollbackAsync();db.ChangeTracker.Clear();throw;}
    }
    public async Task<bool> Confirm(ApplicationUser user,string token)
    {
        string? old=null;
        await using(var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)) {
            var committed=false;
            try {
                await Lock(user);
                var pending=await Pending(user);
                if(!user.IsActive||!await users.IsInRoleAsync(user,"Owner")||pending==null||pending.ExpiresUtc<=DateTime.UtcNow||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(pending.Hash),Encoding.ASCII.GetBytes(Hash(token))))return false;
                if(await users.FindByEmailAsync(pending.Email)!=null)return false;
                old=user.Email;var emailAsName=string.Equals(user.UserName,old,StringComparison.OrdinalIgnoreCase);
                if(!(await users.ChangeEmailAsync(user,pending.Email,token)).Succeeded)return false;
                if(emailAsName&&!(await users.SetUserNameAsync(user,pending.Email)).Succeeded)return false;
                if(!(await users.UpdateSecurityStampAsync(user)).Succeeded)return false;
                if(!(await users.RemoveAuthenticationTokenAsync(user,Provider,Name)).Succeeded)return false;
                await Audit(user,"confirmed");await tx.CommitAsync();committed=true;
            } finally {if(!committed){await tx.RollbackAsync();db.ChangeTracker.Clear();}}
        }
        if(old!=null)try{await email.SendEmailAsync(old,"Aries Magic login email changed","<p>Your Owner login email was changed. Contact your authorized operator immediately if this was unexpected.</p>");}catch(EmailDeliveryException){await Audit(user,"old-address notification unavailable");}
        return true;
    }
    public async Task Cancel(ApplicationUser user){
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await Lock(user);
        if(!user.IsActive||!await users.IsInRoleAsync(user,"Owner"))throw new InvalidOperationException();
        if(!(await users.RemoveAuthenticationTokenAsync(user,Provider,Name)).Succeeded)throw new InvalidOperationException();
        await Audit(user,"cancelled");await tx.CommitAsync();
    }
}
