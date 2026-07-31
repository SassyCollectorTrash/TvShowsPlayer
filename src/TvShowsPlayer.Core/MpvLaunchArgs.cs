using System.Globalization;

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

    /// <summary>Длительности экранной графики, сек; null — дефолт скрипта.</summary>
    public double? SplashSeconds { get; init; }
    public double? BumperSeconds { get; init; }
    public double? PlashkaSeconds { get; init; }

    /// <summary>Часы в углу и ретро-тема; null — дефолт скрипта.</summary>
    public bool? ClockEnabled { get; init; }
    public bool? RetroTheme { get; init; }

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
    private const string NoFullscreenFlag = "--fullscreen=no";

    // Каждая опция скрипта передаётся ОТДЕЛЬНЫМ --script-opts-append: в общем
    // --script-opts запятая разделяет пары, и путь вроде «D:\Мультфильмы, сериалы»
    // разъезжался бы на две опции (сериал переставал определяться → прогресс не писался).
    private const string ScriptOptFlag = "--script-opts-append=";

    public static IReadOnlyList<string> Build(MpvLaunchOptions options)
    {
        var args = new List<string>
        {
            ConfigDirFlag + options.ConfigDir,
            IpcServerFlag + options.PipePath,
        };

        AddScriptOpt(args, "channelosd-root", options.ChannelOsdRoot);
        AddScriptOpt(args, "channelosd-name", options.ChannelName);
        AddScriptOpt(args, "channelosd-splash", Format(options.SplashSeconds));
        AddScriptOpt(args, "channelosd-bumper", Format(options.BumperSeconds));
        AddScriptOpt(args, "channelosd-plashka", Format(options.PlashkaSeconds));
        AddScriptOpt(args, "channelosd-clock", Format(options.ClockEnabled));
        AddScriptOpt(args, "channelosd-retro", Format(options.RetroTheme));

        if (!options.Fullscreen)
            args.Add(NoFullscreenFlag);

        args.Add(options.Playlist);

        return args;
    }

    private static void AddScriptOpt(List<string> args, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            args.Add($"{ScriptOptFlag}{key}={value}");
    }

    // Инвариантная культура: при русской локали "4.5" иначе стало бы "4,5" и Lua
    // не разобрал бы число.
    private static string? Format(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Format(bool? value) =>
        value is null ? null : value.Value ? "yes" : "no";
}
