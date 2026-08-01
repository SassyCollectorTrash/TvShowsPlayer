namespace TvShowsPlayer.Core;

/// <summary>Сериал: имя и упорядоченный список путей к сериям.</summary>
public sealed class Show
{
    public string Name { get; }
    public IReadOnlyList<string> Episodes { get; }

    public Show(string name, IReadOnlyList<string> episodes)
    {
        Name = name;
        Episodes = episodes;
    }
}

/// <summary>Результат сборки карусели.</summary>
public sealed class CarouselResult
{
    public required IReadOnlyList<string> Playlist { get; init; }
    public int Rotations { get; init; }
    public bool Capped { get; init; }
}

/// <summary>
/// Карусель с нахлёстом (round-robin со скользящим окном).
/// Каждый сериал по очереди отдаёт блок из window серий,
/// затем закладка сдвигается на step; нахлёст = window − step.
/// </summary>
public static class Carousel
{
    public static CarouselResult Build(
        IReadOnlyList<Show> shows, int window = 4, int step = 2, int capRotations = 200)
        => Build(shows, startCursors: null, window, step, capRotations);

    /// <summary>
    /// Сборка с засеянными стартовыми курсорами (каждый сериал стартует со своей
    /// серии — см. <see cref="CarouselSeeding"/>). Период завершается по возврату
    /// курсоров к СТАРТОВОМУ значению (для коротких сериалов — 0), поэтому
    /// loop-playlist бесшовен при любом засеве, а длина периода от засева не зависит.
    /// <paramref name="startCursors"/> = <c>null</c> → все нули (обычная сборка).
    /// </summary>
    public static CarouselResult Build(
        IReadOnlyList<Show> shows, IReadOnlyList<int>? startCursors,
        int window = 4, int step = 2, int capRotations = 200)
    {
        var cursors = new int[shows.Count];
        var starts = new int[shows.Count];
        for (var i = 0; i < shows.Count; i++)
        {
            // короткие сериалы всегда играют целиком и заканчивают на 0 → старт 0
            var seed = startCursors is not null && i < startCursors.Count && shows[i].Episodes.Count > window
                ? startCursors[i]
                : 0;
            cursors[i] = seed;
            starts[i] = seed;
        }

        var playlist = new List<string>();
        var rotation = 0;
        var capped = false;

        while (true)
        {
            for (var i = 0; i < shows.Count; i++)
            {
                var eps = shows[i].Episodes;
                var n = eps.Count;
                var c = cursors[i];

                if (n <= window)
                {
                    // короткий сериал — целиком по порядку
                    for (var k = 0; k < n; k++)
                        playlist.Add(eps[k]);
                    cursors[i] = 0;
                }
                else
                {
                    for (var k = 0; k < window; k++)
                        playlist.Add(eps[(c + k) % n]);
                    cursors[i] = (c + step) % n;
                }
            }

            rotation++;
            if (ReturnedToStart(cursors, starts))
                break;
            if (rotation >= capRotations)
            {
                capped = true;
                break;
            }
        }

        return new CarouselResult { Playlist = playlist, Rotations = rotation, Capped = capped };
    }

    private static bool ReturnedToStart(int[] cursors, int[] starts)
    {
        for (var i = 0; i < cursors.Length; i++)
        {
            if (cursors[i] != starts[i])
                return false;
        }

        return true;
    }
}
