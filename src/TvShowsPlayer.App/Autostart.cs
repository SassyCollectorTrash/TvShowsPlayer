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
    private const string ValueName = Branding.AppName;

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
    /// Разовый перенос автозапуска со старого имени значения ("JETIX") на текущее.
    /// Если старый автозапуск был включён — включаем под новым именем (на текущий exe)
    /// и удаляем старое значение. Иначе — ничего не делаем.
    /// </summary>
    public static void MigrateLegacyName()
    {
        const string legacyValueName = "JETIX";

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(legacyValueName) is null)
            return;

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
            key.SetValue(ValueName, $"\"{exe}\"");

        key.DeleteValue(legacyValueName, throwOnMissingValue: false);
    }
}
