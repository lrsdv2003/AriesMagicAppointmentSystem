using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace AriesMagicAppointmentSystem.Services;
// Explicit offline operator operation only. Never called by startup, seeders or HTTP controllers.
public sealed class LegacyRoleRetirement(ApplicationDbContext db,UserManager<ApplicationUser> users,ISystemActivityService audit)
{
    public const string LegacyRole="Admin"; // Historical Identity value; never an active permission.
    static void Require(IdentityResult r){if(!r.Succeeded)throw new InvalidOperationException("Legacy retirement refused.");}
    public async Task RetireAsync(string approvedUserId,string operatorReference)
    {
        if(string.IsNullOrWhiteSpace(approvedUserId)||operatorReference.Length is <4 or >100)throw new InvalidOperationException("Explicit identity and operator reference required.");
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        _=await db.Database.SqlQuery<int>($"SELECT 1 AS Value FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id={approvedUserId}").ToListAsync();
        var target=await users.FindByIdAsync(approvedUserId)??throw new InvalidOperationException("Approved legacy identity unavailable.");await db.Entry(target).ReloadAsync();
        var roles=await users.GetRolesAsync(target);
        if(roles.Contains("Owner"))throw new InvalidOperationException("Owner identities are protected; use a separately reviewed recovery process.");
        var owners=await users.GetUsersInRoleAsync("Owner");var ready=false;
        foreach(var owner in owners)if(owner.IsActive&&owner.EmailConfirmed&&!(owner.LockoutEnd>DateTimeOffset.UtcNow)&&await users.HasPasswordAsync(owner)){ready=true;break;}
        if(!ready)throw new InvalidOperationException("Verify an active enrolled Owner before legacy retirement.");
        var value=await db.UserTokens.AsNoTracking().Where(x=>x.UserId==target.Id&&x.LoginProvider==PersonnelProvisioning.Provider&&x.Name==PersonnelProvisioning.TokenName).Select(x=>x.Value).SingleOrDefaultAsync();
        var pending=value==null?null:JsonSerializer.Deserialize<PersonnelInvitation>(value);
        if(!roles.Contains(LegacyRole)&&pending?.Role!=LegacyRole)throw new InvalidOperationException("Selected account is not a legacy role or invitation.");
        var wasActive=target.IsActive;
        if(roles.Contains(LegacyRole))Require(await users.RemoveFromRoleAsync(target,LegacyRole));
        if(!roles.Any(ApplicationRoles.IsActive)) {target.IsActive=false;Require(await users.UpdateAsync(target));}
        if(pending?.Role==LegacyRole)Require(await users.SetAuthenticationTokenAsync(target,PersonnelProvisioning.Provider,PersonnelProvisioning.TokenName,JsonSerializer.Serialize(pending with {State="Revoked",Digest=""})));
        Require(await users.UpdateSecurityStampAsync(target));
        await audit.LogAsync(SystemActivityType.RoleRemoved,"Personnel legacy role retired by explicitly authorized operator; identity and history retained.","release-operator",affectedRecordId:target.Id,affectedRecordType:"ApplicationUser",metadata:new {OperatorReference=operatorReference,PreviousRole=LegacyRole,WasActive=wasActive,Active=target.IsActive});
        await tx.CommitAsync();
    }
}
