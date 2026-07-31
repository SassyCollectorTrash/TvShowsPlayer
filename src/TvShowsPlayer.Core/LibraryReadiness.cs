namespace TvShowsPlayer.Core;

/// <summary>
/// Пишется ли файл прямо сейчас. Это НЕ признак «серия скачана целиком»: торрент
/// обычно резервирует полный размер заранее, куски приходят вразнобой, а вставшая
/// раздача выглядит как давно не менявшийся файл. Поэтому список сериалов обновляется
/// по команде пользователя, а эта проверка — лишь подстраховка от очевидного:
/// файла-«времянки» и файла, в который идёт запись в эту минуту.
/// </summary>
public static class LibraryReadiness
{
    /// <summary>Сколько файл должен «полежать» без изменений, чтобы считаться готовым.</summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMinutes(10);

    // Расширения-маркеры недокачанных файлов (qBittorrent, uTorrent, Chrome, aria2…).
    private static readonly string[] InProgressExtensions =
    {
        ".part", ".!qb", ".!ut", ".crdownload", ".aria2", ".downloading", ".tmp", ".partial",
    };

    public static bool IsSettled(string path, DateTime lastWriteUtc, DateTime nowUtc, TimeSpan quietPeriod)
    {
        if (HasInProgressMarker(path))
            return false;

        if (quietPeriod <= TimeSpan.Zero)
            return true;   // «не ждать» — состав целиком на совести пользователя

        var age = nowUtc - lastWriteUtc;

        // Отрицательный возраст = время файла в будущем (кривые часы/архив):
        // считаем незрелым, чтобы не пустить в эфир недокачанное.
        return age >= quietPeriod;
    }

    private static bool HasInProgressMarker(string path)
    {
        var name = Path.GetFileName(path);

        foreach (var ext in InProgressExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
