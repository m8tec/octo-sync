using OctoSync.Core.Interfaces;
using OctoSync.Core.Matching;
using OctoSync.Core.Models;

namespace OctoSync.Core.Services;

public class PlaylistSyncEngine(
    ISyncStateManager stateManager,
    ITrackResolver trackResolver,
    ILogger<PlaylistSyncEngine> logger) : IPlaylistSyncEngine
{
    public async Task ProcessPlaylistAsync(
        IPlaylistSource source,
        string externalPlaylistId,
        IPlaylistTarget target,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting sync for {Provider} playlist {Id}...", source.ProviderName, externalPlaylistId);

        var playlistState = stateManager.GetOrCreateState(source.ProviderName, externalPlaylistId);
        var sourcePlaylist = await source.GetPlaylistAsync(externalPlaylistId, cancellationToken);
        var sourceHash = SourcePlaylistFingerprintCalculator.Compute(sourcePlaylist);

        if (stateManager.ShouldSkipSync(source.ProviderName, externalPlaylistId, sourceHash, playlistState))
        {
            return;
        }

        var localPlaylistId = await target.EnsurePlaylistExistsAsync(sourcePlaylist, cancellationToken);
        if (!string.IsNullOrWhiteSpace(sourcePlaylist.ImageUrl))
        {
            await target.EnsurePlaylistImageAsync(localPlaylistId, sourcePlaylist.ImageUrl, cancellationToken);
        }
        var targetPlaylist = await target.GetTargetPlaylistAsync(localPlaylistId, cancellationToken);

        var unresolvedCount = await PerformDiffingAsync(target, localPlaylistId, sourcePlaylist, targetPlaylist, cancellationToken);
        
        stateManager.UpdateState(source.ProviderName, externalPlaylistId, sourceHash, unresolvedCount);
    }

    private async Task<int> PerformDiffingAsync(
        IPlaylistTarget target,
        string localPlaylistId,
        PlaylistModel sourcePlaylist,
        PlaylistModel targetPlaylist,
        CancellationToken cancellationToken)
    {
        var resolveResult = await trackResolver.ResolveTracksAsync(target, sourcePlaylist.Tracks, targetPlaylist.Tracks, cancellationToken);
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

        logger.LogInformation("Playlist sync completed. Removed: {RemoveCount}, Added: {AddCount}", removeCount, addCount);

        return unresolvedCount;
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
                    i, sourceTracks[i].Track.Artist, sourceTracks[i].Track.Title, targetTracks[i].Artist, targetTracks[i].Title);
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
        int removeCount = targetTrackCount - matchingPrefixLength;
        logger.LogInformation("Removing {RemoveCount} tracks from target playlist after position {StartIndex}...", removeCount, matchingPrefixLength
        );

        for (var i = targetTrackCount - 1; i >= matchingPrefixLength; i--)
        {
            logger.LogDebug("Removing track at index {Index} from playlist {PlaylistId}", i, localPlaylistId);
            await target.RemoveTrackAsync(localPlaylistId, i.ToString(), cancellationToken);
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
            logger.LogInformation("Adding track (pos. {Position}): {Title} by {Artist}",
                i, resolvedTrack.Track.Title, resolvedTrack.Track.Artist);

            try
            {
                await target.AddTrackAsync(localPlaylistId, resolvedTrack.TargetId, cancellationToken);
                addCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                logger.LogWarning("Failed to add track '{Title}' by '{Artist}' to playlist {PlaylistId}. Reason: {Reason}",
                    resolvedTrack.Track.Title, resolvedTrack.Track.Artist, localPlaylistId, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to add track '{Title}' by '{Artist}' to playlist {PlaylistId}.",
                    resolvedTrack.Track.Title, resolvedTrack.Track.Artist, localPlaylistId);
            }
        }
        return addCount;
    }
}