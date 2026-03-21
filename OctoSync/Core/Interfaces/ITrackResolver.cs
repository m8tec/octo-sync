using OctoSync.Core.Models;
using OctoSync.Core.Services;

namespace OctoSync.Core.Interfaces;

public interface ITrackResolver
{
    Task<(List<ResolvedTrack> ResolvedTracks, int UnresolvedCount)> ResolveTracksAsync(
        IPlaylistTarget target,
        IReadOnlyList<TrackModel> sourceTracks,
        IReadOnlyList<TrackModel> targetTracks,
        CancellationToken cancellationToken);
}