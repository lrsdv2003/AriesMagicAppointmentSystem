using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using AriesMagicAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;

static class PersonnelOperatorContractTests
{
    public static async Task RunAsync()
    {
        var name="AriesOperator_"+Guid.NewGuid().ToString("N");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!System.Text.RegularExpressions.Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))throw new Exception("Invalid local instance.");
        var connection=Environment.GetEnvironmentVariable("ARIES_TEST_USE_DEFAULT_SQL")=="true"?$"Server=.;Database={name};Integrated Security=True;TrustServerCertificate=True":$"Server=(localdb)\\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var directory=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(directory);var first=Path.Combine(directory,"enrollment.private.txt");
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);checks++;Console.WriteLine("PASS: "+label);}
        async Task<(int Exit,string Output)> Run(string mode,string approval="APPROVED",string? file=null,string? retireId=null,bool retirementApproved=false) {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Directory.GetCurrentDirectory(),CreateNoWindow=true};
            start.ArgumentList.Add(Path.GetFullPath(Environment.GetEnvironmentVariable("ARIES_TEST_OPERATOR_DLL")??"tests/PersonnelOperator/bin/Release/net10.0/PersonnelOperator.dll"));start.ArgumentList.Add(mode);
            foreach(var pair in new Dictionary<string,string>{{"ARIES_OPERATOR_APPROVAL",approval},{"ARIES_OPERATOR_SQL",connection},{"ARIES_OPERATOR_DATABASE",name},{"ARIES_OPERATOR_EMAIL","bootstrap@example.test"},{"ARIES_OPERATOR_NAME","Synthetic Owner"},{"ARIES_OPERATOR_MAILBOX_VERIFIED","VERIFIED"},{"ARIES_OPERATOR_ORIGIN","https://personnel.example.test"},{"ARIES_OPERATOR_APPLICATION_ROOT",Directory.GetCurrentDirectory()},{"ARIES_OPERATOR_DELIVERY_FILE",file??first}})start.Environment[pair.Key]=pair.Value;
            if(retireId!=null){start.Environment["ARIES_OPERATOR_USER_ID"]=retireId;start.Environment["ARIES_OPERATOR_REFERENCE"]="isolated-operator-contract";}
            if(retirementApproved)start.Environment["ARIES_OPERATOR_RETIREMENT_APPROVAL"]="RETIRE_EXACT_LEGACY_ID";
            using var process=Process.Start(start)??throw new Exception("Operator fixture launch failed.");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();return(process.ExitCode,await stdout+await stderr);
        }
        try {
            await db.Database.MigrateAsync();
            Check((await Run("roles","DENIED")).Exit!=0&&!await db.Roles.AnyAsync(),"Offline tool refuses missing explicit approval before writes");
            Check((await Run("roles")).Exit==0&&await db.Roles.CountAsync()==3&&!await db.Users.AnyAsync(),"Offline tool initializes roles without users");
            var result=await Run("bootstrap");Check(result.Exit==0&&File.Exists(first),"Offline Owner bootstrap writes protected operator delivery file");
            var link=await File.ReadAllTextAsync(first);var secret=new Uri(link).Fragment.Split(':')[1];
            Check(!result.Output.Contains(secret)&&!result.Output.Contains(connection),"Offline output contains neither token nor connection string");
            Check(await db.Users.CountAsync()==1&&!await db.UserRoles.AnyAsync()&&!await db.Users.AnyAsync(x=>x.IsActive||x.EmailConfirmed||x.PasswordHash!=null),"Offline bootstrap creates only inactive unverified roleless account");
            var repeat=await Run("bootstrap",file:Path.Combine(directory,"repeated.private.txt"));Check(repeat.Exit!=0&&await db.Users.CountAsync()==1,"Offline repeated bootstrap cannot replace Owner");
            var rotated=Path.Combine(directory,"rotated.private.txt");Check((await Run("deliver",file:rotated)).Exit==0&&File.Exists(rotated)&&await File.ReadAllTextAsync(rotated)!=link,"Offline pending-Owner delivery rotates enrollment link");
            var collection=new ServiceCollection().AddLogging();collection.AddDataProtection().UseEphemeralDataProtectionProvider();collection.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));
            collection.AddIdentityCore<ApplicationUser>(o=>o.Password.RequiredLength=12).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();collection.Configure<PersonnelOptions>(_=>{});collection.AddScoped<ISystemActivityService,SystemActivityService>();collection.AddScoped<PersonnelProvisioning>();
            await using var provider=collection.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await roles.CreateAsync(new IdentityRole("Admin"));var generated="Synthetic!"+Guid.NewGuid().ToString("N")+"A7";
            var legacy=new ApplicationUser{Email="legacy-operator@example.test",UserName="legacy-operator@example.test",FullName="Legacy fixture",EmailConfirmed=true};await users.CreateAsync(legacy,generated);await users.AddToRoleAsync(legacy,"Admin");
            Check((await Run("retire-legacy-admin",retireId:legacy.Id)).Exit!=0&&await users.IsInRoleAsync(legacy,"Admin"),"Offline retirement requires separate exact-identity approval");
            Check((await Run("retire-legacy-admin",retireId:legacy.Id,retirementApproved:true)).Exit!=0&&await users.IsInRoleAsync(legacy,"Admin"),"Offline retirement refuses while only Owner is unenrolled");
            var parts=new Uri(await File.ReadAllTextAsync(rotated)).Fragment.TrimStart('#').Split(':');Check((await scope.ServiceProvider.GetRequiredService<PersonnelProvisioning>().EnrollAsync(parts[0],parts[1],generated)).Succeeded,"Private Owner enrollment remains functional before retirement");
            var retired=await Run("retire-legacy-admin",retireId:legacy.Id,retirementApproved:true);var ownDb=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await ownDb.Entry(legacy).ReloadAsync();
            // The operator is a separate process. Query the store afresh: the caller's previously
            // tracked AspNetUserRoles entity must not masquerade as a role still in SQL.
            var persistedRoles=await ownDb.UserRoles.AsNoTracking().Where(x=>x.UserId==legacy.Id).Join(ownDb.Roles.AsNoTracking(),x=>x.RoleId,r=>r.Id,(x,r)=>r.Name).ToListAsync();
            Check(retired.Exit==0&&!legacy.IsActive&&!persistedRoles.Contains("Admin")&&!persistedRoles.Contains("Owner"),"Offline exact-identity retirement disables legacy access without promotion");
            Check(await db.Users.CountAsync()==2&&await db.SystemActivities.AnyAsync(a=>a.AffectedRecordId==legacy.Id&&a.Type==SystemActivityType.RoleRemoved),"Offline retirement retains both identities and writes audit");
            Check(!retired.Output.Contains(connection)&&!retired.Output.Contains(generated),"Offline retirement emits no connection value or password");
            Console.WriteLine($"SUCCESS: {checks} offline operator SQL checks passed.");
        } finally {
            await db.Database.EnsureDeletedAsync();
            // Only synthetic delivery files in this exact GUID-owned temp directory.
            foreach(var file in Directory.EnumerateFiles(directory,"*.private.txt"))File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
