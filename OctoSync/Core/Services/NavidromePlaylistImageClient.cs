using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;

namespace OctoSync.Core.Services;

public class NavidromePlaylistImageClient(
    IOptions<SubsonicOptions> options,
    NavidromeAuthService authService,
    ILogger<NavidromePlaylistImageClient> logger)
{
    private readonly SubsonicOptions _options = options.Value;

    public async Task<bool> UploadPlaylistImageAsync(
        HttpClient httpClient,
        string localPlaylistId,
        byte[] imageData,
        string contentType,
        CancellationToken cancellationToken)
    {
        var token = await authService.GetNativeApiTokenAsync(httpClient, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var uploadUrl = $"{_options.Url}/api/playlist/{localPlaylistId}/image";
        var fileName = contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
            ? "playlist.webp"
            : contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                ? "playlist.png"
                : contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
                    ? "playlist.gif"
                    : "playlist.jpg";

        var uploadResponse = await SendNativeImageUploadAsync(httpClient, uploadUrl, imageData, contentType, fileName, token, cancellationToken);
        if (uploadResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            authService.InvalidateToken();
            token = await authService.GetNativeApiTokenAsync(httpClient, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                uploadResponse.Dispose();
                return false;
            }

            uploadResponse.Dispose();
            uploadResponse = await SendNativeImageUploadAsync(httpClient, uploadUrl, imageData, contentType, fileName, token, cancellationToken);
        }

        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Failed to upload playlist image: HTTP {StatusCode} for playlist {PlaylistId}. Response: {Response}",
                uploadResponse.StatusCode,
                localPlaylistId,
                errorContent);
            uploadResponse.Dispose();
            return false;
        }

        uploadResponse.Dispose();
        return true;
    }

    private static async Task<HttpResponseMessage> SendNativeImageUploadAsync(
        HttpClient httpClient,
        string uploadUrl,
        byte[] imageData,
        string contentType,
        string fileName,
        string token,
        CancellationToken cancellationToken)
    {
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        using var content = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(imageData);

        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(imageContent, "image", fileName);

        uploadRequest.Content = content;
        uploadRequest.Headers.Add("X-ND-Authorization", $"Bearer {token}");

        return await httpClient.SendAsync(uploadRequest, cancellationToken);
    }
}