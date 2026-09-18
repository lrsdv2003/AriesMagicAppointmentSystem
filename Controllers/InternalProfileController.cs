using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles = "Staff,Admin,Owner")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalProfileController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IWebHostEnvironment environment, ISystemActivityService activity, ILogger<InternalProfileController> logger) : Controller
{
    private const long MaxPhotoSize = 2 * 1024 * 1024;
    private async Task<InternalProfileViewModel> ModelAsync(ApplicationUser user, InternalPersonalInput? input = null)
    {
        var policy = users.Options.Password;
        var rules = new List<string> { $"At least {policy.RequiredLength} characters." };
        if (policy.RequireUppercase) rules.Add("At least one uppercase letter.");
        if (policy.RequireLowercase) rules.Add("At least one lowercase letter.");
        if (policy.RequireDigit) rules.Add("At least one number.");
        if (policy.RequireNonAlphanumeric) rules.Add("At least one special character.");
        if (policy.RequiredUniqueChars > 1) rules.Add($"At least {policy.RequiredUniqueChars} different characters.");
        return new() { DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? "Your account" : user.FullName,
            Email = user.Email ?? "Not provided", Role = string.Join(", ", await users.GetRolesAsync(user)),
            Status = !user.IsActive ? "Inactive" : user.LockoutEnd > DateTimeOffset.UtcNow ? "Locked" : "Active",
            Verified = user.EmailConfirmed, HasPhoto = PhotoPath(user.ProfilePicturePath) != null,
            CreatedAt = user.CreatedAt, LastLoginAt = user.LastLoginAt, PasswordRules = rules,
            Personal = input ?? new() { FullName = user.FullName, PhoneNumber = user.PhoneNumber } };
    }
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        return user == null ? Challenge() : View(await ModelAsync(user));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "Personal")] InternalPersonalInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        input.FullName = input.FullName?.Trim() ?? "";
        if (input.FullName.Length < 2) ModelState.AddModelError("Personal.FullName", "Enter your full name (at least 2 characters).");
        if (!ModelState.IsValid) return View("Index", await ModelAsync(user, input));
        var oldName = user.FullName; var oldPhone = user.PhoneNumber;
        user.FullName = input.FullName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            user.FullName = oldName; user.PhoneNumber = oldPhone;
            Errors(result);
            return View("Index", await ModelAsync(user, input));
        }
        await AuditAsync(user, SystemActivityType.ProfileUpdated, "Personal profile updated.");
        TempData["ProfileSuccess"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] InternalPasswordInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (ModelState.IsValid)
        {
            var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
            if (result.Succeeded)
            {
                await signIn.RefreshSignInAsync(user);
                await AuditAsync(user, SystemActivityType.PasswordChanged, "Account password changed.");
                TempData["ProfileSuccess"] = "Password changed successfully.";
                return RedirectToAction(nameof(Index), null, null, "security");
            }
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? "Password.CurrentPassword" : "Password.NewPassword", error.Description);
        }
        // Never send submitted passwords back in rendered HTML.
        foreach (var key in new[] { "Password.CurrentPassword", "Password.NewPassword", "Password.ConfirmPassword" })
            if (ModelState.TryGetValue(key, out var entry)) entry.RawValue = entry.AttemptedValue = null;
        return View("Index", await ModelAsync(user));
    }
    [HttpGet]
    public async Task<IActionResult> Photo()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var path = PhotoPath(user.ProfilePicturePath);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var type = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return PhysicalFile(path, type);
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(2 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var bytes = await ReadPhotoAsync(photo);
        if (bytes == null)
        {
            ModelState.AddModelError("photo", "Choose a valid JPG, PNG or WEBP image up to 2 MB.");
            return View("Index", await ModelAsync(user));
        }
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "profiles");
        var old = user.ProfilePicturePath;
        var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(photo!.FileName).ToLowerInvariant();
        var path = Path.Combine(folder, fileName);
        try
        {
            Directory.CreateDirectory(folder);
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            user.ProfilePicturePath = "/private-profiles/" + fileName;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) { user.ProfilePicturePath = old; DeletePhoto(path); Errors(result); return View("Index", await ModelAsync(user)); }
        }
        catch (IOException ex)
        {
            user.ProfilePicturePath = old; DeletePhoto(path);
            logger.LogError(ex, "Profile photo storage failed.");
            ModelState.AddModelError("photo", "The photo could not be saved. Please try again.");
            return View("Index", await ModelAsync(user));
        }
        DeletePhoto(PhotoPath(old));
        await AuditAsync(user, SystemActivityType.ProfilePictureChanged, "Profile photo updated.");
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
        if (!result.Succeeded) { user.ProfilePicturePath = old; Errors(result); return View("Index", await ModelAsync(user)); }
        DeletePhoto(PhotoPath(old));
        await AuditAsync(user, SystemActivityType.ProfilePictureChanged, "Profile photo removed.");
        TempData["ProfileSuccess"] = "Profile photo removed.";
        return RedirectToAction(nameof(Index));
    }
    private string? PhotoPath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var prefix = value.StartsWith("/private-profiles/", StringComparison.Ordinal) ? "/private-profiles/" : "/uploads/profiles/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = value[prefix.Length..];
        if (name != Path.GetFileName(name) || name.Contains('\\') || name.Contains('/') ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _) ||
            !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(name).ToLowerInvariant())) return null;
        return prefix == "/private-profiles/" ? Path.Combine(environment.ContentRootPath, "App_Data", "profiles", name)
            : Path.Combine(environment.WebRootPath, "uploads", "profiles", name);
    }
    private static async Task<byte[]?> ReadPhotoAsync(IFormFile? photo)
    {
        if (photo == null || photo.Length < 12 || photo.Length > MaxPhotoSize) return null;
        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        using var memory = new MemoryStream();
        await using var stream = photo.OpenReadStream();
        var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer)) > 0) {
            if (memory.Length + read > MaxPhotoSize) return null;
            memory.Write(buffer, 0, read);
        }
        var data = memory.ToArray();
        if (data.Length < 12) return null;
        var valid = ext switch {
            ".jpg" or ".jpeg" => photo.ContentType == "image/jpeg" && data[0] == 255 && data[1] == 216 && data[2] == 255 && data[^2] == 255 && data[^1] == 217,
            ".png" => photo.ContentType == "image/png" && data.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
            ".webp" => photo.ContentType == "image/webp" && System.Text.Encoding.ASCII.GetString(data,0,4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data,8,4) == "WEBP",
            _ => false };
        return valid ? data : null;
    }
    private void DeletePhoto(string? path) { if (path != null) try { System.IO.File.Delete(path); } catch (IOException ex) { logger.LogWarning(ex, "Old profile photo cleanup failed."); } }
    private void Errors(IdentityResult result) { foreach (var error in result.Errors) ModelState.AddModelError("", error.Description); }
    private async Task AuditAsync(ApplicationUser user, SystemActivityType type, string description)
    {
        try { await activity.LogAsync(type, description, user.Id, user.FullName, user.Id, "Account"); }
        catch (Exception ex) { logger.LogError(ex, "Account change audit failed."); TempData["ProfileWarning"] = "Your change was saved, but the activity record could not be written."; }
    }
}
