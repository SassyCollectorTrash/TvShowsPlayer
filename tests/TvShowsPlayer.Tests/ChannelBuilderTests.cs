using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class ChannelBuilderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"tvsp_build_{Guid.NewGuid():N}");
    private readonly string _root;
    private readonly string _m3u;
    private readonly string _state;

    public ChannelBuilderTests()
    {
        _root = Path.Combine(_dir, "root");
        _m3u = Path.Combine(_dir, "channel.m3u");
        _state = Path.Combine(_dir, "state.json");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void MakeShow(string name, int episodes)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        for (var i = 1; i <= episodes; i++)
            File.WriteAllText(Path.Combine(dir, $"{i:D2}.mkv"), string.Empty);
    }

    private ChannelBuildOptions Options(params string[] excluded) => new()
    {
        Root = _root,
        PlaylistPath = _m3u,
        StatePath = _state,
        ExcludedShows = excluded,
    };

    private IReadOnlyList<string> PlaylistEntries() =>
        File.ReadAllLines(_m3u).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    [Fact]
    public void Build_FreshLibrary_WritesPlaylistAndStartsAtFirstShow()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);

        var result = ChannelBuilder.Build(Options());

        result.Rebuilt.Should().BeTrue();
        result.ShowCount.Should().Be(2);
        File.Exists(_m3u).Should().BeTrue();
        PlaylistEntries()[0].Should().EndWith(@"Alpha\01.mkv");
        ChannelState.Load(_state).PlaylistPos.Should().Be(0);
    }

    [Fact]
    public void Build_CompositionUnchanged_SkipsAndLeavesStateUntouched()
    {
        MakeShow("Alpha", 6);
        ChannelBuilder.Build(Options());
        var state = ChannelState.Load(_state);
        state.PlaylistPos = 42;
        state.Save(_state);

        var result = ChannelBuilder.Build(Options());

        result.Rebuilt.Should().BeFalse();
        ChannelState.Load(_state).PlaylistPos.Should().Be(42);
    }

    // Пересборка происходит от любой мелочи: докачалась серия, добавили сериал. Если
    // при этом обнулять секунду внутри серии, обещанное «продолжится с того же места»
    // работало бы только когда в папке ничего не менялось — то есть почти никогда.
    [Fact]
    public void Build_WhenSameEpisodeResumed_ShouldKeepTimeInsideIt()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);
        ChannelBuilder.Build(Options());

        var state = ChannelState.Load(_state);
        state.Shows["Beta"] = "04.mkv";
        state.Current = "Beta";
        state.TimePos = 431;
        state.Save(_state);

        MakeShow("Gamma", 5);   // состав изменился → пересборка
        ChannelBuilder.Build(Options());

        ChannelState.Load(_state).TimePos.Should().Be(431);
    }

    // А вот если серия, на которой остановились, из эфира ушла — секунда чужая.
    [Fact]
    public void Build_WhenCurrentEpisodeGone_ShouldStartFromBeginning()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);
        ChannelBuilder.Build(Options());

        var state = ChannelState.Load(_state);
        state.Shows["Beta"] = "04.mkv";
        state.Current = "Beta";
        state.TimePos = 431;
        state.Save(_state);

        ChannelBuilder.Build(Options("Beta"));   // сериал выключили из эфира

        ChannelState.Load(_state).TimePos.Should().Be(0);
    }

    [Fact]
    public void Build_CompositionChanged_ResumesCurrentEpisode()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);
        ChannelBuilder.Build(Options());

        // будто смотрели Beta, серия 04; индекс-закладка мусорная
        var state = ChannelState.Load(_state);
        state.Shows["Beta"] = "04.mkv";
        state.Current = "Beta";
        state.PlaylistPos = 99;
        state.Save(_state);

        MakeShow("Gamma", 5);   // состав изменился → пересборка
        var result = ChannelBuilder.Build(Options());

        result.Rebuilt.Should().BeTrue();
        var pos = ChannelState.Load(_state).PlaylistPos;
        pos.Should().BeGreaterThan(0);
        PlaylistEntries()[pos].Should().EndWith(@"Beta\04.mkv");
    }

    [Fact]
    public void Build_ExcludedShow_NotInPlaylist()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);

        ChannelBuilder.Build(Options("Beta"));

        PlaylistEntries().Should().NotContain(e => e.Contains(@"\Beta\"));
        ChannelState.Load(_state);
    }

    [Fact]
    public void Build_Force_RebuildsEvenWhenCompositionUnchanged()
    {
        MakeShow("Alpha", 6);
        ChannelBuilder.Build(Options());

        var result = ChannelBuilder.Build(Options() with { Force = true });

        result.Rebuilt.Should().BeTrue();
    }

    [Fact]
    public void Build_ShowOrder_PutsOrderedShowFirst()
    {
        MakeShow("Alpha", 6);
        MakeShow("Beta", 6);

        ChannelBuilder.Build(Options() with { ShowOrder = new[] { "Beta" } });

        PlaylistEntries()[0].Should().EndWith(@"Beta\01.mkv");
    }
}
