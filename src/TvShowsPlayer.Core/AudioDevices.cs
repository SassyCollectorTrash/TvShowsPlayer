using System.Text.RegularExpressions;

namespace TvShowsPlayer.Core;

/// <summary>Аудио-устройство mpv: id (для <c>--audio-device</c>) и человекочитаемое имя.</summary>
public sealed record AudioDevice(string Id, string Description);

/// <summary>
/// Разбор вывода <c>mpv --audio-device=help</c>. Строки вида
/// <c>  'wasapi/{…}' (DELL S2721D)</c> → id + описание; прочее (заголовок) игнорим.
/// </summary>
public static class AudioDevices
{
    private static readonly Regex Line =
        new(@"^\s*'([^']*)'\s*\((.*)\)\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<AudioDevice> Parse(string? mpvHelpOutput)
    {
        var devices = new List<AudioDevice>();
        if (string.IsNullOrEmpty(mpvHelpOutput))
            return devices;

        foreach (var raw in mpvHelpOutput.Split('\n'))
        {
            var m = Line.Match(raw);
            if (m.Success)
                devices.Add(new AudioDevice(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }

        return devices;
    }
}
