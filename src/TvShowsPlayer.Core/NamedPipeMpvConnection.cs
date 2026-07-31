using System.IO.Pipes;
using System.Text;

namespace TvShowsPlayer.Core;

/// <summary>
/// Боевое соединение с mpv по именованному каналу Windows
/// (mpv: input-ipc-server=\\.\pipe\localtvmpv). Это IO-граница: юнит-тестами не
/// покрыта намеренно — логика протокола живёт и тестируется в
/// <see cref="MpvIpcClient"/> через <see cref="IMpvConnection"/>; здесь только
/// тонкая обвязка над <see cref="NamedPipeClientStream"/> со строчным
/// разделением (одна строка = одно JSON-сообщение, перевод строки = '\n').
/// </summary>
public sealed class NamedPipeMpvConnection : IMpvConnection
{
    public const string DefaultPipeName = Branding.PipeName;

    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    private NamedPipeMpvConnection(NamedPipeClientStream pipe)
    {
        _pipe = pipe;

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(_pipe, utf8);
        _writer = new StreamWriter(_pipe, utf8) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Подключиться к каналу mpv (server=".", pipeName без префикса \\.\pipe\).</summary>
    public static async Task<NamedPipeMpvConnection> ConnectAsync(
        string pipeName = DefaultPipeName,
        int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutMs, cancellationToken);

        return new NamedPipeMpvConnection(pipe);
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return await _reader.ReadLineAsync(cancellationToken);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
        _pipe.Dispose();
    }
}
