namespace TvShowsPlayer.Core;

/// <summary>
/// Разовый перенос данных со старого имени бренда (Jetix) на новое (LocalTV):
/// папка <c>%LOCALAPPDATA%\Jetix</c> → <c>LocalTV</c> и файл состояния с прогрессом
/// просмотра. Только перемещение (не удаление) и только когда цель отсутствует —
/// поэтому прогресс не теряется, а повторный запуск безопасен (идемпотентно).
/// </summary>
public static class LegacyConfigMigration
{
    /// <summary>Возвращает <c>true</c>, если что-то перенесли.</summary>
    public static bool Run(string localAppDataDir)
    {
        if (string.IsNullOrWhiteSpace(localAppDataDir))
            return false;

        var legacyDir = Path.Combine(localAppDataDir, Branding.LegacyAppName);
        var newDir = Path.Combine(localAppDataDir, Branding.AppName);
        var migrated = false;

        // Best-effort: неудача переноса (напр. занятый файл) НЕ должна ронять старт —
        // приложение подхватит старую папку/имя, а перенос повторится в следующий раз.

        // 1. Папка целиком (со всеми настройками, прогрессом, mpv.conf, логами).
        try
        {
            if (Directory.Exists(legacyDir) && !Directory.Exists(newDir))
            {
                Directory.Move(legacyDir, newDir);
                migrated = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        // 2. Файл состояния — к каноничному имени (внутри уже новой папки).
        migrated |= RenameStateFile(newDir);

        return migrated;
    }

    /// <summary>
    /// Привести файл состояния в указанной папке к каноничному имени. Вызывается для
    /// ФАКТИЧЕСКИ используемой config-dir: <c>resume.lua</c> всегда пишет каноничное
    /// имя, поэтому при откате на старую папку файл обязан переименоваться и там —
    /// иначе mpv не увидит прогресс и затрёт его пустым.
    /// </summary>
    public static bool RenameStateFile(string configDir)
    {
        try
        {
            if (!Directory.Exists(configDir))
                return false;

            var legacyState = Path.Combine(configDir, Branding.LegacyStateFileName);
            var newState = Path.Combine(configDir, Branding.StateFileName);
            if (File.Exists(legacyState) && !File.Exists(newState))
            {
                File.Move(legacyState, newState);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return false;
    }
}
