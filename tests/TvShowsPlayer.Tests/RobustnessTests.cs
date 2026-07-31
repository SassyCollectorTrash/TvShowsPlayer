using System.Globalization;
using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Регрессии по итогам ревью: устойчивость к «грязным» данным библиотеки и к
/// повреждённому состоянию — то, что ломало первый запуск и прогресс просмотра.
/// </summary>
public class RobustnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tvsp_rob_{Guid.NewGuid():N}");

    public RobustnessTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeShow(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private static void Touch(string dir, string file) => File.WriteAllText(Path.Combine(dir, file), "");

    [Fact]
    public void Scan_FileNameWithHugeDigitRun_DoesNotThrow()
    {
        var show = MakeShow("Сериал");
        Touch(show, "S01E01.mkv");
        Touch(show, "release 99999999999999999999999 group.mkv");   // не влезает в long

        var shows = ShowScanner.Scan(_root);

        shows.Should().ContainSingle().Which.Episodes.Should().HaveCount(2);
    }

    [Fact]
    public void Order_HugeNumbers_AreComparedWithoutOverflow()
    {
        var ordered = EpisodeOrdering.Order(new[]
        {
            "ep 99999999999999999999999.mkv",
            "ep 11111111111111111111111.mkv",
        });

        ordered.Should().HaveCount(2);
    }

    // --- состояние: атомарная запись + запасная копия ---

    [Fact]
    public void Save_KeepsPreviousVersionAsBackup()
    {
        var path = Path.Combine(_root, "state.json");
        new ChannelState { PlaylistPos = 18, Shows = { ["Геркулес"] = "S01E31.mkv" } }.Save(path);

        new ChannelState { PlaylistPos = 20 }.Save(path);

        File.Exists(path + ".bak").Should().BeTrue();
        ChannelState.Load(path + ".bak").Shows.Should().ContainKey("Геркулес");
    }

    [Fact]
    public void Load_CorruptFile_RecoversProgressFromBackup()
    {
        var path = Path.Combine(_root, "state.json");
        new ChannelState { PlaylistPos = 18, Shows = { ["Геркулес"] = "S01E31.mkv" } }.Save(path);
        new ChannelState { PlaylistPos = 19, Shows = { ["Геркулес"] = "S01E32.mkv" } }.Save(path);

        // mpv усёк файл на середине записи (или выключили питание)
        File.WriteAllText(path, "{\"playlist_pos\":19,\"shows\":{\"Герку");

        var state = ChannelState.Load(path);

        state.Shows.Should().ContainKey("Геркулес", "прогресс должен подниматься из резервной копии");
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        ChannelState.Load(Path.Combine(_root, "нет.json")).Shows.Should().BeEmpty();
    }

    // --- настройки карусели должны влиять на пересборку ---

    [Fact]
    public void Build_WhenCarouselSettingsChanged_RebuildsPlaylist()
    {
        var show = MakeShow("Сериал");
        for (var i = 1; i <= 10; i++)
            Touch(show, $"S01E{i:00}.mkv");

        var options = new ChannelBuildOptions
        {
            Root = _root,
            PlaylistPath = Path.Combine(_root, "channel.m3u"),
            StatePath = Path.Combine(_root, "state.json"),
            Window = 4,
            Step = 2,
        };

        ChannelBuilder.Build(options);
        var second = ChannelBuilder.Build(options with { Window = 8 });

        second.Rebuilt.Should().BeTrue();
    }

    [Fact]
    public void Build_WhenNothingChanged_StillSkipsRebuild()
    {
        var show = MakeShow("Сериал");
        for (var i = 1; i <= 10; i++)
            Touch(show, $"S01E{i:00}.mkv");

        var options = new ChannelBuildOptions
        {
            Root = _root,
            PlaylistPath = Path.Combine(_root, "channel.m3u"),
            StatePath = Path.Combine(_root, "state.json"),
        };

        ChannelBuilder.Build(options);

        ChannelBuilder.Build(options).Rebuilt.Should().BeFalse();
    }

    // --- карусель: чужой засев не должен ронять сборку ---

    [Fact]
    public void Carousel_StartCursorsShorterThanShows_DoesNotThrow()
    {
        var shows = new[]
        {
            new Show("A", new[] { "a1", "a2" }),
            new Show("B", new[] { "b1", "b2" }),
        };

        var act = () => Carousel.Build(shows, startCursors: new[] { 0 });

        act.Should().NotThrow();
    }

    // --- параметры для Lua: запятые и дробные числа ---

    [Fact]
    public void LaunchArgs_LibraryPathWithComma_IsPassedIntact()
    {
        var args = MpvLaunchArgs.Build(new MpvLaunchOptions
        {
            ConfigDir = @"C:\cfg",
            Playlist = @"C:\cfg\channel.m3u",
            PipePath = @"\\.\pipe\x",
            ChannelOsdRoot = @"D:\Мультфильмы, сериалы",
        });

        args.Should().Contain(@"--script-opts-append=channelosd-root=D:\Мультфильмы, сериалы");
    }

    [Fact]
    public void LaunchArgs_FractionalSeconds_UseInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");   // десятичная запятая
        try
        {
            var args = MpvLaunchArgs.Build(new MpvLaunchOptions
            {
                ConfigDir = @"C:\cfg",
                Playlist = @"C:\cfg\channel.m3u",
                PipePath = @"\\.\pipe\x",
                SplashSeconds = 4.5,
            });

            args.Should().Contain("--script-opts-append=channelosd-splash=4.5");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void LaunchArgs_OsdSettings_AreAllForwardedToScript()
    {
        var args = MpvLaunchArgs.Build(new MpvLaunchOptions
        {
            ConfigDir = @"C:\cfg",
            Playlist = @"C:\cfg\channel.m3u",
            PipePath = @"\\.\pipe\x",
            ChannelName = "LocalTV",
            SplashSeconds = 4,
            BumperSeconds = 3,
            PlashkaSeconds = 5,
            ClockEnabled = false,
            RetroTheme = true,
        });

        args.Should().Contain("--script-opts-append=channelosd-name=LocalTV");
        args.Should().Contain("--script-opts-append=channelosd-bumper=3");
        args.Should().Contain("--script-opts-append=channelosd-plashka=5");
        args.Should().Contain("--script-opts-append=channelosd-clock=no");
        args.Should().Contain("--script-opts-append=channelosd-retro=yes");
    }
}
