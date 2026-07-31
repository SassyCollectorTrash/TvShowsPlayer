using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TvShowsPlayer.Core;

/// <summary>
/// Состояние канала — тот же файл/формат, что пишет <c>resume.lua</c>
/// (<c>localtv-channel-state.json</c>): позиция плейлиста, прогресс по каждому сериалу
/// (имя → путь последней серии относительно папки сериала) и текущий сериал.
/// Загрузка терпима к kit-овским причудам (нет <c>shows</c>/<c>current</c>; пустая
/// таблица mpv сериализуется как <c>[]</c>).
/// </summary>
public sealed class ChannelState
{
    [JsonPropertyName("playlist_pos")]
    public int PlaylistPos { get; set; }

    [JsonPropertyName("time_pos")]
    public double TimePos { get; set; }

    [JsonPropertyName("shows")]
    public Dictionary<string, string> Shows { get; set; } = new();

    [JsonPropertyName("current")]
    public string? Current { get; set; }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // кириллица без \uXXXX
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Резервная копия предыдущего состояния (страховка от обрыва записи).</summary>
    public static string BackupPath(string path) => path + ".bak";

    /// <summary>
    /// Прочитать состояние. Если основной файл повреждён (обрыв записи, выключение
    /// питания), поднимаем прогресс из резервной копии — молча начинать с нуля нельзя,
    /// иначе следующая же запись затрёт накопленный прогресс просмотра.
    /// </summary>
    public static ChannelState Load(string path)
    {
        return TryLoad(path) ?? TryLoad(BackupPath(path)) ?? new ChannelState();
    }

    private static ChannelState? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var state = new ChannelState
            {
                PlaylistPos = GetInt(root, "playlist_pos"),
                TimePos = GetDouble(root, "time_pos"),
                Current = GetString(root, "current"),
            };

            if (root.TryGetProperty("shows", out var shows) && shows.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in shows.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String)
                        state.Shows[p.Name] = p.Value.GetString()!;
                }
            }

            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;   // битый или недоступный — пусть решает вызывающий (есть .bak)
        }
    }

    /// <summary>
    /// Записать состояние атомарно: сначала во временный файл, затем подменой поверх
    /// основного с сохранением предыдущей версии в <c>.bak</c>. Так обрыв записи не
    /// оставляет усечённый файл — прогресс просмотра переживает падение и выключение.
    /// </summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, WriteOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);

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
            File.Move(tmp, path, overwrite: true);   // файл был занят — хотя бы не теряем запись
        }
    }

    private static int GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    private static double GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
