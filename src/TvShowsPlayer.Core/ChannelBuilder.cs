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

    /// <summary>Сколько файл должен пролежать без изменений, чтобы попасть в эфир
    /// (защита от недокачанных серий). <c>null</c> — не проверять.</summary>
    public TimeSpan? SettleAfter { get; init; }

    /// <summary>
    /// Сериалы, которые программа уже видела. Всё, чего в списке нет, считается
    /// новинкой и в эфир не ставится, пока пользователь не включит: докачан сериал
    /// или нет — программе неизвестно. <c>null</c> — не отслеживать новинки.
    /// </summary>
    public IReadOnlyList<string>? KnownShows { get; init; }
}

/// <summary>Итог сборки.</summary>
public sealed record ChannelBuildResult
{
    /// <summary>false — состав не менялся, пересборки не было (state не тронут).</summary>
    public bool Rebuilt { get; init; }

    /// <summary>Библиотека не указана или недоступна — плейлист намеренно не трогали.</summary>
    public bool LibraryMissing { get; init; }

    /// <summary>Файлов пропущено как «пишется прямо сейчас» (стоит сказать пользователю).</summary>
    public int SkippedEpisodes { get; init; }

    /// <summary>Замеченные новинки — в эфир не поставлены, ждут решения пользователя.</summary>
    public IReadOnlyList<string> NewShows { get; init; } = Array.Empty<string>();

    /// <summary>Все сериалы, найденные в папке (включая выключенные) — чтобы запомнить.</summary>
    public IReadOnlyList<string> FoundShows { get; init; } = Array.Empty<string>();
    public int ShowCount { get; init; }
    public int PlaylistLength { get; init; }
    public bool Capped { get; init; }
}

/// <summary>
/// Оркестрация сборки канала:
/// скан (с исключениями) → сигнатура состава → если совпала, ничего не трогаем
/// (бережём закладку); иначе засев курсоров из сохранённого прогресса → карусель →
/// запись .m3u/.sig → выставить <c>playlist_pos</c> на серию, что шла (иначе 0).
/// </summary>
public static class ChannelBuilder
{
    public static ChannelBuildResult Build(ChannelBuildOptions options)
    {
        // Библиотека не указана или недоступна (внешний диск не подключён к моменту
        // автозапуска, папку переименовали) — НЕ трогаем существующий плейлист:
        // иначе рабочий канал заменялся бы пустым, а состав «менялся» на глазах.
        if (string.IsNullOrWhiteSpace(options.Root) || !Directory.Exists(options.Root))
            return new ChannelBuildResult { Rebuilt = false, LibraryMissing = true };

        // Сканируем БЕЗ исключений: нужно увидеть всё, что есть в папке, чтобы отличить
        // новинки от того, что пользователь выключил сам.
        var found = ShowScanner.Scan(options.Root, out var skipped, excluded: null, options.SettleAfter);
        var foundNames = found.Select(s => s.Name).ToList();

        var excluded = new HashSet<string>(options.ExcludedShows, StringComparer.OrdinalIgnoreCase);
        var newShows = FindNewShows(foundNames, options.KnownShows, excluded);

        // Новый сериал в эфир сам не идёт: программа не знает, докачан ли он. Ждёт,
        // пока пользователь включит его галочкой.
        foreach (var name in newShows)
            excluded.Add(name);

        var shows = ShowOrdering.Apply(
            found.Where(s => !excluded.Contains(s.Name)).ToList(),
            options.ShowOrder);

        // В сигнатуру входят и параметры карусели: иначе смена «окна»/«шага» в
        // настройках не пересобирала бы плейлист при перезапуске.
        var signature = CompositionSignature.Compute(
            shows, options.Window, options.Step, options.CapRotations);

        if (!options.Force && PlaylistWriter.IsUpToDate(options.PlaylistPath, signature))
        {
            return new ChannelBuildResult
            {
                Rebuilt = false,
                ShowCount = shows.Count,
                SkippedEpisodes = skipped,
                NewShows = newShows,
                FoundShows = foundNames,
            };
        }

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
            SkippedEpisodes = skipped,
            NewShows = newShows,
            FoundShows = foundNames,
        };
    }

    /// <summary>
    /// Сериалы, которых программа раньше не видела. Первый запуск (список известных
    /// пуст) — это настройка библиотеки, а не появление новинок: тогда новых нет.
    /// Выключенные пользователем тоже не новинки.
    /// </summary>
    private static IReadOnlyList<string> FindNewShows(
        IReadOnlyList<string> found, IReadOnlyList<string>? known, IReadOnlySet<string> excluded)
    {
        if (known is null || known.Count == 0)
            return Array.Empty<string>();

        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        return found.Where(name => !knownSet.Contains(name) && !excluded.Contains(name)).ToList();
    }
}
