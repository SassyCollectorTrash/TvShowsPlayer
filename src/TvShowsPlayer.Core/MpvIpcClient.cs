using System.Text.Encodings.Web;
using System.Text.Json;

namespace TvShowsPlayer.Core;

/// <summary>
/// Двунаправленный строковый канал к mpv: одна строка = одно JSON-сообщение.
/// Абстракция транспорта, чтобы протокол в <see cref="MpvIpcClient"/> тестировался
/// без реального mpv. Боевая реализация — <see cref="NamedPipeMpvConnection"/>.
/// </summary>
public interface IMpvConnection : IDisposable
{
    Task SendLineAsync(string line, CancellationToken cancellationToken);

    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
}

/// <summary>Ошибка обмена с mpv по IPC-каналу.</summary>
public sealed class MpvIpcException : Exception
{
    public MpvIpcException(string message) : base(message)
    {
    }
}

/// <summary>
/// Клиент JSON-IPC mpv (input-ipc-server). Шлёт команды и читает свойства,
/// сопоставляя ответы по request_id и пропуская асинхронные события.
/// Рассчитан на последовательное использование (один запрос за раз).
/// </summary>
public sealed class MpvIpcClient : IDisposable
{
    private const string GetPropertyCommand = "get_property";
    private const string RequestIdKey = "request_id";
    private const string DataKey = "data";
    private const string ErrorKey = "error";
    private const string SuccessValue = "success";
    private const string PropertyUnavailable = "property unavailable";

    // Компактный JSON (одна строка = одно сообщение) + кириллица без \uXXXX,
    // чтобы аргументы вроде имён сериалов в script-message были читаемыми.
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IMpvConnection _connection;
    private int _nextRequestId = 1;

    public MpvIpcClient(IMpvConnection connection)
    {
        _connection = connection;
    }

    public async Task SendCommandAsync(IReadOnlyList<object> command, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(new { command }, WireOptions);

        await _connection.SendLineAsync(line, cancellationToken);
    }

    public async Task<T?> GetPropertyAsync<T>(string name, CancellationToken cancellationToken)
    {
        var requestId = _nextRequestId++;
        var request = new { command = new object[] { GetPropertyCommand, name }, request_id = requestId };
        await _connection.SendLineAsync(JsonSerializer.Serialize(request, WireOptions), cancellationToken);

        while (true)
        {
            var reply = await _connection.ReadLineAsync(cancellationToken);
            if (reply is null)
                throw new MpvIpcException($"Канал mpv закрылся без ответа на запрос '{name}'.");

            using var doc = JsonDocument.Parse(reply);
            var root = doc.RootElement;

            // Асинхронное событие (нет request_id) или ответ на чужой запрос — пропускаем.
            if (!root.TryGetProperty(RequestIdKey, out var idElement) || idElement.GetInt32() != requestId)
                continue;

            if (root.TryGetProperty(ErrorKey, out var errorElement)
                && errorElement.GetString() != SuccessValue)
            {
                // «property unavailable» — не ошибка, а «нет значения» (напр. path без
                // файла при loadlist): возвращаем default, чтобы фоновый опрос не ронял.
                if (errorElement.GetString() == PropertyUnavailable)
                    return default;

                throw new MpvIpcException($"mpv вернул ошибку для '{name}': {errorElement.GetString()}");
            }

            return root.TryGetProperty(DataKey, out var dataElement)
                ? dataElement.Deserialize<T>(WireOptions)
                : default;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
