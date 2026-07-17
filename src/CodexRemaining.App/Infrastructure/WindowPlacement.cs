using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexRemaining.App.Infrastructure;

public static class WindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint PrimaryMonitorFlag = 1;

    public static CapsuleAttachmentSettings? SnapToNearestWorkArea(
        Window window,
        double snapDistanceInDips)
    {
        if (!TryGetWindowBounds(window, out var handle, out var nativeBounds))
        {
            return null;
        }

        var monitorHandle = MonitorFromRect(ref nativeBounds, MonitorDefaultToNearest);
        if (!TryGetMonitor(monitorHandle, out var monitor))
        {
            return null;
        }

        var dpi = GetDpiForWindow(handle);
        var snapDistance = snapDistanceInDips * (dpi == 0 ? 1 : dpi / 96d);
        var result = CapsulePlacement.Snap(ToBounds(nativeBounds), monitor.WorkArea, snapDistance);
        MoveWindow(handle, nativeBounds, result.Bounds);

        return result.Anchor is CapsuleAnchor.None
            ? null
            : CreateAttachment(monitor, result.Anchor, result.EdgeOffsetRatio);
    }

    public static CapsuleAttachmentSettings? RestoreAttachment(
        Window window,
        CapsuleAttachmentSettings attachment)
    {
        if (attachment.Anchor is CapsuleAnchor.None ||
            !TryGetWindowBounds(window, out var handle, out var nativeBounds))
        {
            return null;
        }

        var monitor = FindMonitor(attachment);
        if (monitor is null)
        {
            return null;
        }

        return RestoreOnMonitor(
            handle,
            nativeBounds,
            monitor.Value,
            attachment.Anchor,
            attachment.EdgeOffsetRatio);
    }

    public static CapsuleAttachmentSettings? AttachToPrimaryCorner(
        Window window,
        CapsuleAnchor corner)
    {
        if (!TryGetWindowBounds(window, out var handle, out var nativeBounds))
        {
            return null;
        }

        var monitor = EnumerateMonitors().FirstOrDefault(candidate => candidate.IsPrimary);
        if (monitor.Handle == IntPtr.Zero)
        {
            return null;
        }

        return RestoreOnMonitor(handle, nativeBounds, monitor, corner, edgeOffsetRatio: 0);
    }

    private static CapsuleAttachmentSettings RestoreOnMonitor(
        IntPtr handle,
        NativeRect nativeBounds,
        MonitorDetails monitor,
        CapsuleAnchor anchor,
        double edgeOffsetRatio)
    {
        var result = CapsulePlacement.Restore(
            anchor,
            edgeOffsetRatio,
            monitor.WorkArea,
            nativeBounds.Right - nativeBounds.Left,
            nativeBounds.Bottom - nativeBounds.Top);
        MoveWindow(handle, nativeBounds, result.Bounds);

        return CreateAttachment(monitor, result.Anchor, result.EdgeOffsetRatio);
    }

    public static void ConstrainToNearestWorkArea(Window window)
    {
        if (!TryGetWindowBounds(window, out var handle, out var nativeBounds))
        {
            return;
        }

        var monitorHandle = MonitorFromRect(ref nativeBounds, MonitorDefaultToNearest);
        if (!TryGetMonitor(monitorHandle, out var monitor))
        {
            return;
        }

        var result = CapsulePlacement.KeepVisible(ToBounds(nativeBounds), monitor.WorkArea);
        MoveWindow(handle, nativeBounds, result.Bounds);
    }

    private static MonitorDetails? FindMonitor(CapsuleAttachmentSettings attachment)
    {
        var monitors = EnumerateMonitors();
        var selected = CapsulePlacement.SelectDisplay(
            attachment.MonitorDeviceName,
            attachment.MonitorCenterX,
            attachment.MonitorCenterY,
            monitors.Select(monitor => monitor.ToCapsuleDisplay()).ToArray());
        return selected is null
            ? null
            : monitors.First(monitor => string.Equals(
                monitor.DeviceName,
                selected.DeviceName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<MonitorDetails> EnumerateMonitors()
    {
        var monitors = new List<MonitorDetails>();
        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr handle, IntPtr deviceContext, ref NativeRect bounds, IntPtr data) =>
            {
                if (TryGetMonitor(handle, out var monitor))
                {
                    monitors.Add(monitor);
                }

                return true;
            },
            IntPtr.Zero);
        return monitors;
    }

    private static bool TryGetMonitor(IntPtr handle, out MonitorDetails monitor)
    {
        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
            DeviceName = string.Empty,
        };

        if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info))
        {
            monitor = default;
            return false;
        }

        monitor = new MonitorDetails(
            handle,
            ToBounds(info.Monitor),
            ToBounds(info.WorkArea),
            info.DeviceName,
            (info.Flags & PrimaryMonitorFlag) != 0);
        return true;
    }

    private static bool TryGetWindowBounds(
        Window window,
        out IntPtr handle,
        out NativeRect bounds)
    {
        handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            bounds = default;
            return false;
        }

        return GetWindowRect(handle, out bounds);
    }

    private static CapsuleAttachmentSettings CreateAttachment(
        MonitorDetails monitor,
        CapsuleAnchor anchor,
        double edgeOffsetRatio) =>
        new()
        {
            MonitorDeviceName = monitor.DeviceName,
            MonitorCenterX = monitor.CenterX,
            MonitorCenterY = monitor.CenterY,
            Anchor = anchor,
            EdgeOffsetRatio = edgeOffsetRatio,
        };

    private static CapsuleBounds ToBounds(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static void MoveWindow(
        IntPtr handle,
        NativeRect current,
        CapsuleBounds target)
    {
        var left = (int)Math.Round(target.Left, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(target.Top, MidpointRounding.AwayFromZero);
        if (left == current.Left && top == current.Top)
        {
            return;
        }

        _ = SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    private readonly record struct MonitorDetails(
        IntPtr Handle,
        CapsuleBounds MonitorArea,
        CapsuleBounds WorkArea,
        string DeviceName,
        bool IsPrimary)
    {
        public int CenterX => (int)Math.Round(MonitorArea.Left + (MonitorArea.Width / 2));

        public int CenterY => (int)Math.Round(MonitorArea.Top + (MonitorArea.Height / 2));

        public CapsuleDisplay ToCapsuleDisplay() =>
            new(DeviceName, CenterX, CenterY, WorkArea);
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitor,
        IntPtr deviceContext,
        ref NativeRect bounds,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
