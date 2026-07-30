namespace TvShowsPlayer.Core;

/// <summary>Параметры сборки канала.</summary>
public sealed record ChannelBuildOptions
{
    public required string Root { get; init; }
    public required string PlaylistPath { get; init; }
    public required string StatePath { get; init; }
    public IReadOnlyList<string> ExcludedShows { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ShowOrder { get; init; } = Array.Empty<string>();
    public int Window { get; init; } = 4;
    public int Step { get; init; } = 2;
    public int CapRotations { get; init; } = 200;

    /// <summary>Пересобрать даже если состав не менялся (кнопка «Пересобрать»).</summary>
    public bool Force { get; init; }
}

/// <summary>Итог сборки.</summary>
public sealed record ChannelBuildResult
{
    /// <summary>false — состав не менялся, пересборки не было (state не тронут).</summary>
    public bool Rebuilt { get; init; }
    public int ShowCount { get; init; }
    public int PlaylistLength { get; init; }
    public bool Capped { get; init; }
}

/// <summary>
/// Оркестрация сборки канала — зеркало <c>main()</c> generate_playlist.py:
/// скан (с исключениями) → сигнатура состава → если совпала, ничего не трогаем
/// (бережём закладку); иначе засев курсоров из сохранённого прогресса → карусель →
/// запись .m3u/.sig → выставить <c>playlist_pos</c> на серию, что шла (иначе 0).
/// </summary>
public static class ChannelBuilder
{
    public static ChannelBuildResult Build(ChannelBuildOptions options)
    {
        var shows = ShowOrdering.Apply(
            ShowScanner.Scan(options.Root, options.ExcludedShows), options.ShowOrder);
        var signature = CompositionSignature.Compute(shows);

        if (!options.Force && PlaylistWriter.IsUpToDate(options.PlaylistPath, signature))
            return new ChannelBuildResult { Rebuilt = false, ShowCount = shows.Count };

        var state = ChannelState.Load(options.StatePath);
        var startCursors = CarouselSeeding.StartCursors(options.Root, shows, state.Shows);
        var carousel = Carousel.Build(
            shows, startCursors, options.Window, options.Step, options.CapRotations);

        PlaylistWriter.Write(options.PlaylistPath, carousel.Playlist, signature);

        var current = PlaylistIndex.OfCurrentEpisode(
            carousel.Playlist, options.Root, state.Shows, state.Current);
        state.PlaylistPos = current >= 0 ? current : 0;
        state.TimePos = 0;
        state.Save(options.StatePath);

        return new ChannelBuildResult
        {
            Rebuilt = true,
            ShowCount = shows.Count,
            PlaylistLength = carousel.Playlist.Count,
            Capped = carousel.Capped,
        };
    }
}
