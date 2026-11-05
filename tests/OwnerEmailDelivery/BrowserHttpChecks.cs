using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Services;
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
 foreach(var path in new[]{"/Personnel","/Dashboard/Owner","/Home/Packages","/Home/About","/Home/Gallery"}){response=await client.GetAsync(path);Check(response.StatusCode==HttpStatusCode.OK,"render "+path);}
 }
}
