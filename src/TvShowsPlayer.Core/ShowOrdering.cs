namespace TvShowsPlayer.Core;

/// <summary>
/// Переупорядочивание сериалов под желаемый порядок карусели. Имена из
/// <paramref name="order"/> идут первыми (в этом порядке), остальные — следом в
/// исходном порядке скана. Имена в order, которых нет среди сериалов, игнорируются.
/// Пустой order → без изменений (алфавитный порядок скана).
/// </summary>
public static class ShowOrdering
{
    public static IReadOnlyList<Show> Apply(IReadOnlyList<Show> shows, IReadOnlyList<string> order)
    {
        if (order.Count == 0)
            return shows;

        var byName = new Dictionary<string, Show>(StringComparer.OrdinalIgnoreCase);
        foreach (var show in shows)
            byName[show.Name] = show;

        var result = new List<Show>(shows.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in order)
        {
            if (byName.TryGetValue(name, out var show) && used.Add(show.Name))
                result.Add(show);
        }

        foreach (var show in shows)
        {
            if (!used.Contains(show.Name))
                result.Add(show);
        }

        return result;
    }
}
