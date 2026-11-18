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
        var connection=$"Server=(localdb)\\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var directory=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(directory);var first=Path.Combine(directory,"enrollment.private.txt");
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);checks++;Console.WriteLine("PASS: "+label);}
        async Task<(int Exit,string Output)> Run(string mode,string approval="APPROVED",string? file=null) {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Directory.GetCurrentDirectory(),CreateNoWindow=true};
            start.ArgumentList.Add(Path.GetFullPath("tests/PersonnelOperator/bin/Release/net10.0/PersonnelOperator.dll"));start.ArgumentList.Add(mode);
            foreach(var pair in new Dictionary<string,string>{{"ARIES_OPERATOR_APPROVAL",approval},{"ARIES_OPERATOR_SQL",connection},{"ARIES_OPERATOR_DATABASE",name},{"ARIES_OPERATOR_EMAIL","bootstrap@example.test"},{"ARIES_OPERATOR_NAME","Synthetic Owner"},{"ARIES_OPERATOR_MAILBOX_VERIFIED","VERIFIED"},{"ARIES_OPERATOR_ORIGIN","https://personnel.example.test"},{"ARIES_OPERATOR_APPLICATION_ROOT",Directory.GetCurrentDirectory()},{"ARIES_OPERATOR_DELIVERY_FILE",file??first}})start.Environment[pair.Key]=pair.Value;
            using var process=Process.Start(start)??throw new Exception("Operator fixture launch failed.");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();return(process.ExitCode,await stdout+await stderr);
        }
        try {
            await db.Database.MigrateAsync();
            Check((await Run("roles","DENIED")).Exit!=0&&!await db.Roles.AnyAsync(),"Offline tool refuses missing explicit approval before writes");
            Check((await Run("roles")).Exit==0&&await db.Roles.CountAsync()==4&&!await db.Users.AnyAsync(),"Offline tool initializes roles without users");
            var result=await Run("bootstrap");Check(result.Exit==0&&File.Exists(first),"Offline Owner bootstrap writes protected operator delivery file");
            var link=await File.ReadAllTextAsync(first);var secret=new Uri(link).Fragment.Split(':')[1];
            Check(!result.Output.Contains(secret)&&!result.Output.Contains(connection),"Offline output contains neither token nor connection string");
            Check(await db.Users.CountAsync()==1&&!await db.UserRoles.AnyAsync()&&!await db.Users.AnyAsync(x=>x.IsActive||x.EmailConfirmed||x.PasswordHash!=null),"Offline bootstrap creates only inactive unverified roleless account");
            var repeat=await Run("bootstrap",file:Path.Combine(directory,"repeated.private.txt"));Check(repeat.Exit!=0&&await db.Users.CountAsync()==1,"Offline repeated bootstrap cannot replace Owner");
            var rotated=Path.Combine(directory,"rotated.private.txt");Check((await Run("deliver",file:rotated)).Exit==0&&File.Exists(rotated)&&await File.ReadAllTextAsync(rotated)!=link,"Offline pending-Owner delivery rotates enrollment link");
            Console.WriteLine($"SUCCESS: {checks} offline operator SQL checks passed.");
        } finally {
            await db.Database.EnsureDeletedAsync();
            // Only synthetic delivery files in this exact GUID-owned temp directory.
            foreach(var file in Directory.EnumerateFiles(directory,"*.private.txt"))File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
