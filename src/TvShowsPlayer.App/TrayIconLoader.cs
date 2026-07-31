using System.Drawing;
using System.Drawing.Imaging;
using Avalonia.Controls;
using Avalonia.Platform;

namespace TvShowsPlayer.App;

/// <summary>
/// Иконка приложения для трея и окон: берём собственный логотип из ресурсов, а если
/// его вдруг нет — падаем обратно на иконку mpv (как делал AHK-кит).
/// </summary>
internal static class TrayIconLoader
{
    private static readonly Uri IconUri = new("avares://TvShowsPlayer.App/Assets/icon.png");

    /// <summary>Логотип канала из ресурсов сборки; null — ресурс недоступен.</summary>
    public static WindowIcon? AppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(IconUri);
            return new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Иконка из exe (запасной вариант). Windows-only (System.Drawing).</summary>
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
