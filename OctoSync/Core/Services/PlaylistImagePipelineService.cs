using OctoSync.Core.Utilities;

namespace OctoSync.Core.Services;

public class PlaylistImagePipelineService(ILogger<PlaylistImagePipelineService> logger)
{
    private const long MaxImageSizeBytes = 10 * 1024 * 1024;
    private const string SourceUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    public async Task<(byte[] Data, string ContentType)?> TryPrepareImageUploadAsync(
        HttpClient httpClient,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

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
}