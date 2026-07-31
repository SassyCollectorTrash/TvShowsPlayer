namespace TvShowsPlayer.Core;

/// <summary>
/// Единственный источник правды о том, ГДЕ лежат конфиг и состояние канала.
/// Резолв вынесен из UI в Core, потому что путь к состоянию должны одинаково
/// понимать три стороны: старт канала, кнопка «Пересобрать» и <c>resume.lua</c>
/// (он пишет каноничное имя в config-dir). Разъезд этих правил = потерянный
/// прогресс просмотра, поэтому логика здесь и покрыта тестами.
/// </summary>
public static class ChannelPaths
{
    /// <summary>
    /// Рабочая папка prod-канала. Обычно <c>%LOCALAPPDATA%\LocalTV</c>; если перенос
    /// со старого имени не удался (папка занята) — старая, чтобы не потерять прогресс.
    /// </summary>
    public static string ResolveConfigDir(string localAppDataDir)
    {
        var newDir = Path.Combine(localAppDataDir, Branding.AppName);
        var legacyDir = Path.Combine(localAppDataDir, Branding.LegacyAppName);

        return Directory.Exists(newDir) || !Directory.Exists(legacyDir) ? newDir : legacyDir;
    }

    /// <summary>
    /// Файл состояния внутри config-dir: каноничный, а если остался только файл со
    /// старым именем — он (переименование повторится позже).
    /// </summary>
    public static string ResolveStatePath(string configDir)
    {
        var canonical = Path.Combine(configDir, Branding.StateFileName);
        if (File.Exists(canonical))
            return canonical;

        var legacy = Path.Combine(configDir, Branding.LegacyStateFileName);
        return File.Exists(legacy) ? legacy : canonical;
    }
}
