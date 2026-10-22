using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Data;

namespace AriesMagicAppointmentSystem.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    public HomeController(ApplicationDbContext context) { _context = context; }

    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Index()
    {
        var stories=await AriesMagicAppointmentSystem.Services.FeedbackPolicy.PublicQuery(_context)
            .OrderByDescending(f=>f.PublishedAtUtc).Take(3)
            .Select(f=>new AriesMagicAppointmentSystem.ViewModels.ClientTestimonial(f.Experience,
                f.NameConsent?f.PublicDisplayName:"Verified client",f.Booking.EventType,true,true,true) {
                    OverallRating=f.OverallRating,
                    PublicImageIds=f.Media.Where(m=>f.MediaConsent&&m.PublicApproved&&m.ProcessingStatus=="Ready"&&m.ScannedAtUtc!=null).OrderBy(m=>m.Id).Select(m=>m.Id).ToList()
                }).ToListAsync();
        ViewData["PublicTestimonials"]=stories.Where(s=>AriesMagicAppointmentSystem.Services.FeedbackPolicy.SafePublicText(s.Excerpt)
            &&AriesMagicAppointmentSystem.Services.FeedbackPolicy.SafePublicText(s.PublicName))
            .Select(s=>s with {EventLabel=AriesMagicAppointmentSystem.Services.FeedbackPolicy.SafePublicText(s.EventLabel)?s.EventLabel:"Completed event"}).ToList();
        return View(await PublicPackagesAsync());
    }
    public async Task<IActionResult> Packages() => View(await PublicPackagesAsync());
    public async Task<IActionResult> Package(int id)
    {
        var package = await _context.Services.AsNoTracking().Include(s => s.Inclusions)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsArchived);
        return package == null ? NotFound() : View(package);
    }
    private Task<List<Service>> PublicPackagesAsync() => _context.Services.AsNoTracking()
        .Include(s => s.Inclusions).Where(s => !s.IsArchived).OrderBy(s => s.Price).ToListAsync();
    public IActionResult Privacy() => View();
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
