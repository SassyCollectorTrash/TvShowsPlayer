using System.Runtime.InteropServices;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Простой журнал приложения рядом с конфигом (<c>localtv-app.log</c>) и показ
/// фатальной ошибки пользователю. Без него сбой на старте выглядел как «запустил —
/// ничего не произошло»: ни окна, ни трея, ни следа.
/// </summary>
internal static class AppLog
{
    private const int MbIconError = 0x00000010;
    private const int MbIconWarning = 0x00000030;

    private static string? _logPath;

    /// <summary>Куда писать журнал (вызывается, как только известна папка конфига).</summary>
    public static void UseDirectory(string configDir)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            _logPath = Path.Combine(configDir, "localtv-app.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logPath = null;
        }
    }

    public static void Write(string message)
    {
        if (_logPath is null)
            return;

        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // журнал не должен мешать работе канала
        }
    }

    /// <summary>Показать пользователю фатальную ошибку (без неё окно просто не появится).</summary>
    public static void ShowError(string message)
    {
        Write("ОШИБКА: " + message);
        MessageBox(IntPtr.Zero, message, $"{Branding.AppName} — ошибка", MbIconError);
    }

    /// <summary>Показать предупреждение (канал работает, но что-то важное отключено).</summary>
    public static void ShowWarning(string message)
    {
        Write("ВНИМАНИЕ: " + message);
        MessageBox(IntPtr.Zero, message, Branding.AppName, MbIconWarning);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
