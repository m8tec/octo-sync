namespace OctoSync.Core.Interfaces;

public interface IPlaylistSyncEngine
{
    Task ProcessPlaylistAsync(
        IPlaylistSource source,
        string externalPlaylistId,
        IPlaylistTarget target,
        CancellationToken cancellationToken);
}