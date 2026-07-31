using System.Runtime.InteropServices;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Регистрирует глобальные хоткеи Windows на заданное окно (HWND) и раздаёт
/// <c>WM_HOTKEY</c> в действия. Win32-граница: юнит-тестами не покрыта (таблица
/// привязок тестируется в Core), проверяется запуском. Занятые системой/другим
/// приложением комбо тихо пропускаются, а не роняют приложение.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly Action<HotkeyAction> _onPressed;
    private readonly List<int> _registered = new();

    public GlobalHotkeys(IntPtr hwnd, IReadOnlyList<HotkeyBinding> bindings, Action<HotkeyAction> onPressed)
    {
        _hwnd = hwnd;
        _bindings = bindings;
        _onPressed = onPressed;
    }

    /// <summary>Число успешно зарегистрированных хоткеев (для диагностики).</summary>
    public int RegisteredCount => _registered.Count;

    /// <summary>Комбинации, которые не удалось занять (их держит другая программа).</summary>
    public IReadOnlyList<HotkeyAction> Failed { get; private set; } = Array.Empty<HotkeyAction>();

    public void Register()
    {
        var failed = new List<HotkeyAction>();

        foreach (var b in _bindings)
        {
            if (RegisterHotKey(_hwnd, b.Id, (uint)b.Modifiers, b.VirtualKey))
                _registered.Add(b.Id);
            else
                failed.Add(b.Action);   // комбинацию уже занял кто-то другой
        }

        Failed = failed;
    }

    /// <summary>Обработать оконное сообщение; на <c>WM_HOTKEY</c> дёрнуть действие.</summary>
    public void HandleMessage(uint msg, IntPtr wParam)
    {
        if (msg != WmHotkey)
            return;

        var id = wParam.ToInt32();
        foreach (var b in _bindings)
        {
            if (b.Id == id)
            {
                _onPressed(b.Action);
                return;
            }
        }
    }

    public void Dispose()
    {
        foreach (var id in _registered)
            UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
