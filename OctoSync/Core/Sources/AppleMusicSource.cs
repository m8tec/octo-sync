using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;

namespace OctoSync.Core.Sources;

public sealed class AppleMusicSource(HttpClient httpClient, IOptions<AppleMusicOptions> options, ILogger<AppleMusicSource> logger) : IPlaylistSource
{
    private readonly AppleMusicOptions _options = options.Value;

    public string ProviderName => "AppleMusic";

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var playlistUrl = GetPlaylistUrl(playlistId);

        using var request = new HttpRequestMessage(HttpMethod.Get, playlistUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("Apple Music returned empty HTML.");
        }

        var tracks = ExtractTracks(html);
        if (tracks.Count == 0)
        {
            throw new InvalidOperationException("No tracks could be extracted from Apple Music.");
        }

        var name = ExtractMetaContent(html, "apple:title")
            ?? ExtractMetaContent(html, "og:title")
            ?? ExtractHeadingTitle(html)
            ?? playlistId;

        var description = ExtractDescription(html)
            ?? ExtractMetaContent(html, "apple:description")
            ?? ExtractMetaContent(html, "og:description")
            ?? ExtractMetaContent(html, "description");
        
        var imageUrl = ExtractImageUrlFromArtworkComponent(html)
            ?? ExtractMetaContent(html, "og:image:secure_url")
            ?? ExtractMetaContent(html, "og:image")
            ?? ExtractMetaContent(html, "twitter:image");

        logger.LogInformation("Loaded {TrackCount} tracks", tracks.Count);

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = WebUtility.HtmlDecode(name).Trim(),
            Description = description is null ? null : WebUtility.HtmlDecode(description).Trim(),
            ImageUrl = imageUrl,
            Tracks = tracks
        };
    }

    private string GetPlaylistUrl(string playlistId)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var country = string.IsNullOrWhiteSpace(_options.CountryCode) ? "us" : _options.CountryCode.Trim().ToLowerInvariant();
        var url = $"{baseUrl}/{country}/playlist/{playlistId}";
        return url;
    }

    private static List<TrackModel> ExtractTracks(string html)
    {
        var regex = new Regex(
            "\\\"artistName\\\":\\\"(?<artist>(?:\\\\.|[^\\\"\\\\])*)\\\".{0,1600}?\\\"fields\\\":\\{[^{}]*?\\\"id\\\":\\\"(?<id>\\d+)\\\"[^{}]*?\\\"name\\\":\\\"(?<title>(?:\\\\.|[^\\\"\\\\])*)\\\"",
            RegexOptions.Singleline);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tracks = new List<TrackModel>();

        foreach (Match match in regex.Matches(html))
        {
            var id = match.Groups["id"].Value;
            var title = DecodeEscapedJsonString(match.Groups["title"].Value);
            var artist = DecodeEscapedJsonString(match.Groups["artist"].Value);

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            if (!seen.Add(id))
            {
                continue;
            }

            tracks.Add(new TrackModel
            {
                Id = id,
                Title = title,
                Artist = artist
            });
        }

        return tracks;
    }

    private static string DecodeEscapedJsonString(string value)
    {
        var unescaped = Regex.Unescape(value);
        return WebUtility.HtmlDecode(unescaped).Trim();
    }

    private static string? ExtractMetaContent(string html, string metaNameOrProperty)
    {
        var propertyRegex = new Regex(
            $"<meta\\s+[^>]*(?:property|name)=\"{Regex.Escape(metaNameOrProperty)}\"[^>]*content=\"(?<content>[^\"]+)\"[^>]*>",
            RegexOptions.IgnoreCase);

        var match = propertyRegex.Match(html);
        if (match.Success)
        {
            return match.Groups["content"].Value;
        }

        return null;
    }

    private static string? ExtractHeadingTitle(string html)
    {
        var headingMatch = Regex.Match(html, @"<h1[^>]*>\s*<span[^>]*>(?<title>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!headingMatch.Success)
        {
            return null;
        }

        var withoutTags = Regex.Replace(headingMatch.Groups["title"].Value, "<.*?>", string.Empty);
        return string.IsNullOrWhiteSpace(withoutTags) ? null : withoutTags;
    }

    private static string? ExtractDescription(string html)
    {
        var description = ExtractTextByTestId(html, "content-modal-text")
            ?? ExtractTextByTestId(html, "truncate-text");

        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private static string? ExtractTextByTestId(string html, string testId)
    {
        var match = Regex.Match(
            html,
            $"<[^>]*data-testid=\"{Regex.Escape(testId)}\"[^>]*>(?<content>.*?)</[^>]+>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        var withoutTags = Regex.Replace(match.Groups["content"].Value, "<.*?>", string.Empty);
        return string.IsNullOrWhiteSpace(withoutTags) ? null : WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string? ExtractImageUrlFromArtworkComponent(string html)
    {
        var artworkBlockMatch = Regex.Match(
            html,
            "<div[^>]*data-testid=\"artwork-component\"[^>]*>(?<content>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!artworkBlockMatch.Success)
        {
            return null;
        }

        var block = artworkBlockMatch.Groups["content"].Value;
        var srcsetMatches = Regex.Matches(block, "srcset=\"(?<srcset>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (srcsetMatches.Count == 0)
        {
            return null;
        }

        var bestUrl = string.Empty;
        var bestWidth = -1;

        foreach (Match srcsetMatch in srcsetMatches)
        {
            var srcset = srcsetMatch.Groups["srcset"].Value;
            var candidates = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var candidate in candidates)
            {
                var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var url = parts[0];
                var width = 0;
                if (parts.Length > 1)
                {
                    var widthToken = parts[^1].TrimEnd('w');
                    int.TryParse(widthToken, out width);
                }

                if (width > bestWidth)
                {
                    bestUrl = url;
                    bestWidth = width;
                }
            }
        }

        return string.IsNullOrWhiteSpace(bestUrl) ? null : bestUrl;
    }
}