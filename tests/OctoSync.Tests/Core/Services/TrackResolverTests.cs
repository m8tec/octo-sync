using Microsoft.Extensions.Logging;
using Moq;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Services;

namespace OctoSync.Tests.Core.Services;

public class TrackResolverTests
{
    [Fact]
    public async Task ResolveTracksAsync_UsesExistingTargetTrack_WithoutSearchCall()
    {
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<TrackResolver>>();
        var resolver = new TrackResolver(loggerMock.Object);

        var sourceTracks = new[]
        {
            CreateTrack("src-1", "Midnight City", "M83")
        };

        var targetTracks = new[]
        {
            CreateTrack("target-42", "Midnight City", "M83")
        };

        var (resolvedTracks, unresolvedCount) = await resolver.ResolveTracksAsync(
            targetMock.Object,
            sourceTracks,
            targetTracks,
            CancellationToken.None);

        Assert.Single(resolvedTracks);
        Assert.Equal("target-42", resolvedTracks[0].TargetId);
        Assert.Equal(0, unresolvedCount);
        targetMock.Verify(
            x => x.FindBestMatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveTracksAsync_UsesBracketFallback_WhenPrimarySearchReturnsNull()
    {
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<TrackResolver>>();
        var resolver = new TrackResolver(loggerMock.Object);

        var sourceTracks = new[]
        {
            CreateTrack("src-1", "Lost Stars (Official Video) [HD]", "Adam Levine")
        };

        targetMock
            .Setup(x => x.FindBestMatchAsync("Lost Stars (Official Video) [HD]", "Adam Levine", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        targetMock
            .Setup(x => x.FindBestMatchAsync("Lost Stars", "Adam Levine", It.IsAny<CancellationToken>()))
            .ReturnsAsync("resolved-1");

        var (resolvedTracks, unresolvedCount) = await resolver.ResolveTracksAsync(
            targetMock.Object,
            sourceTracks,
            Array.Empty<TrackModel>(),
            CancellationToken.None);

        Assert.Single(resolvedTracks);
        Assert.Equal("resolved-1", resolvedTracks[0].TargetId);
        Assert.Equal(0, unresolvedCount);
        targetMock.Verify(
            x => x.FindBestMatchAsync("Lost Stars (Official Video) [HD]", "Adam Levine", It.IsAny<CancellationToken>()),
            Times.Once);
        targetMock.Verify(
            x => x.FindBestMatchAsync("Lost Stars", "Adam Levine", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveTracksAsync_IncrementsUnresolvedCount_WhenNoMatchIsFound()
    {
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<TrackResolver>>();
        var resolver = new TrackResolver(loggerMock.Object);

        var sourceTracks = new[]
        {
            CreateTrack("src-1", "Unknown Song", "Unknown Artist")
        };

        targetMock
            .Setup(x => x.FindBestMatchAsync("Unknown Song", "Unknown Artist", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var (resolvedTracks, unresolvedCount) = await resolver.ResolveTracksAsync(
            targetMock.Object,
            sourceTracks,
            Array.Empty<TrackModel>(),
            CancellationToken.None);

        Assert.Empty(resolvedTracks);
        Assert.Equal(1, unresolvedCount);
        targetMock.Verify(
            x => x.FindBestMatchAsync("Unknown Song", "Unknown Artist", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static TrackModel CreateTrack(string id, string title, string artist)
    {
        return new TrackModel
        {
            Id = id,
            Title = title,
            Artist = artist
        };
    }
}
