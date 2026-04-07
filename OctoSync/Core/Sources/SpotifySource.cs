using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OctoSync.Core.Sources;

public sealed class SpotifySource(HttpClient httpClient, IOptions<SpotifyOptions> options, ILogger<SpotifySource> logger) : IPlaylistSource
{
    private readonly SpotifyOptions _options = options.Value;

    public string ProviderName => "Spotify";

    public async Task<PlaylistModel> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var playlistUrl = $"https://open.spotify.com/playlist/{playlistId}";

        logger.LogDebug("Fetching Spotify playlist: {PlaylistUrl}", playlistUrl);
        
        var expectedTrackCount = await TryFetchExpectedTrackCountAsync(playlistId, cancellationToken);
        if (expectedTrackCount.HasValue)
        {
            logger.LogDebug("Expected track count for playlist {PlaylistId}: {Count}", playlistId, expectedTrackCount.Value);
        }

        return await FetchPlaylistViaPlaywrightAsync(playlistId, playlistUrl, expectedTrackCount, cancellationToken);
    }

    private async Task<int?> TryFetchExpectedTrackCountAsync(string playlistId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://open.spotify.com/playlist/{playlistId}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cts.Token);
            
            // Look for music:song_count meta tag
            var match = Regex.Match(html, @"<meta\s+name=""music:song_count""\s+content=""(\d+)""", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            {
                return count;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch expected track count from meta tags");
            return null;
        }
    }

    private async Task<PlaylistModel> FetchPlaylistViaPlaywrightAsync(string playlistId, string playlistUrl, int? expectedTrackCount, CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = _options.UserAgent,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(playlistUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = _options.BrowserTimeoutSeconds * 1000
        });

        await Task.Delay(3000, cancellationToken);

        await TryAcceptCookieConsentAsync(page);

        try
        {
            await page.WaitForSelectorAsync("[data-testid=\"tracklist-row\"]", new PageWaitForSelectorOptions
            {
                Timeout = 30000
            });
        }
        catch
        {
            logger.LogWarning("Tracklist selector not found, proceeding anyway");
        }

        var playlistName = await TryGetPlaylistNameAsync(page);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            playlistName = playlistId;
        }

        var imageUrl = await TryGetPlaylistImageAsync(page);

        var seenTracksByIndex = new SortedDictionary<int, TrackModel>();
        var lastSeenCount = 0;
        var lastProgressTime = DateTime.UtcNow;
        var lastStatusLogTime = DateTime.UtcNow;
        var stallTimeoutSeconds = _options.BrowserStallSeconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ExtractVisibleTracksAsync(page, seenTracksByIndex);

            var currentCount = seenTracksByIndex.Count;
                    
            if ((DateTime.UtcNow - lastStatusLogTime).TotalSeconds > 5)
            {
                logger.LogInformation(
                    "Spotify playlist loading: {CurrentCount}/{ExpectedCount} tracks",
                    currentCount,
                    expectedTrackCount?.ToString() ?? "unknown");
                lastStatusLogTime = DateTime.UtcNow;
            }

            if (currentCount >= expectedTrackCount)
            {
                logger.LogDebug("Reached expected track count: {TrackCount}", currentCount);
                break;
            }

            var timeSinceLastProgress = DateTime.UtcNow - lastProgressTime;
            if (timeSinceLastProgress.TotalSeconds > stallTimeoutSeconds)
            {
                logger.LogInformation("Stall timeout reached after {Seconds}s with {TrackCount} tracks",
                    stallTimeoutSeconds, currentCount);
                break;
            }

            var hasRows = await ScrollToLastTrackRowAsync(page);
            if (!hasRows)
            {
                await page.Mouse.WheelAsync(0, 2000);
            }

            if (currentCount > lastSeenCount)
            {
                lastSeenCount = currentCount;
                lastProgressTime = DateTime.UtcNow;
            }

            await Task.Delay(300, cancellationToken);
        }

        if (seenTracksByIndex.Count == 0)
        {
            throw new InvalidOperationException(
                $"Playwright could not extract any tracks from Spotify playlist '{playlistId}'.");
        }

        var tracks = seenTracksByIndex.Values.ToList();

        logger.LogInformation(
            "Loaded {TrackCount} tracks from Spotify playlist (expected: {ExpectedCount})",
            tracks.Count,
            expectedTrackCount?.ToString() ?? "unknown");

        return new PlaylistModel
        {
            ExternalId = playlistId,
            Name = playlistName,
            Description = null,
            ImageUrl = imageUrl,
            Tracks = tracks
        };
    }

    private async Task TryAcceptCookieConsentAsync(IPage page)
    {
        var cookieSelectors = new[]
        {
            "button:has-text('Accept cookies')",
            "button:has-text('Accept Cookies')",
            "button:has-text('ACCEPT COOKIES')",
            "[data-testid='cookie-policy-manage-dialog-accept-button']",
            "button.onetrust-close-btn-handler"
        };

        foreach (var selector in cookieSelectors)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync(selector);
                if (elements.Count == 0)
                    continue;

                var button = elements.First();
                if (!await button.IsVisibleAsync())
                    continue;

                logger.LogDebug("Found cookie button with selector: {Selector}", selector);
                await button.ClickAsync(new ElementHandleClickOptions { Force = true, Timeout = 2000 });
                await Task.Delay(500);
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Cookie selector {Selector} failed", selector);
            }
        }
    }

    private async Task ExtractVisibleTracksAsync(IPage page, SortedDictionary<int, TrackModel> seenTracksByIndex)
    {
        try
        {
            var script = @"() => {
                const result = [];
                const rows = document.querySelectorAll('[data-testid=""tracklist-row""]');
                
                for (const row of rows) {
                    try {
                        const text = row.innerText.trim();
                        const parts = text.split('\n').map(p => p.trim()).filter(p => p);
                        
                        if (!parts.length) continue;
                        
                        // Extract track index from first part
                        const trackIndexRaw = parts[0].replace(/[^\d]/g, '');
                        if (!trackIndexRaw) continue;
                        const trackIndex = parseInt(trackIndexRaw);
                        
                        let remainingParts = parts.slice(1);
                        
                        // Skip 'E' for Explicit marker
                        if (remainingParts.length && remainingParts[0] === 'E') {
                            remainingParts = remainingParts.slice(1);
                        }
                        
                        if (remainingParts.length < 2) continue;
                        
                        let trackName = remainingParts[0].trim();
                        let artist = remainingParts[1].trim();
                        
                        // Handle explicit marker in artist position
                        if (artist === 'E' && remainingParts.length >= 3) {
                            artist = remainingParts[2].trim();
                        }
                        
                        if (!trackName || artist === 'E') continue;
                        
                        // Handle music videos
                        if (artist.toLowerCase() === 'music video') {
                            // Look for bullet separator
                            const bulletIdx = remainingParts.indexOf('•');
                            if (bulletIdx >= 0 && bulletIdx + 1 < remainingParts.length) {
                                const realArtist = remainingParts[bulletIdx + 1].trim();
                                if (realArtist && realArtist.toLowerCase() !== 'music video') {
                                    result.push({ index: trackIndex, artist: realArtist, title: trackName });
                                    continue;
                                }
                            }
                            // Try dash splitting
                            const dashMatch = trackName.match(/\s+[-–—]\s+/);
                            if (dashMatch) {
                                const salvageArtist = trackName.substring(0, dashMatch.index).trim();
                                const salvageTitle = trackName.substring(dashMatch.index + dashMatch[0].length).trim();
                                if (salvageArtist && salvageTitle) {
                                    result.push({ index: trackIndex, artist: salvageArtist, title: salvageTitle });
                                }
                            }
                            continue;
                        }
                        
                        result.push({ index: trackIndex, artist, title: trackName });
                    } catch (e) {
                        continue;
                    }
                }
                return JSON.stringify(result);
            }";

            var jsonResult = await page.EvaluateAsync<string>(script);
            
            if (string.IsNullOrWhiteSpace(jsonResult))
            {
                return;
            }

            using var doc = JsonDocument.Parse(jsonResult);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                try
                {
                    if (!element.TryGetProperty("index", out var indexProp) ||
                        !element.TryGetProperty("artist", out var artistProp) ||
                        !element.TryGetProperty("title", out var titleProp))
                    {
                        continue;
                    }

                    var index = indexProp.GetInt32();
                    var artist = artistProp.GetString() ?? "Unknown Artist";
                    var title = titleProp.GetString();

                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    seenTracksByIndex[index] = new TrackModel
                    {
                        Id = $"{index}",
                        Title = title,
                        Artist = artist
                    };
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error parsing extracted track");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting visible tracks from Spotify page");
        }
    }

    private static async Task<bool> ScrollToLastTrackRowAsync(IPage page)
    {
        var hasTrackRows = await page.EvaluateAsync<bool>("() => document.querySelectorAll('[data-testid=\"tracklist-row\"]').length > 0");
        if (!hasTrackRows)
        {
            return false;
        }

        await page.EvaluateAsync(@"() => {
            const rows = document.querySelectorAll('[data-testid=""tracklist-row""]');
            const lastRow = rows[rows.length - 1];
            if (lastRow) {
                lastRow.scrollIntoView({ block: 'end', inline: 'nearest' });
            }
        }");

        return true;
    }

    private static async Task<string?> TryGetPlaylistNameAsync(IPage page)
    {
        const string script = @"() => {
            const selectors = [
                '[data-testid=""playlist-page""] h1',
                '[data-testid=""entityTitle""] h1',
                'h1'
            ];

            for (const selector of selectors) {
                const el = document.querySelector(selector);
                const name = el?.textContent?.trim();
                if (name) {
                    return name;
                }
            }

            return null;
        }";

        return await page.EvaluateAsync<string?>(script);
    }

    private async Task<string?> TryGetPlaylistImageAsync(IPage page)
    {
        try
        {
            var imageUrl = await page.EvaluateAsync<string?>(@"() => {
                const playlistImageEl = document.querySelector('[data-testid=""playlist-image""]');
                if (!playlistImageEl) {
                    return null;
                }

                const imgs = playlistImageEl.querySelectorAll('img');
                for (const img of imgs) {
                    if (img.src) {
                        return img.src.trim();
                    }
                }
                
                return null;
            }");
            
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                return imageUrl;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract playlist image");
        }

        return null;
    }
}
