using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BongHub;

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

    /// <summary>
    /// One topology commit. Positions are matched by target path, staged, then applied together.
    /// There is no per-monitor enable and no <c>CDS_SET_PRIMARY</c>, which would slide the origin.
    /// </summary>
    public static void ApplyArrangement(IReadOnlyList<DeskScreen> desired)
    {
        DisplayGate.Demand();
        var known = new List<DeskScreen>();
        foreach (var s in Win32.DisplayIdentities())
            known.Add(new DeskScreen
            {
                StableId = s.Path.Length > 0 ? s.Path : (s.Edid.Length > 0 ? s.Edid : "display:" + s.Number),
                Number = s.Number,
                Device = s.Device ?? "",
                X = s.X, Y = s.Y, W = s.W, H = s.H,
                Primary = s.Primary,
                Enabled = true,
                Scale = s.Scale <= 0 ? 100 : s.Scale
            });
        string error = DeskArrange.Validate(desired);
        if (error.Length > 0) throw new InvalidOperationException(error);
        var steps = DeskArrange.Steps(desired, known);
        if (!StageAll(steps))
            throw new InvalidOperationException("화면 배치를 적용하지 못했어요.");
    }

    static bool StageAll(IReadOnlyList<DeskStep> steps)
    {
        int staged = 0;
        foreach (var step in steps)
        {
            if (!step.Enabled || step.Device.Length == 0) continue;
            if (!Stage(step.Device, step.X, step.Y, step.W, step.H)) return false;
            staged++;
        }
        if (staged == 0) return true;
        CommitStaged();
        return true;
    }

    static bool Stage(string device, int x, int y, int w, int h)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        try
        {
            for (int i = 0; i < 256; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt16(buf, 68, 220);
            if (!EnumDisplaySettings(device, -1, buf)) return false;
            int fields = Marshal.ReadInt32(buf, 72);
            fields |= 0x20 | 0x80000 | 0x100000;
            Marshal.WriteInt32(buf, 72, fields);
            Marshal.WriteInt32(buf, 76, x);
            Marshal.WriteInt32(buf, 80, y);
            Marshal.WriteInt32(buf, 172, w);
            Marshal.WriteInt32(buf, 176, h);
            int rc = ChangeDisplaySettingsEx(device, buf, IntPtr.Zero, CdsUpdate | CdsNoReset, IntPtr.Zero);
            DisplaySystem.ChangeDisplaySettingsExCalls++;
            return rc == 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static void CommitStaged()
    {
        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        DisplaySystem.SetDisplayConfigCalls++;
    }

    const uint CdsUpdate = 0x1;
    const uint CdsNoReset = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string? device, IntPtr mode, IntPtr hwnd, uint flags, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")] static extern bool EnumDisplaySettings(string device, int mode, IntPtr devMode);

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
