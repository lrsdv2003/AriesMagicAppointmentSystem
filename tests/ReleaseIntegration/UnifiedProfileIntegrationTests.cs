using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class UnifiedProfileIntegrationTests
{
    private static int checks;
    private static void Check(bool yes, string label)
    {
        if(!yes) throw new InvalidOperationException("FAILED Unified Profile: "+label);
        checks++;Console.WriteLine("PASS Unified Profile: "+label);
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(
        html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<HttpClient> Login(ReleaseFactory factory,string email,string password)
    {
        var http=factory.CreateClient(new WebApplicationFactoryClientOptions{HandleCookies=true,AllowAutoRedirect=false});
        var page=await http.GetStringAsync("/Account/Login");
        Check(Token(page).Length>0,"login anti-forgery field exists");
        using var reply=await http.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{
            ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=Token(page)
        }));
        Check(reply.StatusCode==HttpStatusCode.Redirect &&
            !(reply.Headers.Location?.OriginalString ?? "").StartsWith("/Account/VerifyEmail",StringComparison.OrdinalIgnoreCase),
            "authenticated Identity login succeeds without verification redirect");
        return http;
    }
    private static async Task<(HttpStatusCode Status,string Body)> Read(HttpClient client,string url)
    {
        using var reply=await client.GetAsync(url);
        return (reply.StatusCode,await reply.Content.ReadAsStringAsync());
    }
    private static async Task<HttpResponseMessage> Submit(
        HttpClient client,string url,string token,params (string,string)[] values)
    {
        var data=values.ToDictionary(x=>x.Item1,x=>x.Item2);
        data["__RequestVerificationToken"]=token;
        return await client.PostAsync(url,new FormUrlEncodedContent(data));
    }
    private static byte[] Png()
    {
        using var bitmap=new SkiaSharp.SKBitmap(24,24);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var img=SkiaSharp.SKImage.FromBitmap(bitmap);
        using var bytes=img.Encode(SkiaSharp.SKEncodedImageFormat.Png,95);
        return bytes.ToArray();
    }
    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        var inst=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(inst,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Unsafe test SQL instance");
        var dbname="AriesUnifiedProfile_"+Guid.NewGuid().ToString("N");
        var connection=$@"Server=(localdb)\{inst};Database={dbname};Integrated Security=True;TrustServerCertificate=True";
        var root=Path.Combine(Path.GetTempPath(),dbname);
        Directory.CreateDirectory(root);
        var fixtureRoot=Environment.GetEnvironmentVariable("ARIES_UNIFIED_PROFILE_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtureRoot))
        {
            fixtureRoot=Path.GetFullPath(fixtureRoot);
            if(!fixtureRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixtures must be under Temp");
            Directory.CreateDirectory(fixtureRoot);
        }
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();smtp.Start();
        var testPhotoPaths = new List<string>();
        try
        {
            await db.Database.MigrateAsync();
            await using var factory=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:9"),root);
            using var scope=factory.Services.CreateScope();
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var initial="InitialProfile2026!Safe";
            var next="ChangedProfile2026!Safe";
            foreach(var role in new[]{"Client","Staff","Owner"})
                if(!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded,"isolated "+role+" role");
            var accounts=new Dictionary<string,ApplicationUser>();
            foreach(var role in new[]{"Client","Staff","Owner"})
            {
                var email="profile-"+role.ToLowerInvariant()+"@example.test";
                var u=new ApplicationUser{
                    UserName=email,Email=email,FullName="Synthetic "+role,
                    PhoneNumber="09123456789",EmailConfirmed=true,
                    CreatedAt=DateTime.UtcNow.AddDays(-110),
                    LastLoginAt=DateTime.UtcNow.AddDays(-2),IsActive=true
                };
                Check((await users.CreateAsync(u,initial)).Succeeded &&
                    (await users.AddToRoleAsync(u,role)).Succeeded,
                    "created isolated authenticated "+role);
                accounts[role]=u;
            }
            var outsider=new ApplicationUser{
                Email="profile-outsider@example.test",UserName="profile-outsider@example.test",
                FullName="Untouched Account",EmailConfirmed=false
            };
            Check((await users.CreateAsync(outsider,initial)).Succeeded,
                "unrelated account isolated");
            foreach(var role in new[]{"Client","Staff","Owner"})
            {
                var u=accounts[role];
                var baseRoute=role=="Client"?"/Profile":"/InternalProfile";
                using var http=await Login(factory,u.Email!,initial);
                // A previously verified Staff member may become unverified while
                // retaining an authenticated session. Ensure UI reflects stored state.
                if(role=="Staff") await db.Users.Where(x=>x.Id==u.Id)
                    .ExecuteUpdateAsync(setters=>setters.SetProperty(x=>x.EmailConfirmed,false));
                var (status,html)=await Read(http,baseRoute+"/Index");
                Check(status==HttpStatusCode.OK &&
                    html.Contains("class=\"unified-profile\"",StringComparison.Ordinal) &&
                    html.Contains("class=\"up-grid\"",StringComparison.Ordinal) &&
                    html.Contains("Personal Information",StringComparison.Ordinal) &&
                    html.Contains("Account Information",StringComparison.Ordinal) &&
                    html.Contains("Change Password",StringComparison.Ordinal),
                    role+" shared full-width five-section view");
                Check(html.Contains("Synthetic "+role,StringComparison.Ordinal) &&
                    html.Contains(u.Email!,StringComparison.Ordinal) &&
                    html.Contains("Member Since",StringComparison.Ordinal) &&
                    html.Contains("Last Sign-in (UTC)",StringComparison.Ordinal),
                    role+" displays actual identity and account dates");
                Check(!html.Contains("name=\"Email\"",StringComparison.Ordinal) &&
                    !html.Contains("name=\"Role\"",StringComparison.Ordinal) &&
                    html.Contains((role=="Staff"?"Unverified":"Verified"),StringComparison.Ordinal) &&
                    html.Contains("Active",StringComparison.Ordinal),
                    role+" email/role read-only, accurate verification and status");
                Check(html.Contains("name=\"Personal.FullName\"",StringComparison.Ordinal) &&
                    html.Contains("name=\"Personal.PhoneNumber\"",StringComparison.Ordinal) &&
                    (role=="Owner"
                        ? !html.Contains("name=\"Password.CurrentPassword\"",StringComparison.Ordinal)
                          && !html.Contains("class=\"up-card up-security\"",StringComparison.Ordinal)
                          && html.Contains("/InternalProfile/Password",StringComparison.Ordinal)
                        : html.Contains("name=\"Password.CurrentPassword\"",StringComparison.Ordinal)
                          && html.Contains("minlength=\"12\"",StringComparison.Ordinal)),
                    role+" editable fields and role-appropriate password location");
                if(role=="Owner")
                {
                    var separate=await Read(http,"/InternalProfile/Password");
                    Check(separate.Status==HttpStatusCode.OK &&
                          separate.Body.Contains("name=\"Password.CurrentPassword\"",StringComparison.Ordinal) &&
                          separate.Body.Contains("minlength=\"12\"",StringComparison.Ordinal),
                          "Owner dedicated password view preserves existing 12-character password form");
                }
                Check(html.Contains("name=\"photo\"",StringComparison.Ordinal) &&
                    html.Contains("name=\"__RequestVerificationToken\"",StringComparison.Ordinal) &&
                    html.Contains("/Account/Logout",StringComparison.Ordinal),
                    role+" upload and logout keep antiforgery controls");
                if(fixtureRoot!=null)await File.WriteAllTextAsync(Path.Combine(fixtureRoot,role.ToLowerInvariant()+"-profile.html"),html);
                var token=Token(html);
                using(var invalid=await Submit(http,baseRoute+"/Edit",token,
                    ("Personal.FullName"," "),("Personal.PhoneNumber","09123456789")))
                {
                    var body=await invalid.Content.ReadAsStringAsync();
                    Check(invalid.StatusCode==HttpStatusCode.OK &&
                        Regex.IsMatch(body, @"data-valmsg-for=""Personal.FullName""[^>]*>[^<]+"),
                        role+" rejects whitespace-only name, retains form");
                }
                using(var saved=await Submit(http,baseRoute+"/Edit",token,
                    ("Personal.FullName","Updated "+role),
                    ("Personal.PhoneNumber","09987654321"),
                    ("Email","changed@example.test"),("Role","Owner"),
                    ("IsActive","false"),("EmailConfirmed","false")))
                    Check(saved.StatusCode==HttpStatusCode.Redirect,
                        role+" updates valid personal information");
                var fresh=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id);
                Check(fresh?.FullName=="Updated "+role &&
                    fresh.PhoneNumber=="09987654321" &&
                    fresh.Email==u.Email && fresh.IsActive &&
                    fresh.EmailConfirmed==(role!="Staff") &&
                    (await users.GetRolesAsync(fresh!)).SequenceEqual(new[]{role}) &&
                    (await db.Users.AsNoTracking().SingleAsync(x=>x.Id==outsider.Id))?.FullName=="Untouched Account",
                    role+" cannot change account ID, role, email, or verification through Edit");
                var latest=await Read(http,baseRoute+"/Index");
                token=Token(latest.Body);
                using(var invalidPhoto=new MultipartFormDataContent())
                {
                    var invalidBytes=new ByteArrayContent(new byte[20]);
                    invalidBytes.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    invalidPhoto.Add(invalidBytes,"photo","spoofed.png");
                    invalidPhoto.Add(new StringContent(token),"__RequestVerificationToken");
                    using var response=await http.PostAsync(baseRoute+"/UploadPhoto",invalidPhoto);
                    Check(response.StatusCode==HttpStatusCode.OK &&
                        (await response.Content.ReadAsStringAsync()).Contains("Choose a valid JPG",StringComparison.Ordinal) &&
                        (await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id))?.ProfilePicturePath==null,
                        role+" server rejects corrupt image and preserves old avatar");
                }
                using(var realPhoto=new MultipartFormDataContent())
                {
                    var png=new ByteArrayContent(Png());
                    png.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    realPhoto.Add(png,"photo","picture.png");
                    realPhoto.Add(new StringContent(token),"__RequestVerificationToken");
                    using var response=await http.PostAsync(baseRoute+"/UploadPhoto",realPhoto);
                    Check(response.StatusCode==HttpStatusCode.Redirect,
                        role+" accepts decoded valid image via authenticated upload");
                }
                fresh=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id);
                var currentFile=fresh!.ProfilePicturePath;
                var profileContentRoot=factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath;
                var storedPath=Path.Combine(profileContentRoot,"App_Data","profiles",Path.GetFileName(currentFile!));
                if(Guid.TryParseExact(Path.GetFileNameWithoutExtension(storedPath),"N",out _)) testPhotoPaths.Add(storedPath);
                Check(currentFile?.StartsWith("/private-profiles/",StringComparison.Ordinal)==true &&
                    File.Exists(storedPath),
                    role+" photo stored under opaque private server filename");
                using(var photo=await http.GetAsync(baseRoute+"/Photo"))
                    Check(photo.StatusCode==HttpStatusCode.OK &&
                        photo.Content.Headers.ContentType?.MediaType=="image/png" &&
                        photo.Headers.TryGetValues("X-Content-Type-Options",out var values) &&
                        values.Contains("nosniff"),
                        role+" authorized image retrieval uses safe content type");
                using(var remove=await Submit(http,baseRoute+"/RemovePhoto",token))
                    Check(remove.StatusCode==HttpStatusCode.Redirect,
                        role+" photo removal authenticated");
                Check((await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id))?.ProfilePicturePath==null &&
                    !File.Exists(storedPath),
                    role+" removal affects only own private photo");
                using(var weak=await Submit(http,baseRoute+"/ChangePassword",token,
                    ("Password.CurrentPassword",initial),("Password.NewPassword","Short123"),
                    ("Password.ConfirmPassword","Short123")))
                {
                    Check(weak.StatusCode==HttpStatusCode.OK &&
                        await users.CheckPasswordAsync(fresh,initial),
                        role+" rejects short new password");
                }
                using(var wrong=await Submit(http,baseRoute+"/ChangePassword",token,
                    ("Password.CurrentPassword","InvalidCurrent2026!"),
                    ("Password.NewPassword",next),("Password.ConfirmPassword",next)))
                    Check(wrong.StatusCode==HttpStatusCode.OK &&
                        await users.CheckPasswordAsync(fresh,initial),
                        role+" wrong current password rejected");
                using(var updated=await Submit(http,baseRoute+"/ChangePassword",token,
                    ("Password.CurrentPassword",initial),("Password.NewPassword",next),
                    ("Password.ConfirmPassword",next)))
                    Check(updated.StatusCode==HttpStatusCode.Redirect &&
                        await users.CheckPasswordAsync(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id),next) &&
                        !await users.CheckPasswordAsync(await db.Users.AsNoTracking().SingleAsync(x=>x.Id==u.Id),initial),
                        role+" password changed only after secure Identity verification");
                var postPassword=await Read(http,baseRoute+"/Index");
                Check(postPassword.Status==HttpStatusCode.OK &&
                    postPassword.Body.Contains("Password changed successfully",StringComparison.Ordinal),
                    role+" password result confirmed without displaying secret");
                using(var loggedOut=await Submit(http,"/Account/Logout",Token(postPassword.Body)))
                    Check(loggedOut.StatusCode==HttpStatusCode.Redirect,
                        role+" successful secure signout");
                using(var denied=await http.GetAsync(baseRoute+"/Index"))
                    Check(denied.StatusCode!=HttpStatusCode.OK,
                        role+" profile no longer accessible with signed-out cookie");
            }
            using(var noAuth=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false}))
            {
                Check((await Read(noAuth,"/Profile/Index")).Status!=HttpStatusCode.OK &&
                    (await Read(noAuth,"/InternalProfile/Index")).Status!=HttpStatusCode.OK,
                    "anonymous profiles inaccessible");
            }
            Console.WriteLine($"UNIFIED_PROFILE_INTEGRATION_PASS: {checks} authenticated SQL/MVC checks");
        }
        finally
        {
            foreach(var photo in testPhotoPaths) if(File.Exists(photo)) File.Delete(photo);
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
