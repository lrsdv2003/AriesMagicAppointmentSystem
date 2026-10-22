using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AriesMagicAppointmentSystem.Controllers;

[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FeedbackMediaController(ApplicationDbContext db,FeedbackMediaService store) : Controller
{
    [Authorize(Roles="Client,Staff")]
    public async Task<IActionResult> Private(int id,CancellationToken ct)
    {
        var media=await db.FeedbackMedia.AsNoTracking().Include(m=>m.Feedback).SingleOrDefaultAsync(m=>m.Id==id,ct);
        if(media==null||!User.IsInRole("Staff")&&media.Feedback.ClientId!=User.FindFirstValue(ClaimTypes.NameIdentifier))return NotFound();
        if(media.ProcessingStatus!="Ready"||media.ScannedAtUtc==null||media.Kind!="Image"||media.ContentType!="image/jpeg")return NotFound();
        return await Deliver(media.StorageKey,ct);
    }
    [AllowAnonymous]
    public async Task<IActionResult> Public(int id,CancellationToken ct)
    {
        var eligible=FeedbackPolicy.PublicQuery(db).Select(f=>f.Id);
        var media=await db.FeedbackMedia.AsNoTracking().Where(m=>m.Id==id&&m.PublicApproved&&m.ProcessingStatus=="Ready"&&m.Kind=="Image"&&m.ContentType=="image/jpeg"
            &&m.ScannedAtUtc!=null&&m.Feedback.MediaConsent&&eligible.Contains(m.FeedbackId))
            .Select(m=>new {m.StorageKey}).SingleOrDefaultAsync(ct);
        return media==null?NotFound():await Deliver(media.StorageKey,ct);
    }
    private async Task<IActionResult> Deliver(string key,CancellationToken ct)
    {
        Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers.ContentDisposition="inline; filename=\"event-photo.jpg\"";
        Response.Headers.ContentSecurityPolicy="default-src 'none'; sandbox";
        try{return File(await store.ReadAsync(key,ct),"image/jpeg");}
        catch(FileNotFoundException){return NotFound();}
        catch(DirectoryNotFoundException){return NotFound();}
        catch(IOException){return StatusCode(503,"This photo is temporarily unavailable.");}
        catch(UnauthorizedAccessException){return StatusCode(503,"This photo is temporarily unavailable.");}
    }
}
