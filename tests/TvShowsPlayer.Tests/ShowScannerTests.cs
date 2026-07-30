using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class ShowScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"tvsp_scan_{Guid.NewGuid():N}");

    public ShowScannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeShow(string name)
    {
        return Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    }

    private static void Touch(string dir, string file)
    {
        File.WriteAllText(Path.Combine(dir, file), string.Empty);
    }

    [Fact]
    public void Scan_SubfolderWithVideos_ReturnsShowNamedAfterFolder()
    {
        var show = MakeShow("Pokemon");
        Touch(show, "Pokemon S01E01.mkv");
        Touch(show, "Pokemon S01E02.mkv");

        var shows = ShowScanner.Scan(_root);

        shows.Should().ContainSingle();
        shows[0].Name.Should().Be("Pokemon");
        shows[0].Episodes.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_NonVideoFiles_AreIgnored()
    {
        var show = MakeShow("Naruto");
        Touch(show, "Naruto 01.mp4");
        Touch(show, "readme.txt");
        Touch(show, "cover.jpg");

        var shows = ShowScanner.Scan(_root);

        shows.Should().ContainSingle();
        shows[0].Episodes.Should().HaveCount(1);
        shows[0].Episodes[0].Should().EndWith("Naruto 01.mp4");
    }

    [Fact]
    public void Scan_VideosInNestedSeasonFolders_AreFound()
    {
        var show = MakeShow("He-Man");
        var season2 = Directory.CreateDirectory(Path.Combine(show, "Сезон 2")).FullName;
        Touch(season2, "S02E01.mkv");

        var shows = ShowScanner.Scan(_root);

        shows.Should().ContainSingle();
        shows[0].Episodes.Should().HaveCount(1);
    }

    [Fact]
    public void Scan_Episodes_AreOrderedByEpisodeOrdering()
    {
        var show = MakeShow("Dexter");
        Touch(show, "Dexter S01E10.mkv");
        Touch(show, "Dexter S01E02.mkv");
        Touch(show, "Dexter S01E01.mkv");

        var shows = ShowScanner.Scan(_root);

        shows[0].Episodes.Select(Path.GetFileName)
            .Should().ContainInOrder("Dexter S01E01.mkv", "Dexter S01E02.mkv", "Dexter S01E10.mkv");
    }

    [Fact]
    public void Scan_FolderWithoutVideos_IsSkipped()
    {
        var withVideo = MakeShow("Real");
        Touch(withVideo, "Real S01E01.mkv");
        var empty = MakeShow("Empty");
        Touch(empty, "notes.txt");

        var shows = ShowScanner.Scan(_root);

        shows.Should().ContainSingle();
        shows[0].Name.Should().Be("Real");
    }

    [Fact]
    public void Scan_Shows_AreSortedByNaturalName()
    {
        foreach (var name in new[] { "Show 10", "Show 2", "Show 1" })
            Touch(MakeShow(name), "S01E01.mkv");

        var shows = ShowScanner.Scan(_root);

        shows.Select(s => s.Name)
            .Should().ContainInOrder("Show 1", "Show 2", "Show 10");
    }

    [Fact]
    public void Scan_ExcludedShows_AreSkipped()
    {
        Touch(MakeShow("Ранма 1-2"), "01.mkv");
        Touch(MakeShow("Геркулес"), "01.mkv");
        Touch(MakeShow("Попсовый Эпос"), "01.mkv");

        var shows = ShowScanner.Scan(_root, new[] { "Ранма 1-2", "Попсовый Эпос" });

        shows.Select(s => s.Name).Should().ContainSingle().Which.Should().Be("Геркулес");
    }

    [Fact]
    public void Scan_Exclusion_IsCaseInsensitiveAndTrimmed()
    {
        Touch(MakeShow("Геркулес"), "01.mkv");
        Touch(MakeShow("Чародейки"), "01.mkv");

        var shows = ShowScanner.Scan(_root, new[] { "  чАрОдЕйКи  " });

        shows.Select(s => s.Name).Should().ContainSingle().Which.Should().Be("Геркулес");
    }

    [Fact]
    public void Scan_ExcludingNonexistentName_ReturnsAllShows()
    {
        Touch(MakeShow("Геркулес"), "01.mkv");
        Touch(MakeShow("Чародейки"), "01.mkv");

        var shows = ShowScanner.Scan(_root, new[] { "Нет такого сериала" });

        shows.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_NullExclusions_ReturnsAllShows()
    {
        Touch(MakeShow("Геркулес"), "01.mkv");
        Touch(MakeShow("Чародейки"), "01.mkv");

        var shows = ShowScanner.Scan(_root, excluded: null);

        shows.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_MissingOrEmptyRoot_ReturnsEmpty_NotThrows()
    {
        // свежий получатель: библиотека ещё не указана / путь не существует —
        // не падаем, просто пустой канал (пользователь укажет папку в настройках).
        ShowScanner.Scan(Path.Combine(_root, "нет-такой-папки")).Should().BeEmpty();
        ShowScanner.Scan("").Should().BeEmpty();
    }
}
