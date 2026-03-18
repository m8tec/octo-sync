namespace OctoSync.Core.Services;

public sealed class PlaylistSyncState
{
    public string? LastSourceHash { get; set; }
    public int LastUnresolvedCount { get; set; }
    public int CyclesSinceLastAttempt { get; set; }
}