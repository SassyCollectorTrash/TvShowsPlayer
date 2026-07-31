namespace TvShowsPlayer.Core;

/// <summary>Действие, вызываемое глобальным хоткеем.</summary>
public enum HotkeyAction
{
    Pause,
    VolumeUp,
    VolumeDown,
    NextEpisode,
    ToggleMute,
    Resync,
    ShowNow,
}

/// <summary>Режим набора хоткеев.</summary>
public enum HotkeyMode
{
    /// <summary>Боевые комбо: Ctrl+Alt+клавиша.</summary>
    Production,

    /// <summary>Dev: те же + Shift — не конфликтуют с хоткеями живого AHK-канала.</summary>
    Dev,
}

/// <summary>Модификаторы хоткея. Значения совпадают с Win32 <c>MOD_*</c>,
/// поэтому передаются в <c>RegisterHotKey</c> напрямую.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,      // MOD_ALT
    Control = 0x0002,  // MOD_CONTROL
    Shift = 0x0004,    // MOD_SHIFT
    Win = 0x0008,      // MOD_WIN
}

/// <summary>Одна привязка глобального хоткея.</summary>
public sealed record HotkeyBinding(int Id, HotkeyModifiers Modifiers, uint VirtualKey, HotkeyAction Action);

/// <summary>
/// Набор глобальных хоткеев (паритет с <c>launch_channel.ahk</c>). Чистая таблица —
/// регистрацию и приём <c>WM_HOTKEY</c> делает App (Win32-граница).
/// </summary>
public static class Hotkeys
{
    // VK-коды (WinUser.h)
    private const uint VkSpace = 0x20;
    private const uint VkRight = 0x27;
    private const uint VkOemPlus = 0xBB;    // '='
    private const uint VkOemMinus = 0xBD;   // '-'
    private const uint VkM = 0x4D;
    private const uint VkR = 0x52;
    private const uint VkN = 0x4E;

    /// <summary>Набор модификаторов, который пользователь выбирает в настройках —
    /// на случай конфликта комбинаций с другой программой.</summary>
    public static readonly IReadOnlyList<string> ModifierChoices = new[]
    {
        "Ctrl+Alt", "Ctrl+Shift", "Ctrl+Shift+Alt", "Alt+Shift", "Win+Alt",
    };

    /// <summary>Разобрать выбранный набор («Ctrl+Alt»); неизвестное — Ctrl+Alt.</summary>
    public static HotkeyModifiers ParseModifiers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return HotkeyModifiers.Control | HotkeyModifiers.Alt;

        var mods = HotkeyModifiers.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            mods |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
        }

        return mods == HotkeyModifiers.None ? HotkeyModifiers.Control | HotkeyModifiers.Alt : mods;
    }

    /// <summary>Привязки с пользовательским набором модификаторов (dev — плюс Shift,
    /// чтобы не спорить с боевым каналом).</summary>
    /// <summary>Человеческое имя клавиши действия — чтобы сказать, что именно занято.</summary>
    public static string KeyName(HotkeyAction action) => action switch
    {
        HotkeyAction.Pause => "Пробел",
        HotkeyAction.VolumeUp => "=",
        HotkeyAction.VolumeDown => "−",
        HotkeyAction.NextEpisode => "стрелка вправо",
        HotkeyAction.ToggleMute => "M (без звука)",
        HotkeyAction.Resync => "R (починить звук)",
        HotkeyAction.ShowNow => "N (что идёт сейчас)",
        _ => action.ToString(),
    };

    public static IReadOnlyList<HotkeyBinding> ForMode(HotkeyMode mode, string? modifiers)
    {
        var mods = ParseModifiers(modifiers);
        if (mode == HotkeyMode.Dev)
            mods |= HotkeyModifiers.Shift;

        return Build(mods);
    }

    public static IReadOnlyList<HotkeyBinding> ForMode(HotkeyMode mode)
    {
        var mods = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        if (mode == HotkeyMode.Dev)
            mods |= HotkeyModifiers.Shift;

        return Build(mods);
    }

    private static IReadOnlyList<HotkeyBinding> Build(HotkeyModifiers mods)
    {
        return new[]
        {
            new HotkeyBinding(1, mods, VkSpace, HotkeyAction.Pause),
            new HotkeyBinding(2, mods, VkOemPlus, HotkeyAction.VolumeUp),
            new HotkeyBinding(3, mods, VkOemMinus, HotkeyAction.VolumeDown),
            new HotkeyBinding(4, mods, VkRight, HotkeyAction.NextEpisode),
            new HotkeyBinding(5, mods, VkM, HotkeyAction.ToggleMute),
            new HotkeyBinding(6, mods, VkR, HotkeyAction.Resync),
            new HotkeyBinding(7, mods, VkN, HotkeyAction.ShowNow),
        };
    }
}
