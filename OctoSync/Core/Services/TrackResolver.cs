using OctoSync.Core.Interfaces;
using OctoSync.Core.Matching;
using OctoSync.Core.Models;

namespace OctoSync.Core.Services;

public class TrackResolver(ILogger<TrackResolver> logger) : ITrackResolver
{
    public async Task<(List<ResolvedTrack> ResolvedTracks, int UnresolvedCount)> ResolveTracksAsync(
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

            var matchingTargetTrack = FindMatchingTargetTrack(track, targetTracks);
            if (matchingTargetTrack != null)
            {
                targetId = matchingTargetTrack.Id;
                logger.LogDebug("Found source track '{Title}' by '{Artist}' in target playlist, skipping search.", track.Title, track.Artist);
            }
            else
            {
                targetId = await target.FindBestMatchAsync(track.Title, track.Artist, cancellationToken);
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                resolvedTracks.Add(new ResolvedTrack(track, targetId));
            }
            else
            {
                logger.LogWarning("Skipping unresolved source track for this run: '{Title}' by '{Artist}'.",
                    track.Title, track.Artist);
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
}