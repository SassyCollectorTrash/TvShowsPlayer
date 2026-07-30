namespace TvShowsPlayer.Core;

/// <summary>
/// Скан корня с сериалами: каждая подпапка — сериал, внутри рекурсивно ищутся
/// видеофайлы и упорядочиваются через <see cref="EpisodeOrdering"/>. Папки без
/// видео пропускаются, сериалы — в натуральном порядке имён. Порт
/// scan_shows()/gather_show_episodes() из generate_playlist.py.
/// </summary>
public static class ShowScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".webm", ".ts",
        ".flv", ".wmv", ".mpg", ".mpeg",
    };

    /// <summary>
    /// Скан корня: подпапка = сериал, внутри рекурсивно видеофайлы по порядку.
    /// <paramref name="excluded"/> — имена сериалов вне карусели (качаются/неполные;
    /// без регистра, с тримом). Зеркало Python <c>scan_shows(root, exclude)</c>.
    /// </summary>
    public static IReadOnlyList<Show> Scan(string root, IReadOnlyCollection<string>? excluded = null)
    {
        // Библиотека не указана / папка не существует (свежая установка) → пусто, не падаем.
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<Show>();

        var excludeSet = BuildExcludeSet(excluded);
        var shows = new List<Show>();

        foreach (var showDir in Directory.EnumerateDirectories(root).OrderBy(NameKey))
        {
            var name = Path.GetFileName(showDir);
            if (excludeSet.Contains(name))
                continue;   // временно вне канала

            var episodes = GatherEpisodes(showDir);
            if (episodes.Count > 0)
                shows.Add(new Show(name, episodes));
        }

        return shows;
    }

    private static HashSet<string> BuildExcludeSet(IReadOnlyCollection<string>? excluded)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excluded is not null)
        {
            foreach (var name in excluded)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name.Trim());
            }
        }

        return set;
    }

    /// <summary>Рекурсивно собрать видеофайлы сериала и упорядочить их по показу.</summary>
    private static IReadOnlyList<string> GatherEpisodes(string showDir)
    {
        var files = Directory
            .EnumerateFiles(showDir, "*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        // Порядок считаем по путям ОТНОСИТЕЛЬНО папки сериала (как в Python): так
        // имя самого сериала не влияет на распознавание сезона/номера, а на выходе
        // возвращаем восстановленные полные пути.
        var rels = files.Select(f => Path.GetRelativePath(showDir, f));
        var ordered = EpisodeOrdering.Order(rels);

        return ordered.Select(rel => Path.Combine(showDir, rel)).ToList();
    }

    private static NaturalKey NameKey(string dir)
    {
        return NaturalKey.Parse(Path.GetFileName(dir));
    }
}
