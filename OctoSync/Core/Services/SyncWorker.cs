using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;

namespace OctoSync.Core.Services;

public class SyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncWorker> logger,
    IOptions<SyncOptions> options) : BackgroundService
{
    private readonly SyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OctoSync started.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncOrchestrator>();

                await orchestrator.RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during the sync cycle.");
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Sync cycle finished. Waiting {Minutes} minute(s) for the next cycle.",
                    _options.IntervalMinutes);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
