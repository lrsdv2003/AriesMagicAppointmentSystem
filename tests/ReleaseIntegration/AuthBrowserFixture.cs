using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Explicit test-executable mode. Never part of the production application or publish output.
// No HTTP diagnostics/mailbox endpoint: private filesystem IPC controls synthetic fixture state only.
static class AuthBrowserFixture
{
    public static async Task RunAsync()
    {
        var root=Directory.GetCurrentDirectory();
        var statePath=Path.Combine(root,".tmp-owner-build/phase5-auth-fixture.json");
        if(File.Exists(statePath))throw new InvalidOperationException("Stop the existing isolated auth UI fixture before starting another.");
        var database="AriesAuthVisual_"+Guid.NewGuid().ToString("N");
        var connection=@"Server=(localdb)\MSSQLLocalDB;Database="+database+";Integrated Security=True;TrustServerCertificate=True";
        var directory=Path.Combine(root,".tmp-owner-build",database);Directory.CreateDirectory(directory);
        // Keep cross-runtime IPC outside the OneDrive source tree to avoid sync locks on command/reply files.
        var control=Path.Combine(Path.GetTempPath(),database,"control");Directory.CreateDirectory(control);
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        await using var smtp=new SmtpFixture();smtp.Start();
        var factories=new ConcurrentDictionary<string,ReleaseFactory>();
        var clients=new ConcurrentDictionary<string,HttpClient>();
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="AuthUiFixture", ContentRootPath=directory });builder.Logging.ClearProviders();
        await using var proxy=builder.Build();proxy.Urls.Add("http://127.0.0.1:5296");
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        try {
            await db.Database.MigrateAsync();
            var seed=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),directory,authUi:true);factories["seed"]=seed;
            using var scope=seed.Services.CreateScope();await IdentitySeeder.SeedAsync(scope.ServiceProvider);
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var confirmed=new ApplicationUser{UserName="client@example.test",Email="client@example.test",EmailConfirmed=true,FullName="Synthetic UI client",PhoneNumber="09123456789"};
            var pending=new ApplicationUser{UserName="pending@example.test",Email="pending@example.test",FullName="Synthetic pending client",PhoneNumber="09123456789"};
            foreach(var user in new[]{confirmed,pending}) {
                if(!(await users.CreateAsync(user,password)).Succeeded||!(await users.AddToRoleAsync(user,"Client")).Succeeded)throw new Exception("Synthetic fixture provisioning failed.");
            }
            await scope.ServiceProvider.GetRequiredService<IEmailVerificationService>().IssueCodeAsync(pending,false);
            // Private fixture input only. No real account, connection string or live credential is written.
            var state=new {Pid=Environment.ProcessId,Directory=directory,Control=control,BaseUrl="http://127.0.0.1:5296",Email=confirmed.Email,PendingEmail=pending.Email,PendingId=pending.Id,Password=password};
            await File.WriteAllTextAsync(statePath,JsonSerializer.Serialize(state));
            proxy.Run(async context => {
                var path=context.Request.Path.Value??"/";
                var publicPath=path=="/"||path=="/favicon.ico"||path.StartsWith("/Home/")||path.StartsWith("/css/")||path.StartsWith("/js/")||path.StartsWith("/lib/")||path.StartsWith("/fonts/")||path.StartsWith("/images/client/")||path.EndsWith(".styles.css");
                var accountPath=path.StartsWith("/Account/");
                var dashboard=context.Request.Method=="GET"&&path is "/Bookings/Dashboard" or "/Bookings/ClientDashboard" or "/Bookings/MyBookings" or "/Communications/UnreadCount";
                if(!(accountPath||publicPath||dashboard)||context.Request.Method!="GET"&&context.Request.Method!="HEAD"&&!(accountPath&&context.Request.Method=="POST")){context.Response.StatusCode=405;return;}
                var scenario=context.Request.Headers["X-Aries-UI-Scenario"].FirstOrDefault()??"manual";
                if(!Regex.IsMatch(scenario,"^[A-Za-z0-9-]{1,64}$")){context.Response.StatusCode=400;return;}
                // Each named test scenario is a fresh real app instance. Its actual account limiter is not disabled.
                var factory=factories.GetOrAdd(scenario,_=>new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),directory,authUi:true));
                var client=clients.GetOrAdd(scenario,_=>new HttpClient(factory.Server.CreateHandler()){BaseAddress=new Uri("http://localhost")});
                using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
                if(context.Request.ContentLength>0){using var memory=new MemoryStream();await context.Request.Body.CopyToAsync(memory);request.Content=new ByteArrayContent(memory.ToArray());}
                foreach(var header in context.Request.Headers){if(header.Key is "Host" or "X-Aries-UI-Scenario" or "Connection")continue;if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray()))request.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());}
                request.Headers.Host=context.Request.Host.Value;
                using var response=await client.SendAsync(request);
                context.Response.StatusCode=(int)response.StatusCode;
                foreach(var header in response.Headers.Concat(response.Content.Headers)){if(header.Key.Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))continue;context.Response.Headers[header.Key]=header.Value.ToArray();}
                await context.Response.Body.WriteAsync(await response.Content.ReadAsByteArrayAsync());
            });
            var stop=Path.Combine(directory,"stop");
            var stopWatcher=Task.Run(async()=>{
                while(!proxy.Lifetime.ApplicationStopping.IsCancellationRequested){
                    if(File.Exists(stop)){proxy.Lifetime.StopApplication();break;}
                    await Task.Delay(100);
                }
            });
            var commands=Task.Run(async()=>{
                while(!proxy.Lifetime.ApplicationStopping.IsCancellationRequested){
                    foreach(var command in Directory.EnumerateFiles(control,"*.request.json")) {
                        try {
                        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(command));var item=document.RootElement;
                        var operation=item.GetProperty("operation").GetString();var email=item.GetProperty("email").GetString()??"";
                        if(!email.EndsWith("@example.test",StringComparison.Ordinal))throw new Exception("Fixture commands accept synthetic accounts only.");
                        await using var commandDb=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
                        var user=await commandDb.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Email==email);
                        object result=new {Ok=true};
                        if(operation=="mail"){
                            var message=smtp.Messages.LastOrDefault(m=>m.To.Mailboxes.Any(a=>a.Address==email));var html=message?.HtmlBody??"";
                            var code=Regex.Match(html,@">(\d{6})</div>").Groups[1].Value;
                            var link=WebUtility.HtmlDecode(Regex.Match(html,"href='([^']+)'",RegexOptions.IgnoreCase).Groups[1].Value);
                            result=new {Code=code,Link=link,Count=smtp.Messages.Count(m=>m.To.Mailboxes.Any(a=>a.Address==email))};
                        } else if(user!=null&&operation is "expire" or "ready-resend" or "lock-code") {
                            var verification=await commandDb.EmailVerifications.SingleAsync(v=>v.UserId==user.Id);
                            if(operation=="expire")verification.ExpiresAt=DateTime.UtcNow.AddMinutes(-1);
                            if(operation=="ready-resend")verification.LastSentAt=DateTime.UtcNow.AddMinutes(-2);
                            if(operation=="lock-code")verification.AttemptCount=5;
                            await commandDb.SaveChangesAsync();
                        } else if(operation!="mail")throw new Exception("Unsupported synthetic fixture command.");
                        await ReplyAsync(command,result);
                        File.Delete(command);
                        } catch(Exception error) {
                            // Safe failure metadata only; keep the separate stop watcher available.
                            await ReplyAsync(command,new {ErrorType=error.GetType().Name,ErrorCode=error.HResult});
                            File.Delete(command);
                        }
                    }
                    await Task.Delay(100);
                }
            });
            Console.WriteLine($"ISOLATED AUTH UI http://127.0.0.1:5296 PID {Environment.ProcessId}; private stop marker {stop}");
            await proxy.RunAsync();await Task.WhenAll(commands,stopWatcher);
        } finally {
            foreach(var client in clients.Values)client.Dispose();foreach(var factory in factories.Values)await factory.DisposeAsync();
            if(!Regex.IsMatch(database,"^AriesAuthVisual_[0-9a-f]{32}$"))throw new Exception("Unsafe fixture teardown target.");
            await db.Database.EnsureDeletedAsync();
            // Captured codes/tokens and generated credentials are test-only and unnecessary after this invocation.
            foreach(var file in Directory.EnumerateFiles(control))File.Delete(file);
            if(File.Exists(statePath))File.Delete(statePath);
        }
    }
    static async Task ReplyAsync(string command,object result)
    {
        var target=command.Replace(".request.json",".response.json");
        var pending=target+".pending";
        await File.WriteAllTextAsync(pending,JsonSerializer.Serialize(result));
        File.Move(pending,target,true); // Readers see complete, closed replies only.
    }
}
