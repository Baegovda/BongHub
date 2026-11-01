using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BongHub.Next;

internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint f);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint f);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    public static double SystemScale { get { try { return GetDpiForSystem() / 96.0; } catch { return 1; } } }
    public const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    public const int WM_MOVE = 0x3, WM_SIZE = 0x5, WM_EXITSIZEMOVE = 0x232;

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;
    public static RECT Rect(Window w) { GetWindowRect(Handle(w), out var r); return r; }
    public static void Place(Window w, int x, int y, int cw, int ch) => SetWindowPos(Handle(w), IntPtr.Zero, x, y, cw, ch, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    public static bool OnAnyMonitor(int x, int y, int w, int h) { var r = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }; return MonitorFromRect(ref r, 0) != IntPtr.Zero; }
    public static RECT WorkAreaAtCursor()
    {
        GetCursorPos(out var p); var m = MonitorFromPoint(p, 2); var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(m, ref mi); return mi.rcWork;
    }
    public static double Scale(Window w) { try { return VisualTreeHelper.GetDpi(w).DpiScaleX; } catch { return 1; } }
}

internal static class Dwm
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int l, r, t, b; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE needs Windows 11 22H2 (build 22621) or newer.</summary>
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;
    public static void Dark(IntPtr h) { int v = 1; DwmSetWindowAttribute(h, 20, ref v, 4); }
    public static void Round(IntPtr h) { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); }
    public static void BorderColor(IntPtr h, Color c) { int v = c.R | (c.G << 8) | (c.B << 16); DwmSetWindowAttribute(h, 34, ref v, 4); }
    public static void Cloak(IntPtr h, bool on) { int v = on ? 1 : 0; DwmSetWindowAttribute(h, 13, ref v, 4); }
    public static int Cloaked(IntPtr h) { return DwmGetWindowAttribute(h, 14, out var v, 4) == 0 ? v : -1; }
    public static bool Backdrop(IntPtr h, int type)
    {
        if (!BackdropSupported) return false;
        var m = new MARGINS { l = -1, r = -1, t = -1, b = -1 }; DwmExtendFrameIntoClientArea(h, ref m);
        int v = type; return DwmSetWindowAttribute(h, 38, ref v, 4) == 0;
    }
}
