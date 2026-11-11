using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
static class ThreeRoleRetirementTests
{
 public static async Task RunAsync(ReleaseFactory factory,IServiceProvider services,ApplicationUser owner,ApplicationUser legacy,string password,Action<bool,string> check)
 {
  var db=services.GetRequiredService<ApplicationDbContext>();var users=services.GetRequiredService<UserManager<ApplicationUser>>();var provision=services.GetRequiredService<PersonnelProvisioning>();
  string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
  HttpClient Client(bool cookies=true)=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,HandleCookies=cookies,BaseAddress=new("https://localhost")});
  async Task<HttpClient> Login(string address,string pass){var c=Client();var html=await c.GetStringAsync("/Account/Login");using var signed=await c.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email",address},{"Password",pass},{"__RequestVerificationToken",Token(html)}}));check(signed.StatusCode==HttpStatusCode.Redirect,"Three-role real Identity login status "+(int)signed.StatusCode+" "+(address==owner.Email?"Owner":"non-Owner"));return c;}
  using var ownerHttp=await Login(owner.Email!,password);
  var staff=await users.FindByEmailAsync("staff@example.test")??throw new Exception("Staff fixture unavailable");
  var package=new Service{Name="Retirement HTTP package",Price=10000,DurationInHours=3,Inclusions=[new(){Name="Synthetic approved inclusion",DeductionAmount=0,IsRemovable=false}]};db.Services.Add(package);await db.SaveChangesAsync();
  foreach(var route in new[]{"/Personnel","/Services","/Services/Create","/Services/Edit/"+package.Id,"/Calendar/Settings","/TrashHistory","/SystemActivity","/Dashboard/Owner","/Personnel/EditStaff/"+staff.Id}){
   using var response=await ownerHttp.GetAsync(route);var html=await response.Content.ReadAsStringAsync();check(response.StatusCode==HttpStatusCode.OK&&!html.Contains("/Dashboard/Admin")&&!html.Contains("value=\"Admin\""),"Owner migrated HTTP page and retired navigation: "+route.Split('?')[0]);
  }
  foreach(var route in new[]{"/Dashboard/Admin","/UserManagement","/UserManagement/Details/anything"}){using var response=await ownerHttp.GetAsync(route);check(response.StatusCode==HttpStatusCode.NotFound,"Retired route absent: "+route);}
  var oldPrincipal=await services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(legacy);
  var cookieOptions=factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
  var ticket=new AuthenticationTicket(oldPrincipal,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(15)},IdentityConstants.ApplicationScheme);
  var oldCookie=cookieOptions.TicketDataFormat.Protect(ticket);
  using(var stale=Client(false)){
   stale.DefaultRequestHeaders.Add("Cookie",cookieOptions.Cookie.Name+"="+oldCookie);
   foreach(var route in new[]{"/Notifications","/Personnel","/InternalProfile","/Payments/PendingVerification"}){using var response=await stale.GetAsync(route);check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.OriginalString.Contains("/Account/Login"),"Pre-cutover legacy-only cookie is rejected: "+route);}
  }
  var unactivated=await provision.InviteAsync(owner,"legacy-token@example.test","Historical token fixture","Staff");var pendingUser=await users.FindByIdAsync(unactivated.UserId)??throw new Exception();var pending=(await provision.PendingAsync(pendingUser))!;
  await users.SetAuthenticationTokenAsync(pendingUser,PersonnelProvisioning.Provider,PersonnelProvisioning.TokenName,JsonSerializer.Serialize(pending with {Role="Admin"}));
  check(!(await provision.EnrollAsync(pendingUser.Id,unactivated.Secret,password)).Succeeded,"Correct historical token cannot activate retired Admin role");
  await db.Entry(pendingUser).ReloadAsync();check(!pendingUser.IsActive&&!pendingUser.EmailConfirmed&&!await users.HasPasswordAsync(pendingUser)&&(await users.GetRolesAsync(pendingUser)).Count==0,"Rejected legacy invitation leaves account inactive and roleless");
  var staffOriginalPassword=password+"R"; // Existing personnel reset test set this generated password.
  using var staffHttp=await Login(staff.Email!,staffOriginalPassword);
  foreach(var route in new[]{"/Personnel","/Services/Create","/Calendar/Settings","/TrashHistory","/SystemActivity"}){using var r=await staffHttp.GetAsync(route);check(r.StatusCode==HttpStatusCode.Redirect&&r.Headers.Location!.OriginalString.Contains("AccessDenied"),"Staff cannot access Owner administration: "+route);}
  var clientUser=new ApplicationUser{Email="three-role-client@example.test",UserName="three-role-client@example.test",EmailConfirmed=true,FullName="Synthetic Client"};
  check((await users.CreateAsync(clientUser,password)).Succeeded&&(await users.AddToRoleAsync(clientUser,"Client")).Succeeded,"Create isolated Client for administration denial matrix");
  using var clientHttp=await Login(clientUser.Email!,password);
  foreach(var route in new[]{"/Personnel","/Services/Create","/Calendar/Settings","/TrashHistory","/SystemActivity"}){using var r=await clientHttp.GetAsync(route);check(r.StatusCode==HttpStatusCode.Redirect&&r.Headers.Location!.OriginalString.Contains("AccessDenied"),"Client cannot access Owner administration: "+route);}
  foreach(var route in new[]{"/Calendar/BlockDateAjax","/Calendar/UnblockDateAjax","/Services/Archive/"+package.Id,"/Personnel/EditStaff"}){using var r=await ownerHttp.PostAsync(route,new FormUrlEncodedContent([]));check(r.StatusCode==HttpStatusCode.BadRequest,"Migrated Owner POST retains anti-forgery: "+route);}
  var protectedDate=DateTime.Today.AddDays(372);db.BlockedDates.Add(new(){Date=protectedDate,Reason="Synthetic audited restriction"});await db.SaveChangesAsync();
  var protectedBlock=await db.BlockedDates.AsNoTracking().SingleAsync(x=>x.Date==protectedDate);
  var failedCalendar=new AriesMagicAppointmentSystem.Controllers.CalendarController(db,null!,new FailingAudit(db,SystemActivityType.CalendarModified)){ControllerContext=new(){HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new(System.Security.Claims.ClaimTypes.NameIdentifier,owner.Id),new(System.Security.Claims.ClaimTypes.Role,"Owner")],"Fixture"))}}};
  try{await failedCalendar.UnblockDateAjax(protectedBlock.Id);check(false,"Unblock audit must succeed");}catch(InvalidOperationException){db.ChangeTracker.Clear();check(await db.BlockedDates.AsNoTracking().AnyAsync(x=>x.Id==protectedBlock.Id),"Failed availability audit rolls back unblock without losing restriction");}
  var staffId=staff.Id;var historical=new Conversation{CreatedByUserId=legacy.Id,Title="Private historical fixture",ConversationType=ConversationTypes.InternalDirect,Participants=[new(){UserId=legacy.Id},new(){UserId=staffId}],Messages=[new(){SenderId=legacy.Id,MessageContent="Legacy private history sentinel",SentAt=DateTime.UtcNow}]};
  var audit=new SystemActivity{Type=SystemActivityType.UserUpdated,PerformedByUserId=legacy.Id,PerformedByUserName="Historical operator",Description="Original retained event",MetadataJson="{\"ActorRole\":\"Admin\"}",CreatedAt=DateTime.UtcNow.AddYears(-1)};db.Conversations.Add(historical);db.SystemActivities.Add(audit);await db.SaveChangesAsync();
  var originalAudit=JsonSerializer.Serialize(await db.SystemActivities.AsNoTracking().SingleAsync(x=>x.Id==audit.Id));var messageId=historical.Messages.Single().Id;var legacyStamp=legacy.SecurityStamp;var legacyPassword=legacy.PasswordHash;
  using(var deniedHistory=await ownerHttp.GetAsync("/Communications/MessagesSince?id="+historical.Id)){check(deniedHistory.StatusCode==HttpStatusCode.Redirect&&deniedHistory.Headers.Location!.OriginalString.Contains("AccessDenied")||deniedHistory.StatusCode==HttpStatusCode.Forbidden,"Owner cannot inherit private historical Admin conversation");}
  try{await new LegacyRoleRetirement(db,users,new FailingAudit(db,SystemActivityType.RoleRemoved)).RetireAsync(legacy.Id,"isolated-audit-failure");check(false,"Retirement audit failure must abort");}catch(InvalidOperationException){db.ChangeTracker.Clear();legacy=(await users.FindByIdAsync(legacy.Id))!;check(legacy.IsActive&&await users.IsInRoleAsync(legacy,"Admin"),"Audit failure rolls back legacy role removal and deactivation");}
  var retirement=new LegacyRoleRetirement(db,users,new SystemActivityService(db));await retirement.RetireAsync(legacy.Id,"isolated-approved-identity");await db.Entry(legacy).ReloadAsync();
  check(!legacy.IsActive&&!await users.IsInRoleAsync(legacy,"Admin")&&legacy.PasswordHash==legacyPassword&&legacy.SecurityStamp!=legacyStamp,"Explicit retirement disables legacy-only account and revokes sessions without replacing password/history");
  check(await db.Roles.AnyAsync(x=>x.Name=="Admin")&&!await users.IsInRoleAsync(legacy,"Owner"),"Historical role definition is retained without Owner promotion");
  check(await db.Messages.AnyAsync(x=>x.Id==messageId&&x.SenderId==legacy.Id)&&!await db.ConversationParticipants.AnyAsync(x=>x.ConversationId==historical.Id&&x.UserId==owner.Id),"Original sender and private conversation membership survive retirement");
  var preserved=await db.SystemActivities.AsNoTracking().SingleAsync(x=>x.Id==audit.Id);check(JsonSerializer.Serialize(preserved)==originalAudit,"Historical audit attribution, timestamp and metadata remain byte-equivalent");
  await retirement.RetireAsync(pendingUser.Id,"isolated-approved-pending");check((await provision.PendingAsync(pendingUser))!.State=="Revoked","Explicit pending legacy invitation retirement preserves record with unusable digest");
  try{await retirement.RetireAsync(owner.Id,"isolated-owner-forgery");check(false,"Owner retirement must refuse");}catch(InvalidOperationException){check(owner.IsActive&&await users.IsInRoleAsync(owner,"Owner"),"Final active Owner is protected from legacy retirement");}
  check(typeof(LegacyRoleRetirement).BaseType==typeof(object),"Legacy retirement is an offline service, never a setup controller");
 }
}
