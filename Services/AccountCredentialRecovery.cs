using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

public sealed class AccountCredentialRecovery(ApplicationDbContext database, UserManager<ApplicationUser> users, ISystemActivityService audit)
{
    // Called only by the operator CLI, never by an anonymous or administrative web endpoint.
    public async Task<IdentityResult> InvalidateAsync(ApplicationUser user, bool recoveryChannelVerified)
    {
        if (!recoveryChannelVerified || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
            return IdentityResult.Failed(new IdentityError { Code = "RecoveryRequired", Description = "Verify the account owner's recovery channel before invalidating credentials." });
        await using var transaction = await database.Database.BeginTransactionAsync();
        var result = await users.RemovePasswordAsync(user);
        if (!result.Succeeded) return result;
        result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) return result;
        await database.UserTokens.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
        await audit.LogAsync(SystemActivityType.UserUpdated, "Operator invalidated exposed account credentials after recovery-channel verification.",
            "release-operator", affectedRecordId: user.Id, affectedRecordType: "Account");
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }
}
