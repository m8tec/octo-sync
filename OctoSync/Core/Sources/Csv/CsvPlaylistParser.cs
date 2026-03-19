using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OctoSync.Core.Models;

namespace OctoSync.Core.Sources.Csv;

public sealed class CsvPlaylistParser
{
    private static readonly string[] TitleAliases =
    [
        "Track Name",
        "Title",
        "Song Name",
        "Name"
    ];

    private static readonly string[] ArtistAliases =
    [
        "Artist Name(s)",
        "Artist Name",
        "Artists",
        "Artist",
        "Track Artist"
    ];

    private static readonly string[] UriAliases =
    [
        "Track URI",
        "URI",
        "Spotify URI",
        "Track Id",
        "Track ID"
    ];

    private static readonly string[] AlbumAliases =
    [
        "Album Name",
        "Album"
    ];

    private static readonly string[] IsrcAliases =
    [
        "ISRC",
        "Track ISRC"
    ];

    public async Task<List<TrackModel>> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        var delimiter = await DetectDelimiterAsync(filePath, cancellationToken);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            throw new InvalidOperationException($"CSV file '{filePath}' is empty.");
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        var titleIndex = FindIndex(headers, TitleAliases);
        var artistIndex = FindIndex(headers, ArtistAliases);

        if (titleIndex < 0 || artistIndex < 0)
        {
            throw new InvalidOperationException(
                $"CSV file '{filePath}' is missing required columns. " +
                $"Required: title ({string.Join(", ", TitleAliases)}) and artist ({string.Join(", ", ArtistAliases)}). " +
                $"Found headers: {string.Join(", ", headers)}");
        }

        var uriIndex = FindIndex(headers, UriAliases);
        var albumIndex = FindIndex(headers, AlbumAliases);
        var isrcIndex = FindIndex(headers, IsrcAliases);

        var tracks = new List<TrackModel>();
        var rowNumber = 1;

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var title = GetField(csv, titleIndex);
            var artistRaw = GetField(csv, artistIndex);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artistRaw))
            {
                continue;
            }

            var artist = NormalizeArtists(artistRaw);
            var uri = GetField(csv, uriIndex);
            var album = GetField(csv, albumIndex);
            var isrc = GetField(csv, isrcIndex);

            tracks.Add(new TrackModel
            {
                Id = BuildTrackId(uri, title, artist, rowNumber),
                Title = title,
                Artist = artist,
                Album = album,
                Isrc = isrc
            });
        }

        return tracks;
    }

    private static int FindIndex(IReadOnlyList<string> headers, IEnumerable<string> aliases)
    {
        var indexByNormalizedHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            if (!indexByNormalizedHeader.ContainsKey(normalized))
            {
                indexByNormalizedHeader[normalized] = i;
            }
        }

        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeHeader(alias);
            if (indexByNormalizedHeader.TryGetValue(normalizedAlias, out var index))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static string? GetField(CsvReader csv, int index)
    {
        if (index < 0)
        {
            return null;
        }

        try
        {
            return csv.GetField(index)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeArtists(string artists)
    {
        var separators = new[] { ';', '|', '/' };
        var parts = artists
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
        {
            return artists.Trim();
        }

        return string.Join(", ", parts);
    }

    private static string BuildTrackId(string? uri, string title, string artist, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(uri))
        {
            return uri.Trim();
        }

        var input = $"{title}|{artist}|{rowNumber}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return hash;
    }

    private static async Task<string> DetectDelimiterAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var candidates = new[]
            {
                (Delimiter: ",", Count: line.Count(ch => ch == ',')),
                (Delimiter: ";", Count: line.Count(ch => ch == ';')),
                (Delimiter: "\t", Count: line.Count(ch => ch == '\t')),
                (Delimiter: "|", Count: line.Count(ch => ch == '|'))
            };

            var winner = candidates.OrderByDescending(candidate => candidate.Count).First();
            return winner.Count > 0 ? winner.Delimiter : ",";
        }

        return ",";
    }
}
