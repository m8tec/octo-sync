using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;

namespace OctoSync.Core.Services;

public class SyncOrchestrator(
    IEnumerable<IPlaylistSource> sources,
    IPlaylistTarget target,
    IPlaylistSyncEngine syncEngine,
    IOptions<SyncOptions> options,
    ILogger<SyncOrchestrator> logger) : ISyncOrchestrator
{
    private readonly SyncOptions _options = options.Value;

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        bool foundPlaylistsToSync = false;
        foreach (var source in sources)
        {
            var playlistIds = await ResolvePlaylistIdsAsync(source, cancellationToken);

            foreach (var externalPlaylistId in playlistIds)
            {
                foundPlaylistsToSync = true;

                try
                {
                    await syncEngine.ProcessPlaylistAsync(source, externalPlaylistId, target, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (IsExpectedOperationalException(ex))
                    {
                        logger.LogWarning(
                            "Playlist sync failed for {Provider} playlist {Id}. Reason: {Reason}",
                            source.ProviderName, externalPlaylistId, ex.Message);
                    }
                    else
                    {
                        logger.LogError(
                            ex,
                            "Playlist sync failed for {Provider} playlist {Id}.",
                            source.ProviderName, externalPlaylistId);
                    }
                }
            }
        }

        if (!foundPlaylistsToSync)
        {
            logger.LogInformation("No playlists configured for sync.");
        }
    }

    private async Task<IReadOnlyList<string>> ResolvePlaylistIdsAsync(IPlaylistSource source, CancellationToken cancellationToken)
    {
        if (_options.PlaylistsToSync.TryGetValue(source.ProviderName, out var configuredPlaylistIds))
        {
            var configured = configuredPlaylistIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            if (configured.Length > 0)
            {
                return configured;
            }
        }

        if (source is IPlaylistSourceDiscovery discovery)
        {
            var discovered = await discovery.GetPlaylistIdsAsync(cancellationToken);
            return discovered.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        }

        return Array.Empty<string>();
    }

    private static bool IsExpectedOperationalException(Exception ex)
    {
        return ex is InvalidOperationException or HttpRequestException;
    }
}