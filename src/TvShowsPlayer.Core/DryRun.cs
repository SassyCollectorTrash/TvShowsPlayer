namespace TvShowsPlayer.Core;

/// <summary>Отчёт по одному сериалу для проверки распознавания серий.</summary>
public sealed record ShowReport
{
    public required string Name { get; init; }
    public required int EpisodeCount { get; init; }
    public required string DetectionLabel { get; init; }
    public required IReadOnlyList<string> FirstEpisodes { get; init; }
    public required IReadOnlyList<SeasonAnomaly> Anomalies { get; init; }

    /// <summary>Имя последней серии — если их больше, чем в <see cref="FirstEpisodes"/>
    /// (превью показывает начало и конец диапазона). Иначе null.</summary>
    public string? LastEpisode { get; init; }
}

/// <summary>
/// Проверка распознанного порядка без изменения эфира: по каждому сериалу —
/// число серий, схема распознавания, первые серии и аномалии сезонов.

/// </summary>
public static class DryRun
{
    public static IReadOnlyList<ShowReport> Build(string root, IReadOnlyList<Show> shows, int head = 6)
    {
        var reports = new List<ShowReport>();

        foreach (var show in shows)
        {
            var showDir = Path.Combine(root, show.Name);
            var rels = show.Episodes.Select(e => Path.GetRelativePath(showDir, e)).ToList();
            var label = rels.Count > 0 ? EpisodeOrdering.DetectionLabel(rels[0]) : "—";
            var firstEpisodes = show.Episodes.Take(head).Select(e => Path.GetFileName(e) ?? e).ToList();
            var last = show.Episodes.Count > head
                ? Path.GetFileName(show.Episodes[^1])
                : null;

            reports.Add(new ShowReport
            {
                Name = show.Name,
                EpisodeCount = show.Episodes.Count,
                DetectionLabel = label,
                FirstEpisodes = firstEpisodes,
                Anomalies = EpisodeOrdering.FindSeasonAnomalies(rels),
                LastEpisode = last,
            });
        }

        return reports;
    }
}
