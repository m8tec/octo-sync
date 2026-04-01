using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;

namespace OctoSync.Core.Sources;

public class DeezerSource : IPlaylistSource
{
    private readonly HttpClient _httpClient;
    private readonly DeezerOptions _options;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);
    private DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;

    public string ProviderName => "Deezer";

    public DeezerSource(HttpClient httpClient, IOptions<DeezerOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        await ThrottleRequestAsync(cancellationToken);
        var playlistResponse = await _httpClient.GetAsync($"playlist/{playlistId}", cancellationToken);
        playlistResponse.EnsureSuccessStatusCode();

        var playlistJson = await playlistResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        EnsureNoDeezerError(playlistJson, playlistId);

        var playlistName = playlistJson?["title"]?.ToString() ?? "Unknown Playlist";
        var playlistDescription = playlistJson?["description"]?.ToString();
        var imageUrl = (playlistJson?["picture_xl"] ?? playlistJson?["picture_big"] ?? playlistJson?["picture_medium"] ??
            playlistJson?["picture_small"] ?? playlistJson?["picture"])?.ToString();

        var tracks = new List<TrackModel>();
        ParseTracks(playlistJson?["tracks"]?["data"]?.AsArray(), tracks);

        var nextUrl = playlistJson?["tracks"]?["next"]?.ToString();

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            await ThrottleRequestAsync(cancellationToken);
            var pageResponse = await _httpClient.GetAsync(nextUrl, cancellationToken);
            pageResponse.EnsureSuccessStatusCode();

            var pageJson = await pageResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            EnsureNoDeezerError(pageJson, playlistId);

            ParseTracks(pageJson?["data"]?.AsArray(), tracks);
            nextUrl = pageJson?["next"]?.ToString();
        }

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = playlistName,
            Description = playlistDescription,
            ImageUrl = imageUrl,
            Tracks = tracks
        };
    }

    private static void ParseTracks(JsonArray? items, ICollection<TrackModel> tracks)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            var id = item?["id"]?.ToString();
            var title = item?["title"]?.ToString();
            var artist = item?["artist"]?["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            var album = item?["album"]?["title"]?.ToString();
            var isrc = item?["isrc"]?.ToString();

            tracks.Add(new TrackModel
            {
                Id = $"ext-deezer-{id}",
                Title = title,
                Artist = artist,
                Album = album,
                Isrc = isrc
            });
        }
    }

    private static void EnsureNoDeezerError(JsonObject? json, string externalPlaylistId)
    {
        var errorNode = json?["error"];
        if (errorNode is null)
        {
            return;
        }

        var message = errorNode["message"]?.ToString() ?? "Unknown Deezer API error.";
        var type = errorNode["type"]?.ToString();
        var code = errorNode["code"]?.ToString();

        throw new InvalidOperationException(
            $"Deezer request failed for playlist '{externalPlaylistId}': {message}" +
            (string.IsNullOrWhiteSpace(type) ? string.Empty : $" (type: {type})") +
            (string.IsNullOrWhiteSpace(code) ? string.Empty : $" (code: {code})"));
    }

    private async Task ThrottleRequestAsync(CancellationToken cancellationToken)
    {
        var maxRequestsPerSecond = _options.MaxRequestsPerSecond > 0
            ? _options.MaxRequestsPerSecond
            : 1;

        var minInterval = TimeSpan.FromSeconds(1d / maxRequestsPerSecond);

        await _rateLimitGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;

            if (_nextAllowedRequestUtc > now)
            {
                var delay = _nextAllowedRequestUtc - now;
                await Task.Delay(delay, cancellationToken);
                now = DateTimeOffset.UtcNow;
            }

            _nextAllowedRequestUtc = now.Add(minInterval);
        }
        finally
        {
            _rateLimitGate.Release();
        }
    }
}
