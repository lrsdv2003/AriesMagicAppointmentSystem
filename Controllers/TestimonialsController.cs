using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles="Staff")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TestimonialsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string filter="Pending",string? q=null,int page=1)
    {
        q=(q??"").Trim();if(q.Length>100)q=q[..100];
        var query=db.EventFeedbacks.AsNoTracking().Include(f=>f.Booking).Include(f=>f.Media).AsQueryable();
        query=filter switch {
            "Pending"=>query.Where(f=>f.ModerationStatus=="Pending"),
            "Approved"=>query.Where(f=>f.ModerationStatus=="Approved"),
            "Featured"=>query.Where(f=>f.Featured),
            "NotFeatured"=>query.Where(f=>!f.Featured),
            "Withdrawn"=>query.Where(f=>f.WithdrawnAtUtc!=null),
            _=>query
        };
        if(q!="")query=query.Where(f=>f.PublicDisplayName.Contains(q)||f.Booking.PackageName.Contains(q));
        var total=await query.CountAsync();var pages=Math.Max(1,(int)Math.Ceiling(total/20d));page=Math.Clamp(page,1,pages);
        return View(new FeedbackListPage(await query.OrderByDescending(f=>f.SubmittedAtUtc).Skip((page-1)*20).Take(20).ToListAsync(),page,pages,total,filter,q));
    }
    public async Task<IActionResult> Review(int id)
    {
        var item=await db.EventFeedbacks.AsNoTracking().Include(f=>f.Booking).Include(f=>f.Media)
            .Include(f=>f.PublicationEvents.OrderByDescending(e=>e.AtUtc)).SingleOrDefaultAsync(f=>f.Id==id);
        return item==null?NotFound():View(item);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(FeedbackReviewInput input)
    {
        if(!ModelState.IsValid)return BadRequest("Check the moderation form.");
        if(!input.ConfirmPublication)return BadRequest("Confirm that you reviewed this moderation change.");
        byte[] version;try{version=Convert.FromBase64String(input.Version);}catch(FormatException){return BadRequest("Refresh this review.");}
        if(version.Length!=8)return BadRequest("Refresh this review.");
        var item=await db.EventFeedbacks.Include(f=>f.Booking).Include(f=>f.Media).SingleOrDefaultAsync(f=>f.Id==input.Id);
        if(item==null)return NotFound();
        db.Entry(item).Property(f=>f.Version).OriginalValue=version;
        var publicAction=input.Decision is "Approve" or "Feature";
        if(publicAction) {
            if(!input.ConfirmPublication)return BadRequest("Confirm that you checked consent, content, identities, and media permissions.");
            if(!FeedbackPolicy.Eligible(item.Booking)||!item.TextConsent||item.WithdrawnAtUtc!=null||string.IsNullOrWhiteSpace(item.Experience))
                return BadRequest("This feedback does not have active consent or completed-event eligibility.");
            if(!FeedbackPolicy.SafePublicText(item.Experience)||item.NameConsent&&!FeedbackPolicy.SafePublicText(item.PublicDisplayName))
                return BadRequest("Remove this submission from publication consideration: contact details, markup, or links need privacy review. Staff cannot edit client answers.");
            var selected=input.PublicMediaIds.Distinct().ToList();
            if(selected.Count>0&&(!item.MediaConsent||item.Media.Count(m=>selected.Contains(m.Id)&&m.ProcessingStatus=="Ready"&&m.ScannedAtUtc!=null)!=selected.Count))
                return BadRequest("Only consented media that passed security processing may be selected.");
            if(input.Decision=="Feature"&&item.ModerationStatus!="Approved")return BadRequest("Approve the testimonial before featuring it.");
            item.ModerationStatus="Approved";
            item.Featured=input.Decision=="Feature";
            foreach(var media in item.Media)media.PublicApproved=selected.Contains(media.Id);
            if(item.Featured){item.PublishedAtUtc=DateTime.UtcNow;item.UnpublishedAtUtc=null;}
            else if(item.PublishedAtUtc.HasValue)item.UnpublishedAtUtc=DateTime.UtcNow;
        } else if(input.Decision is "Private" or "Unfeature") {
            item.Featured=false;item.UnpublishedAtUtc=DateTime.UtcNow;
            if(input.Decision=="Private")item.ModerationStatus=item.WithdrawnAtUtc!=null?"Withdrawn":"Private";
            foreach(var media in item.Media)media.PublicApproved=false;
        } else return BadRequest("Choose a valid review action.");
        item.ReviewedBy=User.FindFirstValue(ClaimTypes.NameIdentifier);item.ReviewedAtUtc=DateTime.UtcNow;
        FeedbackPolicy.Audit(db,item,item.ReviewedBy!,input.Decision,input.Reason?.Trim()??"");
        try{await db.SaveChangesAsync();}
        catch(DbUpdateConcurrencyException){return Conflict("Another review or consent withdrawal changed this submission. Refresh before taking another action.");}
        TempData["Success"]="Review saved.";
        return RedirectToAction(nameof(Review),new {id=item.Id});
    }
}
