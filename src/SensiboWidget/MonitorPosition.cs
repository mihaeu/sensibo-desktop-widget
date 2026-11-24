using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SensiboWidget.Core;
using Forms = System.Windows.Forms;

namespace SensiboWidget;

internal static class MonitorPosition
{
    public static void Restore(Window window, SavedPosition? saved)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved?.Monitor)
            ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var area = Area(screen);
        // Move to the target monitor first, allowing WPF to process its DPI change.
        Move(handle, new(area.X + 10, area.Y + 10));
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
        {
            var rect = Bounds(handle);
            Move(handle, WindowPlacement.Restore(area, rect.Width, rect.Height, saved));
        });
    }

    public static SavedPosition SnapAndCapture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screen = Forms.Screen.FromHandle(handle);
        var bounds = Bounds(handle);
        var point = WindowPlacement.Snap(Area(screen), bounds, (int)(16 * VisualTreeHelper.GetDpi(window).DpiScaleX));
        Move(handle, point);
        return WindowPlacement.Capture(screen.DeviceName, Area(screen), bounds with { X = point.X, Y = point.Y });
    }

    private static PixelRect Area(Forms.Screen screen) => new(screen.WorkingArea.X, screen.WorkingArea.Y,
        screen.WorkingArea.Width, screen.WorkingArea.Height);

    private static PixelRect Bounds(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new System.ComponentModel.Win32Exception();
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static void Move(IntPtr handle, PixelPoint point) => SetWindowPos(handle, IntPtr.Zero,
        point.X, point.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
