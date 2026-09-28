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

    public async Task<IActionResult> Index() => View(await PublicPackagesAsync());
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
