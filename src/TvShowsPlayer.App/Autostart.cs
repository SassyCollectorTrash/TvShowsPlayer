using Microsoft.Win32;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Автозапуск канала при входе в систему — через ключ реестра
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run (значение = путь к exe).
/// Проще и надёжнее, чем ярлык в папке Startup; снимается удалением значения.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = Branding.AutostartValueName;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Set(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Разовый перенос автозапуска со старого имени значения на текущее. Трогаем ТОЛЬКО
    /// значение, указывающее на это же приложение (чужую запись с таким именем не
    /// угоняем), и удаляем старое лишь после успешной записи нового. Ошибки реестра
    /// (политики, антивирус) не должны ронять запуск канала — глушим их здесь.
    /// </summary>
    public static void MigrateLegacyName()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(Branding.LegacyAutostartValueName) is not string legacyCommand)
                return;

            var exeName = Path.GetFileName(exe);
            if (!legacyCommand.Contains(exeName, StringComparison.OrdinalIgnoreCase))
                return;   // это не наш автозапуск (напр. старый скриптовый кит) — не трогаем

            key.SetValue(ValueName, $"\"{exe}\"");
            key.DeleteValue(Branding.LegacyAutostartValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // автозапуск не критичен для работы канала — молча пропускаем
        }
    }
}
