namespace OctoSync.Core.Configuration;

public class LastFmOptions
{
    public string BaseUrl { get; set; } = "https://www.last.fm";
    public string? UserName { get; set; }
    public string UserAgent { get; set; } = "OctoSync/1.0";
}