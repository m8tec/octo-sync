using OctoSync.Core.Models;

namespace OctoSync.Core.Services;

public sealed class ResolvedTrack(TrackModel track, string targetId)
{
    public TrackModel Track { get; } = track;
    public string TargetId { get; } = targetId;
}