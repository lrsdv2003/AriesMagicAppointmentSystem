using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
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

        public ServicesController(ApplicationDbContext context)
        {
            _context = context;
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
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Archived()
        {
            var archivedPackages = await _context.Services
                .Where(s => s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return View(archivedPackages);
        }

        // STAFF + ADMIN: create package
        [Authorize(Roles = "Staff,Admin")]
        public IActionResult Create()
        {
            var model = new ServiceManageViewModel
            {
                Inclusions = new List<ServiceInclusionInputViewModel>
                {
                    new ServiceInclusionInputViewModel()
                }
            };

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Staff,Admin")]
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

            return RedirectToAction(nameof(Index));
        }
        // STAFF + ADMIN: edit package
        [Authorize(Roles = "Staff,Admin")]
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
        [Authorize(Roles = "Staff,Admin")]
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

            return RedirectToAction(nameof(Index));
        }

        // STAFF + ADMIN: archive package
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Archive(int? id)
        {
            if (id == null) return NotFound();

            var package = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id);

            if (package == null) return NotFound();

            return View(package);
        }

        [HttpPost, ActionName("Archive")]
        [Authorize(Roles = "Staff,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var package = await _context.Services.FindAsync(id);
            if (package == null) return NotFound();

            package.IsArchived = true;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // STAFF + ADMIN: restore package
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Restore(int? id)
        {
            if (id == null) return NotFound();

            var package = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == id);

            if (package == null) return NotFound();

            return View(package);
        }

        [HttpPost, ActionName("Restore")]
        [Authorize(Roles = "Staff,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreConfirmed(int id)
        {
            var package = await _context.Services.FindAsync(id);
            if (package == null) return NotFound();

            package.IsArchived = false;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Archived));
        }

        private async Task<bool> ServiceExists(int id)
        {
            return await _context.Services.AnyAsync(e => e.Id == id);
        }
    }
}