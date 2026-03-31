using Microsoft.Extensions.Logging;
using Moq;
using OctoSync.Core.Services;

namespace OctoSync.Tests.Core.Services;

public class SyncStateManagerTests
{
    [Fact]
    public void GetOrCreateState_ReturnsSameInstance_ForSameProviderAndPlaylist()
    {
        var sut = CreateSut();

        var first = sut.GetOrCreateState("Spotify", "pl-1");
        var second = sut.GetOrCreateState("Spotify", "pl-1");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreateState_IsCaseInsensitive_ForStateKey()
    {
        var sut = CreateSut();

        var first = sut.GetOrCreateState("Spotify", "PL-1");
        var second = sut.GetOrCreateState("spotify", "pl-1");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreateState_ReturnsDifferentInstance_ForDifferentPlaylistIds()
    {
        var sut = CreateSut();

        var first = sut.GetOrCreateState("Spotify", "pl-1");
        var second = sut.GetOrCreateState("Spotify", "pl-2");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void ShouldSkipSync_ReturnsFalse_WhenSourceHashChanged()
    {
        var sut = CreateSut();
        var state = new PlaylistSyncState
        {
            LastSourceHash = "old-hash",
            LastUnresolvedCount = 0
        };

        var result = sut.ShouldSkipSync("Spotify", "pl-1", "new-hash", state);

        Assert.False(result);
    }

    [Fact]
    public void ShouldSkipSync_ReturnsTrue_WhenHashUnchanged_AndNoUnresolvedTracks()
    {
        var sut = CreateSut();
        var state = new PlaylistSyncState
        {
            LastSourceHash = "same-hash",
            LastUnresolvedCount = 0
        };

        var result = sut.ShouldSkipSync("Spotify", "pl-1", "same-hash", state);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSkipSync_ReturnsFalse_WhenHashUnchanged_ButUnresolvedTracksRemain()
    {
        var sut = CreateSut();
        var state = new PlaylistSyncState
        {
            LastSourceHash = "same-hash",
            LastUnresolvedCount = 2
        };

        var result = sut.ShouldSkipSync("Spotify", "pl-1", "same-hash", state);

        Assert.False(result);
    }

    [Fact]
    public void UpdateState_SetsHashAndUnresolvedCount()
    {
        var sut = CreateSut();
        var state = sut.GetOrCreateState("Spotify", "pl-1");

        sut.UpdateState("Spotify", "pl-1", "hash-123", 3);

        Assert.Equal("hash-123", state.LastSourceHash);
        Assert.Equal(3, state.LastUnresolvedCount);
    }

    [Fact]
    public void UpdateState_CreatesState_WhenMissing()
    {
        var sut = CreateSut();

        sut.UpdateState("Spotify", "pl-1", "hash-abc", 1);

        var state = sut.GetOrCreateState("Spotify", "pl-1");
        Assert.Equal("hash-abc", state.LastSourceHash);
        Assert.Equal(1, state.LastUnresolvedCount);
    }

    private static SyncStateManager CreateSut()
    {
        var logger = Mock.Of<ILogger<SyncStateManager>>();
        return new SyncStateManager(logger);
    }
}
