using OctoSync.Core.Matching;
using OctoSync.Core.Models;

namespace OctoSync.Tests.Core.Matching;

public class TrackMatcherTests
{
    [Fact]
    public void Normalize_ReturnsEmpty_WhenInputIsNull()
    {
        var result = TrackMatcher.Normalize(null);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_ReplacesSeparatorsAndFeatureKeywords()
    {
        var result = TrackMatcher.Normalize("My Song, feat. Artist A & Artist B (Live)");

        Assert.Equal("My Song Artist A Artist B Live", result);
    }

    [Fact]
    public void IsTitleAndArtistMatch_ReturnsTrue_WhenNormalizedTokensOverlapAboveThreshold()
    {
        var result = TrackMatcher.IsTitleAndArtistMatch(
            leftTitle: "Neon Lights feat. Guest",
            leftArtist: "Main Artist",
            rightTitle: "Neon Lights",
            rightArtist: "Main Artist");

        Assert.True(result);
    }

    [Fact]
    public void TracksMatch_ReturnsFalse_WhenArtistDoesNotMatch()
    {
        var source = new TrackModel { Id = "src-1", Title = "Skyline", Artist = "Artist A" };
        var target = new TrackModel { Id = "tgt-1", Title = "Skyline", Artist = "Artist B" };

        var result = TrackMatcher.TracksMatch(source, target);

        Assert.False(result);
    }

    [Fact]
    public void Normalize_CollapsesWhitespace_AndTrims()
    {
        var result = TrackMatcher.Normalize("  Song   Title   ");

        Assert.Equal("Song Title", result);
    }

    [Fact]
    public void IsTitleAndArtistMatch_ReturnsFalse_WhenOneSideIsEmpty()
    {
        var result = TrackMatcher.IsTitleAndArtistMatch(
            leftTitle: null,
            leftArtist: "Artist",
            rightTitle: "Title",
            rightArtist: "Artist");

        Assert.False(result);
    }

    [Fact]
    public void IsTitleAndArtistMatch_IsCaseInsensitive_AndOrderInsensitive()
    {
        var result = TrackMatcher.IsTitleAndArtistMatch(
            leftTitle: "Midnight City",
            leftArtist: "M83",
            rightTitle: "CITY MIDNIGHT",
            rightArtist: "m83");

        Assert.True(result);
    }
}
