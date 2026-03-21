using OctoSync.Core.Interfaces;

namespace OctoSync.Core.Services;

public class SyncStateManager(ILogger<SyncStateManager> logger) : ISyncStateManager
{
    private readonly Dictionary<string, PlaylistSyncState> _playlistStates = new(StringComparer.OrdinalIgnoreCase);

    public PlaylistSyncState GetOrCreateState(string providerName, string externalPlaylistId)
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

    public bool ShouldSkipSync(string providerName, string externalPlaylistId, string sourceHash, PlaylistSyncState state)
    {
        var sourceChanged = !string.Equals(state.LastSourceHash, sourceHash, StringComparison.Ordinal);
        if (sourceChanged)
        {
            return false;
        }

        if (state.LastUnresolvedCount == 0)
        {
            logger.LogInformation("Source playlist unchanged for {Provider} playlist {Id}; skipping sync.",
                providerName, externalPlaylistId);
            return true;
        }

        logger.LogInformation(
            "Source unchanged, but retrying because {UnresolvedCount} unresolved track(s).",
            state.LastUnresolvedCount);
        return false;
    }

    public void UpdateState(string providerName, string externalPlaylistId, string sourceHash, int unresolvedCount)
    {
        var state = GetOrCreateState(providerName, externalPlaylistId);
        state.LastSourceHash = sourceHash;
        state.LastUnresolvedCount = unresolvedCount;
        state.CyclesSinceLastAttempt = 0;
    }
}