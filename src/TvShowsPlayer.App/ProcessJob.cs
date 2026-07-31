using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TvShowsPlayer.App;

/// <summary>
/// Группа процессов Windows (job object) с правилом «закрылась группа — умерли все».
/// Нужна, чтобы mpv НИКОГДА не пережил приложение: он отдельный процесс, и если
/// приложение снимут из диспетчера задач или оно упадёт, проигрыватель продолжал бы
/// играть сиротой — без пульта, но занимая тот же именованный канал. Именно из-за
/// такого «осиротевшего» mpv пульт переставал действовать, а выход из приложения
/// не останавливал показ.
/// </summary>
internal sealed class ProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint LimitKillOnJobClose = 0x00002000;

    private IntPtr _handle;

    public ProcessJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            return;

        var limits = new JobObjectExtendedLimitInfo
        {
            BasicLimitInformation = { LimitFlags = LimitKillOnJobClose },
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInfo>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
            SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, buffer, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Привязать процесс к группе. false — не вышло (работаем как раньше).</summary>
    public bool Assign(Process process)
    {
        if (_handle == IntPtr.Zero)
            return false;

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        CloseHandle(_handle);   // закрытие последнего дескриптора убивает потомков
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInfo
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInfo
    {
        public JobObjectBasicLimitInfo BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr security, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
