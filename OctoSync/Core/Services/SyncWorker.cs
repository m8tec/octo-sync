using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Matching;
using OctoSync.Core.Models;

namespace OctoSync.Core.Services;

public class SyncWorker(IServiceScopeFactory scopeFactory, ILogger<SyncWorker> logger, IOptions<SyncOptions> options) : BackgroundService
{
    private readonly SyncOptions _options = options.Value;
    private readonly Dictionary<string, PlaylistSyncState> _playlistStates = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OctoSync started.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        do
        {
            try
            {
                await RunSyncCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during the sync cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<IPlaylistSource>>();
        var target = scope.ServiceProvider.GetRequiredService<IPlaylistTarget>();

        foreach (var source in sources)
        {
            if (!_options.PlaylistsToSync.TryGetValue(source.ProviderName, out var playlistIds))
                continue;

            foreach (var externalPlaylistId in playlistIds)
            {
                try
                {
                    await ProcessPlaylistAsync(source, externalPlaylistId, target, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Playlist sync failed for {Provider} playlist {Id}. Continuing with next playlist.",
                        source.ProviderName,
                        externalPlaylistId);
                }
            }
        }
    }

    private async Task ProcessPlaylistAsync(
        IPlaylistSource source,
        string externalPlaylistId,
        IPlaylistTarget target,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting sync for {Provider} playlist {Id}...", source.ProviderName, externalPlaylistId);

        var playlistState = GetOrCreatePlaylistState(source.ProviderName, externalPlaylistId);
        var sourcePlaylist = await source.GetPlaylistAsync(externalPlaylistId, cancellationToken);
        var sourceHash = SourcePlaylistFingerprintCalculator.Compute(sourcePlaylist);

        if (ShouldSkipSync(source.ProviderName, externalPlaylistId, sourceHash, playlistState))
        {
            return;
        }

        var localPlaylistId = await target.EnsurePlaylistExistsAsync(sourcePlaylist, cancellationToken);
        var targetPlaylist = await target.GetTargetPlaylistAsync(localPlaylistId, cancellationToken);
        var unresolvedCount = await PerformDiffingAsync(target, localPlaylistId, sourcePlaylist, targetPlaylist, cancellationToken);

        playlistState.LastSourceHash = sourceHash;
        playlistState.LastUnresolvedCount = unresolvedCount;
        playlistState.CyclesSinceLastAttempt = 0;
    }

    private PlaylistSyncState GetOrCreatePlaylistState(string providerName, string externalPlaylistId)
    {
        var stateKey = $"{providerName}:{externalPlaylistId}";
        if (_playlistStates.TryGetValue(stateKey, out var state))
        {
            return state;
        }

        state = new PlaylistSyncState();
        _playlistStates[stateKey] = state;
        return state;
    }

    private bool ShouldSkipSync(string providerName, string externalPlaylistId, string sourceHash, PlaylistSyncState playlistState)
    {
        var sourceChanged = !string.Equals(playlistState.LastSourceHash, sourceHash, StringComparison.Ordinal);
        if (sourceChanged)
        {
            return false;
        }

        if (playlistState.LastUnresolvedCount == 0)
        {
            logger.LogInformation("Source playlist unchanged for {Provider} playlist {Id}; skipping sync.",
                providerName,
                externalPlaylistId);
            return true;
        }

        logger.LogInformation(
            "Source unchanged, but retrying now because previous run had {UnresolvedCount} unresolved track(s).",
            playlistState.LastUnresolvedCount);
        return false;
    }

    private async Task<int> PerformDiffingAsync(
        IPlaylistTarget target,
        string localPlaylistId,
        PlaylistModel sourcePlaylist,
        PlaylistModel targetPlaylist,
        CancellationToken cancellationToken)
    {
        var resolveResult = await ResolveSourceTracksAsync(target, sourcePlaylist.Tracks, targetPlaylist.Tracks, cancellationToken);
        var resolvableSourceTracks = resolveResult.ResolvedTracks;
        var unresolvedCount = resolveResult.UnresolvedCount;

        var matchingPrefixLength = CalculateMatchingPrefixLength(resolvableSourceTracks, targetPlaylist.Tracks);

        if (matchingPrefixLength == resolvableSourceTracks.Count &&
            matchingPrefixLength == targetPlaylist.Tracks.Count)
        {
            logger.LogInformation("Playlist is already in sync, no changes needed.");
            return unresolvedCount;
        }

        var removeCount = await RemoveTargetTailAsync(target, localPlaylistId, targetPlaylist.Tracks.Count, matchingPrefixLength, cancellationToken);
        var addCount = await AddSourceTailAsync(target, localPlaylistId, resolvableSourceTracks, matchingPrefixLength, cancellationToken);

        logger.LogInformation("Playlist sync completed. Removed: {RemoveCount}, Added: {AddCount}",
            removeCount, addCount);

        return unresolvedCount;
    }

    private async Task<(List<ResolvedTrack> ResolvedTracks, int UnresolvedCount)> ResolveSourceTracksAsync(
        IPlaylistTarget target,
        IReadOnlyList<TrackModel> sourceTracks,
        IReadOnlyList<TrackModel> targetTracks,
        CancellationToken cancellationToken)
    {
        var resolvedTracks = new List<ResolvedTrack>();
        var unresolvedCount = 0;

        foreach (var track in sourceTracks)
        {
            string? targetId;

            // First, check if the track already exists in the target playlist using the same matching logic
            var matchingTargetTrack = FindMatchingTargetTrack(track, targetTracks);
            if (matchingTargetTrack != null)
            {
                targetId = matchingTargetTrack.Id;
                logger.LogDebug("Found source track '{Title}' by '{Artist}' in target playlist, skipping search3.", track.Title, track.Artist);
            }
            else
            {
                // If not found in target playlist, search for it
                targetId = await target.FindBestMatchAsync(track.Title, track.Artist, cancellationToken);
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                resolvedTracks.Add(new ResolvedTrack(track, targetId));
            }
            else
            {
                logger.LogWarning("Skipping unresolved source track for this run: '{Title}' by '{Artist}'.",
                    track.Title,
                    track.Artist);
                unresolvedCount++;
            }
        }

        if (unresolvedCount > 0)
        {
            logger.LogInformation("Filtered out {UnresolvedCount} unresolved source tracks for this sync run.", unresolvedCount);
        }

        return (resolvedTracks, unresolvedCount);
    }

    private static TrackModel? FindMatchingTargetTrack(TrackModel sourceTrack, IReadOnlyList<TrackModel> targetTracks)
    {
        foreach (var targetTrack in targetTracks)
        {
            if (TrackMatcher.TracksMatch(sourceTrack, targetTrack))
            {
                return targetTrack;
            }
        }

        return null;
    }

    private int CalculateMatchingPrefixLength(
        IReadOnlyList<ResolvedTrack> sourceTracks,
        IReadOnlyList<TrackModel> targetTracks)
    {
        var matchingPrefixLength = 0;
        var minLength = Math.Min(sourceTracks.Count, targetTracks.Count);

        for (var i = 0; i < minLength; i++)
        {
            if (!TrackMatcher.TracksMatch(sourceTracks[i].Track, targetTracks[i]))
            {
                logger.LogDebug("Mismatch at position {Position}: '{SourceArtist} - {SourceTitle}' vs '{TargetArtist} - {TargetTitle}'",
                    i,
                    sourceTracks[i].Track.Artist,
                    sourceTracks[i].Track.Title,
                    targetTracks[i].Artist,
                    targetTracks[i].Title);
                break;
            }

            matchingPrefixLength++;
        }

        return matchingPrefixLength;
    }

    private async Task<int> RemoveTargetTailAsync(
        IPlaylistTarget target,
        string localPlaylistId,
        int targetTrackCount,
        int matchingPrefixLength,
        CancellationToken cancellationToken)
    {
        var removeCount = 0;

        for (var i = targetTrackCount - 1; i >= matchingPrefixLength; i--)
        {
            logger.LogInformation("Fixing order: removing track at index {Index} from playlist {PlaylistId}", i, localPlaylistId);
            await target.RemoveTrackAsync(localPlaylistId, i.ToString(), cancellationToken);
            removeCount++;
        }

        return removeCount;
    }

    private async Task<int> AddSourceTailAsync(
        IPlaylistTarget target,
        string localPlaylistId,
        IReadOnlyList<ResolvedTrack> sourceTracks,
        int matchingPrefixLength,
        CancellationToken cancellationToken)
    {
        var addCount = 0;

        for (var i = matchingPrefixLength; i < sourceTracks.Count; i++)
        {
            var resolvedTrack = sourceTracks[i];
            logger.LogInformation("Adding track (position {Position}): {Title} by {Artist}",
                i,
                resolvedTrack.Track.Title,
                resolvedTrack.Track.Artist);

            try
            {
                await target.AddTrackAsync(localPlaylistId, resolvedTrack.TargetId, cancellationToken);
                addCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to add track '{Title}' by '{Artist}' to playlist {PlaylistId}. Continuing with next track.",
                    resolvedTrack.Track.Title,
                    resolvedTrack.Track.Artist,
                    localPlaylistId);
            }
        }

        return addCount;
    }
}