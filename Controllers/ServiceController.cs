using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize]
    public class ServicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ISystemActivityService _activityService;

        public ServicesController(ApplicationDbContext context, ISystemActivityService activityService)
        {
            _context = context;
            _activityService = activityService;
        }

        // STAFF + ADMIN: view active packages
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Index()
        {
            var packages = await _context.Services
                .Where(s => !s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return View(packages);
        }

        // STAFF + ADMIN: view package details
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var package = await _context.Services
                .Include(s => s.Inclusions)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (package == null) return NotFound();

            return View(package);
        }

        // STAFF + ADMIN: view archived packages
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Archived()
        {
            var archivedPackages = await _context.Services
                .Where(s => s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return View(archivedPackages);
        }
        // ADMIN: show create package form
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            var model = new ServiceManageViewModel();

            if (model.Inclusions == null || !model.Inclusions.Any())
            {
                model.Inclusions = new List<ServiceInclusionInputViewModel>
                {
                    new ServiceInclusionInputViewModel()
                };
            }

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServiceManageViewModel model)
        {
            model.Inclusions = model.Inclusions
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .ToList();

            if (await _context.Services.AnyAsync(s => s.Name == model.Name))
            {
                ModelState.AddModelError("Name", "A package with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var service = new Service
            {
                Name = model.Name,
                Price = model.Price,
                DurationInHours = model.DurationInHours,
                Description = model.Description,
                IsArchived = false,
                Inclusions = model.Inclusions.Select(i => new ServiceInclusion
                {
                    Name = i.Name,
                    DeductionAmount = i.DeductionAmount,
                    IsRemovable = i.IsRemovable
                }).ToList()
            };

            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            await _activityService.LogAsync(
                SystemActivityType.ServiceCreated,
                $"Created package '{service.Name}' with price ₱{service.Price:N2}",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                service.Id.ToString(),
                "Service",
                new { service.Name, service.Price, service.DurationInHours }
            );

            return RedirectToAction(nameof(Index));
        }
        // STAFF + ADMIN: edit package
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var service = await _context.Services
                .Include(s => s.Inclusions)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (service == null) return NotFound();

            var model = new ServiceManageViewModel
            {
                Id = service.Id,
                Name = service.Name,
                Price = service.Price,
                DurationInHours = service.DurationInHours,
                Description = service.Description,
                Inclusions = service.Inclusions.Select(i => new ServiceInclusionInputViewModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    DeductionAmount = i.DeductionAmount,
                    IsRemovable = i.IsRemovable
                }).ToList()
            };

            if (!model.Inclusions.Any())
            {
                model.Inclusions.Add(new ServiceInclusionInputViewModel());
            }

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ServiceManageViewModel model)
        {
            if (id != model.Id) return NotFound();

            model.Inclusions = model.Inclusions
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .ToList();

            if (await _context.Services.AnyAsync(s => s.Name == model.Name && s.Id != model.Id))
            {
                ModelState.AddModelError("Name", "A package with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var service = await _context.Services
                .Include(s => s.Inclusions)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (service == null) return NotFound();

            var oldName = service.Name;
            var oldPrice = service.Price;

            service.Name = model.Name;
            service.Price = model.Price;
            service.DurationInHours = model.DurationInHours;
            service.Description = model.Description;

            _context.ServiceInclusions.RemoveRange(service.Inclusions);

            service.Inclusions = model.Inclusions.Select(i => new ServiceInclusion
            {
                Name = i.Name,
                DeductionAmount = i.DeductionAmount,
                IsRemovable = i.IsRemovable
            }).ToList();

            await _context.SaveChangesAsync();

            await _activityService.LogAsync(
                SystemActivityType.ServiceUpdated,
                $"Updated package '{oldName}' (was ₱{oldPrice:N2}) to '{service.Name}' (₱{service.Price:N2})",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                service.Id.ToString(),
                "Service",
                new { oldName, oldPrice, newName = service.Name, newPrice = service.Price }
            );

            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ActionName("Archive")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var package = await _context.Services.FindAsync(id);
            if (package == null) return NotFound();

            var packageName = package.Name;
            package.IsArchived = true;
            await _context.SaveChangesAsync();

            if (User.IsInRole("Admin"))
            {
                await _activityService.LogAsync(
                    SystemActivityType.ServiceArchived,
                    $"Archived package '{packageName}'",
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                    User.Identity?.Name ?? "Unknown",
                    package.Id.ToString(),
                    "Service"
                );
            }

            return RedirectToAction(nameof(Index));
        }

        // ADMIN: restore package
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Restore(int? id)
        {
            if (id == null) return NotFound();

            var package = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id);

            if (package == null) return NotFound();

            return View(package);
        }

        [HttpPost, ActionName("Restore")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreConfirmed(int id)
        {
            var package = await _context.Services.FindAsync(id);
            if (package == null) return NotFound();

            var packageName = package.Name;
            package.IsArchived = false;
            await _context.SaveChangesAsync();

            if (User.IsInRole("Admin"))
            {
                await _activityService.LogAsync(
                    SystemActivityType.ServiceRestored,
                    $"Restored package '{packageName}'",
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                    User.Identity?.Name ?? "Unknown",
                    package.Id.ToString(),
                    "Service"
                );
            }

            return RedirectToAction(nameof(Archived));
        }

        private async Task<bool> ServiceExists(int id)
        {
            return await _context.Services.AnyAsync(e => e.Id == id);
        }
    }
}