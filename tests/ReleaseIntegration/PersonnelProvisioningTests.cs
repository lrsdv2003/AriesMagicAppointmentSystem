using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static class PersonnelProvisioningTests
{
    public static async Task RunAsync()
    {
        var checks=0;void Check(bool value,string message){if(!value)throw new Exception("FAILED: "+message);checks++;Console.WriteLine("PASS: "+message);}
        async Task Denied(Func<Task> action,string message){try{await action();Check(false,message);}catch(Exception e)when(e is UnauthorizedAccessException or InvalidOperationException){Check(true,message);}}
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))throw new Exception("Invalid local test instance.");
        var name="AriesPersonnel_"+Guid.NewGuid().ToString("N");var connection=Environment.GetEnvironmentVariable("ARIES_TEST_USE_DEFAULT_SQL")=="true"?$"Server=.;Database={name};Integrated Security=True;TrustServerCertificate=True":$"Server=(localdb)\\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        var dbOptions=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var schema=new ApplicationDbContext(dbOptions);var root=Path.Combine(Path.GetTempPath(),name);
        await using var smtp=new SmtpFixture();smtp.Start();
        try {
            await schema.Database.MigrateAsync();
            await using var factory=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),root);
            using var scope=factory.Services.CreateScope();var services=scope.ServiceProvider;
            var db=services.GetRequiredService<ApplicationDbContext>();var users=services.GetRequiredService<UserManager<ApplicationUser>>();var service=services.GetRequiredService<PersonnelProvisioning>();
            Check(new PersonnelOptions().PublicOrigin=="https://ariesmagicartist-001-site1.htempurl.com","Known production HTTPS enrollment origin is the safe default when pool config is absent");
            var options=services.GetRequiredService<IOptions<PersonnelOptions>>().Value;options.PublicOrigin="https://personnel.example.test";
            await IdentitySeeder.SeedAsync(services);await IdentitySeeder.SeedAsync(services);
            Check(await db.Roles.CountAsync()==3&&!await db.Users.AnyAsync(),"Idempotent role-only initialization creates no privileged users");
            async Task<EnrollmentDelivery?> AttemptBootstrap(){using var s=factory.Services.CreateScope();try{return await s.ServiceProvider.GetRequiredService<PersonnelProvisioning>().InviteAsync(null,"owner@example.test","Test Owner","Owner",true);}catch(InvalidOperationException){return null;}}
            var attempts=await Task.WhenAll(AttemptBootstrap(),AttemptBootstrap());Check(attempts.Count(x=>x!=null)==1,"Concurrent initial bootstrap permits exactly one pending Owner");
            var first=attempts.Single(x=>x!=null)!;var owner=await users.FindByIdAsync(first.UserId)??throw new Exception();
            Check(!owner.IsActive&&!owner.EmailConfirmed&&!await users.HasPasswordAsync(owner)&&(await users.GetRolesAsync(owner)).Count==0,"Pending Owner has no password, privileges or verified email");
            using(var pendingClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true})) {
                var page=await pendingClient.GetStringAsync("/Account/ForgotPassword");var csrf=WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);var mailCount=smtp.Messages.Count;
                using var response=await pendingClient.PostAsync("/Account/ForgotPassword",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"Email",owner.Email!}}));
                Check(smtp.Messages.Count==mailCount&&!await users.HasPasswordAsync(owner),"Pending enrollment cannot bypass activation through password reset");
            }
            Check(!(await users.GetAuthenticationTokenAsync(owner,PersonnelProvisioning.Provider,PersonnelProvisioning.TokenName))!.Contains(first.Secret),"Only invitation digest stored in SQL");
            await Denied(()=>service.InviteAsync(null,"second@example.test","Other","Owner",true),"Repeated bootstrap refused even before enrollment");
            var password="Synthetic-"+Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
            Check(!(await service.EnrollAsync(first.UserId,new string('A',64),password)).Succeeded,"Invalid enrollment token rejected");
            Check(!(await service.EnrollAsync(first.UserId,first.Secret,"short")).Succeeded,"Identity password policy enforced without consuming valid link");
            Check((await service.EnrollAsync(first.UserId,first.Secret,password)).Succeeded,"Initial Owner completes verified enrollment with own password");
            Check(owner.EmailConfirmed&&owner.IsActive&&await users.IsInRoleAsync(owner,"Owner"),"Verified active Owner assigned using Identity");
            Check(!(await service.EnrollAsync(first.UserId,first.Secret,password)).Succeeded,"Enrollment token cannot be reused");
            await Denied(()=>service.InviteAsync(owner,"bad@example.test","Bad","Owner"),"Owner web invitation cannot create another Owner");
            await Denied(()=>service.InviteAsync(owner,"new-admin@example.test","Forbidden Admin","Admin"),"Owner cannot issue obsolete Admin invitation");
            var roleManager=services.GetRequiredService<RoleManager<IdentityRole>>();await roleManager.CreateAsync(new IdentityRole("Admin"));
            var admin=new ApplicationUser{UserName="legacy-admin@example.test",Email="legacy-admin@example.test",FullName="Legacy fixture",EmailConfirmed=true};await users.CreateAsync(admin,password);await users.AddToRoleAsync(admin,"Admin");
            await Denied(()=>service.InviteAsync(admin,"staff-denied@example.test","Denied","Staff"),"Legacy Admin cannot delegate Staff invitations");
            var staffLink=await service.InviteAsync(owner,"staff@example.test","Test Staff","Staff");
            using(var adminBrowser=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true})) {
                var loginPage=await adminBrowser.GetStringAsync("/Account/Login");var csrf=WebUtility.HtmlDecode(Regex.Match(loginPage,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
                using var loginResult=await adminBrowser.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"Email",admin.Email!},{"Password",password}}));
                Check(loginResult.StatusCode==HttpStatusCode.OK&&!(loginResult.Headers.TryGetValues("Set-Cookie",out var cookies)&&cookies.Any(x=>x.StartsWith(".AspNetCore.Identity.Application="))),"Legacy-only credentials do not sign in");
                using var unavailable=await adminBrowser.GetAsync("/Personnel");Check(unavailable.StatusCode==HttpStatusCode.Redirect,"Legacy Admin cannot open consolidated personnel management");
            }
            var expiredUser=await users.FindByIdAsync(staffLink.UserId)??throw new Exception();var pending=(await service.PendingAsync(expiredUser))!;
            await users.SetAuthenticationTokenAsync(expiredUser,PersonnelProvisioning.Provider,PersonnelProvisioning.TokenName,JsonSerializer.Serialize(pending with {ExpiresUtc=DateTime.UtcNow.AddSeconds(-1)}));
            Check(!(await service.EnrollAsync(staffLink.UserId,staffLink.Secret,password)).Succeeded,"Expired invitation rejected");
            var rotated=await service.ReissueAsync(owner,staffLink.UserId);
            Check(!(await service.EnrollAsync(staffLink.UserId,staffLink.Secret,password)).Succeeded,"Resend invalidates previous invitation token");
            async Task<bool> Redeem(){using var s=factory.Services.CreateScope();return (await s.ServiceProvider.GetRequiredService<PersonnelProvisioning>().EnrollAsync(rotated.UserId,rotated.Secret,password)).Succeeded;}
            var redeemed=await Task.WhenAll(Redeem(),Redeem());Check(redeemed.Count(x=>x)==1,"Concurrent single-use redemption activates once");
            await db.Entry(expiredUser).ReloadAsync();var staff=expiredUser;
            await Denied(()=>service.InviteAsync(staff,"escalate@example.test","Bad","Admin"),"Staff cannot invite privileged users");
            await Denied(()=>service.InviteAsync(admin,"admin-bad@example.test","Bad","Admin"),"Admin cannot invite Admin");
            await Denied(()=>service.ManageAsync(admin,owner.Id,"Role","Staff"),"Admin cannot modify Owner");
            await Denied(()=>service.ManageAsync(owner,staff.Id,"Role","Owner"),"Role modification cannot promote Owner");
            var stamp=await users.GetSecurityStampAsync(staff);await service.ManageAsync(owner,staff.Id,"Deactivate");
            Check(!staff.IsActive&&await users.GetSecurityStampAsync(staff)!=stamp,"Deactivation rotates session stamp and disables account");
            await service.ManageAsync(owner,staff.Id,"Reactivate");Check(staff.IsActive,"Verified enrolled personnel may reactivate");
            stamp=await users.GetSecurityStampAsync(staff);await service.ManageAsync(owner,staff.Id,"RevokeSessions");Check(await users.GetSecurityStampAsync(staff)!=stamp,"Explicit session revocation rotates stamp");
            await Denied(()=>service.ManageAsync(owner,staff.Id,"Role","Admin"),"Direct role API cannot grant obsolete Admin role");
            await Denied(()=>service.ManageAsync(owner,staff.Id,"Role","Staff"),"Direct role API cannot rewrite fixed Staff role");
            Check(await users.IsInRoleAsync(staff,"Staff")&&!await users.IsInRoleAsync(staff,"Admin"),"Denied role changes preserve Staff membership");
            var reset=await users.GeneratePasswordResetTokenAsync(staff);Check((await users.ResetPasswordAsync(staff,reset,password+"R")).Succeeded,"Identity password reset works after enrollment");
            var revoked=await service.InviteAsync(owner,"revoke@example.test","Revoked","Staff");await service.ManageAsync(owner,revoked.UserId,"Revoke");Check(!(await service.EnrollAsync(revoked.UserId,revoked.Secret,password)).Succeeded,"Revoked invitation cannot enroll");
            try {await service.ManageAsync(owner,revoked.UserId,"RevokeSessions");throw new Exception("Revoked target incorrectly manageable");}
            catch(PersonnelActionException ex){Check(ex.Reason=="NotManageable","Already-revoked invitation returns safe no-op classification rather than authorization failure");}
            try {await service.ManageAsync(owner,owner.Id,"RevokeSessions");throw new Exception("Self revocation incorrectly manageable");}
            catch(PersonnelActionException ex){Check(ex.Reason=="SelfManagement","Owner cannot manage own account, with safe classification");}
            await Denied(()=>service.InviteAsync(owner,"staff@example.test","Duplicate","Admin"),"Existing accounts cannot be promoted by duplicate invitation");
            using var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});
            string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var login=await client.GetStringAsync("/Account/Login");
            using var loginResponse=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Token(login)},{"Email",owner.Email!},{"Password",password}}));
            Check(loginResponse.StatusCode==HttpStatusCode.Redirect,"Owner real Identity login succeeds");
            var personnel=await client.GetStringAsync("/Personnel");
            Check(personnel.Contains("data-personnel-filters") && personnel.Contains("staff-data-table-frame"),
                "Owner management page renders the active-only Personnel workspace");
            Check(!personnel.Contains("revoke@example.test") && !personnel.Contains("Account management audit"),
                "Revoked invitation and removed audit panel are absent from active Personnel list");
            var accessPage=await client.GetStringAsync("/Personnel/Access");
            Check(accessPage.Contains("Manage Staff Access") && accessPage.Contains("/Personnel/Manage"),
                "Separate Owner Staff access lifecycle view retains authorized actions");
            await File.WriteAllTextAsync(".tmp-owner-build/personnel-owner.synthetic.html",personnel);
            async Task<HttpResponseMessage> Post(HttpClient http,string path,string token,params (string,string)[] fields)=>await http.PostAsync(path,new FormUrlEncodedContent(fields.Select(x=>new KeyValuePair<string,string>(x.Item1,x.Item2)).Append(new("__RequestVerificationToken",token))));
            using(var noCsrf=await client.PostAsync("/Personnel/Invite",new FormUrlEncodedContent(new Dictionary<string,string>{{"Role","Staff"}})))Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"Personnel invitation POST requires anti-forgery");
            using(var refused=await Post(client,"/Personnel/Manage",Token(personnel),("id",revoked.UserId),("operation","RevokeSessions")))Check(refused.StatusCode==HttpStatusCode.Redirect&&!refused.Headers.Location!.OriginalString.Contains("AccessDenied"),"Revoked invitation action does not send Owner to Access Denied");
            var revokedFeedback=await client.GetStringAsync("/Personnel/Access");Check(revokedFeedback.Contains("no active personnel role or pending invitation"),"Revoked invitation action returns helpful feedback in Staff Access workflow");
            using(var self=await Post(client,"/Personnel/Manage",Token(personnel),("id",owner.Id),("operation","RevokeSessions")))Check(self.StatusCode==HttpStatusCode.Redirect&&!self.Headers.Location!.OriginalString.Contains("AccessDenied"),"Self-management is refused without misleading Access Denied redirect");
            var selfFeedback=await client.GetStringAsync("/Personnel/Access");Check(selfFeedback.Contains("cannot change your own personnel access"),"Self-management gives an explicit explanation");
            using(var duplicate=await Post(client,"/Personnel/Invite",Token(personnel),("Email","staff@example.test"),("FullName","Existing Staff"),("Role","Staff")))Check(duplicate.StatusCode==HttpStatusCode.Redirect,"Existing email invitation request is handled safely");
            var duplicateFeedback=await client.GetStringAsync("/Personnel");Check(duplicateFeedback.Contains("email is already registered"),"Duplicate email shows clear action without promoting existing user");
            options.PublicOrigin="http://untrusted.example.test";
            using(var badOrigin=await Post(client,"/Personnel/Invite",Token(personnel),("Email","bad-origin@example.test"),("FullName","Synthetic Origin"),("Role","Staff")))Check(badOrigin.StatusCode==HttpStatusCode.Redirect,"Invalid origin is refused before issuing invitation");
            var originFeedback=await client.GetStringAsync("/Personnel");Check(originFeedback.Contains("secure enrollment URL is not configured correctly"),"Invalid origin explains configuration failure without disclosing secrets");
            Check(await users.FindByEmailAsync("bad-origin@example.test")==null,"Invalid origin creates no personnel account or token");
            options.PublicOrigin="https://personnel.example.test";
            using(var forbidden=await Post(client,"/Personnel/Invite",Token(personnel),("Email","owner-forged@example.test"),("FullName","Forged"),("Role","Owner")))Check(forbidden.StatusCode==HttpStatusCode.Forbidden||forbidden.StatusCode==HttpStatusCode.Redirect&&forbidden.Headers.Location!.OriginalString.Contains("AccessDenied"),"HTTP invitation rejects forged Owner role");
            Check(await users.FindByEmailAsync("owner-forged@example.test")==null,"Forged Owner invitation persisted no account");
            using(var invited=await Post(client,"/Personnel/Invite",Token(personnel),("Email","http-staff@example.test"),("FullName","HTTP Staff"),("Role","Staff")))Check(invited.StatusCode==HttpStatusCode.Redirect,"Owner invitation uses configured email delivery");
            var message=smtp.Messages.Last(x=>x.To.Mailboxes.Any(m=>m.Address=="http-staff@example.test"));
            var href=WebUtility.HtmlDecode(Regex.Match(message.HtmlBody??"","href=\"([^\"]+)\"").Groups[1].Value);var fragment=new Uri(href).Fragment.TrimStart('#').Split(':');
            Check(fragment.Length==2&&href.StartsWith("https://personnel.example.test/"),"Enrollment uses trusted origin and fragment-only secret");
            using var enrolling=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var enrollmentHtml=await enrolling.GetStringAsync("/PersonnelEnrollment/Accept");
            using(var accepted=await Post(enrolling,"/PersonnelEnrollment/Accept",Token(enrollmentHtml),("UserId",fragment[0]),("Token",fragment[1]),("Password",password),("ConfirmPassword",password),("Role","Owner")))Check(accepted.StatusCode==HttpStatusCode.Redirect,"HTTP email-link enrollment accepts valid password");
            var httpStaff=await users.FindByEmailAsync("http-staff@example.test")??throw new Exception();
            Check(httpStaff.EmailConfirmed&&httpStaff.IsActive&&await users.IsInRoleAsync(httpStaff,"Staff")&&!await users.IsInRoleAsync(httpStaff,"Owner"),"Enrollment ignores forged role and verifies invited email");
            // A real cookie must stop working on its next authenticated request after revocation.
            var staffLogin=await enrolling.GetStringAsync("/Account/Login");
            using(var signed=await Post(enrolling,"/Account/Login",Token(staffLogin),("Email",httpStaff.Email!),("Password",password)))Check(signed.StatusCode==HttpStatusCode.Redirect,"Enrolled Staff can sign in");
            await service.ManageAsync(owner,httpStaff.Id,"Deactivate");
            using(var oldCookie=await enrolling.GetAsync("/Dashboard/Staff"))Check(oldCookie.StatusCode==HttpStatusCode.Redirect,"Deactivation immediately rejects existing Staff cookie");
            await service.ManageAsync(owner,httpStaff.Id,"Reactivate");
            using(var reused=await Post(enrolling,"/PersonnelEnrollment/Accept",Token(enrollmentHtml),("UserId",fragment[0]),("Token",fragment[1]),("Password",password),("ConfirmPassword",password)))Check(reused.StatusCode==HttpStatusCode.OK&&!(await reused.Content.ReadAsStringAsync()).Contains(fragment[1]),"Used enrollment rejected without echoing secret");
            using(var removed=await Post(client,"/UserManagement/CreateStaff",Token(personnel),("Email","legacy-bypass@example.test"),("Password",password)))Check(removed.StatusCode==HttpStatusCode.NotFound,"Legacy direct-password onboarding cannot create users");
            Check(await users.FindByEmailAsync("legacy-bypass@example.test")==null,"Legacy direct-password request created no account");
            await using(var broken=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),root,failAuditType:SystemActivityType.UserCreated)) {
                using var bs=broken.Services.CreateScope();var bu=bs.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var actor=await bu.FindByIdAsync(owner.Id)??throw new Exception();
                await Denied(()=>bs.ServiceProvider.GetRequiredService<PersonnelProvisioning>().InviteAsync(actor,"audit-failure@example.test","Audit failure","Staff"),"Audit outage aborts invitation");
            }
            Check(!await db.Users.AnyAsync(x=>x.Email=="audit-failure@example.test"),"Audit failure rolls back user and invitation atomically");
            var enroll=await factory.CreateClient().GetStringAsync("/PersonnelEnrollment/Accept");await File.WriteAllTextAsync(".tmp-owner-build/personnel-enrollment.synthetic.html",enroll);
            using var anonymous=factory.CreateClient(new(){AllowAutoRedirect=false});using var protectedPage=await anonymous.GetAsync("/Personnel");Check(protectedPage.StatusCode==HttpStatusCode.Redirect,"Anonymous account management denied");
            var register=await anonymous.GetStringAsync("/Account/Register");var clientEmail="client@example.test";
            using var registered=await anonymous.PostAsync("/Account/Register",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Token(register)},{"FullName","Synthetic Client"},{"PhoneNumber","09123456789"},{"Email",clientEmail},{"Password",password},{"ConfirmPassword",password},{"Role","Owner"}}));
            var registeredUser=await users.FindByEmailAsync(clientEmail);Check(registeredUser!=null&&await users.IsInRoleAsync(registeredUser,"Client")&&!await users.IsInRoleAsync(registeredUser,"Owner"),"Forged public registration role remains Client-only");
            Check(await db.SystemActivities.CountAsync(x=>x.AffectedRecordType=="ApplicationUser")>=10,"Personnel actions create audit records");
            Check(!await db.SystemActivities.AnyAsync(x=>x.Description.Contains(first.Secret)),"Audit does not contain invitation secrets");
            await using(var mailDown=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),root,emailOverride:new FindingsEmailFailure())) {
                using var ms=mailDown.Services.CreateScope();ms.ServiceProvider.GetRequiredService<IOptions<PersonnelOptions>>().Value.PublicOrigin="https://personnel.example.test";
                using var operatorBrowser=mailDown.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var signInPage=await operatorBrowser.GetStringAsync("/Account/Login");
                using var signed=await Post(operatorBrowser,"/Account/Login",Token(signInPage),("Email",owner.Email!),("Password",password));
                var management=await operatorBrowser.GetStringAsync("/Personnel");
                using var issued=await Post(operatorBrowser,"/Personnel/Invite",Token(management),("Email","offline-delivery@example.test"),("FullName","Offline delivery"),("Role","Staff"));
                var outcome=await operatorBrowser.GetStringAsync("/Personnel");var inactive=await users.FindByEmailAsync("offline-delivery@example.test");
                Check(inactive!=null&&!inactive.IsActive&&!inactive.EmailConfirmed&&!await users.HasPasswordAsync(inactive)&&(await users.GetRolesAsync(inactive)).Count==0,"SMTP failure leaves invitation inactive, roleless and unverified");
                Check(outcome.Contains("operator must rotate")&&!outcome.Contains("/PersonnelEnrollment/Accept#"),"SMTP outage provides private-operator guidance without disclosing link");
            }
            var awaiting=await service.InviteAsync(owner,"issuer-disabled@example.test","Issuer check","Staff");owner.IsActive=false;await users.UpdateAsync(owner);
            Check(!(await service.EnrollAsync(awaiting.UserId,awaiting.Secret,password)).Succeeded,"Independently disabled invitation issuer cannot activate personnel");
            owner.IsActive=true;await users.UpdateAsync(owner);
            var pendingDisabled=await service.InviteAsync(owner,"pending-disabled@example.test","Pending disabled","Staff");await service.ManageAsync(owner,pendingDisabled.UserId,"Deactivate");
            Check(!(await service.EnrollAsync(pendingDisabled.UserId,pendingDisabled.Secret,password)).Succeeded,"Deactivating pending personnel revokes their invitation");
            await using(var migrationFactory=new ReleaseFactory(connection,smtp.Port,new Uri("http://127.0.0.1:1"),root))
                await ThreeRoleRetirementTests.RunAsync(migrationFactory,services,owner,admin,password,Check);
            owner=await users.FindByIdAsync(owner.Id)??throw new Exception("Owner identity unavailable after isolated retirement test.");
            var recovered=await services.GetRequiredService<AccountCredentialRecovery>().InvalidateAsync(owner,true);
            Check(recovered.Succeeded&&!await users.HasPasswordAsync(owner)&&await users.IsInRoleAsync(owner,"Owner"),"Operator recovery invalidates credentials without promoting or replacing Owner");
            var recovery=await users.GeneratePasswordResetTokenAsync(owner);Check((await users.ResetPasswordAsync(owner,recovery,password+"Recovered")).Succeeded,"Existing Owner can recover through Identity password reset");
            Console.WriteLine($"SUCCESS: {checks} personnel provisioning SQL checks passed.");
        } finally {await schema.Database.EnsureDeletedAsync();}
    }
}
