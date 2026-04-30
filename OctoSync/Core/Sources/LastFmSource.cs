using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;

namespace OctoSync.Core.Sources;

public sealed class LastFmSource(HttpClient httpClient, IOptions<LastFmOptions> options, ILogger<LastFmSource> logger) : IPlaylistSource
{
    private readonly LastFmOptions _options = options.Value;

    public string ProviderName => "LastFm";

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var playlistUrl = BuildPlaylistUrl(playlistId);

        using var request = new HttpRequestMessage(HttpMethod.Get, playlistUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var items = json?["playlist"]?.AsArray();
        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException($"Last.fm playlist '{playlistId}' returned no tracks for user '{_options.UserName}'.");
        }

        var tracks = ParseTracks(items);
        if (tracks.Count == 0)
        {
            throw new InvalidOperationException($"Last.fm playlist '{playlistId}' returned no usable tracks for user '{_options.UserName}'.");
        }

        var title = ToPascalCase(playlistId);

        logger.LogInformation("Loaded {TrackCount} tracks from Last.fm playlist {PlaylistKey}", tracks.Count, playlistId);

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = title,
            Description = $"{title} from Last.fm for {_options.UserName}",
            ImageUrl = null,
            Tracks = tracks
        };
    }

    private string BuildPlaylistUrl(string playlistKey)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var username = Uri.EscapeDataString(_options.UserName!.Trim());
        return $"{baseUrl}/player/station/user/{username}/{playlistKey}";
    }

    private List<TrackModel> ParseTracks(JsonArray items)
    {
        var tracks = new List<TrackModel>();

        foreach (var node in items)
        {
            if (node is null)
            {
                continue;
            }

            var title = node["name"]?.ToString() ?? node["_name"]?.ToString();
            var firstArtist = node["artists"]?.AsArray().FirstOrDefault();
            var artist = firstArtist?["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                logger.LogWarning("Skipping track with missing title or artist.");
                continue;
            }

            tracks.Add(new TrackModel
            {
                Id = "lastfm-song",
                Title = title,
                Artist = artist
            });
        }

        return tracks;
    }

    private static string ToPascalCase(string value)
    {
        var parts = value
            .Split(['-', '_', ' ', '.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return value;
        }

        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
}