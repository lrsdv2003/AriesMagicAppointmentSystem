using AriesMagicAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

public sealed class FeedbackMediaWorker(IServiceScopeFactory scopes, ILogger<FeedbackMediaWorker> log) : BackgroundService
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(20);
    private const int MaxAutomaticAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var store = scope.ServiceProvider.GetRequiredService<FeedbackMediaService>();
                var scanner = scope.ServiceProvider.GetRequiredService<IFeedbackMalwareScanner>();
                var now = DateTime.UtcNow;

                // Recover rows left in Processing by a stopped worker, then let them re-enter
                // the bounded quarantine retry path. No such row is served to a viewer.
                var stale = await db.FeedbackMedia
                    .Where(m => m.ProcessingStatus == "Processing" && m.ProcessingStartedAtUtc < now - ProcessingTimeout)
                    .OrderBy(m => m.ProcessingStartedAtUtc).ThenBy(m => m.Id)
                    .Take(50).ToListAsync(ct);
                foreach (var media in stale)
                {
                    media.ProcessingStatus = media.ProcessingAttempts >= MaxAutomaticAttempts ? "Failed" : "Quarantined";
                    media.ProcessingStartedAtUtc = null;
                    media.ProcessingError = "Security processing did not finish. Use the safe retry action to try again.";
                }
                await db.SaveChangesAsync(ct);

                var pending = await db.FeedbackMedia
                    .Where(m => m.ProcessingStatus == "Quarantined"
                        && ((m.Kind == "Image" && m.ContentType == "image/jpeg")
                            || (m.Kind == "Video" && m.ContentType == "video/mp4")))
                    .OrderBy(m => m.Id).Take(20).ToListAsync(ct);

                foreach (var media in pending)
                {
                    media.ProcessingStatus = "Processing";
                    media.ProcessingAttempts++;
                    media.ProcessingStartedAtUtc = DateTime.UtcNow;
                    media.ProcessingError = null;
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException)
                    {
                        db.Entry(media).State = EntityState.Detached;
                        continue;
                    }

                    MediaScan result;
                    try { result = await store.RescanAsync(media.StorageKey, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        result = MediaScan.Unavailable;
                        log.LogWarning("Feedback media processing failed for record {MediaId}; failure type {FailureType}.", media.Id, ex.GetType().Name);
                    }

                    media.ProcessingStartedAtUtc = null;
                    media.PublicApproved = false;
                    if (result == MediaScan.Clean)
                    {
                        media.ProcessingStatus = "Ready";
                        media.ScannedAtUtc = DateTime.UtcNow;
                        media.ProcessingError = null;
                    }
                    else if (result == MediaScan.Infected)
                    {
                        media.ProcessingStatus = "Rejected";
                        media.ScannedAtUtc = DateTime.UtcNow;
                        media.ProcessingError = "The file did not pass the security check.";
                    }
                    else
                    {
                        media.ProcessingStatus = media.ProcessingAttempts >= MaxAutomaticAttempts ? "Failed" : "Quarantined";
                        media.ProcessingError = "The security scanner is temporarily unavailable. Retry when service is restored.";
                    }

                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException)
                    {
                        log.LogInformation("Feedback media record {MediaId} changed while it was being scanned.", media.Id);
                        db.Entry(media).State = EntityState.Detached;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning("Feedback media worker cycle failed; failure type {FailureType}.", ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
