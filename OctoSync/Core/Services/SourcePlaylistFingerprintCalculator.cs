using System.Security.Cryptography;
using System.Text;
using OctoSync.Core.Models;

namespace OctoSync.Core.Services;

public static class SourcePlaylistFingerprintCalculator
{
    public static string Compute(PlaylistModel sourcePlaylist)
    {
        var builder = new StringBuilder();

        foreach (var track in sourcePlaylist.Tracks)
        {
            var identifier = string.IsNullOrWhiteSpace(track.Id)
                ? $"{Normalize(track.Artist)}|{Normalize(track.Title)}"
                : Normalize(track.Id);

            builder.Append(identifier);
            builder.Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}