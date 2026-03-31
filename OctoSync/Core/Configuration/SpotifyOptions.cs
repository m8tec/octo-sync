namespace OctoSync.Core.Configuration;

public sealed class SpotifyOptions
{
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    
    /// <summary>
    /// Timeout in seconds for browser-based fetching of large playlists
    /// </summary>
    public int BrowserTimeoutSeconds { get; set; } = 300;
    
    /// <summary>
    /// Stall time in seconds: wait time before assuming no more content loads
    /// </summary>
    public int BrowserStallSeconds { get; set; } = 10;
}
