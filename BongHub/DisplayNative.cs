using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BongHub;

/// <summary>
/// Real monitor switch and DDC/CI. Installed only after <see cref="DisplayGate.Arm"/> in normal mode.
/// A profile apply goes through CCD <c>SetDisplayConfig</c>. MultiMonitorTool is only the fallback.
/// Explorer and DisplayFusion are not stopped.
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
        CommitChecked(commands);
    }

    /// <summary>
    /// One CCD commit. Disabled monitors lose <c>DISPLAYCONFIG_PATH_ACTIVE</c>.
    /// The primary source position is (0,0). A path that was off is restored with <c>SDC_TOPOLOGY_SUPPLIED</c>
    /// and the saved topology. MultiMonitorTool runs only when CCD returns an error.
    /// </summary>
    public static int TargetNameBytes => Marshal.SizeOf<TARGET_NAME>();
    public static int SourceNameBytes => Marshal.SizeOf<SOURCE_NAME>();
    public static int ModeBytes => Marshal.SizeOf<MODE>();
    public static int PathBytes => Marshal.SizeOf<PATH>();

    public static void ApplyArrangement(IReadOnlyList<DeskScreen> desired, string? name = null)
    {
        DisplayGate.Demand();
        string label = string.IsNullOrWhiteSpace(name) ? "profile" : name!;
        string error = DeskArrange.Validate(desired);
        if (error.Length > 0) throw new InvalidOperationException(error);
        var live = QueryPaths();
        if (live == null)
        {
            AppEnv.Log("display: QueryDisplayConfig failed");
            if (!Fallback(desired, label)) throw new InvalidOperationException("화면 위치를 적용하지 못했어요.");
            return;
        }
        CcdBank.RememberIfFuller(live.Logical);
        var chosen = CcdLayout.Collapse(live.Logical);
        var commit = CcdLayout.Build(chosen, desired, CcdBank.Saved);
        if (!CcdLayout.Covers(chosen, commit))
        {
            AppEnv.Log("display: a monitor has no CCD path");
            if (!Fallback(desired, label)) throw new InvalidOperationException("화면 위치를 적용하지 못했어요.");
            return;
        }
        int rc = ApplyNative(live, chosen, commit, true);
        if (rc != 0)
        {
            AppEnv.Log("display: SetDisplayConfig returned " + rc);
            if (!Fallback(desired, label)) throw new InvalidOperationException("화면 위치를 적용하지 못했어요. (" + rc + ")");
            return;
        }
        AppEnv.Log("display: apply " + label + " via CCD");
    }

    static bool CommitChecked(IReadOnlyList<string>? commands)
    {
        if (commands == null || commands.Count == 0) return false;
        string tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "MultiMonitorTool.exe");
        if (!File.Exists(tool))
        {
            AppEnv.Log("display: MultiMonitorTool.exe missing");
            return false;
        }
        string dir = Path.GetDirectoryName(tool) ?? "";
        bool ok = true;
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
            try
            {
                using var started = Process.Start(psi);
                if (started == null)
                {
                    AppEnv.Log("display: MultiMonitorTool did not start: " + arg);
                    ok = false;
                    continue;
                }
                if (!started.WaitForExit(20000))
                {
                    AppEnv.Log("display: MultiMonitorTool timed out: " + arg);
                    try { started.Kill(true); } catch { }
                    ok = false;
                    continue;
                }
                if (started.ExitCode != 0)
                {
                    AppEnv.Log("display: MultiMonitorTool exit " + started.ExitCode + " for " + arg);
                    ok = false;
                }
            }
            catch (Exception ex)
            {
                AppEnv.Log("display: MultiMonitorTool " + ex.Message);
                ok = false;
            }
        }
        return ok;
    }

    static bool Fallback(IReadOnlyList<DeskScreen> desired, string label)
    {
        var cmds = new List<string>();
        DeskScreen? primary = null;
        foreach (var s in desired)
            if (s.Enabled && s.W > 0 && s.H > 0 && s.Primary) { primary = s; break; }
        if (primary == null)
            foreach (var s in desired)
                if (s.Enabled && s.W > 0 && s.H > 0) { primary = s; break; }
        int ox = primary?.X ?? 0, oy = primary?.Y ?? 0;
        var placed = new List<string>();
        foreach (var s in desired)
        {
            if (!s.Enabled || s.W <= 0 || s.H <= 0 || !MonitorKey.IsGdi(s.Device)) continue;
            placed.Add("Name=" + s.Device + " Left=" + (s.X - ox) + " Top=" + (s.Y - oy) + " Width=" + s.W + " Height=" + s.H);
        }
        bool positions = placed.Count > 0;
        if (positions) cmds.Add("monitors " + string.Join(" | ", placed));
        foreach (var s in desired)
        {
            if (s.StableId.Length == 0 || !s.Enabled) continue;
            cmds.Add("enable " + s.StableId);
        }
        foreach (var s in desired)
        {
            if (s.Enabled && s.Primary && s.StableId.Length > 0)
            {
                cmds.Add("primary " + s.StableId);
                break;
            }
        }
        var off = new List<string>();
        foreach (var s in desired)
            if (!s.Enabled && s.StableId.Length > 0) off.Add(s.StableId);
        if (off.Count > 0) cmds.Add("disable " + string.Join(" ", off));
        if (cmds.Count == 0) return false;
        AppEnv.Log("display: apply " + label + " via MMT");
        if (!positions) AppEnv.Log("display: positions were not applied");
        bool ok = CommitChecked(cmds);
        if (ok) AppEnv.Log("display: MultiMonitorTool applied " + label);
        return ok && positions;
    }

    sealed class NativeSet
    {
        public CcdPath[] Logical = Array.Empty<CcdPath>();
        public PATH[] Paths = Array.Empty<PATH>();
        public MODE[] Modes = Array.Empty<MODE>();
        public string[] Ids = Array.Empty<string>();
    }

    static NativeSet? QueryPaths()
    {
        if (Marshal.SizeOf<MODE>() != 64 || Marshal.SizeOf<PATH>() != 72 || Marshal.SizeOf<TARGET_NAME>() != 420 || Marshal.SizeOf<SOURCE_NAME>() != 84)
        {
            AppEnv.Log("display: CCD struct size mode " + Marshal.SizeOf<MODE>() + " path " + Marshal.SizeOf<PATH>() + " target " + Marshal.SizeOf<TARGET_NAME>() + " source " + Marshal.SizeOf<SOURCE_NAME>());
            return null;
        }
        int rc = GetDisplayConfigBufferSizes(QdcAllPaths, out uint pathCount, out uint modeCount);
        if (rc != 0)
        {
            AppEnv.Log("display: GetDisplayConfigBufferSizes returned " + rc);
            return null;
        }
        if (pathCount == 0)
        {
            AppEnv.Log("display: no display paths");
            return null;
        }
        var paths = new PATH[pathCount];
        var modes = new MODE[Math.Max(1, modeCount)];
        rc = QueryDisplayConfig(QdcAllPaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (rc != 0)
        {
            AppEnv.Log("display: QueryDisplayConfig returned " + rc);
            return null;
        }
        var logical = new List<CcdPath>((int)pathCount);
        var ids = new string[pathCount];
        var cache = new Dictionary<(uint Low, int High, uint Id), TargetFacts>();
        int looked = 0, failed = 0, skipped = 0;
        for (int i = 0; i < pathCount; i++)
        {
            bool active = (paths[i].flags & PathActive) != 0;
            bool available = paths[i].targetInfo.targetAvailable != 0;
            string id = "", edid = "", gdi = "";
            if (active || available)
            {
                var key = (paths[i].targetInfo.adapterId.Low, paths[i].targetInfo.adapterId.High, paths[i].targetInfo.id);
                if (!cache.TryGetValue(key, out var facts))
                {
                    looked++;
                    facts = ReadTarget(paths[i]);
                    cache[key] = facts;
                    if (!facts.Ok) failed++;
                }
                id = facts.Path.Length > 0 ? facts.Path : facts.Edid;
                edid = facts.Edid;
                gdi = SourceName(paths[i]);
            }
            else skipped++;
            ids[i] = id;
            int x = 0, y = 0, w = 0, h = 0;
            uint idx = paths[i].sourceInfo.modeInfoIdx;
            if (idx != ModeInvalid && idx < modeCount && modes[idx].infoType == SourceMode)
            {
                w = (int)modes[idx].sourceWidth;
                h = (int)modes[idx].sourceHeight;
                x = modes[idx].positionX;
                y = modes[idx].positionY;
            }
            logical.Add(new CcdPath
            {
                Index = i,
                StableId = id,
                Edid = edid,
                Gdi = gdi,
                X = x, Y = y, W = w, H = h,
                Active = active,
                Primary = active && x == 0 && y == 0,
                Available = available
            });
        }
        AppEnv.Log("display: paths " + pathCount + " looked " + looked + " failed " + failed + " skipped " + skipped);
        return new NativeSet { Logical = logical.ToArray(), Paths = paths, Modes = modes, Ids = ids };
    }

    static int ApplyNative(NativeSet live, IReadOnlyList<CcdPath> chosen, CcdCommit commit, bool allowRevive)
    {
        bool revive = allowRevive && commit.Kind == "topology";
        var paths = live.Paths;
        var modes = live.Modes;
        uint modeCount = (uint)modes.Length;
        var keep = new HashSet<int>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in chosen)
        {
            if (p.Index >= 0) keep.Add(p.Index);
            if (p.StableId.Length > 0) targets.Add(p.StableId);
        }
        for (int i = 0; i < paths.Length; i++)
        {
            if (!keep.Contains(i))
            {
                string id = i < live.Ids.Length ? live.Ids[i] : "";
                if (id.Length > 0 && targets.Contains(id))
                {
                    var extra = paths[i];
                    extra.flags &= ~PathActive;
                    var srcOff = extra.sourceInfo;
                    var dstOff = extra.targetInfo;
                    srcOff.modeInfoIdx = ModeInvalid;
                    dstOff.modeInfoIdx = ModeInvalid;
                    extra.sourceInfo = srcOff;
                    extra.targetInfo = dstOff;
                    paths[i] = extra;
                }
                continue;
            }
            var want = CcdLayout.Find(live.Ids[i], commit.Paths);
            if (want == null || live.Ids[i].Length == 0) continue;
            var path = paths[i];
            if (!want.Active)
            {
                path.flags &= ~PathActive;
                var src = path.sourceInfo;
                var dst = path.targetInfo;
                src.modeInfoIdx = ModeInvalid;
                dst.modeInfoIdx = ModeInvalid;
                path.sourceInfo = src;
                path.targetInfo = dst;
                paths[i] = path;
                continue;
            }
            path.flags |= PathActive;
            uint idx = path.sourceInfo.modeInfoIdx;
            if (!revive && idx != ModeInvalid && idx < modeCount && modes[idx].infoType == SourceMode)
            {
                modes[idx].positionX = want.X;
                modes[idx].positionY = want.Y;
            }
            else if (allowRevive) revive = true;
            paths[i] = path;
        }
        if (revive)
        {
            int topo = CallSet(paths, 0, null, CcdLayout.ApplyTopology);
            if (topo != 0)
            {
                AppEnv.Log("display: SetDisplayConfig topology returned " + topo);
                return topo;
            }
            var again = QueryPaths();
            if (again == null) return 0;
            var settled = new CcdCommit { Paths = commit.Paths, Kind = "supplied", Flags = CcdLayout.ApplySupplied };
            return ApplyNative(again, CcdLayout.Collapse(again.Logical), settled, false);
        }
        int applied = CallSet(paths, modeCount, modes, CcdLayout.ApplySupplied);
        if (applied != 0) AppEnv.Log("display: SetDisplayConfig supplied returned " + applied);
        return applied;
    }

    static int CallSet(PATH[] paths, uint modeCount, MODE[]? modes, uint flags)
    {
        int rc = SetDisplayConfig((uint)paths.Length, paths, modeCount, modes, flags);
        DisplaySystem.SetDisplayConfigCalls++;
        return rc;
    }

    struct TargetFacts
    {
        public string Path;
        public string Edid;
        public bool Ok;
        public TargetFacts(string path, string edid, bool ok) { Path = path; Edid = edid; Ok = ok; }
    }

    static TargetFacts ReadTarget(PATH path)
    {
        var target = new TARGET_NAME
        {
            header = new DEV_HEADER
            {
                type = 2,
                size = (uint)Marshal.SizeOf<TARGET_NAME>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id
            }
        };
        int rc = DisplayConfigGetDeviceInfo(ref target);
        if (rc != 0) return new TargetFacts("", "", false);
        string devicePath = target.monitorDevicePath ?? "";
        string edid = "";
        if (target.edidManufactureId != 0 || target.edidProductCodeId != 0 || (target.monitorFriendlyDeviceName ?? "").Length > 0)
            edid = target.edidManufactureId.ToString("X4") + ":" + target.edidProductCodeId.ToString("X4") + ":" + (target.monitorFriendlyDeviceName ?? "");
        return new TargetFacts(devicePath, edid, devicePath.Length > 0 || edid.Length > 0);
    }

    static string SourceName(PATH path)
    {
        var src = new SOURCE_NAME
        {
            header = new DEV_HEADER
            {
                type = 1,
                size = (uint)Marshal.SizeOf<SOURCE_NAME>(),
                adapterId = path.sourceInfo.adapterId,
                id = path.sourceInfo.id
            }
        };
        int rc = DisplayConfigGetDeviceInfo(ref src);
        if (rc != 0) return "";
        return src.viewGdiDeviceName ?? "";
    }

    const uint QdcAllPaths = 0x1;
    const uint PathActive = 0x1;
    const uint ModeInvalid = 0xFFFFFFFF;
    const uint SourceMode = 1;

    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Num, Den; }
    [StructLayout(LayoutKind.Sequential)]
    struct SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
    [StructLayout(LayoutKind.Sequential)]
    struct TARGET_INFO
    {
        public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering, targetAvailable, statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PATH { public SOURCE_INFO sourceInfo; public TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct MODE
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public uint sourceWidth;
        [FieldOffset(20)] public uint sourceHeight;
        [FieldOffset(24)] public uint pixelFormat;
        [FieldOffset(28)] public int positionX;
        [FieldOffset(32)] public int positionY;
    }
    [StructLayout(LayoutKind.Sequential)] struct DEV_HEADER { public uint type, size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SOURCE_NAME
    {
        public DEV_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGET_NAME
    {
        public DEV_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PATH[] pathArray, ref uint modes, [Out] MODE[] modeArray, IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint pathCount, [In] PATH[] paths, uint modeCount, [In] MODE[]? modes, uint flags);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME packet);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME packet);

    static string ToArg(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return "";
        if (cmd.StartsWith("enable ", StringComparison.Ordinal)) return "/enable " + cmd.Substring(7);
        if (cmd.StartsWith("disable ", StringComparison.Ordinal)) return "/disable " + cmd.Substring(8);
        if (cmd.StartsWith("primary ", StringComparison.Ordinal)) return "/SetPrimary " + cmd.Substring(8);
        if (cmd.StartsWith("load ", StringComparison.Ordinal)) return "/LoadConfig \"" + cmd.Substring(5) + "\"";
        if (cmd.StartsWith("monitors ", StringComparison.Ordinal))
        {
            var bits = cmd.Substring(9).Split(" | ", StringSplitOptions.RemoveEmptyEntries);
            return "/SetMonitors " + string.Join(" ", bits.Select(b => "\"" + b + "\""));
        }
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
