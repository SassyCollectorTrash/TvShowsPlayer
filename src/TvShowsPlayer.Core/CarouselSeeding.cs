namespace TvShowsPlayer.Core;

/// <summary>
/// Стартовые курсоры карусели из сохранённого прогресса: каждый сериал продолжается
/// с сохранённой серии (по идентичности имени файла, а не по индексу — переживает
/// вставку серии в середину). Новый/неизвестный сериал → курсор 0. Зеркало
/// Новый или незнакомый сериал начинается сначала.
/// </summary>
public static class CarouselSeeding
{
    /// <param name="root">Корень библиотеки (для вычисления rel серий).</param>
    /// <param name="progress">Карта «имя сериала → rel последней проигранной серии».</param>
    public static IReadOnlyList<int> StartCursors(
        string root, IReadOnlyList<Show> shows, IReadOnlyDictionary<string, string> progress)
    {
        var cursors = new int[shows.Count];

        for (var i = 0; i < shows.Count; i++)
        {
            var show = shows[i];
            if (!progress.TryGetValue(show.Name, out var rel) || string.IsNullOrEmpty(rel))
                continue;

            var target = PathIdentity.Normalize(rel);
            var showDir = Path.Combine(root, show.Name);
            for (var j = 0; j < show.Episodes.Count; j++)
            {
                if (PathIdentity.Normalize(Path.GetRelativePath(showDir, show.Episodes[j])) == target)
                {
                    cursors[i] = j;
                    break;
                }
            }
        }

        return cursors;
    }
}
