namespace OctoSync.Core.Configuration;

public class ListenBrainzOptions
{
    public string BaseUrl { get; set; } = "https://api.listenbrainz.org/1/";
    public string? UserName { get; set; }
    public string? UserToken { get; set; }
    public string? UserAgent { get; set; }
    public string? DailyJamsImagePath { get; set; }
    public string? WeeklyExplorationImagePath { get; set; }
}