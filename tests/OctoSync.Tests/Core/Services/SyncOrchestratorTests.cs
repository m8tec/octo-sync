using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Services;

namespace OctoSync.Tests.Core.Services;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task RunCycleAsync_UsesConfiguredPlaylistIds_WhenAvailable()
    {
        var source = new TestPlaylistSource("Spotify");
        var options = CreateOptions(("Spotify", ["pl-1", " ", "pl-2"]));

        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var syncEngineMock = new Mock<IPlaylistSyncEngine>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<SyncOrchestrator>>();

        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "pl-2", targetMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new SyncOrchestrator(
            [source],
            targetMock.Object,
            syncEngineMock.Object,
            Options.Create(options),
            loggerMock.Object);

        await orchestrator.RunCycleAsync(CancellationToken.None);

        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "pl-2", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCycleAsync_UsesDiscovery_WhenConfiguredIdsMissing()
    {
        var source = new DiscoverablePlaylistSource("Csv", ["csv-1", "", "csv-2"]);
        var options = CreateOptions();

        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var syncEngineMock = new Mock<IPlaylistSyncEngine>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<SyncOrchestrator>>();

        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "csv-1", targetMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "csv-2", targetMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new SyncOrchestrator(
            [source],
            targetMock.Object,
            syncEngineMock.Object,
            Options.Create(options),
            loggerMock.Object);

        await orchestrator.RunCycleAsync(CancellationToken.None);

        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "csv-1", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "csv-2", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCycleAsync_SkipsSource_WhenNoConfiguredOrDiscoveredPlaylistsExist()
    {
        var source = new TestPlaylistSource("Deezer");
        var options = CreateOptions();

        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var syncEngineMock = new Mock<IPlaylistSyncEngine>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<SyncOrchestrator>>();

        var orchestrator = new SyncOrchestrator(
            [source],
            targetMock.Object,
            syncEngineMock.Object,
            Options.Create(options),
            loggerMock.Object);

        await orchestrator.RunCycleAsync(CancellationToken.None);

        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(It.IsAny<IPlaylistSource>(), It.IsAny<string>(), It.IsAny<IPlaylistTarget>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunCycleAsync_ContinuesWithNextPlaylist_WhenOperationalExceptionOccurs()
    {
        var source = new TestPlaylistSource("Spotify");
        var options = CreateOptions(("Spotify", ["pl-1", "pl-2"]));

        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var syncEngineMock = new Mock<IPlaylistSyncEngine>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<SyncOrchestrator>>();

        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Boom"));
        syncEngineMock
            .Setup(x => x.ProcessPlaylistAsync(source, "pl-2", targetMock.Object, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new SyncOrchestrator(
            [source],
            targetMock.Object,
            syncEngineMock.Object,
            Options.Create(options),
            loggerMock.Object);

        await orchestrator.RunCycleAsync(CancellationToken.None);

        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
        syncEngineMock.Verify(
            x => x.ProcessPlaylistAsync(source, "pl-2", targetMock.Object, It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    private static SyncOptions CreateOptions(params (string Provider, string[] PlaylistIds)[] entries)
    {
        var options = new SyncOptions();

        foreach (var (provider, playlistIds) in entries)
        {
            options.PlaylistsToSync[provider] = playlistIds.ToList();
        }

        return options;
    }

    private class TestPlaylistSource(string providerName) : IPlaylistSource
    {
        public string ProviderName { get; } = providerName;

        public Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Not required for SyncOrchestrator tests.");
        }
    }

    private sealed class DiscoverablePlaylistSource(string providerName, IReadOnlyList<string> playlistIds)
        : TestPlaylistSource(providerName), IPlaylistSourceDiscovery
    {
        public Task<IReadOnlyList<string>> GetPlaylistIdsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(playlistIds);
        }
    }
}
