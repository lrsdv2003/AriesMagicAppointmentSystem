using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Client")]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        private const long MaxProfileImageSize = 2 * 1024 * 1024;
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "image/webp" };

        public ProfileController(UserManager<ApplicationUser> userManager, ApplicationDbContext context, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _context = context;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var bookings = await _context.Bookings
                .Where(b => b.ApplicationUserId == user.Id)
                .ToListAsync();

            var completeness = 0;
            if (!string.IsNullOrWhiteSpace(user.FullName)) completeness += 30;
            if (!string.IsNullOrWhiteSpace(user.Email)) completeness += 20;
            if (!string.IsNullOrWhiteSpace(user.PhoneNumber)) completeness += 20;
            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath)) completeness += 30;

            var vm = new ProfileViewModel
            {
                FullName = user.FullName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber ?? "",
                ProfilePicturePath = user.ProfilePicturePath,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                TotalBookings = bookings.Count,
                CompletedBookings = bookings.Count(b => b.Status == BookingStatus.Completed),
                PendingBookings = bookings.Count(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingDownpayment || b.Status == BookingStatus.AwaitingVerification),
                CompletionPercentage = completeness
            };

            ViewBag.EditModel = new EditProfileViewModel
            {
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber ?? "",
                Email = user.Email ?? ""
            };
            ViewBag.PasswordModel = new ChangePasswordViewModel();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please check the highlighted fields.";
                return RedirectToAction(nameof(Index));
            }

            bool emailChanged = !string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase);
            if (emailChanged)
            {
                var existing = await _userManager.FindByEmailAsync(model.Email);
                if (existing != null && existing.Id != user.Id)
                {
                    TempData["Error"] = "An account with this email already exists.";
                    return RedirectToAction(nameof(Index));
                }
            }

            user.FullName = model.FullName.Trim();
            user.PhoneNumber = model.PhoneNumber?.Trim();

            if (emailChanged)
            {
                var emailResult = await _userManager.SetEmailAsync(user, model.Email.Trim());
                if (!emailResult.Succeeded)
                {
                    TempData["Error"] = string.Join(" ", emailResult.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
                var userNameResult = await _userManager.SetUserNameAsync(user, model.Email.Trim());
                if (!userNameResult.Succeeded)
                {
                    TempData["Error"] = string.Join(" ", userNameResult.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
                user.EmailConfirmed = false;
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            if (emailChanged)
            {
                TempData["Success"] = "Profile updated. Please confirm your new email address.";
            }
            else
            {
                TempData["Success"] = "Profile updated successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(IFormFile? profileImage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var error = await ValidateImageAsync(profileImage);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction(nameof(Index));
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsFolder);

            var ext = Path.GetExtension(profileImage!.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var savePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await profileImage.CopyToAsync(stream);
            }

            // delete old photo
            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                var oldFile = Path.Combine(_env.WebRootPath, user.ProfilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldFile)) try { System.IO.File.Delete(oldFile); } catch { }
            }

            user.ProfilePicturePath = $"/uploads/profiles/{fileName}";
            await _userManager.UpdateAsync(user);

            TempData["Success"] = "Profile photo updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePhoto()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                var oldFile = Path.Combine(_env.WebRootPath, user.ProfilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldFile)) try { System.IO.File.Delete(oldFile); } catch { }
                user.ProfilePicturePath = null;
                await _userManager.UpdateAsync(user);
                TempData["Success"] = "Profile photo removed.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please check your password fields.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<string?> ValidateImageAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0) return "Please select an image file.";
            if (file.Length > MaxProfileImageSize) return "Image must be less than 2 MB.";
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return "Only JPG, PNG, and WEBP images are allowed.";
            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant())) return "Invalid image type.";
            if (!await HasValidImageSignatureAsync(file)) return "The file does not appear to be a valid image.";
            return null;
        }

        private async Task<bool> HasValidImageSignatureAsync(IFormFile file)
        {
            byte[] header = new byte[12];
            await using var stream = file.OpenReadStream();
            var read = await stream.ReadAsync(header, 0, header.Length);
            if (read < 4) return false;
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return true;
            if (read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return true;
            return false;
        }
    }
}
