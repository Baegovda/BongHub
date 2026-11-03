using System.Windows.Threading;
using Microsoft.Win32;

namespace BongHub;

/// <summary>
/// Polls the pointer while the correction is on. There is no mouse hook.
/// Smoke and <c>--off</c> never read a monitor and never call <c>SetCursorPos</c>.
/// </summary>
public static class CursorCrossService
{
    public static int HookInstalls => 0;
    public static int Reads { get; private set; }
    public static int Moves { get; private set; }
    public static bool Active { get; private set; }

    static DispatcherTimer? timer;
    static CursorPoint last;
    static bool hasLast;
    static List<EdgeMonitor> screens = new();
    static DateTime fetched;

    public static bool NativeAllowed => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;

    public static void Sync()
    {
        if (!NativeAllowed || !AppSettings.CursorCross)
        {
            Stop();
            return;
        }
        if (Active) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Active = true;
    }

    public static void Stop()
    {
        timer?.Stop();
        timer = null;
        Active = false;
        hasLast = false;
        screens = new List<EdgeMonitor>();
    }

    static void Tick()
    {
        if (!GetCursorPos(out var p)) return;
        var now = new CursorPoint(p.X, p.Y);
        if (!hasLast) { last = now; hasLast = true; return; }
        if ((DateTime.UtcNow - fetched).TotalSeconds >= 1)
        {
            screens = ReadScreens();
            fetched = DateTime.UtcNow;
        }
        var next = CursorCross.Step(screens, last, now);
        if (next != now && SetCursorPos(next.X, next.Y)) Moves++;
        last = next;
    }

    static List<EdgeMonitor> ReadScreens()
    {
        Reads++;
        var list = new List<EdgeMonitor>();
        try
        {
            foreach (var s in Win32.DisplayIdentities())
            {
                var (wmm, hmm) = EdidMm(s.Device);
                list.Add(new EdgeMonitor
                {
                    Id = s.Path.Length > 0 ? s.Path : s.Device,
                    X = s.X, Y = s.Y, W = s.W, H = s.H,
                    WidthMm = wmm, HeightMm = hmm,
                    Orientation = s.Orientation
                });
            }
        }
        catch (Exception ex) { AppEnv.Log("cursor cross: " + ex.Message); }
        return list;
    }

    /// <summary>EDID bytes 21 and 22 are the image size in centimetres.</summary>
    static (int W, int H) EdidMm(string? device)
    {
        if (string.IsNullOrWhiteSpace(device)) return (0, 0);
        try
        {
            var dd = new DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(device, 0, ref dd, 0)) return (0, 0);
            string model = ModelOf(dd.DeviceID);
            if (model.Length == 0) return (0, 0);
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + model);
            if (root == null) return (0, 0);
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name + @"\Device Parameters");
                if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 23) continue;
                int w = edid[21], h = edid[22];
                if (w > 0 && h > 0) return (w * 10, h * 10);
            }
        }
        catch { }
        return (0, 0);
    }

    static string ModelOf(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var parts = id.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        int hash = id.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
        if (hash >= 0)
        {
            string rest = id[(hash + 8)..];
            int cut = rest.IndexOf('#');
            return cut > 0 ? rest[..cut] : rest;
        }
        return "";
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string? device, uint num, ref DISPLAY_DEVICE dd, uint flags);
}
