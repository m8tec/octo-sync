using OctoSync.Core.Interfaces;
using OctoSync.Core.Matching;
using OctoSync.Core.Models;
using System.Text;

namespace OctoSync.Core.Services;

public class TrackResolver(ILogger<TrackResolver> logger) : ITrackResolver
{
    public async Task<(List<ResolvedTrack> ResolvedTracks, int UnresolvedCount)> ResolveTracksAsync(
        IPlaylistTarget target,
        IReadOnlyList<TrackModel> sourceTracks,
        IReadOnlyList<TrackModel> targetTracks,
        CancellationToken cancellationToken)
    {
        var resolvedTracks = new List<ResolvedTrack>();
        var unresolvedCount = 0;

        foreach (var track in sourceTracks)
        {
            string? targetId;

            var matchingTargetTrack = FindMatchingTargetTrack(track, targetTracks);
            if (matchingTargetTrack != null)
            {
                targetId = matchingTargetTrack.Id;
                logger.LogDebug("Found source track '{Title}' by '{Artist}' in target playlist, skipping search.", track.Title, track.Artist);
            }
            else
            {
                targetId = await target.FindBestMatchAsync(track.Title, track.Artist, cancellationToken);

                // Fallback: If no match was found, try searching without any bracketed content.
                // Especially useful for YouTube Music where titles often contain extra info for the video.
                if (string.IsNullOrEmpty(targetId))
                {
                    var simplifiedTitle = RemoveBracketedContent(track.Title);
                    if (!string.Equals(simplifiedTitle, track.Title, StringComparison.Ordinal))
                    {
                        targetId = await target.FindBestMatchAsync(simplifiedTitle, track.Artist, cancellationToken);

                        if (!string.IsNullOrEmpty(targetId))
                        {
                            logger.LogDebug(
                                "Resolved track '{Title}' via fallback title normalization to '{SimplifiedTitle}'.",
                                track.Title,
                                simplifiedTitle);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                resolvedTracks.Add(new ResolvedTrack(track, targetId));
            }
            else
            {
                logger.LogWarning("Skipping unresolved source track: '{Title}' by '{Artist}'.",
                    track.Title, track.Artist);
                unresolvedCount++;
            }
        }

        if (unresolvedCount > 0)
        {
            logger.LogInformation("Filtered out {UnresolvedCount} unresolved source tracks.", unresolvedCount);
        }

        return (resolvedTracks, unresolvedCount);
    }

    private static TrackModel? FindMatchingTargetTrack(TrackModel sourceTrack, IReadOnlyList<TrackModel> targetTracks)
    {
        foreach (var targetTrack in targetTracks)
        {
            if (TrackMatcher.TracksMatch(sourceTrack, targetTrack))
            {
                return targetTrack;
            }
        }

        return null;
    }

    private static string RemoveBracketedContent(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        var roundDepth = 0;
        var squareDepth = 0;

        foreach (var ch in title)
        {
            if (ch == '(')
            {
                roundDepth++;
                continue;
            }

            if (ch == ')')
            {
                roundDepth = Math.Max(0, roundDepth - 1);
                continue;
            }

            if (ch == '[')
            {
                squareDepth++;
                continue;
            }

            if (ch == ']')
            {
                squareDepth = Math.Max(0, squareDepth - 1);
                continue;
            }

            if (roundDepth == 0 && squareDepth == 0)
            {
                builder.Append(ch);
            }
        }

        var collapsed = string.Join(' ', builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Trim('-', ' ', ',', '.', ';', ':');
    }
}