namespace OctoSync.Core.Models;

public class TrackModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public string? Isrc { get; init; }
}