using System.Text.Encodings.Web;
using System.Text.Json;

namespace TvShowsPlayer.Core;

/// <summary>
/// Единый конфиг канала (источник правды). Приложение пишет — генератор и Lua
/// читают свои части. Хранится JSON-ом.
/// </summary>
public sealed class AppConfig
{
    // Пути
    // Пусто = взять проигрыватель из комплекта (см. MpvPathResolver). Прежний дефолт
    // указывал на mpv в системе — и программа незаметно играла им.
    public string MpvPath { get; set; } = "";
    public string CartoonsRoot { get; set; } = "";   // укажет пользователь в настройках

    // Сериалы вне карусели (качаются / неполные / битые). Имена папок.
    public List<string> ExcludedShows { get; set; } = new();

    // Желаемый порядок сериалов в карусели (имена папок). Пусто = алфавитный.
    // Отсутствующие в списке сериалы идут следом в алфавитном порядке.
    public List<string> ShowOrder { get; set; } = new();

    // Сериалы, которые программа уже видела. Появившийся позже сериал считается
    // новинкой: он попадает в исключения, пока пользователь не включит его сам —
    // программа не может знать, докачан он или ещё нет.
    public List<string> KnownShows { get; set; } = new();

    // Список сериалов обновляется ТОЛЬКО по команде пользователя: надёжно определить
    // «файл докачан» нельзя (торрент резервирует полный размер сразу, куски приходят
    // вразнобой, а вставшая раздача выглядит как затихший файл). Единственное, что
    // проверяем при обновлении, — не пишется ли файл прямо сейчас.
    public int SettleMinutes { get; set; } = 2;

    // Карусель
    public int Window { get; set; } = 4;
    public int Step { get; set; } = 2;
    public int CapRotations { get; set; } = 200;

    // Плеер
    public string? AudioDevice { get; set; }
    public int Volume { get; set; } = 70;
    // Экран для полноэкранного показа. Имя (\\.\DISPLAY1) надёжнее номера: порядок
    // мониторов в разных программах не совпадает. Пусто — по номеру ниже.
    public string ScreenName { get; set; } = "";

    // 0 = основной монитор. Дефолт намеренно 0: на машине с одним экраном
    // номер 1 не существует, и канал уезжал бы «в никуда».
    public int FsScreen { get; set; }

    // Журнал работы: подробный, поэтому пусть его можно выключить. По умолчанию
    // включён — без него разбирать «нажал, ничего не произошло» нечем.
    public bool LoggingEnabled { get; set; } = true;

    // Горячие клавиши: набор модификаторов (на случай конфликта с другой программой)
    // и общий выключатель.
    public string HotkeyModifiers { get; set; } = "Ctrl+Alt";
    public bool HotkeysEnabled { get; set; } = true;

    // О каких занятых комбинациях уже сообщали. Канал стартует вместе с Windows,
    // поэтому одно и то же окно при каждом запуске — это раздражение, а не помощь.
    public string ReportedHotkeyConflicts { get; set; } = "";

    // OSD
    public bool ClockEnabled { get; set; } = true;
    public double SplashSeconds { get; set; } = 4;
    public double BumperSeconds { get; set; } = 3;
    public double PlashkaSeconds { get; set; } = 5;
    public string ChannelName { get; set; } = Branding.AppName;
    public bool RetroTheme { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Резервная копия предыдущего конфига.</summary>
    public static string BackupPath(string path) => path + ".bak";

    /// <summary>
    /// Загрузить из JSON. Если файл повреждён (обрыв записи, выключение питания) —
    /// поднимаем предыдущую версию из <c>.bak</c>: иначе пользователь молча терял бы
    /// все настройки (папку с библиотекой, порядок сериалов, исключения), а старт
    /// падал бы с ошибкой разбора. Файла нет — обычные дефолты.
    /// </summary>
    public static AppConfig Load(string path)
    {
        return TryLoad(path) ?? TryLoad(BackupPath(path)) ?? new AppConfig();
    }

    private static AppConfig? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Сохранить атомарно: временный файл → подмена основного, предыдущая версия
    /// уезжает в <c>.bak</c>. Обрыв записи не оставляет усечённый конфиг.
    /// </summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }

        try
        {
            File.Replace(tmp, path, BackupPath(path), ignoreMetadataErrors: true);
        }
        catch (IOException)
        {
            File.Move(tmp, path, overwrite: true);
        }
    }
}
