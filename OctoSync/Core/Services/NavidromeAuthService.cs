using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OctoSync.Core.Configuration;

namespace OctoSync.Core.Services;

public class NavidromeAuthService(IOptions<SubsonicOptions> options, ILogger<NavidromeAuthService> logger)
{
    private readonly SubsonicOptions _options = options.Value;
    private string? _nativeApiToken;

    public void InvalidateToken() => _nativeApiToken = null;

    public async Task<string?> GetNativeApiTokenAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_nativeApiToken))
        {
            return _nativeApiToken;
        }

        var loginUrl = _options.Url + "/auth/login";
        logger.LogDebug("Attempting Navidrome native login at {LoginUrl}", loginUrl);

        using var loginBody = JsonContent.Create(new
        {
            username = _options.Username,
            password = _options.Password
        });

        var response = await httpClient.PostAsync(loginUrl, loginBody, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = response.StatusCode;
            response.Dispose();

            logger.LogWarning("Failed to login to Navidrome API at {LoginUrl}: HTTP {StatusCode}. Response: {Response}",
                loginUrl,
                statusCode,
                responseBody);
            return null;
        }

        try
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var payload = JsonNode.Parse(responseBody)?.AsObject();
            var token = payload?["token"]?.ToString();
            response.Dispose();

            if (!string.IsNullOrWhiteSpace(token))
            {
                _nativeApiToken = token;
                return _nativeApiToken;
            }

            logger.LogWarning("Navidrome login at {LoginUrl} did not return a token.", loginUrl);
            return null;
        }
        catch (Exception ex)
        {
            response.Dispose();
            logger.LogWarning(ex, "Navidrome login at {LoginUrl} returned invalid JSON.", loginUrl);
            return null;
        }
    }
}