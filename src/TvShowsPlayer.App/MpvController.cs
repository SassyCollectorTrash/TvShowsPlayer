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

    public MpvController(string pipeName)
    {
        _pipeName = pipeName;
    }

    public bool IsConnected => _client is not null;

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
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendAsync(IReadOnlyList<object> command, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
            return;

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
            // mpv не ответил вовремя — команда пропущена, приложение живёт дальше
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }
}
