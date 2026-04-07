using System.Net.Http.Json;
using System.Text.Json;
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
        if (playlistJson is null)
        {
            throw new InvalidOperationException($"Failed to parse Deezer playlist response for playlist '{playlistId}'.");
        }
        
        EnsureNoDeezerError(playlistJson, playlistId);

        var playlistName = playlistJson["title"]?.ToString() ?? "Unknown Playlist";
        var playlistDescription = playlistJson["description"]?.ToString();
        var imageUrl = (playlistJson["picture_xl"] ?? playlistJson["picture_big"] ?? playlistJson["picture_medium"] ??
            playlistJson["picture_small"] ?? playlistJson["picture"])?.ToString();

        var tracks = await ParseTracks(playlistJson);

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = playlistName,
            Description = playlistDescription,
            ImageUrl = imageUrl,
            Tracks = tracks
        };
    }

    private async Task<List<TrackModel>> ParseTracks(JsonObject playlistJson)
    {
        var items = new List<JsonObject>();

        // Deezer playlist/{id} embeds at most 400 tracks in tracks.data.
        // Use the dedicated tracklist endpoint and follow pagination to load all tracks.
        var tracklistEl = playlistJson["tracklist"];
        if (tracklistEl is not null && tracklistEl.GetValue<string>().Contains("/tracks"))
        {
            var tracklistUrl = tracklistEl.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tracklistUrl))
            {
                var nextPageUrl = $"{tracklistUrl}?limit=1000";

                while (!string.IsNullOrWhiteSpace(nextPageUrl))
                {
                    var tracklistResponse = await _httpClient.GetAsync(nextPageUrl);
                    if (!tracklistResponse.IsSuccessStatusCode)
                    {
                        break;
                    }

                    var tracklistJson = await tracklistResponse.Content.ReadAsStringAsync();
                    using var tracklistDocument = JsonDocument.Parse(tracklistJson);
                    var tracklistElement = tracklistDocument.RootElement;

                    if (!tracklistElement.TryGetProperty("data", out var pageTracks) || pageTracks.ValueKind != JsonValueKind.Array)
                    {
                        break;
                    }

                    foreach (var pageTrack in pageTracks.EnumerateArray())
                    {
                        if (JsonNode.Parse(pageTrack.GetRawText()) is JsonObject trackNode)
                        {
                            items.Add(trackNode);
                        }
                    }

                    nextPageUrl = tracklistElement.TryGetProperty("next", out var nextEl)
                        ? nextEl.GetString()
                        : null;
                }
            }
        }

        List<TrackModel> tracks = new();
        foreach (var item in items)
        {
            var id = item["id"]?.ToString();
            var title = item["title"]?.ToString();
            var artist = item["artist"]?["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            var album = item["album"]?["title"]?.ToString();
            var isrc = item["isrc"]?.ToString();

            tracks.Add(new TrackModel
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = album,
                Isrc = isrc
            });
        }

        return tracks;
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
