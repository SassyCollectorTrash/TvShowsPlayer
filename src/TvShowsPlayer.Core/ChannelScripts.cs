namespace TvShowsPlayer.Core;

/// <summary>Режим запуска канала: dev-изоляция или боевой.</summary>
public enum ChannelMode
{
    /// <summary>Разработка: OSD есть, но БЕЗ resume.lua — не трогаем закладку живого канала.</summary>
    Dev,

    /// <summary>Боевой канал: OSD + резюме позиции.</summary>
    Production,
}

/// <summary>
/// Какие Lua-скрипты класть в config-dir mpv для данного режима. Правило
/// изоляции закладки вынесено сюда явно: dev-набор НИКОГДА не содержит
/// <c>resume.lua</c>, поэтому dev-mpv не пишет в общий
/// <c>localtv-channel-state.json</c> и не портит позицию живого канала.
/// </summary>
public static class ChannelScripts
{
    public const string ChannelOsd = "channel-osd.lua";
    public const string Resume = "resume.lua";

    public static IReadOnlyList<string> ForMode(ChannelMode mode) => mode switch
    {
        ChannelMode.Dev => new[] { ChannelOsd },
        ChannelMode.Production => new[] { ChannelOsd, Resume },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
