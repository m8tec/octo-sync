using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Service;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Utilities;

namespace OctoSync.Core.Sources;

public class QobuzSource : IPlaylistSource
{
    public string ProviderName => "Qobuz";

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        using var apiService = new QobuzApiService();
        var tracks = new List<TrackModel>();
        const int pageSize = 500;

        string? playlistName = null;
        string? playlistDescription = null;
        int offset = 0;
        int? total = null;
        var withAuth = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Playlist page;
            try
            {
                page = await Task.Run(() => apiService.GetPlaylist(
                    playlistId,
                    withAuth: withAuth,
                    extra: "tracks",
                    limit: pageSize,
                    offset: offset), cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Qobuz request failed for playlist '{playlistId}': {ex.Message}", ex);
            }

            playlistName ??= page.Name;
            playlistDescription ??= page.Description;

            var pageItems = page.Tracks?.Items ?? [];
            if (pageItems.Count == 0)
            {
                break;
            }

            ParseTracks(pageItems, tracks);
            offset += pageItems.Count;

            total ??= page.Tracks?.Total ?? page.TracksCount;

            if (offset >= total)
            {
                break;
            }

            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = string.IsNullOrWhiteSpace(playlistName) ? "Unknown Playlist" : playlistName,
            Description = HtmlTextCleaner.StripHtmlTags(playlistDescription),
            Tracks = tracks
        };
    }

    private static void ParseTracks(IEnumerable<Track> items, ICollection<TrackModel> tracks)
    {
        foreach (var item in items)
        {
            var id = item.Id?.ToString();
            var title = item.Title;
            var artist = item.Performer?.Name
                         ?? item.Album?.Artist?.Name
                         ?? item.Composer?.Name;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                continue;
            }

            tracks.Add(new TrackModel
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = item.Album?.Title,
                Isrc = item.Isrc
            });
        }
    }
}