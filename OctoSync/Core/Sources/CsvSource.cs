using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Sources.Csv;

namespace OctoSync.Core.Sources;

public sealed class CsvSource(IOptions<CsvOptions> options) : IPlaylistSource, IPlaylistSourceDiscovery
{
    private readonly CsvOptions _options = options.Value;
    private readonly CsvPlaylistParser _parser = new();

    public string ProviderName => "Csv";

    public async Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken)
    {
        var filePath = ResolveFilePath(externalPlaylistId);

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"CSV playlist file not found: '{filePath}'.");
        }

        var tracks = await _parser.ParseAsync(filePath, cancellationToken);
        var playlistName = Path.GetFileNameWithoutExtension(filePath);

        return new PlaylistModel
        {
            ExternalId = externalPlaylistId,
            Name = string.IsNullOrWhiteSpace(playlistName) ? "CSV Playlist" : playlistName,
            Description = $"Imported from CSV file: {Path.GetFileName(filePath)}",
            ImageUrl = null,
            Tracks = tracks
        };
    }

    public Task<IReadOnlyList<string>> GetPlaylistIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var baseDirectory = ResolveBaseDirectory();
        if (!Directory.Exists(baseDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = Directory
            .EnumerateFiles(baseDirectory, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(files!);
    }

    private string ResolveFilePath(string externalPlaylistId)
    {
        if (string.IsNullOrWhiteSpace(externalPlaylistId))
        {
            throw new InvalidOperationException("CSV playlist path must not be empty.");
        }

        var rawPath = externalPlaylistId.Trim();

        if (Path.IsPathRooted(rawPath))
        {
            return rawPath;
        }

        if (!string.IsNullOrWhiteSpace(_options.BasePath))
        {
            return Path.GetFullPath(Path.Combine(_options.BasePath, rawPath));
        }

        return Path.GetFullPath(rawPath, Directory.GetCurrentDirectory());
    }

    private string ResolveBaseDirectory()
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
        {
            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(_options.BasePath, Directory.GetCurrentDirectory());
    }
}
