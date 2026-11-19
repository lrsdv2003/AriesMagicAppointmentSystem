using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles = "Client")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IWebHostEnvironment environment,
    ILogger<ProfileController> logger) : Controller
{
    private const long MaxPhotoSize = 2 * 1024 * 1024;

    private async Task<InternalProfileViewModel> ModelAsync(ApplicationUser user, InternalPersonalInput? input = null)
    {
        var passwordPolicy = users.Options.Password;
        var rules = new List<string> { $"At least {Math.Max(12, passwordPolicy.RequiredLength)} characters." };
        if (passwordPolicy.RequireUppercase) rules.Add("At least one uppercase letter.");
        if (passwordPolicy.RequireLowercase) rules.Add("At least one lowercase letter.");
        if (passwordPolicy.RequireDigit) rules.Add("At least one number.");
        if (passwordPolicy.RequireNonAlphanumeric) rules.Add("At least one special character.");
        if (passwordPolicy.RequiredUniqueChars > 1) rules.Add($"At least {passwordPolicy.RequiredUniqueChars} different characters.");
        return new()
        {
            DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? "Your account" : user.FullName,
            SavedFullName = user.FullName, SavedPhoneNumber = user.PhoneNumber ?? "",
            Email = user.Email ?? "Not provided",
            Role = string.Join(", ", await users.GetRolesAsync(user)),
            Status = !user.IsActive ? "Inactive" : user.LockoutEnd > DateTimeOffset.UtcNow ? "Locked" : "Active",
            Verified = user.EmailConfirmed,
            HasPhoto = PhotoPath(user.ProfilePicturePath) is string resolvedPhoto && System.IO.File.Exists(resolvedPhoto),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            PasswordRules = rules,
            Personal = input ?? new InternalPersonalInput { FullName = user.FullName, PhoneNumber = user.PhoneNumber }
        };
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        return user == null ? Challenge() : View(await ModelAsync(user));
    }

    // Email, role, security flags and account identity are not bindable here.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "Personal")] InternalPersonalInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        input.FullName = input.FullName?.Trim() ?? "";
        if (input.FullName.Length < 2)
            ModelState.AddModelError("Personal.FullName", "Enter your full name (at least 2 characters).");
        if (!ModelState.IsValid) return View("Index", await ModelAsync(user, input));
        var previousName = user.FullName;
        var previousPhone = user.PhoneNumber;
        user.FullName = input.FullName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            user.FullName = previousName; user.PhoneNumber = previousPhone;
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View("Index", await ModelAsync(user, input));
        }
        TempData["ProfileSuccess"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Photo()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var path = PhotoPath(user.ProfilePicturePath);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(path, contentType);
    }

    // Existing public profile paths remain readable only for the authenticated account.
    // New uploads use the internal private storage scheme, not a public static folder.
    private string? PhotoPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var prefix = value.StartsWith("/private-profiles/", StringComparison.Ordinal)
            ? "/private-profiles/" : "/uploads/profiles/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = value[prefix.Length..];
        if (name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\') ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _) ||
            !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(name).ToLowerInvariant()))
            return null;
        return prefix == "/private-profiles/"
            ? Path.Combine(environment.ContentRootPath, "App_Data", "profiles", name)
            : Path.Combine(environment.WebRootPath, "uploads", "profiles", name);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(2 * 1024 * 1024 + 65536)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("uploads")]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (photo == null || photo.Length < 12 || photo.Length > MaxPhotoSize ||
            !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(photo.FileName).ToLowerInvariant()) ||
            !await ImageUploadValidation.HasMatchingSignatureAsync(photo))
        {
            ModelState.AddModelError("photo", "Choose a valid JPG, PNG or WEBP image up to 2 MB.");
            return View("Index", await ModelAsync(user));
        }
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "profiles");
        var filename = Guid.NewGuid().ToString("N") + Path.GetExtension(photo.FileName).ToLowerInvariant();
        var path = Path.Combine(folder, filename);
        var old = user.ProfilePicturePath;
        try
        {
            Directory.CreateDirectory(folder);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                await photo.CopyToAsync(stream);
            user.ProfilePicturePath = "/private-profiles/" + filename;
            var saved = await users.UpdateAsync(user);
            if (!saved.Succeeded)
            {
                user.ProfilePicturePath = old;
                DeletePhoto(path);
                foreach (var error in saved.Errors) ModelState.AddModelError("", error.Description);
                return View("Index", await ModelAsync(user));
            }
        }
        catch (IOException ex)
        {
            user.ProfilePicturePath = old;
            DeletePhoto(path);
            logger.LogError(ex, "Client profile image save failed.");
            ModelState.AddModelError("photo", "The photo could not be saved. Try again.");
            return View("Index", await ModelAsync(user));
        }
        DeletePhoto(PhotoPath(old));
        TempData["ProfileSuccess"] = "Profile photo updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var old = user.ProfilePicturePath;
        user.ProfilePicturePath = null;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            user.ProfilePicturePath = old;
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View("Index", await ModelAsync(user));
        }
        DeletePhoto(PhotoPath(old));
        TempData["ProfileSuccess"] = "Profile photo removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] InternalPasswordInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var nextPassword = input.NewPassword ?? "";
        if (nextPassword.Length < Math.Max(12, users.Options.Password.RequiredLength))
            ModelState.AddModelError("Password.NewPassword", $"Use at least {Math.Max(12, users.Options.Password.RequiredLength)} characters.");
        if (ModelState.IsValid)
        {
            var result = await users.ChangePasswordAsync(user, input.CurrentPassword, nextPassword);
            if (result.Succeeded)
            {
                await signIn.RefreshSignInAsync(user);
                TempData["ProfileSuccess"] = "Password changed successfully.";
                return RedirectToAction(nameof(Index), null, null, "security");
            }
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? "Password.CurrentPassword" : "Password.NewPassword", error.Description);
        }
        // Identity credentials must not be reflected back into the form or response.
        foreach (var key in new[] { "Password.CurrentPassword", "Password.NewPassword", "Password.ConfirmPassword" })
            if (ModelState.TryGetValue(key, out var field)) field.RawValue = field.AttemptedValue = null;
        return View("Index", await ModelAsync(user));
    }

    private void DeletePhoto(string? path)
    {
        if (path == null) return;
        try { System.IO.File.Delete(path); }
        catch (IOException ex) { logger.LogWarning(ex, "Client profile photo cleanup failed."); }
    }
}
