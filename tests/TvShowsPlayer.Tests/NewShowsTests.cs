using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Новый сериал в папке не попадает в эфир сам: программа его замечает, но держит
/// выключенным, пока пользователь не убедится, что закачка закончена, и не включит.
/// </summary>
public class NewShowsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tvsp_new_{Guid.NewGuid():N}");

    public NewShowsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void MakeShow(string name, int episodes = 3)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        for (var i = 1; i <= episodes; i++)
        {
            var path = Path.Combine(dir, $"S01E{i:00}.mkv");
            File.WriteAllText(path, "");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-5));   // давно скачано
        }
    }

    private ChannelBuildOptions Options(
        IReadOnlyList<string>? known = null, IReadOnlyList<string>? excluded = null) => new()
    {
        Root = _root,
        PlaylistPath = Path.Combine(_root, "channel.m3u"),
        StatePath = Path.Combine(_root, "state.json"),
        KnownShows = known,
        ExcludedShows = excluded ?? Array.Empty<string>(),
        Force = true,
    };

    [Fact]
    public void FirstRun_NothingKnownYet_EverythingGoesOnAir()
    {
        MakeShow("Геркулес");
        MakeShow("Чародейки");

        var result = ChannelBuilder.Build(Options(known: Array.Empty<string>()));

        result.NewShows.Should().BeEmpty("на первом запуске нечего «включать» — это и есть библиотека");
        result.ShowCount.Should().Be(2);
        result.FoundShows.Should().BeEquivalentTo("Геркулес", "Чародейки");
    }

    [Fact]
    public void AppearedAfterFirstRun_IsReportedAndKeptOffAir()
    {
        MakeShow("Геркулес");
        MakeShow("Новый сериал");   // появился уже после того, как канал был настроен

        var result = ChannelBuilder.Build(Options(known: new[] { "Геркулес" }));

        result.NewShows.Should().ContainSingle().Which.Should().Be("Новый сериал");
        result.ShowCount.Should().Be(1, "новый сериал ждёт, пока его включат");
        File.ReadAllText(Path.Combine(_root, "channel.m3u")).Should().NotContain("Новый сериал");
    }

    [Fact]
    public void TurnedOnByUser_GoesOnAir()
    {
        MakeShow("Геркулес");
        MakeShow("Новый сериал");

        // пользователь поставил галочку: сериал уже известен и не в исключениях
        var result = ChannelBuilder.Build(Options(known: new[] { "Геркулес", "Новый сериал" }));

        result.NewShows.Should().BeEmpty();
        result.ShowCount.Should().Be(2);
    }

    [Fact]
    public void ExcludedByUser_IsNotReportedAsNewAgain()
    {
        MakeShow("Геркулес");
        MakeShow("Отключённый");

        var result = ChannelBuilder.Build(Options(
            known: new[] { "Геркулес", "Отключённый" },
            excluded: new[] { "Отключённый" }));

        result.NewShows.Should().BeEmpty("пользователь сам его выключил — это не новинка");
        result.ShowCount.Should().Be(1);
    }

    [Fact]
    public void KnownShowsNotProvided_BehavesAsBefore()
    {
        MakeShow("Геркулес");
        MakeShow("Другой");

        var result = ChannelBuilder.Build(Options(known: null));

        result.NewShows.Should().BeEmpty();
        result.ShowCount.Should().Be(2);
    }

    [Fact]
    public void FoundShows_IncludeEvenThoseKeptOffAir_SoTheyCanBeRemembered()
    {
        MakeShow("Геркулес");
        MakeShow("Новый сериал");

        var result = ChannelBuilder.Build(Options(known: new[] { "Геркулес" }));

        result.FoundShows.Should().BeEquivalentTo("Геркулес", "Новый сериал");
    }
}
