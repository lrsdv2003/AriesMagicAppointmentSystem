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
        if(media.ProcessingStatus!="Ready"||media.ScannedAtUtc==null
            ||media.Kind=="Image"&&media.ContentType!="image/jpeg"
            ||media.Kind=="Video"&&media.ContentType!="video/mp4"
            ||media.Kind is not ("Image" or "Video"))return NotFound();
        return await Deliver(media.StorageKey,media.ContentType,media.Kind=="Video",ct);
    }
    [AllowAnonymous]
    public async Task<IActionResult> Public(int id,CancellationToken ct)
    {
        var eligible=FeedbackPolicy.PublicQuery(db).Select(f=>f.Id);
        var media=await db.FeedbackMedia.AsNoTracking().Where(m=>m.Id==id&&m.PublicApproved&&m.ProcessingStatus=="Ready"&&m.Kind=="Image"&&m.ContentType=="image/jpeg"
            &&m.ScannedAtUtc!=null&&m.Feedback.MediaConsent&&eligible.Contains(m.FeedbackId))
            .Select(m=>new {m.StorageKey}).SingleOrDefaultAsync(ct);
        return media==null?NotFound():await Deliver(media.StorageKey,"image/jpeg",false,ct);
    }
    private Task<IActionResult> Deliver(string key,string contentType,bool video,CancellationToken ct)
    {
        Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers.ContentDisposition=video?"inline; filename=\"event-video.mp4\"":"inline; filename=\"event-photo.jpg\"";
        Response.Headers.ContentSecurityPolicy="default-src 'none'; sandbox";
        try
        {
            if(video)return Task.FromResult<IActionResult>(File(store.OpenRead(key),contentType,enableRangeProcessing:true));
            return Task.FromResult<IActionResult>(File(store.OpenRead(key),contentType));
        }
        catch(FileNotFoundException){return Task.FromResult<IActionResult>(NotFound());}
        catch(DirectoryNotFoundException){return Task.FromResult<IActionResult>(NotFound());}
        catch(IOException){return Task.FromResult<IActionResult>(StatusCode(503,"This media is temporarily unavailable."));}
        catch(UnauthorizedAccessException){return Task.FromResult<IActionResult>(StatusCode(503,"This media is temporarily unavailable."));}
    }
}
