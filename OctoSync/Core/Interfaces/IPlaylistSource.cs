using OctoSync.Core.Models;

namespace OctoSync.Core.Interfaces;

public interface IPlaylistSource
{
    string ProviderName { get; }
    
    Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken);
}