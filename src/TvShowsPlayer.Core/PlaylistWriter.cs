using System.Text;

namespace TvShowsPlayer.Core;

/// <summary>
/// Запись плейлиста .m3u и соседнего .sig (сигнатура состава) + проверка
/// «состав не менялся → не переписывать» — так бережём закладку просмотра.

/// </summary>
public static class PlaylistWriter
{
    /// <summary>Записать .m3u (#EXTM3U + пути) и рядом .sig с сигнатурой состава.</summary>
    public static void Write(string m3uPath, IReadOnlyList<string> playlist, string signature)
    {
        var dir = Path.GetDirectoryName(m3uPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder("#EXTM3U\n");
        foreach (var path in playlist)
            sb.Append(path).Append('\n');

        File.WriteAllText(m3uPath, sb.ToString());
        File.WriteAllText(m3uPath + ".sig", signature);
    }

    /// <summary>Актуален ли плейлист: файлы есть и сохранённая сигнатура совпала с текущей.</summary>
    public static bool IsUpToDate(string m3uPath, string signature)
    {
        var sigPath = m3uPath + ".sig";
        if (!File.Exists(m3uPath) || !File.Exists(sigPath))
            return false;

        return File.ReadAllText(sigPath).Trim() == signature;
    }
}
