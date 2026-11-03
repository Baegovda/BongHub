using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BongHub;

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
    public static POINT Cursor() { GetCursorPos(out var p); return p; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hWnd, bool enable);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);
    /// <summary>Drop pages the process no longer touches (startup code, freed segments). Does not free committed private bytes.</summary>
    public static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch { }
    }
    public static double SystemScale { get { try { return GetDpiForSystem() / 96.0; } catch { return 1; } } }
    public const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_FRAMECHANGED = 0x20;
    public const int WM_MOVE = 0x3, WM_SIZE = 0x5, WM_WINDOWPOSCHANGING = 0x0046, WM_GETMINMAXINFO = 0x0024, WM_EXITSIZEMOVE = 0x232, WM_ENTERSIZEMOVE = 0x231, WM_DISPLAYCHANGE = 0x007E, WM_MOUSEACTIVATE = 0x0021, WM_ACTIVATE = 0x0006;
    public const int MA_NOACTIVATE = 3;
    public const int SIZE_MINIMIZED = 1, SIZE_MAXIMIZED = 2;
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rc, IntPtr data);

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;
    public static RECT Rect(Window w) { GetWindowRect(Handle(w), out var r); return r; }
    public static void Place(Window w, int x, int y, int cw, int ch)
    {
        var h = Handle(w);
        if (AppEnv.Off && h != IntPtr.Zero) OffTarget[h] = (x, y, cw, ch);
        SetWindowPos(h, IntPtr.Zero, x, y, cw, ch, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }
    /// <summary>MONITOR_DEFAULTTONULL: true only when the rect actually meets a monitor. Fully off-screen is false on any layout.</summary>
    public static bool OnAnyMonitor(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return false;
        var r = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        return MonitorFromRect(ref r, 0) != IntPtr.Zero;
    }
    static readonly Dictionary<IntPtr, (int X, int Y, int W, int H)> OffTarget = new();
    public static void RememberOffScreen(IntPtr h, int x, int y)
    {
        if (!AppEnv.Off || h == IntPtr.Zero) return;
        OffTarget.TryGetValue(h, out var prev);
        OffTarget[h] = (x, y, prev.W, prev.H);
    }
    public static void ForgetOffScreen(IntPtr h) { if (h != IntPtr.Zero) OffTarget.Remove(h); }
    /// <summary>
    /// Windows' default <c>ptMaxTrackSize</c> is the monitor work area (1100px tall on the PC that failed).
    /// WPF then does <c>Min(MaxHeight, that track)</c>, so a finite <c>MaxHeight</c> alone still clamps a 1463px note.
    /// This raises the structure before WPF reads it. Do not set <c>handled</c>: WPF must copy the raised track
    /// into its cache and write it back. 3.4.x WinForms does not apply this limit. Not <c>SetWindowsHookEx</c>.
    /// </summary>
    public static void AllowTallWindow(IntPtr lp)
    {
        if (lp == IntPtr.Zero) return;
        try
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lp);
            // ptMaxSize is the maximized rectangle. Leave it so maximize fills the monitor
            // instead of becoming a 30000px window that CaptureRect would store.
            // Min track is physical pixels, same as the note file. A 90 DIP floor becomes 113 px at 125%.
            const int cap = 30000;
            int minW = (int)StickyNoteWindow.NoteMinWidth, minH = (int)StickyNoteWindow.NoteMinHeight;
            if (info.ptMinTrackSize.X != minW) info.ptMinTrackSize.X = minW;
            if (info.ptMinTrackSize.Y != minH) info.ptMinTrackSize.Y = minH;
            if (info.ptMaxTrackSize.X < cap) info.ptMaxTrackSize.X = cap;
            if (info.ptMaxTrackSize.Y < cap) info.ptMaxTrackSize.Y = cap;
            Marshal.StructureToPtr(info, lp, false);
        }
        catch { }
    }

    /// <summary>
    /// While <see cref="AppEnv.Off"/>, refuse a move onto a real monitor and keep the last Place target.
    /// WPF rewrites NaN Left/Top onto the virtual screen; a left-hand monitor then looks like a clamp.
    /// This is a per-window WndProc hook, not SetWindowsHookEx.
    /// </summary>
    public static IntPtr OffScreenHook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (!AppEnv.Off || msg != WM_WINDOWPOSCHANGING || lp == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lp);
            if (!OffTarget.TryGetValue(hwnd, out var t)) return IntPtr.Zero;
            bool changed = false;
            int w = pos.cx > 0 ? pos.cx : t.W;
            int h = pos.cy > 0 ? pos.cy : t.H;
            if ((pos.flags & SWP_NOMOVE) == 0 && w > 0 && h > 0 && OnAnyMonitor(pos.x, pos.y, w, h))
            {
                pos.x = t.X; pos.y = t.Y; changed = true;
            }
            // DIP→device rounding on the viewer can leave the HWND 1px off the stored size. Keep the physical size.
            if ((pos.flags & SWP_NOSIZE) == 0 && t.W > 0 && t.H > 0 && (pos.cx != t.W || pos.cy != t.H))
            {
                pos.cx = t.W; pos.cy = t.H; changed = true;
            }
            if (!changed) return IntPtr.Zero;
            Marshal.StructureToPtr(pos, lp, false);
        }
        catch (Exception e) { AppEnv.Log("offscreen pos: " + e.Message); }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }
    public static RECT WorkAreaAtCursor()
    {
        GetCursorPos(out var p); var m = MonitorFromPoint(p, 2); var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(m, ref mi); return mi.rcWork;
    }
    public static double Scale(Window w) { try { return VisualTreeHelper.GetDpi(w).DpiScaleX; } catch { return 1; } }

    public static long ExStyle(IntPtr h)
    {
        try { return GetWindowLongPtrW(h, GWL_EXSTYLE).ToInt64(); }
        catch { return 0; }
    }

    /// <summary>How many times the last passive show tried to hand focus back, and what the foreground was.</summary>
    public static int PassiveTries { get; private set; }
    public static string PassiveNote { get; private set; } = "";
    public static IntPtr PassivePrevious { get; private set; }

    public static void ResetPassive(IntPtr previous)
    {
        PassiveTries = 0;
        PassiveNote = "";
        PassivePrevious = previous;
    }

    public static void NotePassive(int n, long ms, bool hot)
    {
        PassiveTries = n;
        string bit = ms + "ms:" + (hot ? "hot" : "idle");
        PassiveNote = PassiveNote.Length == 0 ? bit : PassiveNote + " " + bit;
    }

    /// <summary>Class, title, and process of a window, for the smoke log when focus does not settle.</summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "hwnd=0";
        var title = new StringBuilder(240);
        var cls = new StringBuilder(128);
        GetWindowText(hwnd, title, title.Capacity);
        GetClassName(hwnd, cls, cls.Capacity);
        GetWindowThreadProcessId(hwnd, out uint pid);
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch { name = "exited"; }
        return "hwnd=" + hwnd + " class=" + cls + " title=\"" + title + "\" pid=" + pid + " process=" + name;
    }

    public static string FocusEvidence(Window w, long waitedMs)
    {
        var self = Handle(w);
        var fg = GetForegroundWindow();
        long ex = self == IntPtr.Zero ? 0 : ExStyle(self);
        return "waited " + waitedMs + " ms, tries " + PassiveTries + " [" + PassiveNote + "] previous " + Describe(PassivePrevious)
            + " self=" + self + " ex=0x" + ex.ToString("X") + " active=" + w.IsActive + " showActivated=" + w.ShowActivated
            + " fg " + Describe(fg);
    }

    /// <summary>
    /// A passive show (process start, <c>--off</c>, smoke) must not become the foreground window.
    /// If the shell handed us the foreground anyway, give it back. Does not set <c>WS_EX_NOACTIVATE</c>,
    /// so a later user open can still activate.
    /// </summary>
    public static void ReleaseIfStole(IntPtr self, IntPtr previous)
    {
        if (self == IntPtr.Zero || previous == IntPtr.Zero || previous == self) return;
        if (GetForegroundWindow() != self) return;
        uint prevThread = GetWindowThreadProcessId(previous, out _);
        uint appThread = GetCurrentThreadId();
        bool attached = prevThread != 0 && prevThread != appThread && AttachThreadInput(prevThread, appThread, true);
        try { SetForegroundWindow(previous); }
        finally { if (attached) AttachThreadInput(prevThread, appThread, false); }
    }

    /// <summary>
    /// User opened settings (tray, second instance, menu). Another app may own the foreground,
    /// so attach to its thread before <c>SetForegroundWindow</c>. Never call this from <c>--off</c> or smoke.
    /// </summary>
    public static void BringToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || AppEnv.Off) return;
        AllowActivate(hwnd);
        IntPtr fg = GetForegroundWindow();
        uint foreThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint appThread = GetCurrentThreadId();
        bool attached = false;
        if (foreThread != 0 && foreThread != appThread)
            attached = AttachThreadInput(foreThread, appThread, true);
        try
        {
            AllowSetForegroundWindow(-1);
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
            else ShowWindow(hwnd, 5);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(foreThread, appThread, false);
        }
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    /// <summary>3.4.9 clears WS_EX_NOACTIVATE right after show so the first drag is not deferred.</summary>
    public static void AllowActivate(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        long ex = ExStyle(h);
        if ((ex & WS_EX_NOACTIVATE) == 0) return;
        try
        {
            SetWindowLongPtrW(h, GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_NOACTIVATE));
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
    }

    public static NoteLayout.Box[] WorkAreas()
    {
        var list = new List<NoteLayout.Box>();
        MonitorEnumProc cb = (m, _, ref rc, _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref mi))
            {
                var w = mi.rcWork;
                if (w.Width > 0 && w.Height > 0) list.Add(new NoteLayout.Box(w.Left, w.Top, w.Width, w.Height));
            }
            return true;
        };
        try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); }
        catch { }
        GC.KeepAlive(cb);
        return list.ToArray();
    }

    public readonly struct MonitorBox
    {
        public int W { get; init; }
        public int H { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public bool Primary { get; init; }
    }
    /// <summary>Monitor rectangles for the screen-configuration key. This is not the desktop icon list view.</summary>
    public static MonitorBox[] Monitors()
    {
        var list = new List<MonitorBox>();
        MonitorEnumProc cb = (m, _, ref rc, _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref mi))
            {
                var b = mi.rcMonitor;
                list.Add(new MonitorBox { W = b.Width, H = b.Height, X = b.Left, Y = b.Top, Primary = (mi.dwFlags & 1) != 0 });
            }
            return true;
        };
        try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); }
        catch { }
        GC.KeepAlive(cb);
        return list.ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFOEX mi);

    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct DISPLAYCONFIG_RATIONAL { public uint Num, Den; }
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering, targetAvailable, statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_MODE_INFO { public uint infoType, id; public LUID adapterId; public uint a, b, c, d, e, f, g, h, i, j, k, l; }
    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type, size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public ushort outputTechnology, edidManufactureId, edidProductCodeId, connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint modes, [Out] DISPLAYCONFIG_MODE_INFO[] modeArray, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME packet);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME packet);

    /// <summary>Active displays with the Windows Identify number. Call only after the display gate opens.</summary>
    public static List<ScreenInfo> DisplayIdentities()
    {
        var list = new List<ScreenInfo>();
        var byDevice = new Dictionary<string, ScreenInfo>(StringComparer.OrdinalIgnoreCase);
        MonitorEnumProc cb = (m, _, ref rc, _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (GetMonitorInfo(m, ref mi))
            {
                string device = mi.szDevice ?? "";
                var info = new ScreenInfo
                {
                    Number = ScreenBind.WindowsNumber(device),
                    W = mi.rcMonitor.Width, H = mi.rcMonitor.Height,
                    X = mi.rcMonitor.Left, Y = mi.rcMonitor.Top,
                    Primary = (mi.dwFlags & 1) != 0,
                    Device = device,
                    Scale = DpiPercent(m),
                    Orientation = OrientationOf(device)
                };
                list.Add(info);
                if (device.Length > 0) byDevice[device] = info;
            }
            return true;
        };
        try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); } catch { }
        GC.KeepAlive(cb);
        try
        {
            if (GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount) == 0 && pathCount > 0)
            {
                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[Math.Max(1, modeCount)];
                if (QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0)
                {
                    for (int i = 0; i < pathCount; i++)
                    {
                        var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME { header = new DISPLAYCONFIG_DEVICE_INFO_HEADER { type = 1, size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), adapterId = paths[i].sourceInfo.adapterId, id = paths[i].sourceInfo.id } };
                        if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                        string device = src.viewGdiDeviceName ?? "";
                        if (!byDevice.TryGetValue(device, out var info)) continue;
                        var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME { header = new DISPLAYCONFIG_DEVICE_INFO_HEADER { type = 2, size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapterId = paths[i].targetInfo.adapterId, id = paths[i].targetInfo.id } };
                        if (DisplayConfigGetDeviceInfo(ref target) == 0)
                        {
                            info.Path = target.monitorDevicePath ?? "";
                            info.Edid = target.edidManufactureId.ToString("X4") + ":" + target.edidProductCodeId.ToString("X4") + ":" + (target.monitorFriendlyDeviceName ?? "");
                        }
                        int n = ScreenBind.WindowsNumber(device);
                        if (n > 0) info.Number = n;
                    }
                }
            }
        }
        catch { }
        int fallback = 1;
        foreach (var info in list)
            if (info.Number <= 0) info.Number = 1000 + fallback++;
        return list;
    }

    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")] static extern bool EnumDisplaySettings(string device, int mode, IntPtr devMode);

    static int DpiPercent(IntPtr monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX >= 96)
                return (int)Math.Round(dpiX * 100.0 / 96.0);
        }
        catch { }
        return 100;
    }

    /// <summary>DEVMODEW display orientation at the documented offset. 0 when the call fails. Only used from <see cref="DisplayIdentities"/>.</summary>
    static int OrientationOf(string device)
    {
        if (string.IsNullOrEmpty(device)) return 0;
        IntPtr buf = Marshal.AllocHGlobal(256);
        try
        {
            for (int i = 0; i < 256; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt16(buf, 68, 256);
            if (!EnumDisplaySettings(device, -1, buf)) return 0;
            int code = Marshal.ReadInt32(buf, 84);
            return code is 1 or 2 or 3 ? code * 90 : 0;
        }
        catch { return 0; }
        finally { Marshal.FreeHGlobal(buf); }
    }
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
