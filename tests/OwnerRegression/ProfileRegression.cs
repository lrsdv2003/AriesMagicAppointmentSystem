using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static class ProfileRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>(o => {
            o.Password.RequiredLength=6;o.Password.RequireDigit=false;o.Password.RequireLowercase=false;
            o.Password.RequireUppercase=false;o.Password.RequireNonAlphanumeric=false;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider=services.BuildServiceProvider();
        var manager=provider.GetRequiredService<UserManager<ApplicationUser>>();
        var policies=provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization=provider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal Principal(string role,string id) => new(new ClaimsIdentity([new Claim(ClaimTypes.Role,role),new Claim(ClaimTypes.NameIdentifier,id)],"Fixture"));
        var policy=await AuthorizationPolicy.CombineAsync(policies,typeof(InternalProfileController).GetCustomAttributes<AuthorizeAttribute>());
        foreach(var role in new[]{"Staff","Admin","Owner","Client"})
            check((await authorization.AuthorizeAsync(Principal(role,"fixture"),null,policy!)).Succeeded==(role!="Client"),role+" profile authorization");
        check(!(await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()),null,policy!)).Succeeded,"Anonymous profile access denied");
        foreach(var name in new[]{"Edit","ChangePassword","UploadPhoto","RemovePhoto"})
            check(typeof(InternalProfileController).GetMethod(name)!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)),"Profile "+name+" requires anti-forgery");
        check(typeof(InternalPersonalInput).GetProperties().Select(p=>p.Name).Order().SequenceEqual(new[]{"FullName","PhoneNumber"}),"Profile binding accepts only name and phone");
        var directory=Path.Combine(Path.GetTempPath(),"AriesProfileTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var environment=new ProfileEnvironment {ContentRootPath=directory,WebRootPath=Path.Combine(directory,"wwwroot")};
        try {
            var other=new ApplicationUser {UserName="profile-other",Email="profile-other@example.test",FullName="Other User"};
            await manager.CreateAsync(other,"initial");
            foreach(var role in new[]{"Staff","Admin","Owner"}) {
                var user=new ApplicationUser {UserName="profile-"+role,Email="profile-"+role+"@example.test",FullName="Original Name",EmailConfirmed=true};
                check((await manager.CreateAsync(user,"initial")).Succeeded,"Create profile test "+role);
                await manager.AddToRoleAsync(user,role);
                var http=new DefaultHttpContext {User=Principal(role,user.Id)};
                http.Request.QueryString=new QueryString("?id="+other.Id+"&Role=Owner&IsActive=false&Email=evil@example.test");
                var signIn=new ProfileSignIn(manager);
                var controller=new InternalProfileController(manager,signIn,environment,new SystemActivityService(db),NullLogger<InternalProfileController>.Instance) {
                    ControllerContext=new ControllerContext {HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())
                };
                var vm=(InternalProfileViewModel)((ViewResult)await controller.Index()).Model!;
                check(vm.Role==role && vm.PasswordRules.SequenceEqual(new[]{"At least 6 characters."}),role+" profile reflects role and configured password policy");
                await controller.Edit(new() {FullName="Updated "+role,PhoneNumber="09123456789"});
                check(user.FullName=="Updated "+role && other.FullName=="Other User" && user.Email=="profile-"+role+"@example.test" && user.IsActive && user.EmailConfirmed && (await manager.GetRolesAsync(user)).SequenceEqual(new[]{role}),role+" updates only authenticated account and permitted fields");
                controller.ModelState.Clear();
                await controller.Edit(new() {FullName="  "});
                check(!controller.ModelState.IsValid && user.FullName=="Updated "+role,"Whitespace name is rejected for "+role);
                controller.ModelState.Clear();
                var invalidPhone=new InternalPersonalInput {FullName="Test User",PhoneNumber="invalid"};
                check(!Validator.TryValidateObject(invalidPhone,new ValidationContext(invalidPhone),new List<ValidationResult>(),true),"Phone format validates on server");
                await controller.ChangePassword(new() {CurrentPassword="wrong",NewPassword="newpass",ConfirmPassword="newpass"});
                check(!controller.ModelState.IsValid && await manager.CheckPasswordAsync(user,"initial"),role+" incorrect current password rejected");
                controller.ModelState.Clear();
                await controller.ChangePassword(new() {CurrentPassword="initial",NewPassword="short",ConfirmPassword="short"});
                check(!controller.ModelState.IsValid && await manager.CheckPasswordAsync(user,"initial"),role+" Identity password policy preserved");
                controller.ModelState.Clear();
                var mismatch=new InternalPasswordInput {CurrentPassword="initial",NewPassword="newpass",ConfirmPassword="different"};
                check(!Validator.TryValidateObject(mismatch,new ValidationContext(mismatch),new List<ValidationResult>(),true),"Password confirmation validates on server");
                var oldStamp=user.SecurityStamp;
                var changed=(RedirectToActionResult)await controller.ChangePassword(new() {CurrentPassword="initial",NewPassword="newpass",ConfirmPassword="newpass"});
                check(await manager.CheckPasswordAsync(user,"newpass") && !await manager.CheckPasswordAsync(user,"initial") && signIn.Refreshed && oldStamp!=user.SecurityStamp && changed.Fragment=="security",role+" password change rotates security stamp and refreshes current sign-in");
                FormFile Photo(byte[] data,string name,string type) => new(new MemoryStream(data),0,data.Length,"photo",name){Headers=new HeaderDictionary(),ContentType=type};
                controller.ModelState.Clear();
                await controller.UploadPhoto(Photo(new byte[20],"fake.png","image/png"));
                check(!controller.ModelState.IsValid && user.ProfilePicturePath==null,"Fake image signature rejected");
                controller.ModelState.Clear();
                await controller.UploadPhoto(Photo(new byte[2*1024*1024+1],"large.png","image/png"));
                check(!controller.ModelState.IsValid && user.ProfilePicturePath==null,"Oversized photo rejected");
                controller.ModelState.Clear();
                var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
                await controller.UploadPhoto(Photo(png,"wrong.jpg","image/jpeg"));
                check(!controller.ModelState.IsValid && user.ProfilePicturePath==null,"Mismatched extension and signature rejected");
                controller.ModelState.Clear();
                await controller.UploadPhoto(Photo(png,"../../unsafe.png","image/png"));
                var first=user.ProfilePicturePath!;
                var stored=Path.Combine(directory,"App_Data","profiles",Path.GetFileName(first));
                check(first.StartsWith("/private-profiles/") && File.Exists(stored) && !Directory.Exists(environment.WebRootPath),"Photo uses generated name outside public web root");
                check(await controller.Photo() is PhysicalFileResult && http.Response.Headers["X-Content-Type-Options"]=="nosniff","Photo endpoint returns authenticated account image");
                controller.HttpContext.User=Principal(role,other.Id);
                check(await controller.Photo() is NotFoundResult,"Other account cannot read uploaded photo through query ID");
                controller.HttpContext.User=Principal(role,user.Id);
                await controller.UploadPhoto(Photo(png,"replacement.png","image/png"));
                check(!File.Exists(stored) && user.ProfilePicturePath!=first,"Replacing photo cleans previous file after save");
                var replacement=Path.Combine(directory,"App_Data","profiles",Path.GetFileName(user.ProfilePicturePath!));
                await controller.RemovePhoto();
                check(user.ProfilePicturePath==null && !File.Exists(replacement),"Remove photo clears data and file");
                var logs=db.SystemActivities.Where(a=>a.PerformedByUserId==user.Id).ToList();
                check(logs.Any(a=>a.Type==SystemActivityType.ProfileUpdated) && logs.Any(a=>a.Type==SystemActivityType.PasswordChanged) && logs.Any(a=>a.Type==SystemActivityType.ProfilePictureChanged) &&
                    logs.All(a=>!a.Description.Contains("newpass") && !(a.MetadataJson??"").Contains("newpass")),"Account changes audited without passwords");
            }
        } finally { Directory.Delete(directory,true); }
    }
}
sealed class ProfileSignIn : SignInManager<ApplicationUser>
{
    public bool Refreshed {get;private set;}
    public ProfileSignIn(UserManager<ApplicationUser> users) : base(users,new HttpContextAccessor(),new UserClaimsPrincipalFactory<ApplicationUser>(users,Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),NullLogger<SignInManager<ApplicationUser>>.Instance,null!,new DefaultUserConfirmation<ApplicationUser>()) {}
    public override Task RefreshSignInAsync(ApplicationUser user) { Refreshed=true;return Task.CompletedTask; }
}
sealed class ProfileEnvironment : IWebHostEnvironment
{
    public string ApplicationName {get;set;}="";
    public string EnvironmentName {get;set;}="Development";
    public string ContentRootPath {get;set;}="";
    public string WebRootPath {get;set;}="";
    public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
    public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
}
