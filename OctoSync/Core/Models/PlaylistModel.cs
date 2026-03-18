namespace OctoSync.Core.Models;

public class PlaylistModel
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<TrackModel> Tracks { get; init; } = new();
}