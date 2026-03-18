using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;

namespace OctoSync.Core.Sources;

public class TidalSource : IPlaylistSource
{
    private readonly HttpClient _httpClient;
    private readonly TidalOptions _options;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);
    private DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;
    private string? _accessToken;

    public string ProviderName => "Tidal";

    public TidalSource(HttpClient httpClient, IOptions<TidalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri("https://openapi.tidal.com/v2/");
    }

    public async Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        await ThrottleRequestAsync(cancellationToken);
        var playlistMetaResponse = await _httpClient.GetAsync($"playlists/{externalPlaylistId}", cancellationToken);
        playlistMetaResponse.EnsureSuccessStatusCode();

        var metaJson = await playlistMetaResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);

        var metaAttributes = metaJson?["data"]?["attributes"];

        var playlistName = metaAttributes?["name"]?.ToString()
                           ?? metaAttributes?["title"]?.ToString()
                           ?? "Unknown Playlist";

        var playlistDesc = metaAttributes?["description"]?.ToString() ?? "";

        var tracks = new List<TrackModel>();
        string? nextUrl = $"playlists/{externalPlaylistId}/relationships/items?page[limit]=100&include=items,items.artists";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            await ThrottleRequestAsync(cancellationToken);
            var tracksResponse = await _httpClient.GetAsync(nextUrl, cancellationToken);
            tracksResponse.EnsureSuccessStatusCode();

            var pageJson = await tracksResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);

            var data = pageJson?["data"]?.AsArray();
            var included = pageJson?["included"]?.AsArray();

            if (data != null && included != null)
            {
                foreach (var dataNode in data)
                {
                    var trackId = dataNode?["id"]?.ToString();
                    if (string.IsNullOrEmpty(trackId)) continue;

                    var trackNode = included.FirstOrDefault(n => n is not null && n["id"]?.ToString() == trackId && n["attributes"] != null);
                    if (trackNode == null) continue;

                    var attributes = trackNode["attributes"];
                    var title = attributes?["title"]?.ToString();
                    if (string.IsNullOrEmpty(title)) continue;

                    var isrc = attributes?["isrc"]?.ToString();
                    var artistName = ResolveArtistName(trackNode, included);

                    tracks.Add(new TrackModel
                    {
                        Id = $"ext-tidal-{trackId}",
                        Title = title,
                        Artist = artistName,
                        Isrc = isrc
                    });
                }
            }

            nextUrl = NormalizeNextUrl(pageJson?["links"]?["next"]?.ToString());
        }

        return new PlaylistModel
        {
            ExternalId = externalPlaylistId,
            Name = playlistName,
            Description = playlistDesc,
            Tracks = tracks
        };
    }

    private static string ResolveArtistName(JsonNode trackNode, JsonArray included)
    {
        var relationships = trackNode["relationships"];
        JsonNode? artistRef = null;

        if (relationships is JsonObject relationshipsObject &&
            relationshipsObject.TryGetPropertyValue("artists", out var artistsNode))
        {
            if (artistsNode is JsonObject artistsObject &&
                artistsObject.TryGetPropertyValue("data", out var dataNode) &&
                dataNode is JsonArray dataArray)
            {
                artistRef = dataArray.FirstOrDefault();
            }
        }

        var artistId = artistRef?["id"]?.ToString();

        if (!string.IsNullOrEmpty(artistId))
        {
            var artistNode = included.FirstOrDefault(n =>
                n is not null &&
                (n["type"]?.ToString() == "artists" || n["type"]?.ToString() == "artist") &&
                n["id"]?.ToString() == artistId);

            var artistName = artistNode?["attributes"]?["name"]?.ToString();
            if (!string.IsNullOrEmpty(artistName))
            {
                return artistName;
            }
        }

        var attributes = trackNode["attributes"];
        if (attributes is null)
        {
            return "Unknown Artist";
        }

        return attributes["artist"]?["name"]?.ToString() ??
               attributes["artists"]?[0]?["name"]?.ToString() ?? "Unknown Artist";
    }

    private static string? NormalizeNextUrl(string? nextUrl)
    {
        if (string.IsNullOrEmpty(nextUrl))
        {
            return nextUrl;
        }

        if (nextUrl.Contains("openapi.tidal.com/playlists"))
        {
            nextUrl = nextUrl.Replace("openapi.tidal.com/playlists", "openapi.tidal.com/v2/playlists");
        }

        if (nextUrl.StartsWith("/"))
        {
            nextUrl = nextUrl.Substring(1);
        }

        return nextUrl;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken)) return;

        if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret))
        {
            throw new InvalidOperationException("Tidal ClientId and ClientSecret must be configured.");
        }

        using var authClient = new HttpClient();

        var authString = $"{_options.ClientId}:{_options.ClientSecret}";
        var authBytes = System.Text.Encoding.UTF8.GetBytes(authString);
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        await ThrottleRequestAsync(cancellationToken);
        var response = await authClient.PostAsync("https://auth.tidal.com/v1/oauth2/token", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Tidal Auth failed: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        _accessToken = json.GetProperty("access_token").GetString();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
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