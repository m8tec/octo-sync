namespace OctoSync.Core.Configuration;

public class SyncOptions
{
    public int IntervalMinutes { get; set; } = 60;
    public Dictionary<string, List<string>> PlaylistsToSync { get; set; } = new();
}