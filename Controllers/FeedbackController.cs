using System.Data;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles="Client")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FeedbackController(ApplicationDbContext db,FeedbackMediaService media,IOptions<FeedbackMediaOptions> options) : Controller
{
    private string ClientId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index(int page=1)
    {
        var query=db.EventFeedbacks.AsNoTracking().Include(f=>f.Booking).Where(f=>f.ClientId==ClientId);
        var pages=Math.Max(1,(int)Math.Ceiling(await query.CountAsync()/20d));page=Math.Clamp(page,1,pages);
        var now=FeedbackPolicy.BusinessNow;var today=now.Date;
        var eligible=await db.Bookings.AsNoTracking().Where(b=>b.ApplicationUserId==ClientId&&b.Status==BookingStatus.Completed
            &&b.IsCompletedLocked&&b.EventDate<today&&b.EndTime<now&&!db.EventFeedbacks.Any(f=>f.BookingId==b.Id)).OrderByDescending(b=>b.EventDate).Take(20).ToListAsync();
        return View(new ClientFeedbackPage(await query.OrderByDescending(f=>f.SubmittedAtUtc).Skip((page-1)*20).Take(20).ToListAsync(),eligible,page,pages));
    }
    public async Task<IActionResult> Details(int id)
    {
        var feedback=await db.EventFeedbacks.AsNoTracking().Include(f=>f.Booking).Include(f=>f.Media).SingleOrDefaultAsync(f=>f.Id==id&&f.ClientId==ClientId);
        return feedback==null?NotFound():View(feedback);
    }
    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("uploads")]
    public async Task<IActionResult> RetryMedia(int id,CancellationToken ct)
    {
        var item=await db.FeedbackMedia.AsNoTracking().Include(m=>m.Feedback)
            .SingleOrDefaultAsync(m=>m.Id==id&&m.Feedback.ClientId==ClientId,ct);
        if(item==null)return NotFound();
        if(item.ProcessingStatus!="Failed")
        {
            TempData["Error"]="Only failed security checks can be retried. Rejected media cannot be resubmitted from this action.";
            return RedirectToAction(nameof(Details),new {id=item.FeedbackId});
        }
        var changed=await db.FeedbackMedia.Where(m=>m.Id==id&&m.Feedback.ClientId==ClientId&&m.ProcessingStatus=="Failed")
            .ExecuteUpdateAsync(set=>set.SetProperty(m=>m.ProcessingStatus,"Quarantined")
                .SetProperty(m=>m.ProcessingAttempts,0).SetProperty(m=>m.ProcessingStartedAtUtc,(DateTime?)null)
                .SetProperty(m=>m.ProcessingError,(string?)null).SetProperty(m=>m.ScannedAtUtc,(DateTime?)null)
                .SetProperty(m=>m.PublicApproved,false),ct);
        TempData[changed==1?"Success":"Error"]=changed==1
            ?"The private file was queued for another security scan. It remains unavailable until the scan passes."
            :"The media status changed. Refresh and review the current status.";
        return RedirectToAction(nameof(Details),new {id=item.FeedbackId});
    }
    public async Task<IActionResult> Create(int bookingId)
    {
        var booking=await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b=>b.Id==bookingId&&b.ApplicationUserId==ClientId);
        if(booking==null) return NotFound();
        var previous=await db.EventFeedbacks.Where(f=>f.BookingId==bookingId).Select(f=>(int?)f.Id).SingleOrDefaultAsync();
        if(previous.HasValue) return RedirectToAction(nameof(Details),new {id=previous.Value});
        if(!FeedbackPolicy.Eligible(booking)) return BadRequest("Feedback is available after a completed event and its event date have passed.");
        return View(new FeedbackInput {BookingId=booking.Id,EventLabel=booking.PackageName});
    }
    [HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("uploads")]
    [RequestSizeLimit(160*1024*1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=160*1024*1024)]
    public async Task<IActionResult> Create(FeedbackInput input,CancellationToken ct)
    {
        var booking=await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b=>b.Id==input.BookingId&&b.ApplicationUserId==ClientId,ct);
        if(booking==null) return NotFound();
        if(!FeedbackPolicy.Eligible(booking)) return BadRequest("This booking is not eligible for feedback.");
        var existingId=await db.EventFeedbacks.Where(f=>f.BookingId==booking.Id).Select(f=>(int?)f.Id).SingleOrDefaultAsync(ct);
        if(existingId.HasValue)return RedirectToAction(nameof(Details),new {id=existingId.Value});
        input.EventLabel=booking.PackageName;
        input.Files??=[];
        var images=input.Files.Where(f=>!Path.GetExtension(f.FileName).Equals(".mp4",StringComparison.OrdinalIgnoreCase)).ToList();
        var videos=input.Files.Where(f=>Path.GetExtension(f.FileName).Equals(".mp4",StringComparison.OrdinalIgnoreCase)).ToList();
        if(images.Count>options.Value.MaxImages) ModelState.AddModelError("Files",$"Choose up to {options.Value.MaxImages} photos.");
        if(videos.Count>1)ModelState.AddModelError("Files","Choose at most one video.");
        if(images.Any(f=>f.Length<1||f.Length>options.Value.MaxImageBytes)||images.Sum(f=>f.Length)>options.Value.MaxImageBytes*options.Value.MaxImages
            ||videos.Any(f=>f.Length<1||f.Length>options.Value.MaxVideoBytes))
            ModelState.AddModelError("Files","The selected media exceeds the upload limits.");
        if(input.SharingPreference is not ("Public" or "Private"))ModelState.AddModelError(nameof(input.SharingPreference),"Choose whether to share feedback publicly or keep it private.");
        input.TextConsent=input.SharingPreference=="Public";
        if(input.TextConsent&&string.IsNullOrWhiteSpace(input.Experience)) ModelState.AddModelError("Experience","Add your written experience to opt in to public testimonial sharing.");
        if(input.NameConsent&&string.IsNullOrWhiteSpace(input.PublicDisplayName)) ModelState.AddModelError("PublicDisplayName","Choose the display name you permit us to publish.");
        if(!ModelState.IsValid) return View(input);
        var prepared=new List<FeedbackMedia>();var committed=false;
        try {
            foreach(var file in input.Files) prepared.Add(Path.GetExtension(file.FileName).Equals(".mp4",StringComparison.OrdinalIgnoreCase)
                ?await media.PrepareVideoAsync(file,ct):await media.PrepareAsync(file,ct));
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            booking=await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b=>b.Id==input.BookingId&&b.ApplicationUserId==ClientId,ct);
            if(booking==null||!FeedbackPolicy.Eligible(booking)) return BadRequest("This booking is no longer eligible for feedback.");
            var previous=await db.EventFeedbacks.Where(f=>f.BookingId==input.BookingId).Select(f=>(int?)f.Id).SingleOrDefaultAsync(ct);
            if(previous.HasValue) return RedirectToAction(nameof(Details),new {id=previous.Value});
            var now=DateTime.UtcNow;
            var feedback=new EventFeedback {BookingId=input.BookingId,ClientId=ClientId,OverallRating=input.OverallRating,
                PerformanceRating=input.PerformanceRating,BookingRating=input.BookingRating,Experience=input.Experience?.Trim()??"",
                Suggestions=input.Suggestions?.Trim()??"",PublicDisplayName=input.NameConsent?input.PublicDisplayName?.Trim()??"":"",
                TextConsent=input.TextConsent,SharingPreference=input.SharingPreference,ConsentVersion="feedback-sharing-2026-11-03-v1",
                NameConsent=input.NameConsent,MediaConsent=input.MediaConsent,
                ConsentedAtUtc=now,SubmittedAtUtc=now,UpdatedAtUtc=now,
                ModerationStatus=input.TextConsent?"Pending":"Private",Media=prepared};
            db.EventFeedbacks.Add(feedback);
            var staff=await (from userRole in db.UserRoles join role in db.Roles on userRole.RoleId equals role.Id
                             join user in db.Users on userRole.UserId equals user.Id
                             where role.Name=="Staff"&&user.IsActive select user.Id).Distinct().ToListAsync(ct);
            foreach(var id in staff) db.Notifications.Add(new Notification {UserId=id,Title="New event feedback",Message="A completed-event feedback submission is available for authorized review.",Link="/Testimonials?filter=All",CreatedAt=FeedbackPolicy.BusinessNow});
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);committed=true;
            TempData["Success"]="Thank you. Your feedback is saved. Public sharing always requires your consent and Staff approval.";
            return RedirectToAction(nameof(Details),new {id=feedback.Id});
        } catch(FeedbackUploadException ex) {ModelState.AddModelError("Files",ex.Message);return View(input);}
        catch(DbUpdateException ex) when(ex.InnerException is SqlException sql && sql.Number is 2601 or 2627) {
            ModelState.AddModelError("","Feedback has already been submitted for this event. Open My feedback to view it.");return View(input);
        } catch(IOException) {ModelState.AddModelError("Files","The media could not be saved. Please reselect your files and try again.");return View(input);}
        catch(UnauthorizedAccessException) {ModelState.AddModelError("Files","Photo storage is temporarily unavailable. Please try again later.");return View(input);}
        finally {if(!committed)foreach(var file in prepared)media.Delete(file.StorageKey);}
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id)
    {
        await using var transaction=await db.Database.BeginTransactionAsync();
        var now=DateTime.UtcNow;
        // Privacy wins over stale moderation: update the owned parent under a write lock first.
        var changed=await db.EventFeedbacks.Where(f=>f.Id==id&&f.ClientId==ClientId&&f.WithdrawnAtUtc==null)
            .ExecuteUpdateAsync(set=>set.SetProperty(f=>f.TextConsent,false).SetProperty(f=>f.NameConsent,false)
                .SetProperty(f=>f.MediaConsent,false).SetProperty(f=>f.Featured,false).SetProperty(f=>f.WithdrawnAtUtc,now)
                .SetProperty(f=>f.UnpublishedAtUtc,now).SetProperty(f=>f.UpdatedAtUtc,now).SetProperty(f=>f.ModerationStatus,"Withdrawn"));
        if(changed==0) {
            if(!await db.EventFeedbacks.AnyAsync(f=>f.Id==id&&f.ClientId==ClientId))return NotFound();
        } else {
            await db.FeedbackMedia.Where(m=>m.FeedbackId==id).ExecuteUpdateAsync(set=>set.SetProperty(m=>m.PublicApproved,false));
            var feedback=await db.EventFeedbacks.SingleAsync(f=>f.Id==id);
            FeedbackPolicy.Audit(db,feedback,ClientId,"Withdrawn","Client withdrew all public-sharing consent.");
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        TempData["Success"]="Public consent withdrawn. Your testimonial and media are no longer publicly available.";
        return RedirectToAction(nameof(Details),new {id});
    }
}
