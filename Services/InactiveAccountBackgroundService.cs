using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AriesMagicAppointmentSystem.Services
{
    public class InactiveAccountBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InactiveAccountBackgroundService> _logger;

        public InactiveAccountBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<InactiveAccountBackgroundService> logger)
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
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                    var cutoffDate = DateTime.UtcNow - InactivityThreshold;

                    var usersToDisable = await userManager.Users
                        .Where(u => u.IsActive && u.LastLoginAt.HasValue && u.LastLoginAt.Value < cutoffDate)
                        .ToListAsync(stoppingToken);

                    int disabledCount = 0;

                    foreach (var user in usersToDisable)
                    {
                        user.IsActive = false;
                        var result = await userManager.UpdateAsync(user);

                        if (result.Succeeded)
                        {
                            disabledCount++;
                            _logger.LogInformation(
                                "Automatically disabled inactive account: {UserId} ({Email}) - last login: {LastLogin}",
                                user.Id, user.Email, user.LastLoginAt);
                        }
                    }

                    if (disabledCount > 0)
                    {
                        _logger.LogInformation("Inactive account service disabled {Count} account(s).", disabledCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Inactive account background service pass failed.");
                }

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }
}