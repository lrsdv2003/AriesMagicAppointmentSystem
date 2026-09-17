using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace AriesMagicAppointmentSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IEmailVerificationService _emailVerificationService;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IEmailService emailService,
            IEmailVerificationService emailVerificationService)
            {
                _signInManager = signInManager;
                _userManager = userManager;
                _emailService = emailService;
                _emailVerificationService = emailVerificationService;
            }
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("", "An account with this email already exists.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                EmailConfirmed = false,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
                return View(model);
            }

            await _userManager.AddToRoleAsync(user, "Client");
            var issue = await _emailVerificationService.IssueCodeAsync(user, enforceCooldown: false);
            if (!issue.Sent)
                TempData["VerificationError"] = issue.ErrorMessage ?? "We couldn't send your verification code. Please try again.";

            return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> VerifyEmail(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction(nameof(Login));
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed)
            {
                TempData["SuccessMessage"] = "Your email has already been verified.";
                return RedirectToAction(nameof(Login));
            }

            var state = await _emailVerificationService.GetStateAsync(user.Id);
            return View(BuildVerifyEmailViewModel(user, state));
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed)
            {
                TempData["SuccessMessage"] = "Your email has already been verified.";
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                var state = await _emailVerificationService.GetStateAsync(user.Id);
                FillVerificationDisplay(model, user, state);
                return View(model);
            }

            var result = await _emailVerificationService.VerifyCodeAsync(user, model.Code);
            switch (result.Status)
            {
                case EmailVerificationCheckStatus.Success:
                    TempData["VerifiedEmail"] = user.Email;
                    return RedirectToAction(nameof(EmailVerified));
                case EmailVerificationCheckStatus.Incorrect:
                    ModelState.AddModelError(nameof(model.Code), $"Incorrect verification code. Please try again. {result.AttemptsRemaining} attempt(s) remaining.");
                    break;
                case EmailVerificationCheckStatus.Expired:
                    ModelState.AddModelError(nameof(model.Code), "This verification code has expired. Please request a new code.");
                    break;
                case EmailVerificationCheckStatus.Locked:
                    ModelState.AddModelError(nameof(model.Code), "Too many incorrect attempts. Please request a new verification code.");
                    break;
                case EmailVerificationCheckStatus.NoActiveCode:
                    ModelState.AddModelError(nameof(model.Code), "No active verification code is available. Please request a new code.");
                    break;
                case EmailVerificationCheckStatus.AlreadyVerified:
                    TempData["SuccessMessage"] = "Your email has already been verified.";
                    return RedirectToAction(nameof(Login));
                default:
                    ModelState.AddModelError("", "We couldn't verify your email right now. Please try again.");
                    break;
            }

            var currentState = await _emailVerificationService.GetStateAsync(user.Id);
            FillVerificationDisplay(model, user, currentState);
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerificationCode(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed)
            {
                TempData["SuccessMessage"] = "Your email has already been verified.";
                return RedirectToAction(nameof(Login));
            }

            var issue = await _emailVerificationService.IssueCodeAsync(user, enforceCooldown: true);
            if (issue.Sent)
                TempData["VerificationSuccess"] = "A new verification code has been sent to your email.";
            else if (issue.CooldownSeconds > 0)
                TempData["VerificationError"] = $"Please wait {issue.CooldownSeconds} second(s) before requesting another code.";
            else
                TempData["VerificationError"] = issue.ErrorMessage ?? "We couldn't send your verification code. Please try again.";

            return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeVerificationEmail(string userId, string email, string currentPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed)
            {
                TempData["SuccessMessage"] = "Your email has already been verified.";
                return RedirectToAction(nameof(Login));
            }

            if (string.IsNullOrWhiteSpace(currentPassword) || !await _userManager.CheckPasswordAsync(user, currentPassword))
            {
                TempData["VerificationError"] = "Enter your current password to change the email address.";
                return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
            }

            email = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            {
                TempData["VerificationError"] = "Enter a valid email address.";
                return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
            }

            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null && existing.Id != user.Id)
            {
                TempData["VerificationError"] = "That email address is already registered.";
                return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
            }

            user.Email = email;
            user.UserName = email;
            user.EmailConfirmed = false;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                TempData["VerificationError"] = "We couldn't update your email address. Please try again.";
                return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
            }

            await _emailVerificationService.InvalidateAsync(user.Id);
            var issue = await _emailVerificationService.IssueCodeAsync(user, enforceCooldown: false);
            TempData[issue.Sent ? "VerificationSuccess" : "VerificationError"] = issue.Sent
                ? "Your email was updated and a new verification code was sent."
                : issue.ErrorMessage ?? "We couldn't send your verification code. Please try again.";

            return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult EmailVerified()
        {
            if (TempData.Peek("VerifiedEmail") == null)
                return RedirectToAction(nameof(Login));
            return View();
        }

        private static VerifyEmailViewModel BuildVerifyEmailViewModel(ApplicationUser user, EmailVerificationState state)
        {
            var model = new VerifyEmailViewModel { UserId = user.Id };
            FillVerificationDisplay(model, user, state);
            return model;
        }

        private static void FillVerificationDisplay(VerifyEmailViewModel model, ApplicationUser user, EmailVerificationState state)
        {
            model.MaskedEmail = MaskEmail(user.Email ?? string.Empty);
            model.ExpiresAtUtc = state.ExpiresAtUtc;
            model.ResendSecondsRemaining = state.ResendSecondsRemaining;
            model.HasActiveCode = state.HasActiveCode;
            model.AttemptsLocked = state.AttemptsLocked;
        }

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "your email address";
            var local = email[..at];
            var domain = email[at..];
            var visible = local.Length <= 3 ? local[..1] : local[..Math.Min(3, local.Length)];
            return $"{visible}••••{domain}";
        }

        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Incorrect Email or Password.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty,
                    "Your account has been disabled. Please contact the administrator.");
                return View(model);
            }

            if (!user.EmailConfirmed)
            {
                if (!await _userManager.CheckPasswordAsync(user, model.Password))
                {
                    ModelState.AddModelError(string.Empty, "Incorrect Email or Password.");
                    return View(model);
                }

                TempData["VerificationError"] = "Email Verification Required. Please verify your email before continuing.";
                return RedirectToAction(nameof(VerifyEmail), new { userId = user.Id });
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(returnUrl) &&
                    Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                if (await _userManager.IsInRoleAsync(user, "Owner"))
                {
                    return RedirectToAction("Owner", "Dashboard");
                }
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    return RedirectToAction("Admin", "Dashboard");
                }

                if (await _userManager.IsInRoleAsync(user, "Staff"))
                {
                    return RedirectToAction("Staff", "Dashboard");
                }

                if (await _userManager.IsInRoleAsync(user, "Client"))
                {
                    return RedirectToAction("MyBookings", "Bookings");
                }

                await _signInManager.SignOutAsync();

                ModelState.AddModelError(string.Empty,
                    "Your account has no assigned role.");

                return View(model);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty,
                    "Your account is locked.");

                return View(model);
            }

            if (result.IsNotAllowed)
            {
                ModelState.AddModelError(string.Empty,
                    "Login is not allowed.");

                return View(model);
            }

            ModelState.AddModelError(string.Empty,
                "Incorrect Email or Password.");

            return View(model);
        }
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
            {
                TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
                return RedirectToAction(nameof(Login));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var resetLink = Url.Action(
                nameof(ResetPassword),
                "Account",
                new { email = user.Email, token = encodedToken },
                Request.Scheme);

            var message = $@"
                <p>Hello {user.FullName},</p>
                <p>You requested to reset your password.</p>
                <p>
                    <a href='{resetLink}'>Click here to reset your password</a>
                </p>
                <p>If you did not request this, you may ignore this email.</p>";

            await _emailService.SendEmailAsync(user.Email!, "Reset Your Password", message);

            TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult ResetPassword(string? email, string? token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                TempData["ErrorMessage"] = "Invalid password reset request.";
                return RedirectToAction(nameof(Login));
            }

            var model = new ResetPasswordViewModel
            {
                Email = email,
                Token = token
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                TempData["SuccessMessage"] = "Password has been reset successfully. You may now log in.";
                return RedirectToAction(nameof(Login));
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "Invalid password reset request.");
                return View(model);
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.NewPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            TempData["SuccessMessage"] = "Your password has been reset successfully. You may now log in.";
            return RedirectToAction(nameof(Login));
        }
    }
}