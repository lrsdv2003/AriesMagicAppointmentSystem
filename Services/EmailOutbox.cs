using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

public sealed class EmailOutbox(ApplicationDbContext database)
{
    public async Task EnqueueAsync(string eventKey, string recipient, string subject, string body, byte[]? attachment = null, string? attachmentName = null)
    {
        if (await database.EmailOutboxMessages.AnyAsync(x => x.EventKey == eventKey)) return;
        database.EmailOutboxMessages.Add(new EmailOutboxMessage { EventKey = eventKey, Recipient = recipient, Subject = subject,
            HtmlBody = body, Attachment = attachment, AttachmentName = attachmentName });
        await database.SaveChangesAsync();
    }
}

public sealed class EmailOutboxDispatcher(ApplicationDbContext database, IEmailService sender, ILogger<EmailOutboxDispatcher> logger)
{
    public async Task<int> DispatchAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var ids = await database.EmailOutboxMessages.AsNoTracking()
            .Where(x => x.DeliveredAtUtc == null && x.FailedAtUtc == null && x.NextAttemptAtUtc <= now && (x.LockedUntilUtc == null || x.LockedUntilUtc < now))
            .OrderBy(x => x.Id).Select(x => x.Id).Take(20).ToListAsync(cancellationToken);
        var delivered = 0;
        foreach (var id in ids)
        {
            // Atomic lease prevents two workers from sending the same item concurrently.
            var lease = DateTime.UtcNow.AddMinutes(5);
            var claimed = await database.EmailOutboxMessages.Where(x => x.Id == id && x.DeliveredAtUtc == null && x.FailedAtUtc == null
                && (x.LockedUntilUtc == null || x.LockedUntilUtc < now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntilUtc, lease), cancellationToken);
            if (claimed == 0) continue;
            var item = await database.EmailOutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
            string? failure = null;
            try
            {
                if (item.Attachment != null)
                    await sender.SendEmailWithAttachmentAsync(item.Recipient, item.Subject, item.HtmlBody, item.Attachment, item.AttachmentName ?? "document.pdf");
                else await sender.SendEmailAsync(item.Recipient, item.Subject, item.HtmlBody);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name;
                logger.LogError("Outbox delivery failed for item {OutboxId} ({FailureType}); attempt {Attempt}.", id, failure, item.Attempts + 1);
            }
            // Do not classify a database acknowledgement failure as an SMTP failure. Lease recovery
            // can redeliver after a crash: delivery is at-least-once, never an external money action.
            var attempts = item.Attempts + 1;
            var next = DateTime.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(attempts - 1, 7))));
            DateTime? deliveredAt = failure == null ? DateTime.UtcNow : null;
            DateTime? failedAt = failure != null && attempts >= 10 ? DateTime.UtcNow : null;
            await database.EmailOutboxMessages.Where(x => x.Id == id && x.LockedUntilUtc == lease)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, attempts).SetProperty(x => x.LockedUntilUtc, (DateTime?)null)
                    .SetProperty(x => x.DeliveredAtUtc, deliveredAt).SetProperty(x => x.FailedAtUtc, failedAt)
                    .SetProperty(x => x.LastFailureType, failure).SetProperty(x => x.NextAttemptAtUtc, next), cancellationToken);
            if (deliveredAt != null)
                await database.EmailOutboxMessages.Where(x => x.Id == id && x.DeliveredAtUtc != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attachment, (byte[]?)null).SetProperty(x => x.HtmlBody, "")
                        .SetProperty(x => x.Recipient, "").SetProperty(x => x.AttachmentName, (string?)null), cancellationToken);
            if (deliveredAt != null) delivered++;
        }
        return delivered;
    }
}

public sealed class EmailOutboxBackgroundService(IServiceScopeFactory scopes, ILogger<EmailOutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Outbox worker failed ({FailureType}).", ex.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
