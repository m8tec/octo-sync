using System.Text;
using OctoSync.Core.Models;

namespace OctoSync.Core.Matching;

public static class TrackMatcher
{
    private const double MatchThreshold = 0.8;

    public static bool TracksMatch(TrackModel sourceTrack, TrackModel targetTrack)
    {
        return IsTitleAndArtistMatch(sourceTrack.Title, sourceTrack.Artist, targetTrack.Title, targetTrack.Artist);
    }

    public static bool IsTitleAndArtistMatch(string? leftTitle, string? leftArtist, string? rightTitle, string? rightArtist)
    {
        var normalizedLeftTitle = Normalize(leftTitle);
        var normalizedRightTitle = Normalize(rightTitle);
        var normalizedLeftArtist = Normalize(leftArtist);
        var normalizedRightArtist = Normalize(rightArtist);

        var titleMatch = ComputeIntersectionRatio(normalizedLeftTitle, normalizedRightTitle) > MatchThreshold;
        var artistMatch = ComputeIntersectionRatio(normalizedLeftArtist, normalizedRightArtist) > MatchThreshold;

        return titleMatch && artistMatch;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var replaced = value
            .Replace(',', ' ')
            .Replace(';', ' ')
            .Replace('-', ' ')
            .Replace('&', ' ')
            .Replace('/', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace("feat.", " ")
            .Replace("featuring", " ")
            .Trim();

        if (replaced.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(replaced.Length);
        var previousWasWhitespace = false;

        foreach (var character in replaced)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    private static double ComputeIntersectionRatio(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return 0;
        }

        var leftSet = new HashSet<string>(leftTokens, StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(rightTokens, StringComparer.OrdinalIgnoreCase);

        var intersectionCount = 0;
        foreach (var token in leftSet)
        {
            if (rightSet.Contains(token))
            {
                intersectionCount++;
            }
        }

        var shorterLength = Math.Min(leftSet.Count, rightSet.Count);
        if (shorterLength == 0)
        {
            return 0;
        }

        return (double)intersectionCount / shorterLength;
    }
}