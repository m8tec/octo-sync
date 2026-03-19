namespace OctoSync.Core.Interfaces;

public interface IPlaylistSourceDiscovery
{
    Task<IReadOnlyList<string>> GetPlaylistIdsAsync(CancellationToken cancellationToken);
}
