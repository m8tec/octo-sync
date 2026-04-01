using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Utilities;

namespace OctoSync.Core.Sources;

public sealed class ListenBrainzSource : IPlaylistSource
{
    private const string SourcePatchDailyJams = "daily-jams";
    private const string SourcePatchWeeklyExploration = "weekly-exploration";
    private const string JspfPlaylistKey = "https://musicbrainz.org/doc/jspf#playlist";

    private readonly HttpClient _httpClient;
    private readonly ListenBrainzOptions _options;

    public string ProviderName => "ListenBrainz";

    public ListenBrainzSource(HttpClient httpClient, IOptions<ListenBrainzOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken)
    {
        ValidateOptions();

        var sourcePatch = NormalizeSourcePatch(externalPlaylistId);
        var targetPlaylist = await ResolveCreatedForPlaylistAsync(sourcePatch, cancellationToken);
        var playlistId = ExtractPlaylistId(targetPlaylist.Identifier)
                         ?? throw new InvalidOperationException($"Could not extract playlist id from identifier '{targetPlaylist.Identifier}'.");

        var playlistDetails = await GetJsonAsync($"playlist/{playlistId}", cancellationToken);
        var playlistNode = playlistDetails?["playlist"];

        if (playlistNode is null)
        {
            throw new InvalidOperationException($"ListenBrainz playlist '{playlistId}' response is missing playlist data.");
        }

        var tracks = ParseTracks(playlistNode["track"]?.AsArray());

        var description = playlistNode["annotation"]?.ToString();
        if (!string.IsNullOrWhiteSpace(description))
        {
            description = HtmlTextCleaner.StripHtmlTags(description);
        }

        return new PlaylistModel
        {
            ExternalId = externalPlaylistId,
            Name = GetStablePlaylistName(sourcePatch),
            Description = description,
            ImageUrl = null,
            Tracks = tracks
        };
    }

    private async Task<(string Identifier, string Title)> ResolveCreatedForPlaylistAsync(string requiredSourcePatch, CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync($"user/{_options.UserName}/playlists/createdfor", cancellationToken);
        var playlists = json?["playlists"]?.AsArray();

        if (playlists is null || playlists.Count == 0)
        {
            throw new InvalidOperationException($"No created-for playlists found for ListenBrainz user '{_options.UserName}'.");
        }

        JsonNode? selectedNode = null;
        var selectedDate = DateTimeOffset.MinValue;

        foreach (var item in playlists)
        {
            var playlistNode = item?["playlist"];
            if (playlistNode is null)
            {
                continue;
            }

            var sourcePatch = playlistNode["extension"]?[JspfPlaylistKey]?["additional_metadata"]?["algorithm_metadata"]?["source_patch"]?.ToString();
            if (!string.Equals(sourcePatch, requiredSourcePatch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsedDate = ParsePlaylistDate(playlistNode["date"]?.ToString());
            if (selectedNode is null || parsedDate > selectedDate)
            {
                selectedNode = playlistNode;
                selectedDate = parsedDate;
            }
        }

        if (selectedNode is null)
        {
            throw new InvalidOperationException(
                $"No '{requiredSourcePatch}' playlist found for ListenBrainz user '{_options.UserName}'.");
        }

        var identifier = selectedNode["identifier"]?.ToString();
        var title = selectedNode["title"]?.ToString();

        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Selected ListenBrainz playlist is missing identifier or title.");
        }

        return (identifier, title);
    }

    private async Task<JsonObject?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        if (!string.IsNullOrWhiteSpace(_options.UserToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.UserToken}");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
    }

    private static List<TrackModel> ParseTracks(JsonArray? trackArray)
    {
        var tracks = new List<TrackModel>();

        if (trackArray is null)
        {
            return tracks;
        }

        foreach (var trackNode in trackArray)
        {
            if (trackNode is null)
            {
                continue;
            }

            var title = trackNode["title"]?.ToString();
            var artist = trackNode["creator"]?.ToString();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            var recordingId = ExtractRecordingMbid(trackNode);
            if (string.IsNullOrWhiteSpace(recordingId))
            {
                continue;
            }

            tracks.Add(new TrackModel
            {
                Id = $"ext-listenbrainz-{recordingId}",
                Title = title,
                Artist = artist
            });
        }

        return tracks;
    }

    private static string? ExtractRecordingMbid(JsonNode trackNode)
    {
        var identifierArray = trackNode["identifier"]?.AsArray();
        if (identifierArray is null || identifierArray.Count == 0)
        {
            return null;
        }

        var rawIdentifier = identifierArray[0]?.ToString();
        if (string.IsNullOrWhiteSpace(rawIdentifier))
        {
            return null;
        }

        var uri = new Uri(rawIdentifier);
        return uri.Segments.LastOrDefault()?.Trim('/');
    }

    private static string? ExtractPlaylistId(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var uri = new Uri(identifier);
        return uri.Segments.LastOrDefault()?.Trim('/');
    }

    private static DateTimeOffset ParsePlaylistDate(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    private static string NormalizeSourcePatch(string externalPlaylistId)
    {
        if (string.IsNullOrWhiteSpace(externalPlaylistId))
        {
            throw new InvalidOperationException("ListenBrainz playlist id must not be empty. Use 'daily-jams' or 'weekly-exploration'.");
        }

        var normalized = externalPlaylistId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "daily-jams" => SourcePatchDailyJams,
            "weekly-exploration" => SourcePatchWeeklyExploration,
            _ => throw new InvalidOperationException(
                $"Unsupported ListenBrainz playlist key '{externalPlaylistId}'. Use 'daily-jams' or 'weekly-exploration'.")
        };
    }

    private static string GetStablePlaylistName(string sourcePatch)
    {
        return sourcePatch switch
        {
            SourcePatchDailyJams => "Daily Jams",
            SourcePatchWeeklyExploration => "Weekly Exploration",
            _ => throw new InvalidOperationException($"Unsupported source patch '{sourcePatch}'.")
        };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.UserName))
        {
            throw new InvalidOperationException("ListenBrainz UserName must be configured in Sources:ListenBrainz:UserName.");
        }

        if (string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            throw new InvalidOperationException("ListenBrainz UserAgent must be configured in Sources:ListenBrainz:UserAgent.");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("ListenBrainz BaseUrl must be a valid absolute URL.");
        }
    }
}