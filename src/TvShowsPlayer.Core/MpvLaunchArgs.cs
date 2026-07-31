namespace TvShowsPlayer.Core;

/// <summary>Параметры запуска mpv (для надзора из приложения).</summary>
public sealed record MpvLaunchOptions
{
    /// <summary>config-dir mpv — откуда грузятся mpv.conf и scripts\.</summary>
    public required string ConfigDir { get; init; }

    /// <summary>Плейлист (.m3u), идёт последним аргументом.</summary>
    public required string Playlist { get; init; }

    /// <summary>Имя именованного канала IPC (input-ipc-server).</summary>
    public required string PipePath { get; init; }

    /// <summary>Корень библиотеки для channel-osd.lua; null — не передавать.</summary>
    public string? ChannelOsdRoot { get; init; }

    /// <summary>Имя канала для заставки channel-osd.lua; null — оставить дефолтное.</summary>
    public string? ChannelName { get; init; }

    /// <summary>true — fullscreen из mpv.conf; false — принудительно оконный.</summary>
    public bool Fullscreen { get; init; } = true;
}

/// <summary>
/// Строит аргументы командной строки mpv из <see cref="MpvLaunchOptions"/>.
/// Чистая функция (без exe-пути и без квотинга — квотит ProcessStartInfo.ArgumentList).
/// </summary>
public static class MpvLaunchArgs
{
    private const string ConfigDirFlag = "--config-dir=";
    private const string IpcServerFlag = "--input-ipc-server=";
    private const string ScriptOptsFlag = "--script-opts=";
    private const string NoFullscreenFlag = "--fullscreen=no";

    public static IReadOnlyList<string> Build(MpvLaunchOptions options)
    {
        var args = new List<string>
        {
            ConfigDirFlag + options.ConfigDir,
            IpcServerFlag + options.PipePath,
        };

        // Все опции скриптов — одним --script-opts (пары через запятую), поэтому
        // запятые из значений убираем: иначе mpv разберёт хвост как отдельную опцию.
        var scriptOpts = new List<string>();
        if (!string.IsNullOrEmpty(options.ChannelOsdRoot))
            scriptOpts.Add("channelosd-root=" + options.ChannelOsdRoot.Replace(",", string.Empty));
        if (!string.IsNullOrEmpty(options.ChannelName))
            scriptOpts.Add("channelosd-name=" + options.ChannelName.Replace(",", string.Empty));

        if (scriptOpts.Count > 0)
            args.Add(ScriptOptsFlag + string.Join(",", scriptOpts));

        if (!options.Fullscreen)
            args.Add(NoFullscreenFlag);

        args.Add(options.Playlist);

        return args;
    }
}
