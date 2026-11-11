using System.Net;
using AriesMagicAppointmentSystem.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
sealed class EmailFactory(string connection):WebApplicationFactory<Program>{
 protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Development");b.UseContentRoot(Directory.GetCurrentDirectory());b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:DefaultConnection"]=connection,["BackgroundJobs:Enabled"]="false",["Database:InitializeOnStartup"]="false",["Personnel:PublicOrigin"]="https://example.test",["FeedbackMedia:UploadsEnabled"]="false",["Logging:LogLevel:Default"]="Warning"}));b.ConfigureTestServices(s=>{s.AddDataProtection().UseEphemeralDataProtectionProvider();s.RemoveAll<IEmailService>();s.AddSingleton<IEmailService,FakeEmail>();});}
}
static class BrowserHttpChecks {
 public static async Task Run(string connection,string password){
 using var f=new EmailFactory(connection);using var client=f.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new("https://localhost")});
 void Check(bool ok,string name){if(!ok)throw new Exception("FAILED: "+name);Console.WriteLine("PASS HTTP: "+name);}
 string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
 var anon=await client.GetAsync("/OwnerEmail");Check(anon.StatusCode==HttpStatusCode.Redirect,"anonymous denied Owner settings");
 var html=await client.GetStringAsync("/Account/Login");var response=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","new@example.test"},{"Password",password},{"__RequestVerificationToken",Token(html)}}));Check(response.StatusCode==HttpStatusCode.Redirect,"existing password new email login");
 response=await client.GetAsync("/OwnerEmail");html=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK&&html.Contains("Change login email"),"Owner settings renders");Check(html.Contains("__RequestVerificationToken")&&!html.Contains(password),"antiforgery and no password echo");
 Directory.CreateDirectory(".tmp-owner-build/owner-email");await File.WriteAllTextAsync(".tmp-owner-build/owner-email/settings.synthetic.html",html);
 response=await client.PostAsync("/OwnerEmail/SendVerification",new FormUrlEncodedContent(new Dictionary<string,string>{{"NewEmail","blocked@example.test"}}));Check(response.StatusCode==HttpStatusCode.BadRequest,"missing antiforgery denied");
 using(var scope=f.Services.CreateScope()) {
 var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();var u=(await users.FindByEmailAsync("new@example.test"))!;
 await scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>().Cancel(u);
 foreach(var role in new[]{"Staff","Client"}){if(!await roles.RoleExistsAsync(role))await roles.CreateAsync(new(role));var other=new ApplicationUser{Email=role+"@example.test",UserName=role+"@example.test",EmailConfirmed=true,FullName="Synthetic "+role};await users.CreateAsync(other,password);await users.AddToRoleAsync(other,role);}
 }
 async Task<HttpResponseMessage> Send(string pass,string? code=null,string address="proposed@example.test") {
 var form=await client.GetStringAsync("/OwnerEmail");return await client.PostAsync("/OwnerEmail/SendVerification",new FormUrlEncodedContent(new Dictionary<string,string>{{"NewEmail",address},{"ConfirmEmail",address},{"CurrentPassword",pass},{"AuthenticatorCode",code??""},{"__RequestVerificationToken",Token(form)}}));}
 async Task<bool> HasPending(){using var scope=f.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();return await scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>().Pending((await users.FindByEmailAsync("new@example.test"))!)!=null;}
 await Send("wrong-password");Check(!await HasPending(),"wrong password creates no pending change");
 using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;await users.ResetAuthenticatorKeyAsync(u);await users.SetTwoFactorEnabledAsync(u,true);}
 async Task MfaRequest(string? code){using var scope=f.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var signIn=scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;var context=new DefaultHttpContext{RequestServices=scope.ServiceProvider,User=await signIn.CreateUserPrincipalAsync(u)};signIn.Context=context;var controller=new OwnerEmailController(users,signIn,scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>()){ControllerContext=new ControllerContext{HttpContext=context},Url=new TestUrlHelper(),TempData=new TempDataDictionary(context,new EmptyTempData())};await controller.SendVerification(new(){NewEmail="mfa@example.test",ConfirmEmail="mfa@example.test",CurrentPassword=password,AuthenticatorCode=code});}
 await MfaRequest(null);Check(!await HasPending(),"missing authenticator refused");await MfaRequest("invalid");Check(!await HasPending(),"invalid authenticator refused");
 using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;Check(await users.GetAccessFailedCountAsync(u)>0,"authenticator failures count toward lockout");}
 using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;await MfaRequest(Totp.Code((await users.GetAuthenticatorKeyAsync(u))!));Check(await HasPending(),"valid password and authenticator accepted");await scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>().Cancel(u);await users.SetTwoFactorEnabledAsync(u,false);await users.ResetAccessFailedCountAsync(u);}
 html=await client.GetStringAsync("/Account/Login");await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","new@example.test"},{"Password",password},{"__RequestVerificationToken",Token(html)}}));
 await Send(password,address:"Staff@example.test");Check(!await HasPending(),"duplicate email refused without mutation");
 await Send(password);Check(await HasPending(),"correct reauthentication submits request");
 using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;var pending=await scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>().Pending(u);Check(pending!.Email=="proposed@example.test","pending target server-controlled");}
 await Send(password,address:"cooldown@example.test");using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("new@example.test"))!;Check((await scope.ServiceProvider.GetRequiredService<OwnerEmailChangeService>().Pending(u))!.Email=="proposed@example.test","resend cooldown preserves issued request");}
 foreach(var role in new[]{"Staff","Client"}){using var other=f.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new("https://localhost")});var login=await other.GetStringAsync("/Account/Login");await other.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email",role+"@example.test"},{"Password",password},{"__RequestVerificationToken",Token(login)}}));var denied=await other.GetAsync("/OwnerEmail");Check(denied.StatusCode==HttpStatusCode.Forbidden||(denied.StatusCode==HttpStatusCode.Redirect&&denied.Headers.Location!.ToString().Contains("AccessDenied")),role+" cannot access Owner changes");}
 foreach(var path in new[]{"/Personnel","/Dashboard/Owner","/Home/Packages","/Home/About","/Home/Gallery"}){response=await client.GetAsync(path);Check(response.StatusCode==HttpStatusCode.OK,"render "+path);}
 // Let the real account rate-limit window expire; do not weaken production rate limiting for this suite.
 await Task.Delay(TimeSpan.FromSeconds(61));
 using var second=f.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new("https://localhost")});
 async Task<HttpResponseMessage> Login(HttpClient browser,string address){var form=await browser.GetStringAsync("/Account/Login");return await browser.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email",address},{"Password",password},{"__RequestVerificationToken",Token(form)}}));}
 await Login(second,"new@example.test");Check((await second.GetAsync("/OwnerEmail")).StatusCode==HttpStatusCode.OK,"old login works while change pending");
 var confirm=await client.GetStringAsync("/OwnerEmail/Confirm");await File.WriteAllTextAsync(".tmp-owner-build/owner-email/confirm.synthetic.html",confirm);var delivery=(FakeEmail)f.Services.GetRequiredService<IEmailService>();
 response=await client.PostAsync("/OwnerEmail/Confirm",new FormUrlEncodedContent(new Dictionary<string,string>{{"token",delivery.Token()},{"__RequestVerificationToken",Token(confirm)}}));Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("Login"),"confirmation signs current Owner out");
 Check((await second.GetAsync("/OwnerEmail")).StatusCode==HttpStatusCode.Redirect,"old Owner session revoked after confirmation");
 using var fresh=f.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new("https://localhost")});Check((await Login(fresh,"new@example.test")).StatusCode==HttpStatusCode.OK,"old email login rejected after confirmation");Check((await Login(fresh,"proposed@example.test")).StatusCode==HttpStatusCode.Redirect,"verified new email login succeeds");
 using(var scope=f.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var u=(await users.FindByEmailAsync("proposed@example.test"))!;Check(await users.IsInRoleAsync(u,"Owner")&&await users.CheckPasswordAsync(u,password),"HTTP change preserves Owner role and password");}
 }
}

sealed class EmptyTempData:ITempDataProvider {public IDictionary<string,object> LoadTempData(HttpContext c)=>new Dictionary<string,object>();public void SaveTempData(HttpContext c,IDictionary<string,object> values){}}

static class Totp {public static string Code(string key){var bytes=new List<byte>();int bits=0,value=0;foreach(char c in key.ToUpperInvariant()){int x="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);if(x<0)continue;value=(value<<5)|x;bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(value>>bits));}}var counter=BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);if(BitConverter.IsLittleEndian)Array.Reverse(counter);var hash=System.Security.Cryptography.HMACSHA1.HashData(bytes.ToArray(),counter);int offset=hash[^1]&15;int number=((hash[offset]&127)<<24)|(hash[offset+1]<<16)|(hash[offset+2]<<8)|hash[offset+3];return (number%1000000).ToString("D6");}}

sealed class TestUrlHelper:Microsoft.AspNetCore.Mvc.IUrlHelper {public ActionContext ActionContext=>new();public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext c)=>"/OwnerEmail";public string? Content(string? s)=>s;public bool IsLocalUrl(string? u)=>true;public string? Link(string? n,object? v)=>null;public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext c)=>null;}
