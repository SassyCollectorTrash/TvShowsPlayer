using System.Text.RegularExpressions;

namespace TvShowsPlayer.Core;

/// <summary>
/// Порядок серий внутри сериала по относительным путям файлов.
/// Порт логики из generate_playlist.py.
///
/// Сезон берётся из папки-сезона, если она есть (она авторитетнее — переживает
/// опечатки в SxxExx вроде S21E07 в папке «Сезон 2»); иначе из SxxExx/NxNN.
/// Серия — номер из SxxExx/NxNN, иначе «естественный» ключ по имени файла
/// (имя файла, а не весь путь — чтобы текстовые названия саг не задавали порядок,
/// как в «Драконьем жемчуге», где номер серии лежит в имени файла).
/// </summary>
public static class EpisodeOrdering
{
    // S01E01, s1.e2, S1 E1 …
    private static readonly Regex Se =
        new(@"[Ss](\d{1,3})[ ._\-]*[Ee](\d{1,4})", RegexOptions.Compiled);

    // 1x01, 2х05 (латинская x и кириллическая х). Сезон ≤ 2 цифр, чтобы не цеплять
    // разрешения вида 720x480.
    private static readonly Regex NxNn =
        new(@"(?<!\d)(\d{1,2})[xXхХ](\d{1,3})(?!\d)", RegexOptions.Compiled);

    // Папка-сезон: «N_season», «N sezon», «Season N», «Сезон N» (в любом порядке).
    private static readonly Regex SeasonDir = new(
        @"(?:(\d{1,3})\s*[_\-. ]*(?:season|sezon|сезон|сезона)|(?:season|sezon|сезон|сезона)\s*[_\-. ]*(\d{1,3}))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Возвращает пути, отсортированные в порядке показа серий.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> relativePaths)
    {
        return relativePaths.OrderBy(KeyFor).ToList();
    }

    private static EpisodeSortKey KeyFor(string rel)
    {
        var path = rel.Replace('/', '\\');
        var baseName = BaseName(path);
        var dirRel = DirName(path);
        var folderSeason = DetectSeason(dirRel);

        var m = Se.Match(baseName);
        if (!m.Success)
            m = NxNn.Match(baseName);

        if (m.Success)
        {
            var season = folderSeason ?? int.Parse(m.Groups[1].Value);
            var episode = int.Parse(m.Groups[2].Value);
            return new EpisodeSortKey(season, kind: 0, episode, baseKey: null, NaturalKey.Parse(path));
        }

        return new EpisodeSortKey(folderSeason ?? 0, kind: 1, episode: 0,
            NaturalKey.Parse(baseName), NaturalKey.Parse(path));
    }

    /// <summary>Номер сезона из имени папки (относительно папки сериала), иначе null.</summary>
    public static int? DetectSeason(string dirRel)
    {
        if (string.IsNullOrEmpty(dirRel))
            return null;

        var m = SeasonDir.Match(dirRel);
        if (!m.Success)
            return null;

        var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return int.Parse(g);
    }

    /// <summary>Какой схемой распознан порядок (для dry-run). Порт detection_label().</summary>
    public static string DetectionLabel(string rel)
    {
        var path = rel.Replace('/', '\\');
        var baseName = BaseName(path);

        if (Se.IsMatch(baseName))
            return "SxxExx";
        if (NxNn.IsMatch(baseName))
            return "NxNN";
        if (DetectSeason(DirName(path)) is not null)
            return "папка-сезон + № в имени";

        return "№ в имени файла";
    }

    /// <summary>Файлы с сезоном в имени (SxxExx), не совпавшим с папкой-сезоном. Порт find_season_anomalies().</summary>
    public static IReadOnlyList<SeasonAnomaly> FindSeasonAnomalies(IEnumerable<string> relativePaths)
    {
        var result = new List<SeasonAnomaly>();

        foreach (var rel in relativePaths)
        {
            var path = rel.Replace('/', '\\');
            var baseName = BaseName(path);
            var folderSeason = DetectSeason(DirName(path));
            var m = Se.Match(baseName);
            if (m.Success && folderSeason is int fs && int.Parse(m.Groups[1].Value) != fs)
                result.Add(new SeasonAnomaly(baseName, int.Parse(m.Groups[1].Value), fs));
        }

        return result;
    }

    private static string BaseName(string path)
    {
        var i = path.LastIndexOf('\\');
        return i < 0 ? path : path[(i + 1)..];
    }

    private static string DirName(string path)
    {
        var i = path.LastIndexOf('\\');
        return i < 0 ? string.Empty : path[..i];
    }
}

/// <summary>Подозрительный файл: сезон в имени (SxxExx) не совпал с папкой-сезоном.</summary>
public sealed record SeasonAnomaly(string FileName, int SeasonInName, int SeasonFolder);

/// <summary>«Естественный» ключ: числа сравниваются как числа, текст — как текст.</summary>
internal sealed class NaturalKey : IComparable<NaturalKey>
{
    private static readonly Regex Digits = new(@"(\d+)", RegexOptions.Compiled);

    private readonly object[] _segments;   // long | string, чередуются как в Python re.split

    private NaturalKey(object[] segments) => _segments = segments;

    public static NaturalKey Parse(string s)
    {
        var parts = Digits.Split(s ?? string.Empty);
        var segs = new object[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            segs[i] = p.Length > 0 && IsAllDigits(p) ? long.Parse(p) : p.ToLowerInvariant();
        }

        return new NaturalKey(segs);
    }

    public int CompareTo(NaturalKey? other)
    {
        if (other is null)
            return 1;

        var n = Math.Min(_segments.Length, other._segments.Length);
        for (var i = 0; i < n; i++)
        {
            var a = _segments[i];
            var b = other._segments[i];
            int c;
            if (a is long la && b is long lb)
                c = la.CompareTo(lb);
            else if (a is string sa && b is string sb)
                c = string.CompareOrdinal(sa, sb);
            else
                c = a is long ? -1 : 1;   // числа раньше текста (в норме типы совпадают позиционно)

            if (c != 0)
                return c;
        }

        return _segments.Length.CompareTo(other._segments.Length);
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var ch in s)
        {
            if (!char.IsDigit(ch))
                return false;
        }

        return true;
    }
}

/// <summary>Ключ сортировки серии: (сезон, вид, серия|натуральный-ключ-имени, натуральный-ключ-пути).</summary>
internal sealed class EpisodeSortKey : IComparable<EpisodeSortKey>
{
    private readonly int _season;
    private readonly int _kind;        // 0 = явный номер (SxxExx/NxNN), 1 = натуральный
    private readonly int _episode;     // при _kind == 0
    private readonly NaturalKey? _baseKey;  // при _kind == 1
    private readonly NaturalKey _relKey;

    public EpisodeSortKey(int season, int kind, int episode, NaturalKey? baseKey, NaturalKey relKey)
    {
        _season = season;
        _kind = kind;
        _episode = episode;
        _baseKey = baseKey;
        _relKey = relKey;
    }

    public int CompareTo(EpisodeSortKey? other)
    {
        if (other is null)
            return 1;

        var c = _season.CompareTo(other._season);
        if (c != 0)
            return c;

        c = _kind.CompareTo(other._kind);
        if (c != 0)
            return c;

        if (_kind == 0)
            c = _episode.CompareTo(other._episode);
        else
            c = _baseKey!.CompareTo(other._baseKey!);

        if (c != 0)
            return c;

        return _relKey.CompareTo(other._relKey);
    }
}
