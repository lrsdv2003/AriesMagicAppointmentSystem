using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
class OwnerEmailTests { static async Task Main() {
var count=0;
void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);Console.WriteLine("PASS: "+label);count++;}
foreach(var provider in new[]{"Brevo","Resend"}) {
 var handler=new FakeHandler();var o=new EmailSettings{Provider=provider,ApiKey="synthetic-key",SenderEmail="sender@example.test",SenderName="Aries Magic"};
 var service=new HttpsEmailService(new HttpClient(handler),Options.Create(o),NullLogger<HttpsEmailService>.Instance);
 await service.SendEmailAsync("recipient@example.test","Synthetic","<p>synthetic</p>");Check(handler.Uri!.Scheme=="https","HTTPS "+provider);Check(handler.Json!.Contains("recipient@example.test"),"payload "+provider);
 foreach(var code in new[]{401,403,429,500}){handler.Status=(HttpStatusCode)code;try{await service.SendEmailAsync("recipient@example.test","Synthetic","body");throw new Exception("Accepted rejection");}catch(EmailDeliveryException e){Check(e.Classification==EmailFailure.Classify((HttpStatusCode)code),"classification "+provider+code);}}
 handler.Status=HttpStatusCode.OK;await service.SendEmailWithAttachmentAsync("recipient@example.test","Synthetic","body",new byte[]{1,2},"receipt.pdf");Check(handler.Json!.Contains("AQI="),"attachment "+provider);
}
Check(EmailFailure.Classify(new TaskCanceledException())=="Timeout","timeout diagnostics");Check(EmailFailure.Classify(new System.Net.Sockets.SocketException())=="NetworkOrTransport","network diagnostics");
var name="AriesOwnerEmail_"+Guid.NewGuid().ToString("N");
var connection=$"Server=localhost;Database={name};Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
var services=new ServiceCollection();services.AddLogging();services.AddDataProtection().UseEphemeralDataProtectionProvider();services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));
services.AddIdentityCore<ApplicationUser>(o=>{o.User.RequireUniqueEmail=true;o.Password.RequiredLength=12;}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
var mail=new FakeEmail();services.AddSingleton<IEmailService>(mail);services.Configure<PersonnelOptions>(o=>o.PublicOrigin="https://example.test");services.AddScoped<OwnerEmailChangeService>();
await using var container=services.BuildServiceProvider();await using var scope=container.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
try {
 await db.Database.MigrateAsync();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();await roles.CreateAsync(new("Owner"));
 var owner=new ApplicationUser{UserName="old@example.test",Email="old@example.test",EmailConfirmed=true,FullName="Synthetic Owner"};var password="Synthetic-Only-"+Guid.NewGuid()+"aA1!";Check((await users.CreateAsync(owner,password)).Succeeded,"isolated Owner created");await users.AddToRoleAsync(owner,"Owner");
 var service=scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>();var id=owner.Id;var passwordHash=owner.PasswordHash;
 await service.Request(owner,"new@example.test");Check(owner.Email=="old@example.test"&&await users.CheckPasswordAsync(owner,password),"old login unchanged before confirmation");var token=mail.Token();
 Check(!await service.Confirm(owner,"invalid"),"invalid token refused");Check(await service.Confirm(owner,token),"valid email confirmation");Check(owner.Email=="new@example.test"&&owner.UserName==owner.Email&&owner.EmailConfirmed,"email and username updated");Check(owner.Id==id&&owner.PasswordHash==passwordHash&&await users.IsInRoleAsync(owner,"Owner"),"identity password and role preserved");Check(!await service.Confirm(owner,token),"single-use token rejected");
 await service.Request(owner,"third@example.test");token=mail.Token();await service.Cancel(owner);Check(!await service.Confirm(owner,token),"cancelled token refused");
 mail.Fail=true;try{await service.Request(owner,"failure@example.test");throw new Exception("Expected failure");}catch(EmailDeliveryException){Check(owner.Email=="new@example.test","failed delivery preserves login");}
 Check(await service.Pending(owner)==null,"failed delivery leaves no pending change");mail.Fail=false;owner=(await users.FindByIdAsync(id))!;
 await service.Request(owner,"expiry@example.test");token=mail.Token();var pending=await service.Pending(owner);await users.SetAuthenticationTokenAsync(owner,"AriesOwnerEmail","Pending",JsonSerializer.Serialize(pending! with{ExpiresUtc=DateTime.UtcNow.AddSeconds(-1)}));Check(!await service.Confirm(owner,token),"expired token refused");
 await BrowserHttpChecks.Run(connection,password);
 Console.WriteLine($"PASS: {count} service checks plus HTTP checks; disposable local database only");
} finally {await db.Database.EnsureDeletedAsync();}
} }
sealed class FakeHandler:HttpMessageHandler{public HttpStatusCode Status=HttpStatusCode.OK;public Uri? Uri;public string? Json;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Uri=r.RequestUri;Json=await r.Content!.ReadAsStringAsync(ct);return new(Status){Content=new StringContent("{}")};}}
sealed class FakeEmail:IEmailService{public bool Fail;string Body="";public Task SendEmailAsync(string to,string subject,string body){if(Fail)throw new EmailDeliveryException("ProviderUnavailable");if(body.Contains("Verify email change"))Body=body;return Task.CompletedTask;}public Task SendEmailWithAttachmentAsync(string a,string b,string c,byte[] d,string e)=>SendEmailAsync(a,b,c);public string Token(){var value=Regex.Match(Body,"Confirm#([^\"]+)").Groups[1].Value;return Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(value));}}
