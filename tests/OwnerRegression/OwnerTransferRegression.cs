using Microsoft.AspNetCore.DataProtection;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
static class OwnerTransferRegression
{
 public static async Task RunAsync(ApplicationDbContext db,Action<bool,string> check)
 {
  var services=new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
  services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
  services.AddDataProtection().UseEphemeralDataProtectionProvider();
  services.AddSingleton<Microsoft.AspNetCore.Mvc.Routing.IUrlHelperFactory,SnapshotUrls>();
  await using var provider=services.BuildServiceProvider();var users=provider.GetRequiredService<UserManager<ApplicationUser>>();var roles=provider.GetRequiredService<RoleManager<IdentityRole>>();
  var owner=await users.FindByNameAsync("admin-test-owner")??throw new Exception();owner.EmailConfirmed=true;await users.UpdateAsync(owner);
  var staff=await users.FindByNameAsync("admin-test-staff")??throw new Exception();
  var principal=new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role,"Owner"),new(ClaimTypes.NameIdentifier,owner.Id)],"Fixture"));
  var http=new DefaultHttpContext{User=principal,RequestServices=provider};var audit=new SystemActivityService(db);
  var provisioning=new PersonnelProvisioning(db,users,roles,audit,Options.Create(new PersonnelOptions()));
  var personnel=new PersonnelController(users,provisioning,db,null!,Options.Create(new PersonnelOptions())){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())};
  var policyProvider=provider.GetRequiredService<IAuthorizationPolicyProvider>();var authorization=provider.GetRequiredService<IAuthorizationService>();
  foreach(var pair in new[]{(typeof(CalendarController),"Settings"),(typeof(CalendarController),"BlockDateAjax"),(typeof(CalendarController),"UnblockDateAjax"),(typeof(TrashHistoryController),"Index"),(typeof(SystemActivityController),"Index"),(typeof(ServicesController),"Create"),(typeof(ServicesController),"Edit"),(typeof(ServicesController),"ArchiveConfirmed"),(typeof(ServicesController),"RestoreConfirmed")}){
   foreach(var method in pair.Item1.GetMethods().Where(m=>m.Name==pair.Item2)){
    var policy=await AuthorizationPolicy.CombineAsync(policyProvider,pair.Item1.GetCustomAttributes<AuthorizeAttribute>().Concat(method.GetCustomAttributes<AuthorizeAttribute>()));
    check((await authorization.AuthorizeAsync(principal,null,policy!)).Succeeded,"Owner migrated authorization: "+pair.Item1.Name+"."+method.Name);
    foreach(var denied in new[]{"Staff","Client"})check(!(await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role,denied)],"Fixture")),null,policy!)).Succeeded,denied+" denied business administration: "+method.Name);
   }
  }
  var listing=(PersonnelPage)((ViewResult)await personnel.Index(roleFilter:"Owner")).Model!;check(listing.Accounts.Any(x=>x.Id==owner.Id),"Personnel reuses account listing for Owner read-only identity");
  var filtered=(PersonnelPage)((ViewResult)await personnel.Index(search:"Test Staff",roleFilter:"Staff")).Model!;check(filtered.Accounts.Any(x=>x.Id==staff.Id)&&filtered.Accounts.All(x=>x.Role=="Staff"),"Personnel search and role filter are authoritative");
  var email=staff.Email;var hash=staff.PasswordHash;var stamp=staff.SecurityStamp;await provisioning.UpdateStaffProfileAsync(owner,staff.Id,"Updated Staff Contact","09123456789");
  check(staff.FullName=="Updated Staff Contact"&&staff.Email==email&&staff.PasswordHash==hash&&staff.SecurityStamp!=stamp,"Owner Staff contact update preserves login credentials and revokes old sessions");
  staff.FullName="Test Staff";await users.UpdateAsync(staff);
  try{await provisioning.UpdateStaffProfileAsync(owner,owner.Id,"Tampered",null);throw new Exception("Owner edit accepted");}catch(UnauthorizedAccessException){check(owner.FullName=="Test Owner","Staff profile endpoint cannot overwrite final Owner");}
  var enrollment=await provisioning.InviteAsync(owner,"owner-transfer-staff@example.test","Transfer Staff","Staff");
  var invited=await users.FindByIdAsync(enrollment.UserId)??throw new Exception();check(!invited.IsActive&&(await users.GetRolesAsync(invited)).Count==0,"Owner Staff invitation remains inactive and unassigned before enrollment");
  var password="Isolated!"+Guid.NewGuid().ToString("N")+"A7";
  check((await provisioning.EnrollAsync(enrollment.UserId,enrollment.Secret,password)).Succeeded&&invited.IsActive&&await users.IsInRoleAsync(invited,"Staff"),"Owner-issued Staff invitation completes verified single-use enrollment");
  check(!(await provisioning.EnrollAsync(enrollment.UserId,enrollment.Secret,password)).Succeeded,"Transferred Staff invitation cannot replay enrollment");
  var packageController=new ServicesController(db,audit){ControllerContext=new(){HttpContext=http}};
  var model=new ServiceManageViewModel{Name="Owner migration catalog fixture",Price=12000,DurationInHours=3,Description="Original",Inclusions=[new(){Name="Verified fixture inclusion",DeductionAmount=100,IsRemovable=true}]};
  check(await packageController.Create(model) is RedirectToActionResult,"Owner creates package through existing flow");
  var package=await db.Services.Include(s=>s.Inclusions).SingleAsync(s=>s.Name==model.Name);var client=new User{FullName="Owner transfer fixture",Email="",Role="Client"};var day=DateTime.Today.AddDays(341);
  var booking=new Booking{Service=package,Client=client,PackageName=package.Name,BasePrice=12000,FinalPrice=11900,RequiredDownpayment=2000,RemovedInclusionsJson="[\"Verified fixture inclusion\"]",EventDate=day,StartTime=day.AddHours(9),EndTime=day.AddHours(12),Status=BookingStatus.Confirmed};db.Bookings.Add(booking);await db.SaveChangesAsync();
  var snapshot=JsonSerializer.Serialize(new{booking.PackageName,booking.BasePrice,booking.FinalPrice,booking.RequiredDownpayment,booking.RemovedInclusionsJson,booking.StartTime,booking.EndTime});
  model.Id=package.Id;model.Name="Updated future catalog";model.Price=15000;model.DurationInHours=4;model.Inclusions=[new(){Name="Future inclusion",DeductionAmount=150,IsRemovable=true}];
  check(await packageController.Edit(package.Id,model) is RedirectToActionResult,"Owner edits package with existing validation");await db.Entry(booking).ReloadAsync();
  check(snapshot==JsonSerializer.Serialize(new{booking.PackageName,booking.BasePrice,booking.FinalPrice,booking.RequiredDownpayment,booking.RemovedInclusionsJson,booking.StartTime,booking.EndTime}),"Catalog edit preserves committed booking name, financial, inclusion-removal and time snapshots");
  await packageController.ArchiveConfirmed(package.Id);check(package.IsArchived&&await db.SystemActivities.AnyAsync(x=>x.Type==SystemActivityType.ServiceArchived&&x.PerformedByUserId==owner.Id&&x.AffectedRecordId==package.Id.ToString()),"Owner package archive records original actor and event");
  await packageController.RestoreConfirmed(package.Id);check(!package.IsArchived&&await db.SystemActivities.AnyAsync(x=>x.Type==SystemActivityType.ServiceRestored&&x.PerformedByUserId==owner.Id&&x.AffectedRecordId==package.Id.ToString()),"Owner package restore is retained and audited");
  var calendar=new CalendarController(db,null!,audit){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())};
  check(await calendar.Settings() is ViewResult,"Owner separate calendar settings renders");
  var conflict=(JsonResult)await calendar.BlockDateAjax(new(){BlockDate=day,BlockReason="Synthetic blocked date"});check(!JsonSerializer.SerializeToElement(conflict.Value).GetProperty("success").GetBoolean()&&!await db.BlockedDates.AnyAsync(x=>x.Date==day),"Calendar refuses to block confirmed event without altering schedule");
  var free=day.AddDays(1);var blocked=(JsonResult)await calendar.BlockDateAjax(new(){BlockDate=free,BlockReason="Authorized fixture closure"});check(JsonSerializer.SerializeToElement(blocked.Value).GetProperty("success").GetBoolean(),"Owner can block an unreserved date");
  var block=await db.BlockedDates.SingleAsync(x=>x.Date==free);await calendar.UnblockDateAjax(block.Id);check(!await db.BlockedDates.AnyAsync(x=>x.Date==free),"Owner can unblock date through existing audited flow");
  check(booking.Status==BookingStatus.Confirmed&&booking.EventDate==day,"Availability administration preserves confirmed booking");
  db.SystemActivities.Add(new(){Type=SystemActivityType.LoginFailed,Description="Technical diagnostic sentinel",PerformedByUserId="fixture"});await db.SaveChangesAsync();
  var activity=(SystemActivityIndexViewModel)((ViewResult)await new SystemActivityController(audit).Index()).Model!;
  check(activity.Activities.All(x=>BusinessActivityTypes.All.Contains(x.Type))&&!activity.ActivityTypes.Contains(SystemActivityType.LoginFailed),"Business activity excludes protected technical authentication diagnostics");
  check(await new TrashHistoryService(db).GetDetailsAsync(booking.Id)==null,"Archives retain read-only failed-record scope");
 }
}
