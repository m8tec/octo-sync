using OctoSync.Core.Services;

namespace OctoSync.Core.Interfaces;

public interface ISyncStateManager
{
    PlaylistSyncState GetOrCreateState(string providerName, string externalPlaylistId);
    bool ShouldSkipSync(string providerName, string externalPlaylistId, string sourceHash, PlaylistSyncState state);
    void UpdateState(string providerName, string externalPlaylistId, string sourceHash, int unresolvedCount);
}