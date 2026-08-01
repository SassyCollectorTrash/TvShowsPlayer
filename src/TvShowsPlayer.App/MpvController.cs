using TvShowsPlayer.Core;

namespace TvShowsPlayer.App;

/// <summary>
/// Управление dev-mpv через IPC: подключение к именованному каналу с ретраем
/// (mpv поднимает pipe не мгновенно) + команды трея. Отправки сериализуются —
/// <see cref="MpvIpcClient"/> рассчитан на один запрос за раз.
/// </summary>
public sealed class MpvController : IDisposable
{
    private const string ScriptMessage = "script-message";
    private const string NextShowMessage = "localtv-next-show";
    private const string ResyncMessage = "localtv-resync";
    private const string NowMessage = "localtv-now";
    private const string SetProperty = "set_property";
    private const string MuteProperty = "mute";

    private readonly string _pipeName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MpvIpcClient? _client;
    private int _reconnecting;
    private bool _disposed;

    public MpvController(string pipeName)
    {
        _pipeName = pipeName;
    }

    public bool IsConnected => _client is not null;

    /// <summary>
    /// Закрыть проигрыватель, оставшийся от прошлого запуска. На именованном канале
    /// с нашим именем может отвечать только НАШ mpv, поэтому чужие плееры не тронем.
    /// Возвращает true, если сирота нашлась и её попросили закрыться.
    /// </summary>
    public static async Task<bool> QuitOrphanAsync(string pipeName)
    {
        try
        {
            using var connection = await NamedPipeMpvConnection.ConnectAsync(pipeName, timeoutMs: 700);
            using var client = new MpvIpcClient(connection);

            await client.SendCommandAsync(new object[] { "quit" }, CancellationToken.None);
            await Task.Delay(700);   // даём процессу закрыться до старта нового

            return true;
        }
        catch
        {
            return false;   // никто не ответил — сирот нет, это норма
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 40;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var connection = await NamedPipeMpvConnection.ConnectAsync(_pipeName, 1000, cancellationToken);
                _client = new MpvIpcClient(connection);
                return;
            }
            catch (Exception) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(300, cancellationToken);
            }
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "cycle", "pause" }, cancellationToken);

    public Task VolumeAsync(int delta, CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "add", "volume", delta }, cancellationToken);

    public Task NextEpisodeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "playlist-next" }, cancellationToken);

    public Task NextShowAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { ScriptMessage, NextShowMessage }, cancellationToken);

    public Task ResyncAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { ScriptMessage, ResyncMessage }, cancellationToken);

    public Task ShowNowAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { ScriptMessage, NowMessage }, cancellationToken);

    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { SetProperty, MuteProperty, muted }, cancellationToken);

    public Task QuitAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "quit" }, cancellationToken);

    /// <summary>Текущий проигрываемый файл (свойство mpv <c>path</c>); null если не подключены.</summary>
    public Task<string?> GetCurrentPathAsync(CancellationToken cancellationToken = default) =>
        GetAsync<string>("path", cancellationToken);

    /// <summary>Текущая позиция в плейлисте (<c>playlist-pos</c>).</summary>
    public Task<int> GetPlaylistPosAsync(CancellationToken cancellationToken = default) =>
        GetAsync<int>("playlist-pos", cancellationToken);

    /// <summary>Текущая секунда внутри серии (<c>time-pos</c>).</summary>
    public Task<double> GetTimePosAsync(CancellationToken cancellationToken = default) =>
        GetAsync<double>("time-pos", cancellationToken);

    /// <summary>Выключен ли сейчас звук у плеера (<c>mute</c>).</summary>
    public Task<bool> GetMuteAsync(CancellationToken cancellationToken = default) =>
        GetAsync<bool>("mute", cancellationToken);

    /// <summary>Экраны, на которых сейчас окно канала (<c>display-names</c>).</summary>
    public Task<string[]?> GetDisplayNamesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<string[]>("display-names", cancellationToken);

    /// <summary>Текущая громкость плеера (<c>volume</c>).</summary>
    public Task<double> GetVolumeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<double>("volume", cancellationToken);

    /// <summary>Перемотать на заданную секунду серии.</summary>
    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "seek", seconds, "absolute" }, cancellationToken);

    /// <summary>Перейти к позиции в плейлисте (сдвинуть указатель живого mpv).</summary>
    public Task SetPlaylistPosAsync(int pos, CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "set_property", "playlist-pos", pos }, cancellationToken);

    /// <summary>Перезагрузить плейлист в живом mpv (после пересборки .m3u).</summary>
    public Task ReloadPlaylistAsync(string m3uPath, CancellationToken cancellationToken = default) =>
        SendAsync(new object[] { "loadlist", m3uPath, "replace" }, cancellationToken);

    // Любая операция ограничена по времени: без этого один «повисший» запрос держал
    // бы семафор вечно, и весь пульт (включая «Выход») переставал бы отвечать.
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private async Task<T?> GetAsync<T>(string property, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
            return default;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationTimeout);

        try
        {
            await _gate.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return default;
        }

        try
        {
            return await client.GetPropertyAsync<T>(property, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (Exception ex) when (IsConnectionLost(ex))
        {
            AppLog.Write($"связь с плеером оборвалась при запросе «{property}» ({ex.GetType().Name}) — переподключаюсь");
            DropConnection(client);
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendAsync(IReadOnlyList<object> command, CancellationToken cancellationToken)
    {
        var text = string.Join(" ", command);
        var client = _client;
        if (client is null)
        {
            AppLog.Write($"плееру НЕ отправлено (нет связи): {text}");
            return;
        }

        AppLog.Write($"плееру: {text}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationTimeout);

        try
        {
            await _gate.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await client.SendCommandAsync(command, cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppLog.Write($"плеер не ответил вовремя: {text}");
        }
        catch (Exception ex) when (IsConnectionLost(ex))
        {
            AppLog.Write($"связь с плеером оборвалась ({ex.GetType().Name}) — переподключаюсь");
            DropConnection(client);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Связь с mpv рвётся не только когда он закрылся: посторонняя программа может
    // убить процесс (например, закрыв все плееры по имени), да и сам
    // канал перезапускается. Молчать до перезапуска приложения нельзя — переподключаемся.
    private static bool IsConnectionLost(Exception ex) =>
        ex is IOException or ObjectDisposedException or MpvIpcException;

    private void DropConnection(MpvIpcClient dead)
    {
        if (!ReferenceEquals(_client, dead))
            return;   // уже заменили на новое соединение

        _client = null;
        dead.Dispose();

        if (_disposed)
            return;

        _ = ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            return;

        try
        {
            await Task.Delay(500);   // даём mpv подняться заново
            if (_disposed)
                return;

            await ConnectAsync();
            AppLog.Write(IsConnected ? "связь с плеером восстановлена" : "переподключиться не удалось");
        }
        catch
        {
            // не вышло — следующая команда попробует снова
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _client?.Dispose();
        _gate.Dispose();
    }
}
