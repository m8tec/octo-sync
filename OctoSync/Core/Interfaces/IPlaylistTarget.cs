using OctoSync.Core.Models;

namespace OctoSync.Core.Interfaces;

public interface IPlaylistTarget
{
    Task<string> EnsurePlaylistExistsAsync(PlaylistModel playlist, CancellationToken cancellationToken);

    Task<PlaylistModel> GetTargetPlaylistAsync(string localPlaylistId, CancellationToken cancellationToken);

    Task<string?> FindBestMatchAsync(string title, string artist, CancellationToken cancellationToken);

    Task AddTrackAsync(string localPlaylistId, string externalTrackId, CancellationToken cancellationToken);
    Task RemoveTrackAsync(string localPlaylistId, string localTrackId, CancellationToken cancellationToken);
}