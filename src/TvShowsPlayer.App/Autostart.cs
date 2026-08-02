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
            key.SetValue(ValueName, Command(exe));
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Починить запись автозапуска, если она указывает в никуда. Windows запоминает
    /// путь: стоит перенести или переименовать папку — и канал перестаёт включаться
    /// сам, а галочка в настройках при этом стоит. Человеку такое не отладить.
    ///
    /// Правим ТОЛЬКО битую запись. Если файл по записанному пути на месте — это может
    /// быть другая копия программы, которую человек и хотел запускать; перетягивать
    /// автозапуск на себя только потому, что запустили нас, нельзя.
    /// Возвращает true, если путь пришлось поправить.
    /// </summary>
    public static bool Repair()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string stored)
            return false;   // автозапуск не включён — чинить нечего

        var target = ExecutableFrom(stored);
        if (target.Length == 0 || File.Exists(target))
            return false;

        key.SetValue(ValueName, Command(exe));

        return true;
    }

    /// <summary>Путь к программе из строки автозапуска (она записана в кавычках).</summary>
    private static string ExecutableFrom(string command)
    {
        var value = command.Trim();
        if (!value.StartsWith('"'))
            return value;

        var end = value.IndexOf('"', 1);

        return end > 1 ? value[1..end] : string.Empty;
    }

    // Кавычки обязательны: путь почти всегда с пробелами.
    private static string Command(string exe) => $"\"{exe}\"";
}
