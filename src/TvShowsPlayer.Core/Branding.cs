namespace TvShowsPlayer.Core;

/// <summary>
/// Единые константы бренда и путей — чтобы имя не было «магической строкой»,
/// разбросанной по коду. <see cref="LegacyAppName"/> нужен только для разовой
/// миграции данных со старого имени (см. <see cref="LegacyConfigMigration"/>).
/// </summary>
public static class Branding
{
    public const string AppName = "LocalTV";
    public const string LegacyAppName = "Jetix";

    // Файл состояния (прогресс по сериалам + позиция плейлиста) внутри config-dir.
    public const string StateFileName = "localtv-channel-state.json";
    public const string LegacyStateFileName = "jetix-channel-state.json";

    // Имя значения автозапуска в HKCU\...\Run (старое — только для миграции).
    public const string AutostartValueName = AppName;
    public const string LegacyAutostartValueName = "JETIX";

    // Именованный pipe mpv JSON-IPC (dev — отдельный, чтобы не пересекаться с prod).
    public const string PipeName = "localtvmpv";
    public const string PipeNameDev = "localtvmpv-dev";

    // GitHub-репозиторий (для проверки обновлений и ссылки «Скачать»).
    public const string RepoOwner = "SassyCollectorTrash";
    public const string RepoName = "TvShowsPlayer";
    public const string ReleasesUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases";
}
