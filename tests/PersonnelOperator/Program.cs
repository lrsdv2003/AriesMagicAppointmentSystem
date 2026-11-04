using System.ComponentModel.DataAnnotations;
using System.Security.AccessControl;
using System.Security.Principal;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;

// Offline, explicitly invoked operator utility. Never installed under the IIS website.
try {
    string Input(string name)=>Environment.GetEnvironmentVariable(name)??throw new InvalidOperationException("Required private operator input absent.");
    var mode=args.Length==1?args[0]:"";
    if(mode is not ("roles" or "bootstrap" or "deliver" or "recover-owner"))throw new InvalidOperationException("Select roles, bootstrap, deliver or recover-owner.");
    if(Input("ARIES_OPERATOR_APPROVAL")!="APPROVED")throw new InvalidOperationException("Explicit operator approval required.");
    var services=new ServiceCollection();services.AddLogging();services.AddDataProtection().UseEphemeralDataProtectionProvider();
    services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(Input("ARIES_OPERATOR_SQL")));
    services.AddIdentityCore<ApplicationUser>(o=>{o.Password.RequiredLength=12;o.User.RequireUniqueEmail=true;}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
    services.Configure<PersonnelOptions>(o=>o.InvitationHours=24);services.AddScoped<ISystemActivityService,SystemActivityService>();services.AddScoped<PersonnelProvisioning>();services.AddScoped<AccountCredentialRecovery>();
    await using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.OpenConnectionAsync();
    if(db.Database.GetDbConnection().Database!=Input("ARIES_OPERATOR_DATABASE"))throw new InvalidOperationException("Approved database target mismatch.");
    if((await db.Database.GetPendingMigrationsAsync()).Any())throw new InvalidOperationException("Apply supported migrations separately before provisioning.");
    if(mode=="roles"){await IdentitySeeder.SeedAsync(scope.ServiceProvider);Console.WriteLine("Role definitions initialized; no users created.");return;}
    var email=Input("ARIES_OPERATOR_EMAIL").Trim();if(!new EmailAddressAttribute().IsValid(email))throw new InvalidOperationException("Approved email required.");
    var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var service=scope.ServiceProvider.GetRequiredService<PersonnelProvisioning>();
    if(mode=="recover-owner") {
        if(Input("ARIES_OPERATOR_MAILBOX_VERIFIED")!="VERIFIED")throw new InvalidOperationException("Independently verify mailbox ownership first.");
        var user=await users.FindByEmailAsync(email)??throw new InvalidOperationException("Owner unavailable.");
        if(!await users.IsInRoleAsync(user,"Owner"))throw new InvalidOperationException("Selected account is not an Owner.");
        var result=await scope.ServiceProvider.GetRequiredService<AccountCredentialRecovery>().InvalidateAsync(user,true);
        if(!result.Succeeded)throw new InvalidOperationException("Recovery refused.");
        await using(var recovery=await db.Database.BeginTransactionAsync()) {
            user.IsActive=true;
            if(!(await users.UpdateAsync(user)).Succeeded)throw new InvalidOperationException("Owner reactivation refused.");
            await scope.ServiceProvider.GetRequiredService<ISystemActivityService>().LogAsync(SystemActivityType.UserEnabled,"Personnel operator reactivated existing Owner after independently verified recovery.","release-operator",affectedRecordId:user.Id,affectedRecordType:"ApplicationUser");
            await recovery.CommitAsync();
        }
        Console.WriteLine("Existing Owner credentials invalidated. Complete the established verified-email password-reset flow. No Owner role created or promoted.");return;
    }
    if(Input("ARIES_OPERATOR_MAILBOX_VERIFIED")!="VERIFIED")throw new InvalidOperationException("Verify recipient mailbox ownership through an independent approved channel before private delivery.");
    var origin=new Uri(Input("ARIES_OPERATOR_ORIGIN"));if(origin.Scheme!="https"||origin.AbsolutePath!="/"||origin.UserInfo!=""||origin.Query!=""||origin.Fragment!="")throw new InvalidOperationException("Approved HTTPS origin required.");
    var output=Path.GetFullPath(Input("ARIES_OPERATOR_DELIVERY_FILE"));var directory=Path.GetDirectoryName(output)!;
    if(!Directory.Exists(directory)||File.Exists(output))throw new InvalidOperationException("Use a new file inside an existing protected operator directory.");
    var appRoot=Path.GetFullPath(Input("ARIES_OPERATOR_APPLICATION_ROOT")).TrimEnd(Path.DirectorySeparatorChar);
    if(output.StartsWith(appRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Delivery file must be outside application/source/publish roots.");
    var toolRoot=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
    if(output.StartsWith(toolRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Delivery file cannot be a tool build artifact.");
    var workingRoot=Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar);
    if(Directory.EnumerateFiles(workingRoot,"*.csproj").Any()&&output.StartsWith(workingRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Delivery file cannot be inside the source checkout.");
    for(var ancestor=new DirectoryInfo(directory);ancestor!=null;ancestor=ancestor.Parent)
        if((ancestor.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Use a protected directory without junctions or reparse points.");
    if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Use the approved Windows operator environment for protected delivery.");
    var acl=new DirectoryInfo(directory).GetAccessControl();
    var broad=new[]{"S-1-1-0","S-1-5-11","S-1-5-32-545"};
    foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))if(rule.AccessControlType==AccessControlType.Allow&&broad.Contains(rule.IdentityReference.Value))throw new InvalidOperationException("Delivery directory has broad access; choose an already protected destination. This tool changes no ACLs.");
    EnrollmentDelivery delivery;
    if(mode=="bootstrap") {
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
        delivery=await service.InviteAsync(null,email,Input("ARIES_OPERATOR_NAME"),"Owner",bootstrap:true);
    } else {
        var user=await users.FindByEmailAsync(email)??throw new InvalidOperationException("Pending recipient unavailable.");
        var pending=await service.PendingAsync(user)??throw new InvalidOperationException("No invitation.");
        if(pending.Role=="Owner")delivery=await service.ReissueAsync(null,user.Id,operatorOwner:true);
        else {
            var issuer=await users.FindByIdAsync(pending.Issuer)??throw new InvalidOperationException("Invitation issuer unavailable.");
            delivery=await service.ReissueAsync(issuer,user.Id);
        }
    }
    await using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    await using(var writer=new StreamWriter(file))await writer.WriteAsync(origin.GetLeftPart(UriPartial.Authority)+"/PersonnelEnrollment/Accept#"+delivery.UserId+":"+delivery.Secret);
    Console.WriteLine("Enrollment rotated/created and written to the approved private delivery file. No secret printed. Deliver only to the independently verified recipient; expire/remove the private copy under the approved retention procedure.");
} catch {Console.Error.WriteLine("Operator action stopped. Check approved target, recovery, permissions and enrollment state privately. No secret values are printed.");Environment.ExitCode=1;}
