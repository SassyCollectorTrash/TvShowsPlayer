using System.Text.Json;

namespace TvShowsPlayer.Core;

/// <summary>Последний релиз с GitHub: разобранная версия и ссылка на страницу.</summary>
public sealed record UpdateInfo(Version Version, string? ReleaseUrl);

/// <summary>
/// Проверка обновлений через GitHub Releases. Разбор ответа и сравнение версий —
/// чистые и покрыты тестами; сам сетевой запрос тонкий и «тихий» (офлайн/таймаут/
/// битый ответ → <c>null</c>, без исключений в UI).
/// </summary>
public static class UpdateChecker
{
    /// <summary>Разобрать JSON GitHub <c>releases/latest</c>. Возвращает <c>null</c>, если не вышло.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)
                || tagEl.ValueKind != JsonValueKind.String)
                return null;

            var version = ParseVersion(tagEl.GetString());
            if (version is null)
                return null;

            var url = doc.RootElement.TryGetProperty("html_url", out var urlEl)
                      && urlEl.ValueKind == JsonValueKind.String
                ? urlEl.GetString()
                : null;
            return new UpdateInfo(version, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Есть ли более новая версия (сравнение по Major.Minor.Build, revision игнорим).</summary>
    public static bool HasUpdate(Version current, UpdateInfo latest)
    {
        return Normalize(latest.Version) > Normalize(current);
    }

    /// <summary>
    /// Запросить последний релиз с GitHub. Никогда не бросает — при любой ошибке
    /// (нет сети, не 2xx, битый JSON) возвращает <c>null</c>.
    /// </summary>
    public static async Task<UpdateInfo?> FetchLatestAsync(
        HttpClient http, string owner, string repo, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd($"{Branding.AppName}-update-check");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            return Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return null;   // проверка обновлений не должна ломать приложение
        }
    }

    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        var cut = s.IndexOfAny(new[] { '-', '+' });   // отбросить -rc.1 / +build
        if (cut >= 0)
            s = s[..cut];

        return Version.TryParse(s, out var v) ? v : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
