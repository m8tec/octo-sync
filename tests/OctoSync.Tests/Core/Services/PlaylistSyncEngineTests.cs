using Microsoft.Extensions.Logging;
using Moq;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Models;
using OctoSync.Core.Services;

namespace OctoSync.Tests.Core.Services;

public class PlaylistSyncEngineTests
{
    [Fact]
    public async Task ProcessPlaylistAsync_Skips_WhenStateManagerRequestsSkip()
    {
        var sourcePlaylist = CreatePlaylist("pl-1", CreateTrack("s1", "Song 1", "Artist 1"));
        var source = new TestPlaylistSource("Spotify", sourcePlaylist);
        var state = new PlaylistSyncState();

        var stateManagerMock = new Mock<ISyncStateManager>(MockBehavior.Strict);
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var resolverMock = new Mock<ITrackResolver>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<PlaylistSyncEngine>>();

        stateManagerMock
            .Setup(x => x.GetOrCreateState("Spotify", "pl-1"))
            .Returns(state);
        stateManagerMock
            .Setup(x => x.ShouldSkipSync("Spotify", "pl-1", It.IsAny<string>(), state))
            .Returns(true);

        var sut = new PlaylistSyncEngine(stateManagerMock.Object, resolverMock.Object, loggerMock.Object);

        await sut.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, CancellationToken.None);

        targetMock.Verify(x => x.EnsurePlaylistExistsAsync(It.IsAny<PlaylistModel>(), It.IsAny<CancellationToken>()), Times.Never);
        resolverMock.Verify(x => x.ResolveTracksAsync(It.IsAny<IPlaylistTarget>(), It.IsAny<IReadOnlyList<TrackModel>>(), It.IsAny<IReadOnlyList<TrackModel>>(), It.IsAny<CancellationToken>()), Times.Never);
        stateManagerMock.Verify(x => x.UpdateState(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPlaylistAsync_DoesNotModifyPlaylist_WhenAlreadyInSync()
    {
        var sourceTracks = new[]
        {
            CreateTrack("s1", "Song 1", "Artist 1"),
            CreateTrack("s2", "Song 2", "Artist 2")
        };

        var sourcePlaylist = CreatePlaylist("pl-1", sourceTracks);
        var targetPlaylist = CreatePlaylist("local-1", sourceTracks);
        var source = new TestPlaylistSource("Spotify", sourcePlaylist);
        var state = new PlaylistSyncState();

        var stateManagerMock = new Mock<ISyncStateManager>(MockBehavior.Strict);
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var resolverMock = new Mock<ITrackResolver>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<PlaylistSyncEngine>>();

        stateManagerMock.Setup(x => x.GetOrCreateState("Spotify", "pl-1")).Returns(state);
        stateManagerMock.Setup(x => x.ShouldSkipSync("Spotify", "pl-1", It.IsAny<string>(), state)).Returns(false);
        stateManagerMock
            .Setup(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 1));

        targetMock
            .Setup(x => x.EnsurePlaylistExistsAsync(sourcePlaylist, It.IsAny<CancellationToken>()))
            .ReturnsAsync("local-1");
        targetMock
            .Setup(x => x.GetTargetPlaylistAsync("local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPlaylist);

        resolverMock
            .Setup(x => x.ResolveTracksAsync(targetMock.Object, sourcePlaylist.Tracks, targetPlaylist.Tracks, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                [
                    new(sourceTracks[0], "t1"),
                    new(sourceTracks[1], "t2")
                ],
                1));

        var sut = new PlaylistSyncEngine(stateManagerMock.Object, resolverMock.Object, loggerMock.Object);

        await sut.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, CancellationToken.None);

        targetMock.Verify(x => x.RemoveTrackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        targetMock.Verify(x => x.AddTrackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        stateManagerMock.Verify(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 1), Times.Once);
    }

    [Fact]
    public async Task ProcessPlaylistAsync_RemovesTailAndAddsTail_WhenPrefixDiverges()
    {
        var sourceTracks = new[]
        {
            CreateTrack("s1", "Song 1", "Artist 1"),
            CreateTrack("s2", "Song 2", "Artist 2"),
            CreateTrack("s3", "Song 3", "Artist 3")
        };

        var targetTracks = new[]
        {
            CreateTrack("t1", "Song 1", "Artist 1"),
            CreateTrack("t2", "Wrong Song", "Artist 2"),
            CreateTrack("t3", "Another Song", "Artist 3")
        };

        var sourcePlaylist = CreatePlaylist("pl-1", sourceTracks);
        var targetPlaylist = CreatePlaylist("local-1", targetTracks);
        var source = new TestPlaylistSource("Spotify", sourcePlaylist);
        var state = new PlaylistSyncState();

        var stateManagerMock = new Mock<ISyncStateManager>(MockBehavior.Strict);
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var resolverMock = new Mock<ITrackResolver>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<PlaylistSyncEngine>>();

        stateManagerMock.Setup(x => x.GetOrCreateState("Spotify", "pl-1")).Returns(state);
        stateManagerMock.Setup(x => x.ShouldSkipSync("Spotify", "pl-1", It.IsAny<string>(), state)).Returns(false);
        stateManagerMock
            .Setup(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 0));

        targetMock
            .Setup(x => x.EnsurePlaylistExistsAsync(sourcePlaylist, It.IsAny<CancellationToken>()))
            .ReturnsAsync("local-1");
        targetMock
            .Setup(x => x.GetTargetPlaylistAsync("local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPlaylist);

        resolverMock
            .Setup(x => x.ResolveTracksAsync(targetMock.Object, sourcePlaylist.Tracks, targetPlaylist.Tracks, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                [
                    new(sourceTracks[0], "rid-1"),
                    new(sourceTracks[1], "rid-2"),
                    new(sourceTracks[2], "rid-3")
                ],
                0));

        targetMock
            .Setup(x => x.RemoveTrackAsync("local-1", "2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        targetMock
            .Setup(x => x.RemoveTrackAsync("local-1", "1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        targetMock
            .Setup(x => x.AddTrackAsync("local-1", "rid-2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        targetMock
            .Setup(x => x.AddTrackAsync("local-1", "rid-3", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PlaylistSyncEngine(stateManagerMock.Object, resolverMock.Object, loggerMock.Object);

        await sut.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, CancellationToken.None);

        targetMock.Verify(x => x.RemoveTrackAsync("local-1", "2", It.IsAny<CancellationToken>()), Times.Once);
        targetMock.Verify(x => x.RemoveTrackAsync("local-1", "1", It.IsAny<CancellationToken>()), Times.Once);
        targetMock.Verify(x => x.AddTrackAsync("local-1", "rid-2", It.IsAny<CancellationToken>()), Times.Once);
        targetMock.Verify(x => x.AddTrackAsync("local-1", "rid-3", It.IsAny<CancellationToken>()), Times.Once);
        stateManagerMock.Verify(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 0), Times.Once);
    }

    [Fact]
    public async Task ProcessPlaylistAsync_ContinuesAddingTracks_WhenOperationalAddFails()
    {
        var sourceTracks = new[]
        {
            CreateTrack("s1", "Song 1", "Artist 1"),
            CreateTrack("s2", "Song 2", "Artist 2")
        };

        var sourcePlaylist = CreatePlaylist("pl-1", sourceTracks);
        var targetPlaylist = CreatePlaylist("local-1");
        var source = new TestPlaylistSource("Spotify", sourcePlaylist);
        var state = new PlaylistSyncState();

        var stateManagerMock = new Mock<ISyncStateManager>(MockBehavior.Strict);
        var targetMock = new Mock<IPlaylistTarget>(MockBehavior.Strict);
        var resolverMock = new Mock<ITrackResolver>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<PlaylistSyncEngine>>();

        stateManagerMock.Setup(x => x.GetOrCreateState("Spotify", "pl-1")).Returns(state);
        stateManagerMock.Setup(x => x.ShouldSkipSync("Spotify", "pl-1", It.IsAny<string>(), state)).Returns(false);
        stateManagerMock
            .Setup(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 0));

        targetMock
            .Setup(x => x.EnsurePlaylistExistsAsync(sourcePlaylist, It.IsAny<CancellationToken>()))
            .ReturnsAsync("local-1");
        targetMock
            .Setup(x => x.GetTargetPlaylistAsync("local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPlaylist);

        resolverMock
            .Setup(x => x.ResolveTracksAsync(targetMock.Object, sourcePlaylist.Tracks, targetPlaylist.Tracks, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                [
                    new(sourceTracks[0], "rid-1"),
                    new(sourceTracks[1], "rid-2")
                ],
                0));

        targetMock
            .Setup(x => x.AddTrackAsync("local-1", "rid-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("add failed"));
        targetMock
            .Setup(x => x.AddTrackAsync("local-1", "rid-2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PlaylistSyncEngine(stateManagerMock.Object, resolverMock.Object, loggerMock.Object);

        await sut.ProcessPlaylistAsync(source, "pl-1", targetMock.Object, CancellationToken.None);

        targetMock.Verify(x => x.AddTrackAsync("local-1", "rid-1", It.IsAny<CancellationToken>()), Times.Once);
        targetMock.Verify(x => x.AddTrackAsync("local-1", "rid-2", It.IsAny<CancellationToken>()), Times.Once);
        stateManagerMock.Verify(x => x.UpdateState("Spotify", "pl-1", It.IsAny<string>(), 0), Times.Once);
    }
    private static PlaylistModel CreatePlaylist(string externalId, params TrackModel[] tracks)
    {
        return new PlaylistModel
        {
            ExternalId = externalId,
            Name = externalId,
            Tracks = tracks.ToList()
        };
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

    private sealed class TestPlaylistSource(string providerName, PlaylistModel playlist) : IPlaylistSource
    {
        public string ProviderName { get; } = providerName;

        public Task<PlaylistModel> GetPlaylistAsync(string externalPlaylistId, CancellationToken cancellationToken)
        {
            return Task.FromResult(playlist);
        }
    }
}
