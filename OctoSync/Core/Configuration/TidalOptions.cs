namespace OctoSync.Core.Configuration;

public class TidalOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public int MaxRequestsPerSecond { get; set; } = 1;
}