using System.Net;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.EntityFrameworkCore;

// Authenticated staging flows run only inside ReleaseIntegration's disposable TestServer/SQL fixture.
internal static class OwnerCalendarLimitHttpChecks
{
    private static async Task<(HttpStatusCode Status, JsonElement Data)> Json(HttpClient http, string path)
    {
        using var response=await http.GetAsync(path);
        var content=await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(content).RootElement.Clone());
    }
    private static async Task<(HttpStatusCode Status, JsonElement Data)> Save(
        HttpClient http, string token, DateTime date, int id, int expected, string limit, bool sendToken=true)
    {
        var fields=new Dictionary<string,string> {
            ["limitDate"]=date.ToString("yyyy-MM-dd"),
            ["recordId"]=id.ToString(),["expectedStoredLimit"]=expected.ToString(),
            ["newLimit"]=limit
        };
        if(sendToken)fields["__RequestVerificationToken"]=token;
        using var response=await http.PostAsync("/Calendar/SaveDateLimit",new FormUrlEncodedContent(fields));
        var content=await response.Content.ReadAsStringAsync();
        if(response.StatusCode==HttpStatusCode.Redirect || response.StatusCode==HttpStatusCode.BadRequest &&
            !content.TrimStart().StartsWith("{")) return (response.StatusCode,default);
        return (response.StatusCode,JsonDocument.Parse(content).RootElement.Clone());
    }
    public static async Task RunAsync(HttpClient owner,HttpClient staff,HttpClient client,
        string ownerToken,ApplicationDbContext db,Action<bool,string> check)
    {
        var a=DateTime.Today.AddDays(365);
        var b=a.AddDays(1);
        using(var page=await owner.GetAsync("/Calendar/Settings"))
        {
            var html=await page.Content.ReadAsStringAsync();
            check(page.StatusCode==HttpStatusCode.OK && html.Contains("data-calendar-limit-read-url") &&
                html.Contains("calendarLimitModal") && html.Contains("calendarLimitForm") &&
                html.Contains("calendarLimitDatePicker") && html.Contains("No custom date settings"),
                "Owner settings exposes functional date picker, edit modal and empty state");
            check(!html.Contains("Created by</th>",StringComparison.OrdinalIgnoreCase) &&
                !html.Contains("Calendar availability</h2>") &&
                !html.Contains("workspace-page-heading"),"Owner settings removes redundant header and Created By column");
            check(html.Contains("/js/calendar-settings.js") && html.Contains("/Calendar/SaveDateLimit"),
                "Owner date-limit client logic and protected POST action are rendered");
        }
        foreach(var role in new[]{(staff,"Staff"),(client,"Client")})
        {
            using var page=await role.Item1.GetAsync("/Calendar/Settings");
            using var read=await role.Item1.GetAsync("/Calendar/GetDateLimit?date="+a.ToString("yyyy-MM-dd"));
            var post=await Save(role.Item1,ownerToken,a,0,0,"4");
            check(page.StatusCode==HttpStatusCode.Redirect && read.StatusCode==HttpStatusCode.Redirect
                  && post.Status==HttpStatusCode.Redirect,
                  role.Item2+" cannot read or mutate Owner-only Calendar Settings");
        }
        var dateA=a.ToString("yyyy-MM-dd");
        var initial=await Json(owner,"/Calendar/GetDateLimit?date="+dateA);
        check(initial.Status==HttpStatusCode.OK && initial.Data.GetProperty("effectiveLimit").GetInt32()==3 &&
              initial.Data.GetProperty("id").GetInt32()==0,
              "Owner date lookup returns the established default when no override exists");
        var noToken=await Save(owner,ownerToken,a,0,0,"4",sendToken:false);
        check(noToken.Status==HttpStatusCode.BadRequest &&
              !await db.DateBookingLimits.AnyAsync(x=>x.Date.Date==a),
              "Date-limit mutation requires valid antiforgery token");

        var created=await Save(owner,ownerToken,a,0,0,"4");
        check(created.Status==HttpStatusCode.OK && created.Data.GetProperty("success").GetBoolean(),
            "Owner saves first date override over real authenticated HTTP");
        var after=await Json(owner,"/Calendar/GetDateLimit?date="+dateA);
        var id=after.Data.GetProperty("id").GetInt32();
        check(id>0 && after.Data.GetProperty("storedLimit").GetInt32()==4 &&
              await DateBookingCapacity.EffectiveLimitAsync(db,a)==4,
              "Date-specific limit survives a fresh GET and SQL query");

        var second=await Save(owner,ownerToken,b,0,0,"2");
        check(second.Status==HttpStatusCode.OK && await DateBookingCapacity.EffectiveLimitAsync(db,b)==2 &&
              await DateBookingCapacity.EffectiveLimitAsync(db,a)==4,
              "Adjacent date remains independent after setting its own capacity");

        foreach(var invalid in new[]{"0","21","1.5","abc",""})
        {
            var response=await Save(owner,ownerToken,a,id,4,invalid);
            check(response.Status==HttpStatusCode.BadRequest &&
                  await DateBookingCapacity.EffectiveLimitAsync(db,a)==4,
                  "Server-side binding/range validation rejects daily limit '"+invalid+"'");
        }
        var stale=await Save(owner,ownerToken,a,id,3,"5");
        check(stale.Status==HttpStatusCode.Conflict &&
              await DateBookingCapacity.EffectiveLimitAsync(db,a)==4,
              "Stale form submission cannot overwrite a saved limit");
        var otherId=(await Json(owner,"/Calendar/GetDateLimit?date="+b.ToString("yyyy-MM-dd")))
            .Data.GetProperty("id").GetInt32();
        var tampered=await Save(owner,ownerToken,a,otherId,2,"5");
        check(tampered.Status==HttpStatusCode.Conflict &&
              await DateBookingCapacity.EffectiveLimitAsync(db,b)==2,
              "Altered record identifier cannot change another date");

        var updated=await Save(owner,ownerToken,a,id,4,"5");
        check(updated.Status==HttpStatusCode.OK && updated.Data.GetProperty("success").GetBoolean() &&
              await DateBookingCapacity.EffectiveLimitAsync(db,a)==5 &&
              await DateBookingCapacity.EffectiveLimitAsync(db,b)==2,
              "Owner edits existing record while preserving the adjacent date");

        var clientAvail=await Json(client,"/Bookings/CheckDateAvailability?date="+dateA);
        check(clientAvail.Status==HttpStatusCode.OK && clientAvail.Data.GetProperty("maxPerDay").GetInt32()==5 &&
              clientAvail.Data.GetProperty("remainingSlots").GetInt32()==5,
              "Client booking availability reflects the updated date-specific limit");
        var clientReschedule=await Json(client,"/RescheduleRequests/CheckRescheduleDateAvailability?date="+dateA);
        check(clientReschedule.Status==HttpStatusCode.OK &&
              clientReschedule.Data.GetProperty("maxPerDay").GetInt32()==5,
              "Client reschedule availability reads the same saved limit");
        foreach(var user in new[]{(owner,"Owner"),(staff,"Staff")})
        {
            using var calendar=await user.Item1.GetAsync("/Calendar/Index");
            var html=await calendar.Content.ReadAsStringAsync();
            check(calendar.StatusCode==HttpStatusCode.OK &&
                html.Contains("\""+dateA+"\":5",StringComparison.Ordinal),
                user.Item2+" calendar renders the updated capacity map");
        }

        // Two Owner POSTs with the same stale version must not both commit.
        var requests=await Task.WhenAll(Save(owner,ownerToken,a,id,5,"6"),
            Save(owner,ownerToken,a,id,5,"7"));
        var accepted=requests.Count(x=>x.Status==HttpStatusCode.OK &&
            x.Data.ValueKind==JsonValueKind.Object && x.Data.GetProperty("success").GetBoolean());
        check(accepted==1 && requests.Any(x=>x.Status==HttpStatusCode.Conflict) &&
              await db.DateBookingLimits.AsNoTracking().CountAsync(x=>x.Date.Date==a)==1,
              "Concurrent Owner edits commit at most one version and preserve one row");
    }
}
