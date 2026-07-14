namespace AriesMagicAppointmentSystem.Services
{
    /// <summary>
    /// Runs in the background for the lifetime of the app and periodically moves any booking
    /// whose event has ended into the Completed/archived state, independent of anyone actually
    /// browsing the site. Each History-module read also triggers the same check as a lazy
    /// fallback (see HistoryService methods), so archiving happens promptly either way and this
    /// service is safe even if it is delayed or misses a tick.
    /// </summary>
    public class BookingArchivingBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingArchivingBackgroundService> _logger;

        public BookingArchivingBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<BookingArchivingBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var historyService = scope.ServiceProvider.GetRequiredService<IHistoryService>();
                    var archivedCount = await historyService.ArchiveDueBookingsAsync();

                    if (archivedCount > 0)
                    {
                        _logger.LogInformation(
                            "Booking archiving service moved {Count} booking(s) into History.",
                            archivedCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Booking archiving background pass failed.");
                }

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected during shutdown.
                }
            }
        }
    }
}
