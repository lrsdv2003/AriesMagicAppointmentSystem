using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Operator tool: no settings files, seeded credentials, web endpoint or implicit writes.
var connection = Environment.GetEnvironmentVariable("RELEASE_ADMIN_CONNECTIONSTRING");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Set RELEASE_ADMIN_CONNECTIONSTRING securely in the operator process; do not put it in arguments or source.");
var emailIndex = Array.IndexOf(args,"--email");
if (emailIndex<0 || emailIndex+1>=args.Length || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(args[emailIndex+1]))
    throw new InvalidOperationException("Specify exactly one reviewed account using --email. No bulk mutation is supported.");
var services=new ServiceCollection().AddLogging().AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<ISystemActivityService,SystemActivityService>();services.AddScoped<AccountCredentialRecovery>();
await using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var user=await users.FindByEmailAsync(args[emailIndex+1]);
if(user==null) { Console.WriteLine("Reviewed account is absent; no changes made.");return; }
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {Mode=args.Contains("--apply")?"Apply requested":"Read only", user.Id, user.EmailConfirmed, HasPassword=await users.HasPasswordAsync(user),Roles=await users.GetRolesAsync(user)}));
if(!args.Contains("--apply")) return;
if(!args.Contains("--recovery-verified") || !args.Contains("--production-change-approved"))
    throw new InvalidOperationException("Apply requires --recovery-verified and --production-change-approved after explicit authorization and recovery-channel verification.");
var result=await scope.ServiceProvider.GetRequiredService<AccountCredentialRecovery>().InvalidateAsync(user,true);
if(!result.Succeeded) throw new InvalidOperationException("Credential invalidation refused; inspect recovery state and Identity policy. No password was printed.");
Console.WriteLine("Password removed, security stamp rotated and stored user tokens removed. Verified owner must complete the normal Forgot Password flow. Verify old-password denial and cookie revalidation before clearance.");
