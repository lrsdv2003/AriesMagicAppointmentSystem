using AriesMagicAppointmentSystem.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AriesMagicAppointmentSystem.Services;

public sealed class EmailDeliveryException() : Exception("Email delivery failed. Check the configured provider and retry.");

public sealed class SmtpEmailService(IOptions<EmailSettings> options, ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly EmailSettings settings = options.Value;
    public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        => SendAsync(toEmail, subject, htmlBody, null, null);
    public Task SendEmailWithAttachmentAsync(string toEmail, string subject, string htmlMessage, byte[] attachmentBytes, string attachmentFileName)
        => SendAsync(toEmail, subject, htmlMessage, attachmentBytes, attachmentFileName);
    private async Task SendAsync(string recipient, string subject, string html, byte[]? attachment, string? attachmentName)
    {
        try
        {
            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            var body = new BodyBuilder { HtmlBody = html };
            if (attachment != null) body.Attachments.Add(Path.GetFileName(attachmentName ?? "document.pdf"), attachment);
            message.Body = body.ToMessageBody();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new SmtpClient { Timeout = 30000 };
            var security = settings.EnableSsl
                ? (settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None; // Production startup rejects plaintext SMTP.
            await client.ConnectAsync(settings.Host, settings.Port, security, timeout.Token);
            await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (Exception ex)
        {
            // Do not log provider responses: these may contain secrets or personal data.
            logger.LogError("SMTP delivery failed ({FailureType}).", ex.GetType().Name);
            throw new EmailDeliveryException();
        }
    }
}
