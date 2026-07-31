using System.Runtime.InteropServices;

namespace TvShowsPlayer.App;

/// <summary>
/// Монитор: системное имя (его понимает mpv) и человеческое описание для списка.
/// </summary>
public sealed record DisplayDevice(string DeviceName, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Перечисление мониторов Windows. Имя вида <c>\\.\DISPLAY1</c> уходит в mpv
/// (<c>fs-screen-name</c>), а пользователю показываем модель и разрешение — выбирать
/// «второй экран» вслепую по номеру неудобно и ненадёжно.
/// </summary>
internal static class DisplayDevices
{
    public static IReadOnlyList<DisplayDevice> List()
    {
        var devices = new List<DisplayDevice>();

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
            {
                var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                    devices.Add(Describe(info));

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return devices;
        }

        return devices;
    }

    private static DisplayDevice Describe(MonitorInfoEx info)
    {
        var width = info.rcMonitor.Right - info.rcMonitor.Left;
        var height = info.rcMonitor.Bottom - info.rcMonitor.Top;
        var isPrimary = (info.dwFlags & MonitorPrimary) != 0;

        // Номер обязателен: Windows часто зовёт все мониторы «Generic PnP Monitor»,
        // и одинаковые подписи в списке различить было бы нельзя. Модель добавляем,
        // только если она о чём-то говорит.
        var number = info.szDevice.Replace(@"\\.\DISPLAY", string.Empty);
        var model = MonitorModel(info.szDevice);
        var title = $"Экран {number}";
        if (model.Length > 0 && !model.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
            title += $" · {model}";

        var description = $"{title} — {width}×{height}" + (isPrimary ? " (основной)" : string.Empty);

        return new DisplayDevice(info.szDevice, description);
    }

    /// <summary>Модель монитора («Dell S2721D») из описания устройства отображения.</summary>
    private static string MonitorModel(string deviceName)
    {
        var display = new DisplayDeviceInfo { cb = Marshal.SizeOf<DisplayDeviceInfo>() };

        return EnumDisplayDevices(deviceName, 0, ref display, 0) ? display.DeviceString : string.Empty;
    }

    private const int MonitorPrimary = 0x00000001;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDeviceInfo
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool EnumDisplayDevices(string device, uint devNum, ref DisplayDeviceInfo info, uint flags);
}
