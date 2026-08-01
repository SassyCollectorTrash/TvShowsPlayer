namespace TvShowsPlayer.Core;

/// <summary>
/// Единственный источник правды о том, ГДЕ лежат настройки и состояние канала.
/// Резолв вынесен из интерфейса в ядро, потому что путь к состоянию должны одинаково
/// понимать три стороны: запуск канала, кнопка «Применить к эфиру» и <c>resume.lua</c>.
/// Разъезд этих правил = потерянный прогресс просмотра, поэтому логика здесь и
/// покрыта тестами.
/// </summary>
public static class ChannelPaths
{
    /// <summary>Рабочая папка канала: <c>%LOCALAPPDATA%\LocalTV</c>.</summary>
    public static string ResolveConfigDir(string localAppDataDir) =>
        Path.Combine(localAppDataDir, Branding.AppName);

    /// <summary>Файл состояния внутри рабочей папки.</summary>
    public static string ResolveStatePath(string configDir) =>
        Path.Combine(configDir, Branding.StateFileName);
}
