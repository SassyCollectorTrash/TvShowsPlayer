using System.Diagnostics;

namespace TvShowsPlayer.App;

/// <summary>
/// Надзор за СВОИМ процессом mpv: запуск с готовыми аргументами, единственный
/// экземпляр (бьём ТОЛЬКО свой прежний процесс, а не все mpv в системе — поэтому
/// живой AHK-канал не затрагивается), событие выхода.
/// </summary>
public sealed class MpvSupervisor : IDisposable
{
    private readonly string _mpvPath;
    private readonly IReadOnlyList<string> _args;
    private Process? _process;

    public event EventHandler? Exited;

    public MpvSupervisor(string mpvPath, IReadOnlyList<string> args)
    {
        _mpvPath = mpvPath;
        _args = args;
    }

    public void Start()
    {
        StopCurrent();

        var info = new ProcessStartInfo
        {
            FileName = _mpvPath,
            UseShellExecute = false,
        };
        foreach (var arg in _args)
            info.ArgumentList.Add(arg);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        process.Start();

        _process = process;
    }

    /// <summary>Дождаться появления окна mpv и вернуть его HWND (0 при таймауте/выходе).</summary>
    public IntPtr WaitForWindowHandle(int timeoutMs)
    {
        var process = _process;
        if (process is null)
            return IntPtr.Zero;

        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            if (process.HasExited)
                return IntPtr.Zero;

            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
                return handle;

            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }

    private void StopCurrent()
    {
        if (_process is null)
            return;

        try
        {
            // Гасим СВОЙ mpv осознанно — событие Exited здесь не нужно, иначе
            // перезапуск канала выглядел бы как «mpv умер» и закрывал приложение.
            _process.EnableRaisingEvents = false;
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                // ДОЖИДАЕМСЯ смерти: пока старый mpv жив, жив и его именованный канал.
                // Новый клиент успевал подключиться к умирающему экземпляру — связь
                // тут же рвалась («Pipe is broken»), а пульт замолкал при живом эфире.
                _process.WaitForExit(5000);
            }
        }
        catch
        {
            // мог уже завершиться сам — это не ошибка
        }

        _process.Dispose();
        _process = null;
    }

    public void Dispose() => StopCurrent();
}
