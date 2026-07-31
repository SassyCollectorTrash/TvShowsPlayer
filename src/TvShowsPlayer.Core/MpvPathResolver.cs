namespace TvShowsPlayer.Core;

/// <summary>
/// Какой проигрыватель запускать. Правило простое: если рядом с программой лежит
/// свой mpv — берём его. Он проверен именно с этой версией, а случайный mpv из
/// системы может оказаться другим по возможностям, и разбираться, почему у одного
/// человека работает, а у другого нет, будет невозможно.
/// </summary>
public static class MpvPathResolver
{
    /// <summary>Путь из старого значения по умолчанию: он ссылался на mpv в системе.</summary>
    public const string LegacyDefaultPath = @"C:\mpv\mpv.exe";

    public static string Resolve(string? configured, string appDir)
    {
        var bundled = Path.Combine(appDir, "mpv", "mpv.exe");
        var hasBundled = File.Exists(bundled);

        // Свой mpv побеждает пустое значение, несуществующий путь и прежний дефолт
        // (его записывали в конфиг автоматически, это не осознанный выбор).
        if (hasBundled && (string.IsNullOrWhiteSpace(configured)
                           || IsLegacyDefault(configured)
                           || !File.Exists(configured)))
        {
            return bundled;
        }

        // Пользователь указал свой путь — уважаем его.
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return hasBundled ? bundled : configured ?? string.Empty;
    }

    private static bool IsLegacyDefault(string path) =>
        string.Equals(path.Trim(), LegacyDefaultPath, StringComparison.OrdinalIgnoreCase);
}
