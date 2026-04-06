using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models;

namespace OctoSync.Core.Sources;

public sealed class YouTubeMusicSource(HttpClient httpClient, IOptions<YouTubeMusicOptions> options, ILogger<YouTubeMusicSource> logger) : IPlaylistSource
{
    private readonly YouTubeMusicOptions _options = options.Value;

    public string ProviderName => "YouTubeMusic";

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var client = new YouTubeMusicClient(
            _options.GeographicalLocation,
            null,
            null,
            null,
            null,
            httpClient);

        var browseId = client.GetCommunityPlaylistBrowseId(playlistId);
        var playlistInfo = await client.GetCommunityPlaylistInfoAsync(browseId, cancellationToken);
        var songs = client.GetCommunityPlaylistSongsAsync(browseId);

        var tracks = new List<TrackModel>();
        await foreach (var song in songs.WithCancellation(cancellationToken))
        {
            var artist = song.Artists.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(song.Id) || string.IsNullOrWhiteSpace(song.Name) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            tracks.Add(new TrackModel
            {
                Id = song.Id,
                Title = song.Name,
                Artist = artist,
                Album = song.Album?.Name
            });
        }

        if (tracks.Count == 0)
        {
            throw new InvalidOperationException($"No tracks were returned for YouTube Music playlist '{playlistId}'.");
        }

        logger.LogInformation("Successfully loaded {TrackCount} tracks from YouTube Music playlist", tracks.Count);

        var imageUrl = ExtractThumbnailUrl(playlistInfo);

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = playlistInfo.Name,
            Description = playlistInfo.Description,
            ImageUrl = imageUrl,
            Tracks = tracks
        };
    }
    
    private string? ExtractThumbnailUrl(dynamic playlistInfo)
    {
        try
        {
            int? bestHeight = -1;
            string? bestUrl = null;
            foreach (Thumbnail thumbnail in playlistInfo.Thumbnails)
            {
                if (thumbnail.Height > bestHeight)
                {
                    bestHeight = thumbnail.Height;
                    bestUrl = thumbnail.Url;
                }
            }

            return bestUrl;
        }
        catch
        {
            logger.LogWarning("Failed to extract thumbnail URL from YouTube Music playlist info");
        }

        return null;
    }
}
