namespace AriesMagicAppointmentSystem.Services
{
    public class BookingExpirationBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingExpirationBackgroundService> _logger;

        public BookingExpirationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BookingExpirationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small startup delay so DB migrations/seeding complete first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IBookingExpirationService>();
                    var count = await svc.ExpireOverdueBookingsAsync();
                    if (count > 0) _logger.LogInformation("Booking expiration service auto-trashed {Count} booking(s).", count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Booking expiration background pass failed.");
                }

                try { await Task.Delay(CheckInterval, stoppingToken); } catch (TaskCanceledException) { }
            }
        }
    }
}
