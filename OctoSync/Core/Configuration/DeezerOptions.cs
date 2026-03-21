namespace OctoSync.Core.Configuration;

public class DeezerOptions
{
    public string BaseUrl { get; set; } = "https://api.deezer.com/";
    public int MaxRequestsPerSecond { get; set; } = 1;
}
