using System.Drawing;
using System.Drawing.Imaging;
using Avalonia.Controls;

namespace TvShowsPlayer.App;

/// <summary>
/// Достаёт иконку из exe (как AHK <c>TraySetIcon(mpv)</c>) и отдаёт её
/// в виде <see cref="WindowIcon"/> для трея. Windows-only (System.Drawing).
/// </summary>
internal static class TrayIconLoader
{
    public static WindowIcon? FromExecutable(string exePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }
}
