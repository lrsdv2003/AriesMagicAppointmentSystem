using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;

namespace AriesMagicAppointmentSystem.Services
{
    public enum EmailVerificationCheckStatus
    {
        Success,
        Incorrect,
        Expired,
        Locked,
        NoActiveCode,
        AlreadyVerified,
        Error
    }

    public sealed record EmailVerificationIssueResult(
        bool Sent,
        int CooldownSeconds = 0,
        string? ErrorMessage = null);

    public sealed record EmailVerificationCheckResult(
        EmailVerificationCheckStatus Status,
        int AttemptsRemaining = 0);
    public sealed record EmailVerificationState(
        DateTime? ExpiresAtUtc,
        int ResendSecondsRemaining,
        bool HasActiveCode,
        bool AttemptsLocked);

    public interface IEmailVerificationService
    {
        Task<EmailVerificationIssueResult> IssueCodeAsync(ApplicationUser user, bool enforceCooldown = true);
        Task<EmailVerificationCheckResult> VerifyCodeAsync(ApplicationUser user, string code);
        Task<EmailVerificationState> GetStateAsync(string userId);
        Task InvalidateAsync(string userId);
    }

    public class EmailVerificationService : IEmailVerificationService
    {
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private const int MaxAttempts = 5;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IPasswordHasher<EmailVerification> _passwordHasher;
        private readonly ILogger<EmailVerificationService> _logger;

        public EmailVerificationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailService emailService,
            IPasswordHasher<EmailVerification> passwordHasher,
            ILogger<EmailVerificationService> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailService = emailService;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<EmailVerificationIssueResult> IssueCodeAsync(
            ApplicationUser user,
            bool enforceCooldown = true)
        {
            var now = DateTime.UtcNow;
            var record = await _context.EmailVerifications
                .SingleOrDefaultAsync(v => v.UserId == user.Id);

            if (enforceCooldown && record != null)
            {
                var remaining = (int)Math.Ceiling(
                    (record.LastSentAt + ResendCooldown - now).TotalSeconds);
                if (remaining > 0)
                    return new(false, remaining);
            }

            var code = GenerateSecureCode();
            record ??= new EmailVerification { UserId = user.Id };
            record.CreatedAt = now;
            record.ExpiresAt = now.Add(CodeLifetime);
            record.LastSentAt = now;
            record.AttemptCount = 0;
            record.IsUsed = false;
            record.VerifiedAt = null;
            record.CodeHash = _passwordHasher.HashPassword(record, code);

            if (record.Id == 0)
                _context.EmailVerifications.Add(record);

            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailAsync(
                    user.Email!,
                    "Your Aries Magic Verification Code",
                    BuildVerificationEmail(user.FullName, code));
                return new(true, (int)ResendCooldown.TotalSeconds);
            }
            catch (Exception ex)
            {
                record.IsUsed = true;
                record.ExpiresAt = now;
                record.LastSentAt = now.Subtract(ResendCooldown);
                await _context.SaveChangesAsync();
                _logger.LogError(ex,
                    "Failed to send account verification email for user {UserId}.",
                    user.Id);
                return new(false, 0,
                    "We couldn't send your verification code. Please try again.");
            }
        }

        public async Task<EmailVerificationCheckResult> VerifyCodeAsync(
            ApplicationUser user,
            string code)
        {
            if (user.EmailConfirmed)
                return new(EmailVerificationCheckStatus.AlreadyVerified);

            var record = await _context.EmailVerifications
                .SingleOrDefaultAsync(v => v.UserId == user.Id);

            if (record == null || record.IsUsed)
                return new(EmailVerificationCheckStatus.NoActiveCode);

            if (record.AttemptCount >= MaxAttempts)
                return new(EmailVerificationCheckStatus.Locked);

            if (record.ExpiresAt <= DateTime.UtcNow)
                return new(EmailVerificationCheckStatus.Expired);

            var result = _passwordHasher.VerifyHashedPassword(
                record,
                record.CodeHash,
                code);
            if (result == PasswordVerificationResult.Failed)
            {
                record.AttemptCount++;
                await _context.SaveChangesAsync();

                if (record.AttemptCount >= MaxAttempts)
                    return new(EmailVerificationCheckStatus.Locked);

                return new(
                    EmailVerificationCheckStatus.Incorrect,
                    MaxAttempts - record.AttemptCount);
            }

            var identityToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmResult = await _userManager.ConfirmEmailAsync(user, identityToken);
            if (!confirmResult.Succeeded)
            {
                _logger.LogWarning(
                    "Identity email confirmation failed for user {UserId} after a valid OTP.",
                    user.Id);
                return new(EmailVerificationCheckStatus.Error);
            }

            record.IsUsed = true;
            record.VerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new(EmailVerificationCheckStatus.Success);
        }
        public async Task<EmailVerificationState> GetStateAsync(string userId)
        {
            var now = DateTime.UtcNow;
            var record = await _context.EmailVerifications
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.UserId == userId);

            if (record == null)
                return new(null, 0, false, false);

            var resendSeconds = Math.Max(0, (int)Math.Ceiling(
                (record.LastSentAt + ResendCooldown - now).TotalSeconds));
            var locked = !record.IsUsed && record.AttemptCount >= MaxAttempts;
            var active = !record.IsUsed && !locked && record.ExpiresAt > now;

            return new(
                record.ExpiresAt,
                resendSeconds,
                active,
                locked);
        }

        public async Task InvalidateAsync(string userId)
        {
            var record = await _context.EmailVerifications
                .SingleOrDefaultAsync(v => v.UserId == userId);
            if (record == null) return;

            record.IsUsed = true;
            record.ExpiresAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        private static string GenerateSecureCode()
        {
            while (true)
            {
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                if (!IsWeakCode(code))
                    return code;
            }
        }

        private static bool IsWeakCode(string code)
        {
            if (code.All(c => c == code[0])) return true;
            return code is "123456" or "654321";
        }

        private static string BuildVerificationEmail(string fullName, string code)
        {
            var safeName = WebUtility.HtmlEncode(fullName);
            return $@"
<div style='font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2937'>
  <h2 style='color:#2d1b69'>Verify Your Email</h2>
  <p>Hello {safeName},</p>
  <p>Welcome to the Magic Artist Scheduling System.</p>
  <p>Use the verification code below to complete your account registration:</p>
  <div style='font-size:32px;font-weight:700;letter-spacing:8px;text-align:center;padding:18px;margin:20px 0;background:#f5f3ff;border:1px solid #ddd6fe;border-radius:12px'>{code}</div>
  <p>This code will expire in <strong>10 minutes</strong>.</p>
  <p>If you did not create this account, you may ignore this email.</p>
</div>";
        }
    }
}
