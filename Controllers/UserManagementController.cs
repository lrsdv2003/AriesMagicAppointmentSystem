using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Claims;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ISystemActivityService _activityService;

        public UserManagementController(UserManager<ApplicationUser> userManager, ISystemActivityService activityService)
        {
            _userManager = userManager;
            _activityService = activityService;
        }

        public async Task<IActionResult> Index(string? search, string? status = "All")
        {
            var staffUsers = new List<ApplicationUser>();

            foreach (var user in _userManager.Users.ToList())
            {
                if (await _userManager.IsInRoleAsync(user, "Staff"))
                {
                    staffUsers.Add(user);
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowered = search.ToLower();

                staffUsers = staffUsers.Where(u =>
                    (u.FullName != null && u.FullName.ToLower().Contains(lowered)) ||
                    (u.Email != null && u.Email.ToLower().Contains(lowered)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(lowered)))
                    .ToList();
            }

            if (status == "Active")
            {
                staffUsers = staffUsers.Where(u => u.IsActive).ToList();
            }
            else if (status == "Disabled")
            {
                staffUsers = staffUsers.Where(u => !u.IsActive).ToList();
            }
            else if (status == "NeverLoggedIn")
            {
                staffUsers = staffUsers.Where(u => !u.LastLoginAt.HasValue).ToList();
            }

            ViewBag.Search = search;
            ViewBag.Status = status;

            return View(staffUsers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStaff(StaffUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please complete all required staff account fields.";
                return RedirectToAction(nameof(Index));
            }

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                TempData["ErrorMessage"] = "An account with this email already exists.";
                return RedirectToAction(nameof(Index));
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password!);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _userManager.AddToRoleAsync(user, "Staff");

            await _activityService.LogAsync(
                SystemActivityType.UserCreated,
                $"Created staff account: {user.FullName} ({user.Email})",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                user.Id,
                "ApplicationUser");

            TempData["SuccessMessage"] = "Staff account created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStaff(StaffUserViewModel model)
        {
            if (string.IsNullOrEmpty(model.Id))
            {
                TempData["ErrorMessage"] = "Invalid staff account.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Staff account not found.";
                return RedirectToAction(nameof(Index));
            }

            user.FullName = model.FullName;
            user.Email = model.Email;
            user.UserName = model.Email;
            user.PhoneNumber = model.PhoneNumber;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _activityService.LogAsync(
                SystemActivityType.UserEnabled, // Reusing for edit
                $"Updated staff account: {user.FullName} ({user.Email})",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                user.Id,
                "ApplicationUser");

            TempData["SuccessMessage"] = "Staff account updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableStaff(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Staff account not found.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            await _activityService.LogAsync(
                SystemActivityType.UserDisabled,
                $"Disabled staff account: {user.FullName} ({user.Email})",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                user.Id,
                "ApplicationUser");

            TempData["SuccessMessage"] = "Staff account disabled successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateStaff(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Staff account not found.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = true;
            await _userManager.UpdateAsync(user);

            await _activityService.LogAsync(
                SystemActivityType.UserEnabled,
                $"Activated staff account: {user.FullName} ({user.Email})",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                user.Id,
                "ApplicationUser");

            TempData["SuccessMessage"] = "Staff account activated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}