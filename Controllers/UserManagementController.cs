using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public async Task<IActionResult> Index(string? search, string? status = "All", string? role = null, string? verification = null)
        {
            var users = await _userManager.Users.OrderBy(u => u.FullName).ToListAsync();
            var roles = new Dictionary<string, string>();
            foreach (var user in users)
                roles[user.Id] = string.Join(", ", await _userManager.GetRolesAsync(user));
            if (!string.IsNullOrWhiteSpace(search))
                users = users.Where(u => u.FullName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || (u.Email?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            if (!string.IsNullOrWhiteSpace(role))
                users = users.Where(u => roles[u.Id].Split(", ").Contains(role)).ToList();
            users = status switch
            {
                "Active" => users.Where(u => u.IsActive && !(u.LockoutEnd > DateTimeOffset.UtcNow)).ToList(),
                "Disabled" => users.Where(u => !u.IsActive).ToList(),
                "Locked" => users.Where(u => u.LockoutEnd > DateTimeOffset.UtcNow).ToList(),
                _ => users
            };
            if (verification == "Verified") users = users.Where(u => u.EmailConfirmed).ToList();
            if (verification == "Unverified") users = users.Where(u => !u.EmailConfirmed).ToList();
            ViewBag.UserRoles = roles;
            return View(users);
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            ViewBag.Roles = string.Join(", ", await _userManager.GetRolesAsync(user));
            ViewBag.CanManage = await CanManageStaffAsync(user);
            return View(user);
        }

        private async Task<bool> CanManageStaffAsync(ApplicationUser user) =>
            user.Id != User.FindFirstValue(ClaimTypes.NameIdentifier)
            && await _userManager.IsInRoleAsync(user, "Staff")
            && !await _userManager.IsInRoleAsync(user, "Admin")
            && !await _userManager.IsInRoleAsync(user, "Owner");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStaff(StaffUserViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError(nameof(model.Password), "A password is required.");
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

            var roleResult = await _userManager.AddToRoleAsync(user, "Staff");
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                TempData["ErrorMessage"] = "Unable to create the staff account. Please try again.";
                return RedirectToAction(nameof(Index));
            }

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
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please check the staff account fields.";
                return RedirectToAction(nameof(Index));
            }

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

            if (!await CanManageStaffAsync(user)) return Forbid();

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
                SystemActivityType.UserUpdated,
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

            if (!await CanManageStaffAsync(user)) return Forbid();

            user.IsActive = false;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = "Unable to update this user. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            await _userManager.UpdateSecurityStampAsync(user);

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

            if (!await CanManageStaffAsync(user)) return Forbid();

            user.IsActive = true;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = "Unable to update this user. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            await _userManager.UpdateSecurityStampAsync(user);

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