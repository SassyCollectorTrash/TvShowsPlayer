namespace TvShowsPlayer.Core;

/// <summary>
/// Единые константы имени программы и её путей — чтобы они не были «магическими
/// строками», разбросанными по коду.
/// </summary>
public static class Branding
{
    public const string AppName = "LocalTV";

    // Файл состояния (прогресс по сериалам + позиция плейлиста) внутри config-dir.
    public const string StateFileName = "localtv-channel-state.json";

    // Имя значения автозапуска в HKCU\...\Run.
    public const string AutostartValueName = AppName;

    // Именованный pipe mpv JSON-IPC (dev — отдельный, чтобы не пересекаться с prod).
    public const string PipeName = "localtvmpv";
    public const string PipeNameDev = "localtvmpv-dev";

    // GitHub-репозиторий (для проверки обновлений и ссылки «Скачать»).
    public const string RepoOwner = "SassyCollectorTrash";
    public const string RepoName = "TvShowsPlayer";
    public const string ReleasesUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases";
}
