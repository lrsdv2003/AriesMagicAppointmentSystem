using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Real Owner/Staff/Client cookies, disposable SQL database; no production requests.
internal static class OwnerPersonnelAccountsHttpChecks
{
    private static string Body(string html) {
        var match=Regex.Match(html,@"<table\b[^>]*id=""ownerPersonnelTable""[^>]*>.*?<tbody>(.*?)</tbody>",
            RegexOptions.Singleline|RegexOptions.IgnoreCase);
        if(!match.Success)throw new InvalidOperationException("Personnel active table was not rendered");
        return match.Groups[1].Value;
    }
    private static async Task<(HttpStatusCode Code,string Html)> Get(HttpClient http,string url) {
        using var response=await http.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static int Rows(string tbody)=>Regex.Matches(tbody,@"<tr\b(?![^>]*staff-data-table-empty-row)",RegexOptions.IgnoreCase).Count;

    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        IServiceProvider services,ApplicationDbContext db,Action<bool,string> check)
    {
        var manager=services.GetRequiredService<UserManager<ApplicationUser>>();
        var provisioning=services.GetRequiredService<PersonnelProvisioning>();
        var actor=await manager.FindByEmailAsync("owner@example.test")
            ??throw new InvalidOperationException("Fixture Owner is unavailable");
        var password="Personnel#Test-2026-Aa!";
        for(var i=1;i<=6;i++){
            var email="verified-personnel-client-"+i.ToString("D2")+"@example.test";
            var entry=new ApplicationUser{
                UserName=email,Email=email,FullName="QA Verified Client "+i.ToString("D2"),
                EmailConfirmed=true,IsActive=true
            };
            var created=await manager.CreateAsync(entry,password);
            if(!created.Succeeded)throw new InvalidOperationException("Synthetic active account fixture could not be created");
            if(!(await manager.AddToRoleAsync(entry,"Client")).Succeeded)
                throw new InvalidOperationException("Synthetic Client role assignment failed");
        }
        var unverified=new ApplicationUser{
            UserName="unverified-personnel@example.test",Email="unverified-personnel@example.test",
            FullName="QA Unverified",EmailConfirmed=false,IsActive=true
        };
        check((await manager.CreateAsync(unverified,password)).Succeeded &&
            (await manager.AddToRoleAsync(unverified,"Staff")).Succeeded,
            "Synthetic unverified Staff identity created for exclusion test");
        var disabled=new ApplicationUser{
            UserName="disabled-personnel@example.test",Email="disabled-personnel@example.test",
            FullName="QA Disabled",EmailConfirmed=true,IsActive=false
        };
        check((await manager.CreateAsync(disabled,password)).Succeeded &&
            (await manager.AddToRoleAsync(disabled,"Staff")).Succeeded,
            "Synthetic deactivated Staff account created for exclusion and lifecycle tests");
        var unassigned=new ApplicationUser{
            UserName="unassigned-personnel@example.test",Email="unassigned-personnel@example.test",
            FullName="QA Unassigned",EmailConfirmed=true,IsActive=true
        };
        check((await manager.CreateAsync(unassigned,password)).Succeeded,
            "Synthetic unassigned Identity account created for role exclusion");
        var pending=await provisioning.InviteAsync(actor,"pending-personnel@example.test",
            "QA Pending Staff","Staff");
        var pendingUser=await manager.FindByIdAsync(pending.UserId)
            ??throw new InvalidOperationException("Pending Staff fixture missing");
        check(!pendingUser.IsActive&&!pendingUser.EmailConfirmed,
            "Pending Staff account remains unverified and inactive");
        var all=await Get(owner,"/Personnel/Index");
        var allRows=Body(all.Html);
        check(all.Code==HttpStatusCode.OK &&
              all.Html.Contains("data-personnel-filters") &&
              all.Html.Contains("data-personnel-refresh") &&
              all.Html.Contains("staff-data-table-frame") &&
              all.Html.Contains("data-page-size=\"5\"") &&
              Rows(allRows)==5,"Owner sees a five-row, filterable active-only account table");
        check(!allRows.Contains("pending-personnel@example.test") &&
              !allRows.Contains("disabled-personnel@example.test") &&
              !allRows.Contains("unverified-personnel@example.test") &&
              !allRows.Contains("unassigned-personnel@example.test"),
              "Active table excludes invitation, disabled, unverified and unsupported identities");
        check(!all.Html.Contains("id=\"accountStatus\"") &&
              !all.Html.Contains("Account management audit") &&
              !all.Html.Contains("Edit Staff profile") &&
              !all.Html.Contains("<th scope=\"col\">Actions</th>"),
              "Retired status filtering, audit, Staff editor and Actions column are absent");
        check(Regex.Matches(all.Html,@"<option\b[^>]*value=""(?:Owner|Staff|Client)""").Count==3 &&
              !all.Html.Contains("value=\"Admin\"") &&
              !all.Html.Contains("value=\"Legacy internal\""),
              "Role dropdown exposes only Owner, Staff and Client");
        check(!all.Html.Contains("Apply filters")&&!all.Html.Contains("Clear filters")&&
              all.Html.Contains("data-personnel-role"),
              "Explicit Apply and Clear filters removed; role selector remains functional");
        var second=await Get(owner,"/Personnel/Index?page=2");
        check(second.Code==HttpStatusCode.OK && Rows(Body(second.Html))==5 &&
              second.Html.Contains("Page 2 of 2"),
              "Server paging shows only five of ten eligible accounts on second page");
        var clamped=await Get(owner,"/Personnel/Index?page=9999");
        check(clamped.Code==HttpStatusCode.OK && clamped.Html.Contains("Page 2 of 2"),
              "Out-of-range page clamps after eligibility filtering");
        var owners=await Get(owner,"/Personnel/Index?roleFilter=Owner");
        check(owners.Code==HttpStatusCode.OK && Rows(Body(owners.Html))==1 &&
              !Body(owners.Html).Contains("staff@example.test") &&
              Body(owners.Html).Contains("owner@example.test"),
              "Owner role selection returns only the verified Owner");
        var staffOnly=await Get(owner,"/Personnel/Index?roleFilter=Staff");
        check(staffOnly.Code==HttpStatusCode.OK && Rows(Body(staffOnly.Html))==1 &&
              Body(staffOnly.Html).Contains("staff@example.test") &&
              !Body(staffOnly.Html).Contains("disabled-personnel@example.test"),
              "Staff role selection excludes deactivated and unverified Staff");
        var clients=await Get(owner,"/Personnel/Index?roleFilter=Client");
        check(clients.Code==HttpStatusCode.OK && Rows(Body(clients.Html))==5 &&
              clients.Html.Contains("Page 1 of 2"),
              "Client role selection paginates verified Clients after role filtering");
        var filterSearch=await Get(owner,
            "/Personnel/Index?search=QA%20Verified%20Client%2003&roleFilter=Client&page=99");
        check(filterSearch.Code==HttpStatusCode.OK && Rows(Body(filterSearch.Html))==1 &&
              Body(filterSearch.Html).Contains("QA Verified Client 03") &&
              filterSearch.Html.Contains("Page 1 of 1") &&
              filterSearch.Html.Contains("roleFilter=Client") &&
              filterSearch.Html.Contains("search=QA"),
              "Search composes with role filter and refresh preserves query after page clamp");
        foreach(var excluded in new[]{"pending-personnel@example.test","disabled-personnel@example.test",
            "unverified-personnel@example.test","unassigned-personnel@example.test"}){
            var probe=await Get(owner,"/Personnel/Index?search="+Uri.EscapeDataString(excluded));
            check(probe.Code==HttpStatusCode.OK && Rows(Body(probe.Html))==0 &&
                Body(probe.Html).Contains("No matching accounts"),
                "Excluded account cannot reappear through direct search: "+excluded);
        }
        var unsupported=await Get(owner,"/Personnel/Index?roleFilter=Admin");
        check(unsupported.Code==HttpStatusCode.OK &&
              !Body(unsupported.Html).Contains("pending-personnel@example.test") &&
              !unsupported.Html.Contains("value=\"Admin\""),
              "Unsupported role parameter cannot expose retired or pending records");
        var access=await Get(owner,"/Personnel/Access");
        check(access.Code==HttpStatusCode.OK &&
              access.Html.Contains("pending-personnel@example.test") &&
              access.Html.Contains("disabled-personnel@example.test") &&
              access.Html.Contains("/Personnel/Manage") &&
              access.Html.Contains("/Personnel/Resend") &&
              !access.Html.Contains("/Personnel/EditStaff"),
              "Owner-only Staff Access page preserves legitimate lifecycle operations separately");
        foreach(var role in new[]{(staff,"Staff"),(client,"Client")}){
            foreach(var route in new[]{"/Personnel/Index","/Personnel/Access"}){
                var response=await Get(role.Item1,route);
                check(response.Code==HttpStatusCode.Redirect &&
                      response.Html!=null,
                      role.Item2+" cannot retrieve Owner Personnel/Staff Access via real cookie");
            }
        }
        var retiredGet=await Get(owner,"/Personnel/EditStaff/"+disabled.Id);
        using var retiredPost=await owner.PostAsync("/Personnel/EditStaff",new FormUrlEncodedContent([]));
        check(retiredGet.Code==HttpStatusCode.NotFound &&
              retiredPost.StatusCode==HttpStatusCode.NotFound,
              "Retired Owner-to-Staff editing cannot be called via direct GET or POST");
        using var tamperedPost=await owner.PostAsync("/Personnel/Manage",new FormUrlEncodedContent(new Dictionary<string,string>{
            ["id"]=disabled.Id,["operation"]="Reactivate"
        }));
        check(tamperedPost.StatusCode==HttpStatusCode.BadRequest &&
              !(await manager.FindByIdAsync(disabled.Id))!.IsActive,
              "Staff access mutation requires antiforgery and cannot reactivate by direct unauthenticated form");
        check(await db.SystemActivities.AsNoTracking().AnyAsync(x=>
            x.AffectedRecordId==pending.UserId && x.AffectedRecordType=="ApplicationUser" &&
            x.Type==SystemActivityType.UserCreated),
            "Invitation audit record remains persisted despite removal of visible audit panel");
    }
}
