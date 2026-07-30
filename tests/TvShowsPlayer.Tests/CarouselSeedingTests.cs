using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class CarouselSeedingTests
{
    private const string Root = @"D:\Lib";

    private static Show MakeShow(string name, params string[] rels) =>
        new(name, rels.Select(r => Path.Combine(Root, name, r)).ToArray());

    [Fact]
    public void StartCursors_SeedsCursorToSavedEpisodeIndex()
    {
        var shows = new[] { MakeShow("A", "01.mkv", "02.mkv", "03.mkv", "04.mkv", "05.mkv", "06.mkv") };
        var progress = new Dictionary<string, string> { ["A"] = "05.mkv" };

        var cursors = CarouselSeeding.StartCursors(Root, shows, progress);

        cursors[0].Should().Be(4);
    }

    [Fact]
    public void StartCursors_UnknownOrMissingShow_IsZero()
    {
        var shows = new[] { MakeShow("A", "01.mkv", "02.mkv") };
        var progress = new Dictionary<string, string> { ["B"] = "01.mkv" };

        var cursors = CarouselSeeding.StartCursors(Root, shows, progress);

        cursors[0].Should().Be(0);
    }

    [Fact]
    public void StartCursors_IdentityCaseInsensitiveWithSubfolders()
    {
        var shows = new[] { MakeShow("A", @"Season 1\01.mkv", @"Season 1\02.mkv") };
        var progress = new Dictionary<string, string> { ["A"] = @"season 1\02.MKV" };

        var cursors = CarouselSeeding.StartCursors(Root, shows, progress);

        cursors[0].Should().Be(1);
    }
}
