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
    public string MpvPath { get; set; } = @"C:\mpv\mpv.exe";
    public string CartoonsRoot { get; set; } = "";   // укажет пользователь в настройках

    // Сериалы вне карусели (качаются / неполные / битые). Имена папок.
    public List<string> ExcludedShows { get; set; } = new();

    // Желаемый порядок сериалов в карусели (имена папок). Пусто = алфавитный.
    // Отсутствующие в списке сериалы идут следом в алфавитном порядке.
    public List<string> ShowOrder { get; set; } = new();

    // Карусель
    public int Window { get; set; } = 4;
    public int Step { get; set; } = 2;
    public int CapRotations { get; set; } = 200;

    // Плеер
    public string? AudioDevice { get; set; }
    public int Volume { get; set; } = 70;
    public int FsScreen { get; set; } = 1;

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

    /// <summary>Загрузить из JSON; если файла нет — вернуть дефолты.</summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    /// <summary>Сохранить в JSON (с созданием папки).</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}
