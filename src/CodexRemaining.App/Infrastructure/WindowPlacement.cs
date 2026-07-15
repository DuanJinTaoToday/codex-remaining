using System.Runtime.InteropServices;
using System.Windows;

namespace CodexRemaining.App.Infrastructure;

public static class WindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;

    public static Rect GetNearestWorkArea(Rect windowBounds)
    {
        var nativeBounds = new NativeRect
        {
            Left = (int)Math.Floor(windowBounds.Left),
            Top = (int)Math.Floor(windowBounds.Top),
            Right = (int)Math.Ceiling(windowBounds.Right),
            Bottom = (int)Math.Ceiling(windowBounds.Bottom),
        };
        var monitor = MonitorFromRect(ref nativeBounds, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        var area = monitorInfo.WorkArea;
        return new Rect(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
