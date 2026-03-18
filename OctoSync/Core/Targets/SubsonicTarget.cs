using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;

namespace OctoSync.Core.Targets;

public class SubsonicTarget : IPlaylistTarget
{
    private readonly HttpClient _httpClient;
    private readonly SubsonicOptions _options;

    public SubsonicTarget(HttpClient httpClient, IOptions<SubsonicOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/rest/");
    }

    private string GetAuthParams()
    {
        var salt = Guid.NewGuid().ToString().Substring(0, 8);
        var tokenInput = _options.Password + salt;
        var token = ComputeMd5Hash(tokenInput);

        return $"?u={_options.Username}&t={token}&s={salt}&v=1.16.1&c=OctoSync&f=json";
    }

    private static string ComputeMd5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public async Task<string> EnsurePlaylistExistsAsync(PlaylistModel playlist, CancellationToken cancellationToken)
    {
        var getPlaylistsUrl = $"getPlaylists{GetAuthParams()}";
        var response = await _httpClient.GetAsync(getPlaylistsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var playlists = json?["subsonic-response"]?["playlists"]?["playlist"]?.AsArray();

        if (playlists != null)
        {
            foreach (var p in playlists)
            {
                if (p is null)
                {
                    continue;
                }

                var name = p["name"]?.ToString();
                if (string.Equals(name, playlist.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return p["id"]!.ToString();
                }
            }
        }

        var createUrl = $"createPlaylist{GetAuthParams()}&name={Uri.EscapeDataString(playlist.Name)}";
        var createResponse = await _httpClient.GetAsync(createUrl, cancellationToken);
        createResponse.EnsureSuccessStatusCode();

        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var newId = createJson?["subsonic-response"]?["playlist"]?["id"]?.ToString();

        if (string.IsNullOrEmpty(newId))
            throw new Exception("Failed to create playlist or parse the new ID from Navidrome.");

        return newId;
    }

    public async Task<PlaylistModel> GetTargetPlaylistAsync(string localPlaylistId, CancellationToken cancellationToken)
    {
        var url = $"getPlaylist{GetAuthParams()}&id={localPlaylistId}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var playlistNode = json?["subsonic-response"]?["playlist"];
        var entryNodes = playlistNode?["entry"]?.AsArray();

        var tracks = new List<TrackModel>();

        if (entryNodes != null)
        {
            foreach (var entry in entryNodes)
            {
                if (entry is null)
                {
                    continue;
                }

                tracks.Add(new TrackModel
                {
                    Id = entry["id"]?.ToString() ?? "",
                    Title = entry["title"]?.ToString() ?? "",
                    Artist = entry["artist"]?.ToString() ?? ""
                });
            }
        }

        return new PlaylistModel
        {
            ExternalId = localPlaylistId,
            Name = playlistNode?["name"]?.ToString() ?? "Unknown",
            Tracks = tracks
        };
    }

    public async Task AddTrackAsync(string localPlaylistId, string externalTrackId, CancellationToken cancellationToken)
    {
        var updateUrl = $"updatePlaylist{GetAuthParams()}&playlistId={localPlaylistId}&songIdToAdd={externalTrackId}";
        var response = await _httpClient.GetAsync(updateUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveTrackAsync(string localPlaylistId, string localTrackIndex, CancellationToken cancellationToken)
    {
        var updateUrl = $"updatePlaylist{GetAuthParams()}&playlistId={localPlaylistId}&songIndexToRemove={localTrackIndex}";
        var response = await _httpClient.GetAsync(updateUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> FindBestMatchAsync(string title, string artist, CancellationToken cancellationToken)
    {
        title = title.Trim();
        artist = artist.Trim();

        var query = Uri.EscapeDataString($"{artist} {title}");
        var searchUrl = $"search3{GetAuthParams()}&query={query}&songCount=10";

        var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var songs = json?["subsonic-response"]?["searchResult3"]?["song"]?.AsArray();

        if (songs == null || songs.Count == 0)
        {
            return null;
        }

        foreach (var songNode in songs)
        {
            if (songNode is null)
            {
                continue;
            }

            var songTitle = songNode["title"]?.ToString();
            var songArtist = songNode["artist"]?.ToString();
            var songId = songNode["id"]?.ToString();

            if (!string.IsNullOrEmpty(songId) &&
                string.Equals(songTitle, title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(songArtist, artist, StringComparison.OrdinalIgnoreCase))
            {
                return songId;
            }
        }

        return null;
    }
}