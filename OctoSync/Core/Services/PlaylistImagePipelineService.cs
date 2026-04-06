using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;
using OctoSync.Core.Models;
using OctoSync.Core.Utilities;

namespace OctoSync.Core.Services;

public class PlaylistImagePipelineService(
    IOptions<AppleMusicOptions> appleMusicOptions,
    ILogger<PlaylistImagePipelineService> logger)
{
    private sealed record VariantCandidate(Uri Uri, int Width, int Height);

    private readonly AppleMusicOptions _appleMusicOptions = appleMusicOptions.Value;

    private const long MaxImageSizeBytes = 10 * 1024 * 1024;
    private const string SourceUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    public async Task<(byte[] Data, string ContentType)?> TryPrepareImageUploadAsync(
        HttpClient httpClient,
        PlaylistModel sourcePlaylist,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sourcePlaylist.ImageM3U8Url))
        {
            var animatedCover = await TryPrepareAnimatedImageUploadAsync(httpClient, sourcePlaylist.ImageM3U8Url, cancellationToken);
            if (animatedCover is not null)
            {
                return animatedCover;
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePlaylist.ImageUrl))
        {
            return null;
        }

        return await TryPrepareStaticImageUploadAsync(httpClient, sourcePlaylist.ImageUrl, cancellationToken);
    }

    private async Task<(byte[] Data, string ContentType)?> TryPrepareAnimatedImageUploadAsync(
        HttpClient httpClient,
        string animatedImageUrl,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing animated cover...");
        
        try
        {
            var selectedVariant = await ResolvePreferredVariantAsync(httpClient, animatedImageUrl, cancellationToken);
            var outputFilePath = Path.Combine(Path.GetTempPath(), $"octosync_cover_{Guid.NewGuid():N}.webp");

            try
            {
                var ffmpegBinary = string.IsNullOrWhiteSpace(_appleMusicOptions.FfmpegBinaryPath)
                    ? "ffmpeg"
                    : _appleMusicOptions.FfmpegBinaryPath;
                var webpQuality = Math.Clamp(_appleMusicOptions.AnimatedWebpQuality, 0, 100);
                var args =
                    $"-y -i \"{selectedVariant.Uri}\" -vf \"scale=-1:-1:flags=lanczos\" -c:v libwebp -compression_level 6 -q:v {webpQuality} -loop 0 -an \"{outputFilePath}\"";

                await RunFfmpegAsync(ffmpegBinary, args, cancellationToken);

                if (!File.Exists(outputFilePath))
                {
                    logger.LogDebug("ffmpeg did not produce animated webp output for {AnimatedImageUrl}", animatedImageUrl);
                    return null;
                }

                var data = await File.ReadAllBytesAsync(outputFilePath, cancellationToken);
                if (data.Length == 0)
                {
                    return null;
                }

                if (data.Length > MaxImageSizeBytes)
                {
                    logger.LogWarning("Animated cover exceeds size limit after conversion: {Size} bytes", data.Length);
                    return null;
                }

                return (data, "image/webp");
            }
            finally
            {
                if (File.Exists(outputFilePath))
                {
                    File.Delete(outputFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed preparing animated cover from {AnimatedImageUrl}. Falling back to static artwork.", animatedImageUrl);
            return null;
        }
    }

    private async Task<(byte[] Data, string ContentType)?> TryPrepareStaticImageUploadAsync(
        HttpClient httpClient,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            requestMessage.Headers.TryAddWithoutValidation("User-Agent", SourceUserAgent);

            var downloadResponse = await httpClient.SendAsync(requestMessage, cancellationToken);
            if (!downloadResponse.IsSuccessStatusCode)
            {
                logger.LogDebug("Failed to download playlist image: HTTP {StatusCode} from {ImageUrl}",
                    downloadResponse.StatusCode, imageUrl);
                return null;
            }

            var imageData = await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            if (imageData.Length == 0)
            {
                logger.LogDebug("Downloaded image is empty: {ImageUrl}", imageUrl);
                return null;
            }

            if (imageData.Length > MaxImageSizeBytes)
            {
                logger.LogWarning("Playlist image exceeds size limit: {Size} bytes from {ImageUrl}",
                    imageData.Length, imageUrl);
                return null;
            }

            var contentType = ImageMimeTypeDetector.Detect(imageData);
            if (contentType == "application/octet-stream")
            {
                logger.LogDebug("Image format not supported by Navidrome: {ImageUrl}", imageUrl);
                return null;
            }

            return (imageData, contentType);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Network error downloading playlist image from {ImageUrl}", imageUrl);
            return null;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Image download timeout for {ImageUrl}", imageUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unexpected error handling playlist image for {ImageUrl}", imageUrl);
            return null;
        }
    }

    private async Task<VariantCandidate> ResolvePreferredVariantAsync(
        HttpClient httpClient,
        string playlistUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(playlistUrl, UriKind.Absolute, out var playlistUri))
        {
            return new VariantCandidate(new Uri(playlistUrl, UriKind.RelativeOrAbsolute), 0, 0);
        }

        if (!playlistUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return new VariantCandidate(playlistUri, 0, 0);
        }

        var playlistBody = await httpClient.GetStringAsync(playlistUri, cancellationToken);
        var lines = playlistBody.Split('\n', StringSplitOptions.TrimEntries);
        var variants = new List<VariantCandidate>();

        for (var i = 0; i < lines.Length; i++)
        {
            var streamInfoLine = lines[i];
            if (!streamInfoLine.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (width, height) = ParseResolution(streamInfoLine);
            for (var j = i + 1; j < lines.Length; j++)
            {
                var candidate = lines[j];
                if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith('#'))
                {
                    continue;
                }

                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteVariantUri))
                {
                    absoluteVariantUri = new Uri(playlistUri, candidate);
                }

                variants.Add(new VariantCandidate(absoluteVariantUri, width, height));
                break;
            }
        }

        if (variants.Count == 0)
        {
            return new VariantCandidate(playlistUri, 0, 0);
        }

        var minResolution = Math.Max(0, _appleMusicOptions.AnimatedMinVariantResolution);
        var candidateAboveMin = variants
            .Where(v => v.Width >= minResolution)
            .OrderBy(v => v.Width)
            .ThenBy(v => v.Height)
            .FirstOrDefault();

        var selectedVariant = candidateAboveMin ?? variants
            .OrderByDescending(v => v.Width)
            .ThenByDescending(v => v.Height)
            .First();
        
        logger.LogInformation("Selected animated cover variant: {Width}p",
            selectedVariant.Width);

        return selectedVariant;
    }

    private static (int Width, int Height) ParseResolution(string streamInfoLine)
    {
        var marker = "RESOLUTION=";
        var markerIndex = streamInfoLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return (0, 0);
        }

        var startIndex = markerIndex + marker.Length;
        var endIndex = streamInfoLine.IndexOf(',', startIndex);
        var resolutionPart = endIndex >= 0
            ? streamInfoLine[startIndex..endIndex]
            : streamInfoLine[startIndex..];

        var parts = resolutionPart.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : (0, 0);
    }

    private async Task RunFfmpegAsync(string ffmpegBinary, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegBinary,
            Arguments = $"-progress pipe:1 -nostats {arguments}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}. stdout: {stdOut} stderr: {stdErr}");
        }
    }
}
