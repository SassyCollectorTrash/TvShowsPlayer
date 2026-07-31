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

    public static ChannelState Load(string path)
    {
        if (!File.Exists(path))
            return new ChannelState();

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
        catch (JsonException)
        {
            return new ChannelState();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));
    }

    private static int GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    private static double GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
