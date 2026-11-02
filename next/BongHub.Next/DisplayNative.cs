using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BongHub.Next;

/// <summary>
/// Real monitor switch and DDC/CI. Installed only after <see cref="DisplayGate.Arm"/> in normal mode.
/// ChangeDisplaySettingsEx and SetDisplayConfig are not imported. Explorer and DisplayFusion are not stopped.
/// </summary>
public static class DisplayNative
{
    public static void Install()
    {
        if (!DisplayGate.RealAllowed) return;
        DisplaySink.Commit = Commit;
        BrightSink.Monitors = MonitorIds;
        BrightSink.Set = SetBrightness;
    }

    public static void Commit(IReadOnlyList<string> commands)
    {
        DisplayGate.Demand();
        if (commands == null || commands.Count == 0) return;
        string tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "MultiMonitorTool.exe");
        if (!File.Exists(tool)) return;
        string dir = Path.GetDirectoryName(tool) ?? "";
        foreach (var cmd in commands)
        {
            string arg = ToArg(cmd);
            if (arg.Length == 0) continue;
            DisplaySystem.NoteMmt();
            var psi = new ProcessStartInfo(tool, arg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            };
            var started = Process.Start(psi);
            try { if (started != null) started.WaitForExit(20000); }
            finally { started?.Dispose(); }
        }
    }

    static string ToArg(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return "";
        if (cmd.StartsWith("enable ", StringComparison.Ordinal)) return "/enable " + cmd.Substring(7);
        if (cmd.StartsWith("disable ", StringComparison.Ordinal)) return "/disable " + cmd.Substring(8);
        if (cmd.StartsWith("primary ", StringComparison.Ordinal)) return "/SetPrimary " + cmd.Substring(8);
        if (cmd.StartsWith("load ", StringComparison.Ordinal)) return "/LoadConfig \"" + cmd.Substring(5) + "\"";
        return "";
    }

    public static List<string> MonitorIds()
    {
        DisplaySystem.NoteDdc();
        return new List<string>(Handles().Keys);
    }

    public static bool SetBrightness(string id, int pct, out string how)
    {
        how = "";
        DisplaySystem.NoteDdc();
        if (pct < 0) pct = 0;
        if (pct > 100) pct = 100;
        if (!Handles().TryGetValue(id, out IntPtr hm)) { how = "모니터가 아직 켜지지 않음"; return false; }
        try
        {
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out uint n) && n > 0)
            {
                var a = new PHYS[n];
                if (GetPhysicalMonitorsFromHMONITOR(hm, n, a))
                {
                    try
                    {
                        foreach (var ph in a)
                        {
                            if (GetMonitorBrightness(ph.h, out uint min, out _, out uint max) && max > min)
                            {
                                if (SetMonitorBrightness(ph.h, min + (max - min) * (uint)pct / 100)) { how = "DDC/CI"; return true; }
                            }
                            if (GetVCPFeatureAndVCPFeatureReply(ph.h, 0x10, IntPtr.Zero, out _, out uint vmax) && vmax > 0)
                            {
                                if (SetVCPFeature(ph.h, 0x10, vmax * (uint)pct / 100)) { how = "DDC/CI VCP 0x10"; return true; }
                            }
                        }
                    }
                    finally { DestroyPhysicalMonitors(n, a); }
                }
            }
        }
        catch { }
        how = "DDC/CI 미지원";
        return false;
    }

    static Dictionary<string, IntPtr> Handles()
    {
        var d = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        MonitorProc cb = (m, _, _, _) =>
        {
            var mi = new MIEX { cb = Marshal.SizeOf<MIEX>() };
            if (GetMonitorInfo(m, ref mi))
            {
                string id = IdOfDevice(mi.dev);
                if (id.Length > 0 && !d.ContainsKey(id)) d[id] = m;
            }
            return true;
        };
        try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); }
        catch { }
        GC.KeepAlive(cb);
        return d;
    }

    static string IdOfDevice(string dev)
    {
        try
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(dev, 0, ref dd, 0)) return "";
            var parts = (dd.DeviceID ?? "").Split('\\');
            return parts.Length > 1 ? parts[1] : "";
        }
        catch { return ""; }
    }

    delegate bool MonitorProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MIEX
    {
        public int cb;
        public RECT mon, work;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dev;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYS
    {
        public IntPtr h;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc;
    }

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MIEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYS[] monitors);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PHYS[] monitors);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint value);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint current, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
}
