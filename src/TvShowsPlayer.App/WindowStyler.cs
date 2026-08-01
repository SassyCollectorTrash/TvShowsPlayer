using System.Runtime.InteropServices;

namespace TvShowsPlayer.App;

/// <summary>
/// Убирает окно mpv из панели задач и Alt-Tab (канал-«киоск»):
/// +WS_EX_TOOLWINDOW, −WS_EX_APPWINDOW, затем Hide/Show, чтобы стиль применился.
/// </summary>
internal static class WindowStyler
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    /// <summary>
    /// Проверить, спрятано ли окно, и вернуть его в это состояние при необходимости.
    /// mpv пересоздаёт своё окно при переинициализации вывода (например, когда у
    /// следующей серии другие параметры видео) — новое окно приходит без нашего
    /// стиля, и значок снова появляется в панели задач.
    /// Возвращает true, если пришлось исправлять.
    /// </summary>
    public static bool EnsureHiddenFromTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var hidden = (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0;
        if (hidden)
            return false;

        HideFromTaskbar(hwnd);

        return true;
    }

    public static void HideFromTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        ShowWindow(hwnd, SW_HIDE);
        ShowWindow(hwnd, SW_SHOW);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
