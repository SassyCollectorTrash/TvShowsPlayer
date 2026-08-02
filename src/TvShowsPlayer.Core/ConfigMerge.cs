namespace TvShowsPlayer.Core;

/// <summary>
/// Сведение настроек при сохранении из окна. У файла настроек два хозяина: окно
/// (человек) и сама программа (запоминает громкость с пульта, уводит найденные
/// новинки в исключения). Окно держит снимок с момента открытия, поэтому запись
/// «как есть» затирала бы всё, что программа успела сделать за это время —
/// в первую очередь выпускала бы в эфир сериал, который ещё качается.
/// </summary>
public static class ConfigMerge
{
    /// <param name="toSave">Что окно собирается записать (снимок + правки человека).</param>
    /// <param name="onDisk">Что лежит в файле прямо сейчас.</param>
    /// <param name="volumeWhenOpened">Громкость на момент открытия окна — по ней
    /// видно, трогал ли человек ползунок.</param>
    /// <param name="showsInWindow">Сериалы, которые окно показывало. Про остальные
    /// человек не решал — значит и отменять решение программы по ним нельзя.</param>
    public static void KeepBackgroundChanges(
        AppConfig toSave, AppConfig onDisk, int volumeWhenOpened, IReadOnlyCollection<string> showsInWindow)
    {
        var seen = new HashSet<string>(showsInWindow, StringComparer.OrdinalIgnoreCase);

        // Сериал, найденный уже после открытия окна, в списке не показывался —
        // его исключение оставляем: программа не знает, докачан ли он.
        toSave.ExcludedShows = Combine(
            toSave.ExcludedShows,
            onDisk.ExcludedShows.Where(name => !seen.Contains(name)));

        // Список известных ведёт программа: он должен пополняться, а не откатываться.
        toSave.KnownShows = Combine(toSave.KnownShows, onDisk.KnownShows);

        // Громкость: если в окне её не трогали, побеждает та, что накрутили пультом.
        if (toSave.Volume == volumeWhenOpened)
            toSave.Volume = onDisk.Volume;
    }

    private static List<string> Combine(IEnumerable<string> first, IEnumerable<string> second)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in first.Concat(second))
        {
            if (seen.Add(name))
                result.Add(name);
        }

        return result;
    }
}
