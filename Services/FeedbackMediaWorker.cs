using AriesMagicAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;
namespace AriesMagicAppointmentSystem.Services;

public sealed class FeedbackMediaWorker(IServiceScopeFactory scopes,ILogger<FeedbackMediaWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(2));
        do {
            try {
                using var scope=scopes.CreateScope();
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var store=scope.ServiceProvider.GetRequiredService<FeedbackMediaService>();
                var scanner=scope.ServiceProvider.GetRequiredService<IFeedbackMalwareScanner>();
                var pending=await db.FeedbackMedia.Where(m=>m.ProcessingStatus=="Quarantined"&&m.Kind=="Image"&&m.ContentType=="image/jpeg").OrderBy(m=>m.Id).Take(20).ToListAsync(ct);
                foreach(var media in pending) {
                    var result=await scanner.ScanAsync(await store.ReadAsync(media.StorageKey,ct),ct);
                    if(result==MediaScan.Unavailable) break;
                    media.ProcessingStatus=result==MediaScan.Clean?"Ready":"Rejected";
                    media.ScannedAtUtc=DateTime.UtcNow;
                    media.PublicApproved=false;
                }
                await db.SaveChangesAsync(ct);
            } catch(OperationCanceledException) when(ct.IsCancellationRequested) {break;}
            catch(Exception ex) {log.LogWarning("Feedback media retry failed: {FailureType}",ex.GetType().Name);}
        } while(await timer.WaitForNextTickAsync(ct));
    }
}
