using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class PlaylistIndexTests
{
    private const string Root = @"D:\Lib";

    [Fact]
    public void OfCurrentEpisode_FindsPlayingEpisode()
    {
        var playlist = new[]
        {
            @"D:\Lib\A\01.mkv",
            @"D:\Lib\B\03.mkv",
            @"D:\Lib\A\03.mkv",
        };
        var progress = new Dictionary<string, string> { ["B"] = "03.mkv" };

        var idx = PlaylistIndex.OfCurrentEpisode(playlist, Root, progress, "B");

        idx.Should().Be(1);
    }

    [Fact]
    public void OfCurrentEpisode_NoCurrentOrUnknown_ReturnsMinusOne()
    {
        var playlist = new[] { @"D:\Lib\A\01.mkv" };
        var progress = new Dictionary<string, string> { ["B"] = "03.mkv" };

        PlaylistIndex.OfCurrentEpisode(playlist, Root, progress, "B").Should().Be(-1);
        PlaylistIndex.OfCurrentEpisode(playlist, Root, progress, null).Should().Be(-1);
    }

    [Fact]
    public void OfCurrentEpisode_MatchesCaseInsensitively()
    {
        var playlist = new[] { @"D:\Lib\A\Season 1\02.mkv" };
        var progress = new Dictionary<string, string> { ["A"] = @"season 1\02.mkv" };

        PlaylistIndex.OfCurrentEpisode(playlist, Root, progress, "A").Should().Be(0);
    }
}
