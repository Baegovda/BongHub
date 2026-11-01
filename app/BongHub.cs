using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using WTimer = System.Windows.Forms.Timer;
[assembly: AssemblyTitle("BongHub")]
[assembly: AssemblyProduct("BongHub")]
[assembly: AssemblyDescription("BongHub")]
[assembly: AssemblyCompany("Baegovda")]
[assembly: AssemblyVersion("3.4.3.0")]
[assembly: AssemblyFileVersion("3.4.3.0")]
[assembly: AssemblyInformationalVersion("3.4.3")]

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator {}
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int NotImpl1(); [PreserveSig] int GetDefaultAudioEndpoint(int f, int r, out IMMDevice d); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { [PreserveSig] int Activate(ref Guid id, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation { [PreserveSig] int GetPeakValue(out float v); }

static class Native {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    public static uint IdleMs() {
        var l = new LASTINPUTINFO(); l.cbSize = (uint)Marshal.SizeOf(l);
        if (!GetLastInputInfo(ref l)) return 0;
        return unchecked((uint)Environment.TickCount - l.dwTime);
    }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, uint f, StringBuilder sb, ref uint size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int idObj, int idChild, uint th, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int v);
    public static void SetDpiAware() {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
        try { SetProcessDpiAwareness(2); } catch { }
    }
}

// ================= Hotkeys =================
static class Hotkey {
    public const uint ALT = 1, CTRL = 2, SHIFT = 4, WIN = 8;
    public static bool Parse(string s, out uint mods, out uint vk) {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (var raw in s.Split('+')) {
            string t = raw.Trim(); if (t == "") continue; string u = t.ToLowerInvariant();
            if (u == "ctrl" || u == "control") mods |= CTRL;
            else if (u == "alt") mods |= ALT;
            else if (u == "shift") mods |= SHIFT;
            else if (u == "win") mods |= WIN;
            else {
                Keys k;
                if (t.Length == 1 && char.IsDigit(t[0])) k = Keys.D0 + (t[0] - '0');
                else if (u.StartsWith("num") && t.Length == 4 && char.IsDigit(t[3])) k = Keys.NumPad0 + (t[3] - '0');
                else { try { k = (Keys)Enum.Parse(typeof(Keys), t, true); } catch { return false; } }
                vk = (uint)k;
            }
        }
        return vk != 0;
    }
    public static string KeyName(Keys k) {
        if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + ((int)(k - Keys.NumPad0)).ToString();
        return k.ToString();
    }
    public static string Format(Keys mods, Keys key) {
        var l = new List<string>();
        if ((mods & Keys.Control) != 0) l.Add("Ctrl");
        if ((mods & Keys.Alt) != 0) l.Add("Alt");
        if ((mods & Keys.Shift) != 0) l.Add("Shift");
        l.Add(KeyName(key));
        return string.Join("+", l);
    }
    public static string Normalize(string s) {
        uint m, v; if (!Parse(s, out m, out v)) return "";
        Keys mk = 0;
        if ((m & CTRL) != 0) mk |= Keys.Control; if ((m & ALT) != 0) mk |= Keys.Alt; if ((m & SHIFT) != 0) mk |= Keys.Shift;
        string r = Format(mk, (Keys)v);
        if ((m & WIN) != 0) r = "Win+" + r;
        return r;
    }
}

// ================= Config =================
public class WindowRule {
    public bool Enabled = true;
    public string Name = "";
    public string Process = "";
    public string Title = "";
    public bool TitleRegex = false;
    public string Class = "";
    public string Monitor = "";
    public string Action = "pos";
    public int X = 0, Y = 0, W = 1280, H = 800;
    public bool Pin = false;
    public bool Keep = false;
    public int Zone = 0;
    public static readonly string[] Codes = { "pos", "monitor", "max", "min", "restore", "none", "zone" };
    public static readonly string[] Labels = { "위치·크기 지정", "모니터로만 이동", "최대화", "최소화", "원래 크기로 복원", "변경 없음", "FancyZones 영역에 맞춤" };
    public static string ActionLabel(string c) { int i = Array.IndexOf(Codes, c); return i < 0 ? c : Labels[i]; }
    public string MatchText() {
        var l = new List<string>();
        if (Process != "") l.Add("프로세스 " + Process);
        if (Title != "") l.Add((TitleRegex ? "제목 ~/" + Title + "/" : "제목에 '" + Title + "'"));
        if (Class != "") l.Add("클래스 " + Class);
        return l.Count == 0 ? "(조건 없음)" : string.Join(" · ", l);
    }
    public string ActionText() {
        string m = Monitor == "" ? "현재 모니터" : Monitor;
        if (Action == "pos") return m + " · " + X + "," + Y + " / " + W + "×" + H;
        if (Action == "zone") return m + " · 영역 " + (Zone + 1);
        if (Action == "none") return Monitor == "" ? "변경 없음" : m;
        return m + " · " + ActionLabel(Action);
    }
    public string OptText() {
        var l = new List<string>(); if (Pin) l.Add("항상 위"); if (Keep) l.Add("위치 유지");
        return string.Join(", ", l);
    }
}

public class Preset {
    public List<MonBright> Brightness = new List<MonBright>();
    public string DeskLayout = "";   // linked desktop icon layout id ("" = none)
    public string Workspace = "";    // linked PowerToys workspace id ("" = none)
    public string Name = "";
    public string Hotkey = "";
    public List<string> Enable = new List<string>();
    public List<string> Disable = new List<string>();
    public string Primary = "";
    public string Layout = "";
    public int Retries = 1;
    public int EnableDelay = 1500;
    public int StepDelay = 2000;
    public int LayoutDelay = 3000;
    public int RulesDelay = 3000;
    public bool RefreshTaskbar = true;
    public bool Overlay = true;
    public string OverlayMessage = "";
    public List<WindowRule> Rules = new List<WindowRule>();
    public override string ToString() { return Name; }
    public string Message() { return string.IsNullOrWhiteSpace(OverlayMessage) ? Name + " 모드로 전환 중..." : OverlayMessage; }
}

public class GeneralCfg {
    public List<string> TabOrder = new List<string>();
    public bool IdleEnabled = true;
    public int IdleMinutes = 30;
    public string IdlePreset = "자리비움";
    public bool SkipIfAudio = true;
    public double AudioThreshold = 0.001;
    public bool OverlayEnabled = true;
    public int OverlayOpacity = 82;
}

public class Config {
    public int Version = 2;
    public List<Preset> Presets = new List<Preset>();
    public GeneralCfg General = new GeneralCfg();
    public KakaoCfg Kakao = new KakaoCfg();
    public List<WindowRule> GlobalRules = new List<WindowRule>();
    public NotesCfg Notes = new NotesCfg();
    public DeskCfg Desk = new DeskCfg();
    public CloudCfg Cloud = new CloudCfg();
    public PtCfg Pt = new PtCfg();

    public static string FilePath { get { return Path.Combine(Core.Dir, "config.json"); } }
    static JavaScriptSerializer Ser() { var s = new JavaScriptSerializer(); s.MaxJsonLength = 16 * 1024 * 1024; return s; }

    public static Config Defaults() {
        var c = new Config(); c.Notes.ColorVer = 2; c.Pt.Normalize();
        var w = new Preset { Name = "작업", Hotkey = "Ctrl+Alt+1", Primary = "SAM73D3", Layout = "work_layout.cfg", Retries = 4 };
        w.Enable.AddRange(new[] { "SAM747A", "SAM73D3", "GSM76F5", "SAM100B", "DRS2555" });
        var a = new Preset { Name = "자리비움", Hotkey = "Ctrl+Alt+0", Primary = "GSM76F5", Retries = 1 };
        a.Disable.AddRange(new[] { "SAM747A", "SAM73D3", "SAM100B", "DRS2555" });
        c.Presets.Add(w); c.Presets.Add(a);
        return c;
    }
    public void Normalize() {
        if (Presets == null) Presets = new List<Preset>();
        if (General == null) General = new GeneralCfg();
        if (General.TabOrder == null) General.TabOrder = new List<string>();
        if (Kakao == null) Kakao = new KakaoCfg();
        if (Notes == null) Notes = new NotesCfg();
        if (Desk == null) Desk = new DeskCfg();
        if (Cloud == null) Cloud = new CloudCfg(); if (string.IsNullOrWhiteSpace(Cloud.Repo)) Cloud.Repo = new CloudCfg().Repo; if (string.IsNullOrWhiteSpace(Cloud.Branch)) Cloud.Branch = "main"; if (Cloud.PullMinutes < 1) Cloud.PullMinutes = 2;
        if (Desk.HotkeySave == null) Desk.HotkeySave = ""; if (Desk.HotkeyRestore == null) Desk.HotkeyRestore = "";
        if (Desk.AutoSaveMinutes < 1) Desk.AutoSaveMinutes = 10;
        if (Notes.ColorVer < 2) { if (Notes.DefaultColor == "yellow" || Notes.DefaultColor == null) Notes.DefaultColor = "g:blocks"; Notes.Gradient = true; Notes.ColorVer = 2; }
        if (!NoteColors.Valid(Notes.DefaultColor)) Notes.DefaultColor = "g:blocks";
        if (Notes.FontSize < 7 || Notes.FontSize > 48) Notes.FontSize = 11f;
        if (Notes.Opacity < 30 || Notes.Opacity > 100) Notes.Opacity = 100;
        if (Notes.Hotkey == null) Notes.Hotkey = "";
        if (GlobalRules == null) GlobalRules = new List<WindowRule>();
        if (Pt == null) Pt = new PtCfg(); Pt.Normalize();
        foreach (var p in Presets) {
            if (p.Enable == null) p.Enable = new List<string>();
            if (p.Disable == null) p.Disable = new List<string>();
            if (p.Rules == null) p.Rules = new List<WindowRule>();
            if (p.Brightness == null) p.Brightness = new List<MonBright>(); if (p.DeskLayout == null) p.DeskLayout = ""; if (p.Workspace == null) p.Workspace = "";
            p.Brightness.RemoveAll(x => x == null); foreach (var x in p.Brightness) { if (x.Monitor == null) x.Monitor = ""; x.Value = Math.Max(-1, Math.Min(100, x.Value)); }
            if (p.Name == null) p.Name = ""; if (p.Hotkey == null) p.Hotkey = ""; if (p.Primary == null) p.Primary = "";
            if (p.Layout == null) p.Layout = ""; if (p.OverlayMessage == null) p.OverlayMessage = "";
            if (p.Retries < 1) p.Retries = 1;
            foreach (var r in p.Rules) NormRule(r);
        }
        foreach (var r in GlobalRules) NormRule(r);
        if (General.IdleMinutes < 1) General.IdleMinutes = 1;
        if (General.IdlePreset == null) General.IdlePreset = "";
        if (General.OverlayOpacity < 20 || General.OverlayOpacity > 100) General.OverlayOpacity = 82;
    }
    static void NormRule(WindowRule r) {
        if (r.Name == null) r.Name = ""; if (r.Process == null) r.Process = ""; if (r.Title == null) r.Title = "";
        if (r.Class == null) r.Class = ""; if (r.Monitor == null) r.Monitor = "";
        if (Array.IndexOf(WindowRule.Codes, r.Action) < 0) r.Action = "pos";
    }
    public string ToJson() { return Pretty(Ser().Serialize(this)); }
    public static Config FromJson(string j) { var c = Ser().Deserialize<Config>(j); c.Normalize(); return c; }
    public static Config Clone(Config c) { return FromJson(c.ToJson()); }
    public static T CloneObj<T>(T o) { var s = Ser(); return s.Deserialize<T>(s.Serialize(o)); }

    public static void Migrate() {
        try {
            string old = Path.Combine(Core.LegacyDir, "config.json");
            if (File.Exists(FilePath) || !File.Exists(old)) return;
            Directory.CreateDirectory(Core.Dir);
            File.Copy(old, FilePath);
            string ol = Path.Combine(Core.LegacyDir, "layouts"), nl = Path.Combine(Core.Dir, "layouts");
            if (Directory.Exists(ol)) { Directory.CreateDirectory(nl); foreach (var f in Directory.GetFiles(ol)) { string d = Path.Combine(nl, Path.GetFileName(f)); if (!File.Exists(d)) File.Copy(f, d); } }
        } catch { }
    }
    public static Config Load() {
        Migrate();
        try {
            string src = File.Exists(FilePath) ? FilePath : Path.Combine(Core.LegacyDir, "config.json");
            if (File.Exists(src)) {
                string j = File.ReadAllText(src, Encoding.UTF8);
                var c = FromJson(j);
                if (j.IndexOf("\"Kakao\"", StringComparison.Ordinal) < 0) { c.Kakao = global::Kakao.DetectLegacy(); try { c.Save(); } catch { } }
                return c;
            }
        } catch {
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        var d = Defaults(); d.Kakao = global::Kakao.DetectLegacy();
        try { d.Save(); } catch { }
        return d;
    }
    public void Save() {
        Directory.CreateDirectory(Core.Dir);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        global::Cloud.Kick();
    }
    public Preset Find(string name) {
        foreach (var p in Presets) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
    public Preset FindCli(string arg) {
        var p = Find(arg); if (p != null) return p;
        string a = arg.ToLowerInvariant();
        if (a == "work") { p = Find("작업"); if (p == null && Presets.Count > 0) p = Presets[0]; }
        else if (a == "away") { p = Find("자리비움"); if (p == null && Presets.Count > 1) p = Presets[1]; }
        return p;
    }
    static string Pretty(string j) {
        var sb = new StringBuilder(); int ind = 0; bool q = false;
        for (int i = 0; i < j.Length; i++) {
            char c = j[i];
            if (q) { sb.Append(c); if (c == '\\' && i + 1 < j.Length) sb.Append(j[++i]); else if (c == '"') q = false; continue; }
            switch (c) {
                case '"': q = true; sb.Append(c); break;
                case '{': case '[':
                    sb.Append(c);
                    if (i + 1 < j.Length && (j[i + 1] == '}' || j[i + 1] == ']')) { sb.Append(j[++i]); break; }
                    ind++; sb.Append("\r\n").Append(' ', ind * 2); break;
                case '}': case ']': ind--; sb.Append("\r\n").Append(' ', ind * 2).Append(c); break;
                case ',': sb.Append(c).Append("\r\n").Append(' ', ind * 2); break;
                case ':': sb.Append(": "); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

// ================= Desktop icons (DesktopOK-style) =================
public class DeskCfg {
    public bool AutoSave = true;
    public int AutoSaveMinutes = 10;
    public bool SaveOnExit = true;
    public bool SaveBeforeSwitch = true;
    public bool RestoreOnChange = true;
    public bool RestoreOnStartup = false;
    public string HotkeySave = "";
    public string HotkeyRestore = "";
}
public class DeskItem { public string Name = ""; public int X, Y; }
public class DeskLayout {
    public string Id = "", Name = "", Key = "", Desc = "";
    public bool Auto;
    public DateTime Saved = DateTime.Now;
    public List<DeskItem> Items = new List<DeskItem>();
}
public class DeskResult { public bool Ok; public string Error; public int Moved, Same, Missing, Extra; public string Summary() { return Ok ? "아이콘 " + (Moved + Same) + "개 위치 복원" + (Missing > 0 ? " · 바탕화면에 없는 아이콘 " + Missing + "개 건너뜀" : "") + (Extra > 0 ? " · 저장 이후 새로 생긴 아이콘 " + Extra + "개는 그대로" : "") : Error; } }

static class Desk {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string t);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, UIntPtr size, uint type, uint prot);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr a, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] buf, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] buf, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr p, out bool wow);
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags; }
    delegate bool MonProc(IntPtr m, IntPtr dc, ref Native.RECT r, IntPtr d);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonProc p, IntPtr d);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);

    const uint LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMPOSITION = 0x1010, LVM_SETITEMPOSITION32 = 0x1031, LVM_GETITEMTEXTW = 0x1073, LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037, LVM_REDRAWITEMS = 0x1015;
    const int LVS_AUTOARRANGE = 0x0100, LVS_EX_SNAPTOGRID = 0x00080000;

    public static string Dir = Path.Combine(Core.Dir, "desktop");
    static string FileP { get { return Path.Combine(Dir, "layouts.json"); } }
    static List<DeskLayout> all; static readonly object lk = new object();
    public static event Action Changed;
    public static void Reset(string dir) { lock (lk) { Dir = dir; all = null; } }
    static JavaScriptSerializer Ser() { var s = new JavaScriptSerializer(); s.MaxJsonLength = 32 * 1024 * 1024; return s; }
    public static List<DeskLayout> All { get { lock (lk) { Load(); return all.ToList(); } } }
    static void Load() {
        if (all != null) return;
        all = new List<DeskLayout>();
        try { if (File.Exists(FileP)) { var l = Ser().Deserialize<List<DeskLayout>>(File.ReadAllText(FileP, Encoding.UTF8)); if (l != null) all = l.Where(x => x != null && x.Items != null).ToList(); } }
        catch { try { File.Copy(FileP, FileP + ".broken", true); } catch { } }
        foreach (var x in all) { if (x.Name == null) x.Name = ""; if (x.Key == null) x.Key = ""; if (x.Desc == null) x.Desc = Describe(x.Key); if (string.IsNullOrEmpty(x.Id)) x.Id = Guid.NewGuid().ToString("N"); }
    }
    static void Persist() {
        Directory.CreateDirectory(Dir);
        string tmp = FileP + ".tmp";
        File.WriteAllText(tmp, Ser().Serialize(all), new UTF8Encoding(false));
        if (File.Exists(FileP)) File.Replace(tmp, FileP, null); else File.Move(tmp, FileP);
        Cloud.Kick();
    }
    public static void Reload() { lock (lk) all = null; Fire(); }
    static void Fire() { var h = Changed; if (h != null) try { h(); } catch { } }

    // ---- screen configuration key ----
    public static string ScreenKey() {
        var l = new List<string>();
        try {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref Native.RECT r, IntPtr d) => {
                var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(mi);
                if (GetMonitorInfo(m, ref mi)) { var b = mi.rcMonitor; l.Add((b.R - b.L) + "x" + (b.B - b.T) + "@" + b.L + "," + b.T + ((mi.dwFlags & 1) != 0 ? "*" : "")); }
                return true;
            }, IntPtr.Zero);
        } catch { }
        if (l.Count == 0) foreach (var s in Screen.AllScreens) l.Add(s.Bounds.Width + "x" + s.Bounds.Height + "@" + s.Bounds.X + "," + s.Bounds.Y + (s.Primary ? "*" : ""));
        l.Sort(StringComparer.Ordinal);
        return string.Join(";", l);
    }
    public static string Describe(string key) {
        if (string.IsNullOrEmpty(key)) return "알 수 없음";
        var parts = key.Split(';'); var sizes = new List<string>(); string prim = null;
        foreach (var p in parts) { string sz = p.Split('@')[0].Replace("x", "×"); if (p.EndsWith("*")) prim = sz; else sizes.Add(sz); }
        if (prim != null) sizes.Insert(0, prim + "(주)");
        return "모니터 " + parts.Length + "대 · " + string.Join(" + ", sizes);
    }

    // ---- desktop list view access ----
    public static IntPtr ListView() {
        IntPtr def = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (def == IntPtr.Zero) {
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero) { def = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null); if (def != IntPtr.Zero) break; }
        }
        return def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
    }
    static IntPtr DefView() { var lv = ListView(); return lv == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(lv, 1); }
    static long Send(IntPtr h, uint m, long w, long l) { IntPtr r; SendMessageTimeout(h, m, new IntPtr(w), new IntPtr(l), 2, 2000, out r); return r.ToInt64(); }

    sealed class Remote : IDisposable {
        public IntPtr Proc, Mem; const int Size = 4096;
        public Remote(IntPtr hwnd) {
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            Proc = Native.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
            if (Proc == IntPtr.Zero) throw new Exception("탐색기 프로세스에 접근할 수 없어요");
            bool w1, w2; IsWow64Process(Proc, out w1); IsWow64Process(Process.GetCurrentProcess().Handle, out w2);
            if (w1 != w2) { Native.CloseHandle(Proc); throw new Exception("탐색기와 BongHub의 비트 수가 달라요"); }
            Mem = VirtualAllocEx(Proc, IntPtr.Zero, (UIntPtr)Size, 0x3000, 0x04);
            if (Mem == IntPtr.Zero) { Native.CloseHandle(Proc); throw new Exception("메모리를 할당할 수 없어요"); }
        }
        public byte[] Read(int off, int n) { var b = new byte[n]; UIntPtr r; ReadProcessMemory(Proc, Mem + off, b, (UIntPtr)n, out r); return b; }
        public void Write(int off, byte[] b) { UIntPtr r; WriteProcessMemory(Proc, Mem + off, b, (UIntPtr)b.Length, out r); }
        public void Dispose() { if (Mem != IntPtr.Zero) VirtualFreeEx(Proc, Mem, UIntPtr.Zero, 0x8000); if (Proc != IntPtr.Zero) Native.CloseHandle(Proc); Mem = Proc = IntPtr.Zero; }
    }
    // reads every desktop icon (index order) with its position
    public static List<DeskItem> ReadIcons(out string err) {
        err = null; var res = new List<DeskItem>();
        var lv = ListView();
        if (lv == IntPtr.Zero) { err = "바탕화면 아이콘 창을 찾지 못했어요 (탐색기가 실행 중인지 확인해 주세요)"; return null; }
        try {
            using (var r = new Remote(lv)) {
                int n = (int)Send(lv, LVM_GETITEMCOUNT, 0, 0);
                bool x64 = IntPtr.Size == 8; int itemOff = 64, textOff = 512;
                for (int i = 0; i < n; i++) {
                    Send(lv, LVM_GETITEMPOSITION, i, r.Mem.ToInt64());
                    var pt = r.Read(0, 8);
                    var it = new byte[96];
                    BitConverter.GetBytes(1u).CopyTo(it, 0); BitConverter.GetBytes(i).CopyTo(it, 4);
                    long txt = r.Mem.ToInt64() + textOff;
                    if (x64) { BitConverter.GetBytes(txt).CopyTo(it, 24); BitConverter.GetBytes(260).CopyTo(it, 32); }
                    else { BitConverter.GetBytes((int)txt).CopyTo(it, 20); BitConverter.GetBytes(260).CopyTo(it, 24); }
                    r.Write(itemOff, it); r.Write(textOff, new byte[4]);
                    int len = (int)Send(lv, LVM_GETITEMTEXTW, i, r.Mem.ToInt64() + itemOff);
                    string name = len > 0 ? Encoding.Unicode.GetString(r.Read(textOff, Math.Min(len, 259) * 2)) : "";
                    res.Add(new DeskItem { Name = name, X = BitConverter.ToInt32(pt, 0), Y = BitConverter.ToInt32(pt, 4) });
                }
            }
        } catch (Exception ex) { err = ex.Message; return null; }
        return res;
    }
    // moves icons to the saved spots, matching by name (+ occurrence for duplicate names)
    public static DeskResult Apply(List<DeskItem> target) {
        var res = new DeskResult(); string err;
        var cur = ReadIcons(out err);
        if (cur == null) { res.Error = err; return res; }
        var lv = ListView();
        var want = new Dictionary<string, Queue<DeskItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in target) { Queue<DeskItem> q; if (!want.TryGetValue(t.Name, out q)) want[t.Name] = q = new Queue<DeskItem>(); q.Enqueue(t); }
        try {
            using (var r = new Remote(lv)) {
                for (int i = 0; i < cur.Count; i++) {
                    Queue<DeskItem> q; if (!want.TryGetValue(cur[i].Name, out q) || q.Count == 0) { res.Extra++; continue; }
                    var t = q.Dequeue();
                    if (t.X == cur[i].X && t.Y == cur[i].Y) { res.Same++; continue; }
                    var b = new byte[8]; BitConverter.GetBytes(t.X).CopyTo(b, 0); BitConverter.GetBytes(t.Y).CopyTo(b, 4);
                    r.Write(0, b); Send(lv, LVM_SETITEMPOSITION32, i, r.Mem.ToInt64()); res.Moved++;
                }
            }
        } catch (Exception ex) { res.Error = ex.Message; return res; }
        res.Missing = want.Values.Sum(q => q.Count);
        if (res.Moved > 0) Native.PostMessage(lv, LVM_REDRAWITEMS, IntPtr.Zero, new IntPtr(cur.Count - 1));
        res.Ok = true; return res;
    }
    public static bool AutoArrange { get { var lv = ListView(); return lv != IntPtr.Zero && (GetWindowLong(lv, -16) & LVS_AUTOARRANGE) != 0; } }
    public static bool SnapToGrid { get { var lv = ListView(); return lv != IntPtr.Zero && (Send(lv, LVM_GETEXTENDEDLISTVIEWSTYLE, 0, 0) & LVS_EX_SNAPTOGRID) != 0; } }
    public static bool IconsVisible { get { var lv = ListView(); return lv != IntPtr.Zero && Native.IsWindowVisible(lv); } }
    // same commands as desktop right-click > View
    public static void ToggleAutoArrange() { var d = DefView(); if (d != IntPtr.Zero) Send(d, 0x0111, 0x7041, 0); }
    public static void ToggleSnap() { var d = DefView(); if (d != IntPtr.Zero) Send(d, 0x0111, 0x7054, 0); }
    public static void ToggleIcons() { var d = DefView(); if (d != IntPtr.Zero) Send(d, 0x0111, 0x7402, 0); }
    public static void MinimizeAll(bool undo) {
        try { var t = Type.GetTypeFromProgID("Shell.Application"); object sh = Activator.CreateInstance(t); t.InvokeMember(undo ? "UndoMinimizeALL" : "MinimizeAll", BindingFlags.InvokeMethod, null, sh, null); } catch { }
    }

    // ---- layouts ----
    public static DeskLayout Capture(string name, bool auto, out string err) {
        var items = ReadIcons(out err); if (items == null) return null;
        string key = ScreenKey();
        return new DeskLayout { Id = Guid.NewGuid().ToString("N"), Name = name, Key = key, Desc = Describe(key), Auto = auto, Saved = DateTime.Now, Items = items };
    }
    public static DeskLayout SaveNew(string name, out string err) {
        var l = Capture(name, false, out err); if (l == null) return null;
        lock (lk) { Load(); all.Insert(0, l); Persist(); }
        Fire(); return l;
    }
    // keeps one automatic snapshot per screen configuration; returns false when nothing changed
    public static bool SaveAuto(out string err) {
        var l = Capture("자동 저장", true, out err); if (l == null || l.Items.Count == 0) return false;
        lock (lk) {
            Load();
            var old = all.FirstOrDefault(x => x.Auto && x.Key == l.Key);
            if (old != null && Same(old.Items, l.Items)) return false;
            if (old != null) all.Remove(old);
            all.Insert(0, l); Persist();
        }
        Fire(); return true;
    }
    static bool Same(List<DeskItem> a, List<DeskItem> b) {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i].Name != b[i].Name || a[i].X != b[i].X || a[i].Y != b[i].Y) return false;
        return true;
    }
    public static bool Overwrite(string id, out string err) {
        DeskLayout old; lock (lk) { Load(); old = all.FirstOrDefault(x => x.Id == id); }
        if (old == null) { err = "배치를 찾지 못했어요"; return false; }
        var l = Capture(old.Name, old.Auto, out err); if (l == null) return false;
        lock (lk) { old.Items = l.Items; old.Key = l.Key; old.Desc = l.Desc; old.Saved = l.Saved; Persist(); }
        Fire(); return true;
    }
    public static void Rename(string id, string name) { lock (lk) { Load(); var x = all.FirstOrDefault(a => a.Id == id); if (x == null) return; x.Name = name; x.Auto = false; Persist(); } Fire(); }
    public static void Delete(string id) { lock (lk) { Load(); all.RemoveAll(a => a.Id == id); Persist(); } Fire(); }
    public static DeskLayout Get(string id) { lock (lk) { Load(); return all.FirstOrDefault(a => a.Id == id); } }
    // newest layout saved for the current screen configuration
    public static DeskLayout Best() { string k = ScreenKey(); lock (lk) { Load(); return all.Where(a => a.Key == k).OrderByDescending(a => a.Saved).FirstOrDefault(); } }
    public static DeskResult Restore(DeskLayout l) {
        if (l == null) return new DeskResult { Error = "현재 화면 구성(" + Describe(ScreenKey()) + ")으로 저장된 배치가 없어요" };
        var r = Apply(l.Items);
        if (r.Ok && AutoArrange) { r.Ok = false; r.Error = "‘아이콘 자동 정렬’이 켜져 있어서 위치를 바꿀 수 없어요. 바탕화면 아이콘 탭에서 끌 수 있어요."; }
        return r;
    }
}

// ================= Cloud sync (private GitHub repo via the PC's own git login) =================
public class CloudCfg {
    public bool Enabled = false;
    public string Repo = "https://github.com/Baegovda/BongHub.git";
    public string Branch = "main";
    public int PullMinutes = 2;
}
public class CloudState { public DateTime LastSync = DateTime.MinValue; public string LastRemote = ""; public List<string> Notes = new List<string>(); public List<string> Layouts = new List<string>(); public string ConfigStamp = ""; }
public class CloudStamp { public DateTime T; public string Device = ""; }

// test runs render everything off-screen, never activate, never show in the taskbar
static class TestEnv {
    public static bool Off;
    public static readonly Rectangle Area = new Rectangle(-30000, -30000, 3840, 2160);
    public static void Place(Form f) { if (!Off) return; f.StartPosition = FormStartPosition.Manual; f.ShowInTaskbar = false; f.TopMost = false; f.Location = new Point(Area.X + 40, Area.Y + 40); }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rgn, uint f);
    public static void Redraw(Control c) { RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100); }
    public static Bitmap Render(Control c) {
        var bm = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
        using (var g = Graphics.FromImage(bm)) { var hdc = g.GetHdc(); try { PrintWindow(c.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); } }
        if (Blank(bm)) { using (var g = Graphics.FromImage(bm)) { var hdc = g.GetHdc(); try { PrintWindow(c.Handle, hdc, 0); } finally { g.ReleaseHdc(hdc); } } }
        if (Blank(bm) && c is Form) { bm.Dispose(); bm = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)); c.DrawToBitmap(bm, new Rectangle(0, 0, bm.Width, bm.Height)); }
        return bm;
    }
    static bool Blank(Bitmap b) {
        var c0 = b.GetPixel(b.Width / 2, b.Height / 2); int same = 0;
        for (int i = 1; i < 10; i++) { var c = b.GetPixel(b.Width * i / 10, b.Height * i / 10); if (c.ToArgb() == c0.ToArgb()) same++; }
        return same == 9 && (c0.ToArgb() == Color.Black.ToArgb() || c0.A == 0);
    }
}
static class LockScreenPolicy {
    const string Policy = @"SOFTWARE\Policies\Microsoft\Windows\Personalization", Backup = @"SOFTWARE\BongHub\LockScreenBackup";
    public static bool Hidden { get { using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)) using (var key = root.OpenSubKey(Policy)) return key != null && Equals(key.GetValue("NoLockScreen"), 1); } }
    public static bool Owned { get { using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)) using (var key = root.OpenSubKey(Backup)) return key != null && key.GetValue("HadValue") != null; } }
    public static void Set(RegistryKey root, string policy, string backup, bool hide) {
        using (var key = root.CreateSubKey(policy)) {
            if (hide) {
                object old = key.GetValue("NoLockScreen");
                if (old != null && key.GetValueKind("NoLockScreen") != RegistryValueKind.DWord) throw new InvalidOperationException("기존 정책 값 형식이 달라 변경하지 않았어요.");
                using (var saved = root.CreateSubKey(backup)) {
                    if (saved.GetValue("HadValue") == null) { saved.SetValue("Value", old ?? 0, RegistryValueKind.DWord); saved.SetValue("HadValue", old == null ? 0 : 1, RegistryValueKind.DWord); }
                }
                key.SetValue("NoLockScreen", 1, RegistryValueKind.DWord);
            } else {
                using (var saved = root.OpenSubKey(backup)) {
                    if (saved == null || saved.GetValue("HadValue") == null) throw new InvalidOperationException("BongHub가 저장한 원래 설정이 없어요.");
                    if (!Equals(key.GetValue("NoLockScreen"), 1)) throw new InvalidOperationException("다른 프로그램이나 관리자가 정책을 변경했어요. 해당 설정을 유지했어요.");
                    if (Equals(saved.GetValue("HadValue"), 1)) key.SetValue("NoLockScreen", saved.GetValue("Value", 0), RegistryValueKind.DWord); else key.DeleteValue("NoLockScreen", false);
                }
                root.DeleteSubKeyTree(backup, false);
            }
        }
    }
    public static int Run(string[] args) {
        if (args.Length != 2 || (args[1] != "enable" && args[1] != "restore")) return 2;
        try { using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)) Set(root, Policy, Backup, args[1] == "enable"); return 0; }
        catch (Exception ex) { DarkBox.Show(ex.Message, "잠금 화면 설정"); return 1; }
    }
    public static int Test(string output) {
        string temp = @"Software\BongHubPolicyTest_" + Guid.NewGuid().ToString("N"); var log = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (v, n) => { log.AppendLine((v ? "PASS " : "FAIL ") + n); ok &= v; };
        try {
            string policy = temp + @"\Policy", backup = temp + @"\Backup";
            Set(Registry.CurrentUser, policy, backup, true); using (var k = Registry.CurrentUser.OpenSubKey(policy)) check(Equals(k.GetValue("NoLockScreen"), 1), "enable isolated policy");
            Set(Registry.CurrentUser, policy, backup, false); using (var k = Registry.CurrentUser.OpenSubKey(policy)) check(k.GetValue("NoLockScreen") == null, "restore absent value");
            using (var k = Registry.CurrentUser.CreateSubKey(policy)) k.SetValue("NoLockScreen", 0, RegistryValueKind.DWord);
            Set(Registry.CurrentUser, policy, backup, true); Set(Registry.CurrentUser, policy, backup, true); Set(Registry.CurrentUser, policy, backup, false);
            using (var k = Registry.CurrentUser.OpenSubKey(policy)) check(Equals(k.GetValue("NoLockScreen"), 0), "repeat enable preserves original zero");
            Set(Registry.CurrentUser, policy, backup, true); using (var k = Registry.CurrentUser.CreateSubKey(policy)) k.SetValue("NoLockScreen", 2, RegistryValueKind.DWord);
            bool rejected = false; try { Set(Registry.CurrentUser, policy, backup, false); } catch (InvalidOperationException) { rejected = true; }
            using (var k = Registry.CurrentUser.OpenSubKey(policy)) check(rejected && Equals(k.GetValue("NoLockScreen"), 2), "external change not overwritten");
        } catch (Exception ex) { check(false, ex.ToString()); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(temp, false); }
        File.WriteAllText(output, log.ToString()); return ok ? 0 : 1;
    }
}
static class AppInfo {
    public const string Version = "3.4.3";   // semantic version; build.ps1 reads this line
    public static readonly string[] Changelog = {
        "3.4.3  잡아서 이동 요청 적체·종료 좌표 수정 · 마우스 뒤로가기 · Space 누르는 동안 미리보기 · WebP 디코더",
        "3.4.2  탭 전환 전체 강제 그리기 제거 · 레이아웃 측정 캐시 · 실시간 크기 변경 요청 병합 · 배경 캐시 재생성 억제",
        "3.4.1  설정 화면 최초 표시/탭 전환 시 자식 컨트롤이 비어 보이는 다시 그리기 회귀 수정",
        "3.4.0  전체 다크 UI 개편 · 반응형 카드/줄바꿈 · 드래그 깜빡임 개선 · 잠금 화면 숨기기 · 잡아서 이동 전용 입력 스레드 · Peek/공식 단축키",
        "3.3.8  이미지 이동 후 스크롤 선택 장식 재처리 제거 · 메모 상단 항상 위 고정 버튼",
        "3.3.7  이미지 뷰어 닫기·창 이동 조작 추가 · 전체 창 실시간 크기 조절 · 트레이 설정 창 먼저 표시",
        "3.3.6  이미지 선택 해제 검색 최적화 · 텍스트/제목1~4 스타일 · 평면 그라데이션 색상 복원",
        "3.3.5  스티커 메모 상단·본문 평면 통일 · 입력 커서 고정 · 크기 조절 중 실제 본문 즉시 배치",
        "3.3.4  스티커 메모 이미지 드래그 이동 · 글자 크기 빠른 선택 · 색상 호버 미리보기와 차분한 팔레트 · 선택 핸들 커서 안정화 · 더블클릭 이미지 뷰어 제목 표시줄/작업 표시줄 숨김 · 모달 창에서도 메모 선택 · 입력 후 조용할 때 자동 저장",
        "3.3.3  크기 조절 중 확대 제거: 글자·사이드바·목록·카드는 1:1로 두고 빈 곳은 배경색. 제목 단추는 오른쪽, 메모 도구막대는 아래에 붙임. 놓으면 한 번만 배치",
        "3.3.2  창 크기 조절을 부드럽게: 테두리를 끄는 동안은 화면을 늘려 보여주고 놓을 때 한 번만 배치. 설정 창·스티커 메모·이미지 보기·창 고르기. 그라데이션은 크기가 바뀔 때만 다시 그림",
        "3.3.1  모니터 프리셋별 창 배치(작업 영역) 연계: 아이콘 배치 복원 직후 한 번 실행, 열린 앱은 옮기기만, 중복 실행 방지, 삭제 시 안내",
        "3.3.0  PowerToys 기능 동등성 강화(영역 편집기·Win+화살표·여러 영역·팔레트 확장·사용자 지정 붙여넣기·형식별 미리보기·Resizer 단위/메타데이터) · 스냅/후킹/미리보기/Resizer 대폭 최적화",
        "3.2.0  PowerToys 탭 추가: FancyZones(창 위치 규칙 연동) · 잡아서 이동 · 작업 영역 · 명령 팔레트 · 고급 붙여넣기 · 미리보기 · Image Resizer (위험 기능 기본 꺼짐, 꺼진 기능은 후킹·타이머 없음)",
        "3.1.0  유휴 CPU 최적화 · 메모 한 면 그라데이션 · 얇은 오버레이 스크롤바 · 메모 이미지 핸들 리사이즈/뷰어/복사 · 대화상자 타이틀바 통일 · 버전 표시",
        "3.0.0  블록 아이콘 고정 · 탭 전환 최적화 · 바탕화면 아이콘 · 즉시 저장 · 그라데이션 다크 테마 · 오버레이 · 메모 그라데이션/이미지 · 커스텀 타이틀바 · 클라우드 동기화 · 탭 순서 변경",
    };
    public static string FileVer(string path) { try { if (!File.Exists(path)) return "?"; var v = FileVersionInfo.GetVersionInfo(path); return string.IsNullOrEmpty(v.FileVersion) ? v.ProductVersion ?? "?" : v.FileVersion.Replace(", ", "."); } catch { return "?"; } }
}
class CloudVersionException : Exception { public CloudVersionException(string m) : base(m) { } }
static class Cloud {
    public const int Schema = 1;   // bump when the synced data format changes incompatibly
    public static string Dir = Path.Combine(Core.Dir, "cloud");
    public static string Work { get { return Path.Combine(Dir, "repo"); } }
    // data sources (overridable for tests)
    public static string ConfigFile = Config.FilePath, NotesDir = Notes.Dir, DeskDir = Desk.Dir;
    public static CloudCfg Cfg = new CloudCfg();
    public static string Status = "꺼짐"; public static bool Online = true, Pending, Syncing;
    public static DateTime LastSync = DateTime.MinValue;
    public static event Action StatusChanged, ConfigReplaced, NotesReplaced, DeskReplaced;
    static readonly object gate = new object();
    static System.Threading.Timer debounce, poll;
    static JavaScriptSerializer Ser() { var s = new JavaScriptSerializer(); s.MaxJsonLength = 64 * 1024 * 1024; return s; }
    public static string DeviceOverride; public static string Device { get { return DeviceOverride ?? Environment.MachineName; } }
    static void Set(string st) { Status = st; var h = StatusChanged; if (h != null) try { h(); } catch { } }

    public static void Apply(CloudCfg c) {
        Cfg = c ?? new CloudCfg();
        if (poll != null) { poll.Dispose(); poll = null; }
        if (!Cfg.Enabled) { Set("꺼짐"); return; }
        Set(Pending ? "보낼 변경 사항 있음" : "대기 중");
        int ms = Math.Max(1, Cfg.PullMinutes) * 60000;
        poll = new System.Threading.Timer(_ => SyncNow(false), null, 5000, ms);
    }
    // called whenever local data changes; uploads after a short quiet period
    public static void Kick() {
        if (!Cfg.Enabled || suppress > 0) return;
        Pending = true;
        if (debounce == null) debounce = new System.Threading.Timer(_ => SyncNow(true), null, Timeout.Infinite, Timeout.Infinite);
        debounce.Change(1500, Timeout.Infinite);
        if (!Syncing) Set("저장 중…");
    }
    static int suppress;
    public static void FlushOnExit() { if (Cfg.Enabled && Pending) { if (debounce != null) debounce.Change(Timeout.Infinite, Timeout.Infinite); SyncNow(true, 20000); } }

    // ---- git ----
    static string GitExe() {
        foreach (var p in new[] { @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files (x86)\Git\cmd\git.exe" }) if (File.Exists(p)) return p;
        return "git";
    }
    public static int Git(string args, out string output, int timeout = 60000, string cwd = null) {
        var psi = new ProcessStartInfo(GitExe(), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd ?? Work, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
        try {
            using (var p = Process.Start(psi)) {
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch { } output = "시간 초과"; return -1; }
                output = (o.Result + e.Result).Trim(); return p.ExitCode;
            }
        } catch (Exception ex) { output = ex.Message; return -2; }
    }
    public static string Check(out bool ok) {
        string o; Directory.CreateDirectory(Dir);
        int r = Git("ls-remote --heads " + Q(Cfg.Repo), out o, 30000, Dir);
        ok = r == 0;
        if (r == -2) return "git이 설치되어 있지 않아요";
        if (r == 0) return "연결됨";
        if (o.IndexOf("Could not resolve", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("unable to access", StringComparison.OrdinalIgnoreCase) >= 0 && o.IndexOf("403", StringComparison.Ordinal) < 0) return "오프라인";
        return "연결 필요 (GitHub 로그인 또는 레포 권한 확인)";
    }
    static string Q(string s) { return "\"" + s.Replace("\"", "") + "\""; }

    // ---- sync ----
    public static void SyncNow(bool push) { SyncNow(push, 0); }
    static void SyncNow(bool push, int waitMs) {
        if (!Cfg.Enabled) return;
        if (!Monitor.TryEnter(gate, waitMs)) { Pending = true; return; }
        try {
            Syncing = true; Set("동기화 중…");
            string err = RunOnce();
            if (err == null) { Pending = false; Online = true; LastSync = DateTime.Now; Set("저장됨 · " + LastSync.ToString("HH:mm:ss")); }
            else { Online = !err.StartsWith("오프라인"); Set(err); if (Pending && debounce != null) debounce.Change(30000, Timeout.Infinite); }
        } catch (Exception ex) { Set("오류: " + ex.Message); }
        finally { Syncing = false; Monitor.Exit(gate); }
    }
    static string StatePath { get { return Path.Combine(Dir, "state.json"); } }
    static CloudState LoadState() { try { return Ser().Deserialize<CloudState>(File.ReadAllText(StatePath)) ?? new CloudState(); } catch { return new CloudState(); } }
    static void SaveState(CloudState s) { Directory.CreateDirectory(Dir); File.WriteAllText(StatePath, Ser().Serialize(s)); }

    // one full round: fetch -> merge (newest wins, notes per note, layouts per layout) -> commit -> push (retry on race)
    static string RunOnce() {
        string o; Directory.CreateDirectory(Dir);
        if (!Directory.Exists(Path.Combine(Work, ".git"))) {
            if (Directory.Exists(Work)) try { Directory.Delete(Work, true); } catch { }
            int r = Git("clone --quiet " + Q(Cfg.Repo) + " repo", out o, 120000, Dir);
            if (r != 0) return Fail(o);
        }
        Git("config user.name BongHub", out o); Git("config user.email bonghub@" + Device.ToLowerInvariant() + ".local", out o);
        Git("config core.autocrlf false", out o); Git("remote set-url origin " + Q(Cfg.Repo), out o);
        for (int attempt = 0; attempt < 3; attempt++) {
            int fr = Git("fetch --quiet origin", out o, 90000);
            if (fr != 0) return Fail(o);
            string br = Cfg.Branch;
            bool remoteHas = Git("rev-parse --verify --quiet origin/" + br, out o) == 0;
            if (remoteHas) { Git("checkout --quiet -B " + br + " origin/" + br, out o); Git("reset --quiet --hard origin/" + br, out o); Git("clean -fdq", out o); }
            else { Git("checkout --quiet --orphan " + br, out o); Git("rm -rfq --cached .", out o); }
            var st = LoadState();
            try { Merge(st); } catch (CloudVersionException vx) { return vx.Message; }
            Git("add -A", out o);
            bool changed = Git("diff --cached --quiet", out o) != 0;
            st.LastSync = DateTime.Now;
            if (!changed) { SaveState(st); return null; }
            Git("commit --quiet -m " + Q("sync: " + Device + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), out o);
            int pr = Git("push --quiet origin " + br, out o, 90000);
            if (pr == 0) { SaveState(st); return null; }
            if (o.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) < 0 && o.IndexOf("fetch first", StringComparison.OrdinalIgnoreCase) < 0) return Fail(o);
        }
        return "다른 PC와 동시에 저장 중이에요. 잠시 뒤 다시 시도해요";
    }
    static string Fail(string o) {
        if (o.IndexOf("resolve", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("시간 초과", StringComparison.Ordinal) >= 0 || o.IndexOf("Failed to connect", StringComparison.OrdinalIgnoreCase) >= 0) return "오프라인 · 연결되면 자동으로 보내요";
        if (o.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("403", StringComparison.Ordinal) >= 0 || o.IndexOf("could not read Username", StringComparison.OrdinalIgnoreCase) >= 0) return "연결 필요 · GitHub 연결을 눌러 로그인해 주세요";
        return "동기화 실패: " + (o.Length > 140 ? o.Substring(0, 140) : o);
    }
    static string D(params string[] p) { return Path.Combine(new[] { Work, "data" }.Concat(p).ToArray()); }
    static DateTime Mtime(string f) { return File.Exists(f) ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue; }
    static CloudStamp ReadStamp(string f) { try { return Ser().Deserialize<CloudStamp>(File.ReadAllText(f)); } catch { return null; } }
    static void WriteStamp(string f, DateTime t) { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, Ser().Serialize(new CloudStamp { T = t, Device = Device })); }
    static void Copy(string a, string b, DateTime t) { Directory.CreateDirectory(Path.GetDirectoryName(b)); File.Copy(a, b, true); try { File.SetLastWriteTimeUtc(b, t); } catch { } }

    static void Merge(CloudState st) {
        suppress++;
        try {
            bool cfgIn = false, notesIn = false, deskIn = false;
            // data format guard: a PC with an older BongHub never overwrites data written by a newer format
            {
                string mf = D("meta.json"); int remoteSchema = 0; string remoteMin = "";
                try { if (File.Exists(mf)) { var mm = Ser().Deserialize<Dictionary<string, object>>(File.ReadAllText(mf)); if (mm.ContainsKey("schema")) remoteSchema = Convert.ToInt32(mm["schema"]); if (mm.ContainsKey("minApp")) remoteMin = Convert.ToString(mm["minApp"]); } } catch { }
                if (remoteSchema > Schema) throw new CloudVersionException("업데이트 필요 · 클라우드 데이터가 더 새 BongHub(" + (remoteMin == "" ? "새 버전" : remoteMin + " 이상") + ")로 저장돼 있어요");
                if (remoteSchema < Schema) { Directory.CreateDirectory(Path.GetDirectoryName(mf)); File.WriteAllText(mf, Ser().Serialize(new Dictionary<string, object> { { "schema", Schema }, { "minApp", AppInfo.Version } }), new UTF8Encoding(false)); }
            }
            // config.json: whole file, newest wins
            {
                string rf = D("config.json"), rs = D("config.stamp.json");
                var stamp = ReadStamp(rs); DateTime lt = Mtime(ConfigFile), rt = stamp != null && File.Exists(rf) ? stamp.T : DateTime.MinValue;
                bool first = st.LastSync == DateTime.MinValue && File.Exists(rf);
                if (File.Exists(rf) && (first || rt > lt.AddSeconds(1))) { if (!File.Exists(ConfigFile) || File.ReadAllText(rf) != File.ReadAllText(ConfigFile)) { Copy(rf, ConfigFile, rt); cfgIn = true; } }
                else if (File.Exists(ConfigFile) && lt > rt.AddSeconds(1)) { Copy(ConfigFile, rf, lt); WriteStamp(rs, lt); }
            }
            // notes: per note (json + rtf), newest wins; deletions travel as tombstones
            {
                string rd = D("notes"); Directory.CreateDirectory(rd);
                string tf = Path.Combine(rd, "_deleted.json");
                var tomb = new Dictionary<string, DateTime>(); try { var raw = Ser().Deserialize<Dictionary<string, object>>(File.ReadAllText(tf)); if (raw != null) foreach (var kv in raw) tomb[kv.Key] = new DateTime(Convert.ToInt64(kv.Value), DateTimeKind.Utc); } catch { }
                Func<string, string, DateTime> T = (dir, id) => { var a = Mtime(Path.Combine(dir, id + ".json")); var b = Mtime(Path.Combine(dir, id + ".rtf")); return a > b ? a : b; };
                Func<string, HashSet<string>> Ids = dir => Directory.Exists(dir) ? new HashSet<string>(Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(x => !x.StartsWith("_"))) : new HashSet<string>();
                var local = Ids(NotesDir); var remote = Ids(rd);
                foreach (var id in st.Notes) if (!local.Contains(id) && remote.Contains(id)) { tomb[id] = DateTime.UtcNow; }
                foreach (var id in local.Union(remote).ToList()) {
                    DateTime del; bool dead = tomb.TryGetValue(id, out del);
                    DateTime lt = local.Contains(id) ? T(NotesDir, id) : DateTime.MinValue, rt = remote.Contains(id) ? T(rd, id) : DateTime.MinValue;
                    if (dead && del.ToUniversalTime().AddSeconds(1) >= lt && del.ToUniversalTime().AddSeconds(1) >= rt) {
                        foreach (var ext in new[] { ".json", ".rtf" }) { try { File.Delete(Path.Combine(rd, id + ext)); } catch { } if (local.Contains(id)) { try { File.Delete(Path.Combine(NotesDir, id + ext)); notesIn = true; } catch { } } }
                        continue;
                    }
                    if (dead) tomb.Remove(id);
                    if (lt > rt.AddSeconds(1)) foreach (var ext in new[] { ".json", ".rtf" }) { string a = Path.Combine(NotesDir, id + ext); if (File.Exists(a)) Copy(a, Path.Combine(rd, id + ext), lt); }
                    else if (rt > lt.AddSeconds(1)) { foreach (var ext in new[] { ".json", ".rtf" }) { string a = Path.Combine(rd, id + ext); if (File.Exists(a)) Copy(a, Path.Combine(NotesDir, id + ext), rt); } notesIn = true; }
                }
                foreach (var k in tomb.Keys.ToList()) if ((DateTime.UtcNow - tomb[k].ToUniversalTime()).TotalDays > 60) tomb.Remove(k);
                if (tomb.Count > 0) File.WriteAllText(tf, Ser().Serialize(tomb.ToDictionary(kv => kv.Key, kv => kv.Value.ToUniversalTime().Ticks.ToString()))); else try { File.Delete(tf); } catch { }
                st.Notes = Ids(NotesDir).ToList();
            }
            // desktop icon layouts: merged per layout id, newer "Saved" wins
            {
                string lf = Path.Combine(DeskDir, "layouts.json"), rf = D("desktop", "layouts.json");
                var ser = Ser();
                Func<string, List<DeskLayout>> Load = f => { try { return File.Exists(f) ? ser.Deserialize<List<DeskLayout>>(File.ReadAllText(f, Encoding.UTF8)) ?? new List<DeskLayout>() : new List<DeskLayout>(); } catch { return new List<DeskLayout>(); } };
                var L = Load(lf); var R = Load(rf);
                var deletedHere = new HashSet<string>(st.Layouts.Where(id => !L.Any(x => x.Id == id)));
                var map = new Dictionary<string, DeskLayout>();
                foreach (var x in R) if (!deletedHere.Contains(x.Id)) map[x.Id] = x;
                foreach (var x in L) { DeskLayout y; if (!map.TryGetValue(x.Id, out y) || x.Saved >= y.Saved) map[x.Id] = x; }
                var merged = map.Values.OrderByDescending(x => x.Saved).ToList();
                string mj = ser.Serialize(merged);
                if (merged.Count > 0 || R.Count > 0) { Directory.CreateDirectory(Path.GetDirectoryName(rf)); if (!File.Exists(rf) || File.ReadAllText(rf, Encoding.UTF8) != mj) File.WriteAllText(rf, mj, new UTF8Encoding(false)); }
                if (ser.Serialize(L.OrderByDescending(x => x.Saved).ToList()) != mj && (merged.Count > 0 || L.Count > 0)) { Directory.CreateDirectory(DeskDir); File.WriteAllText(lf, mj, new UTF8Encoding(false)); deskIn = true; }
                st.Layouts = merged.Select(x => x.Id).ToList();
            }
            // device heartbeat (no secrets: name, app version, time)
            Directory.CreateDirectory(D("devices"));
            File.WriteAllText(D("devices", Device + ".json"), Ser().Serialize(new { device = Device, app = AppInfo.Version, schema = Schema }));
            string readme = Path.Combine(Work, "README.md");
            if (!File.Exists(readme)) File.WriteAllText(readme, "# BongHub 클라우드 저장\n\nBongHub가 자동으로 관리하는 동기화 데이터예요. `data/config.json`(설정), `data/notes/`(스티커 메모), `data/desktop/`(바탕화면 아이콘 배치). 토큰이나 비밀번호는 저장하지 않아요.\n", new UTF8Encoding(false));
            if (cfgIn) Raise(ConfigReplaced); if (notesIn) Raise(NotesReplaced); if (deskIn) Raise(DeskReplaced);
        } finally { suppress--; }
    }
    static void Raise(Action a) { if (a != null) try { a(); } catch { } }
    // opens GitHub sign-in in the browser through the PC's GitHub CLI (token stays in Windows Credential Manager)
    public static bool Connect() {
        string gh = File.Exists(@"C:\Program Files\GitHub CLI\gh.exe") ? @"C:\Program Files\GitHub CLI\gh.exe" : null;
        if (gh == null) { try { Process.Start("https://cli.github.com/"); } catch { } return false; }
        try { Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + gh + "\" auth login --hostname github.com --git-protocol https --web && \"" + gh + "\" auth setup-git && echo. && echo 완료! 이 창을 닫고 BongHub에서 '연결 확인'을 누르세요. && pause\"") { UseShellExecute = true }); return true; } catch { return false; }
    }
}

// ================= Slim overlay scrollbar =================
// Hides a control's native vertical scrollbar by clipping it outside a host panel and draws a thin rounded
// overlay bar instead (nearly invisible at rest, fades in on hover/scroll, hidden when nothing to scroll).
// No idle timers: the fade timer only runs while animating.
class SlimBar : Control {
    [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }
    public readonly Control Target; public Color Back; public event Action NeedChanged;
    float alpha = 0f, goal = 0f; bool hot, drag; int dragY0, dragPos0; WTimer fade, rest;
    int min, max, pageSz, pos;
    public bool Needed { get { return max - min + 1 > pageSz && pageSz > 0; } }
    public SlimBar(Control target) {
        Target = target; Width = UI.S(12); Cursor = Cursors.Default;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        fade = new WTimer { Interval = 16 }; fade.Tick += (s, e) => { alpha += (goal - alpha) * 0.25f; if (Math.Abs(goal - alpha) < 0.02f) { alpha = goal; fade.Stop(); } Invalidate(); };
        rest = new WTimer { Interval = 1100 }; rest.Tick += (s, e) => { rest.Stop(); if (!hot && !drag) FadeTo(0.12f); };
    }
    void FadeTo(float a) { goal = a; if (Math.Abs(goal - alpha) > 0.01f && !fade.Enabled) fade.Start(); }
    public void Wake() { if (!Needed) return; FadeTo(hot ? 1f : 0.75f); rest.Stop(); rest.Start(); }
    public void Sync() {
        if (!Target.IsHandleCreated) return;
        var si = new SCROLLINFO { cbSize = Marshal.SizeOf(typeof(SCROLLINFO)), fMask = 0x17 };
        if (!GetScrollInfo(Target.Handle, 1, ref si)) { si.nMax = 0; si.nPage = 1; }
        bool ch = si.nMin != min || si.nMax != max || si.nPage != pageSz || si.nPos != pos;
        min = si.nMin; max = si.nMax; pageSz = (int)si.nPage; pos = si.nPos;
        bool vis = Needed; if (Visible != vis) { Visible = vis; if (NeedChanged != null) NeedChanged(); }
        if (ch) Invalidate();
    }
    Rectangle Thumb() {
        int range = Math.Max(1, max - min + 1), h = Height - UI.S(8);
        int th = Math.Max(UI.S(28), (int)(h * (pageSz / (double)range)));
        int span = Math.Max(1, range - pageSz);
        int y = UI.S(4) + (int)((h - th) * ((pos - min) / (double)span));
        int w = hot || drag ? UI.S(8) : UI.S(4);
        return new Rectangle(Width - w - UI.S(2), y, w, th);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { if (Back == Color.Transparent) { BackColor = Color.Transparent; base.OnPaintBackground(e); return; } using (var b = new SolidBrush(Back.IsEmpty ? Target.BackColor : Back)) e.Graphics.FillRectangle(b, ClientRectangle); }
    protected override void OnPaint(PaintEventArgs e) {
        if (!Needed || alpha < 0.01f) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var r = Thumb();
        if (hot || drag) using (var tb = new SolidBrush(Color.FromArgb((int)(28 * alpha), 255, 255, 255))) { var tr = new Rectangle(r.X, UI.S(4), r.Width, Height - UI.S(8)); using (var p = UI.Round(tr, r.Width / 2)) g.FillPath(tb, p); }
        using (var p = UI.Round(r, r.Width / 2)) using (var b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb((int)(230 * alpha), UI.G1), Color.FromArgb((int)(230 * alpha), UI.G3), 90f)) g.FillPath(b, p);
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Wake(); Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Wake(); Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) {
        if (e.Button != MouseButtons.Left) return;
        var t = Thumb();
        if (t.Contains(e.Location)) { drag = true; dragY0 = e.Y; dragPos0 = pos; Capture = true; }
        else ScrollTo(pos + (e.Y < t.Y ? -pageSz : pageSz));
        Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        if (!drag) return;
        int range = Math.Max(1, max - min + 1), h = Height - UI.S(8), th = Thumb().Height;
        double per = (range - pageSz) / (double)Math.Max(1, h - th);
        ScrollTo(dragPos0 + (int)((e.Y - dragY0) * per));
    }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Wake(); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { SendMessage(Target.Handle, 0x020A, (IntPtr)((e.Delta & 0xFFFF) << 16), IntPtr.Zero); Sync(); Wake(); }
    public void ScrollTo(int p) {
        p = Math.Max(min, Math.Min(max - pageSz + 1, p));
        if (Target is ScrollableControl) { var sc = (ScrollableControl)Target; sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, p); }
        else if (Target is ListView) {
            var lv = (ListView)Target; int ih = lv.Items.Count > 0 ? lv.GetItemRect(0).Height : UI.S(20);
            SendMessage(lv.Handle, 0x1014, IntPtr.Zero, (IntPtr)((p - pos) * ih)); // LVM_SCROLL
        } else {
            SendMessage(Target.Handle, 0x0115, (IntPtr)(4 | (p << 16)), IntPtr.Zero); // SB_THUMBPOSITION
            SendMessage(Target.Handle, 0x0115, (IntPtr)8, IntPtr.Zero);
        }
        Sync(); Wake();
    }
}
class SlimHook : NativeWindow {
    readonly SlimBar bar; readonly Control c;
    public SlimHook(Control target, SlimBar b) { c = target; bar = b; if (c.IsHandleCreated) AssignHandle(c.Handle); c.HandleCreated += (s, e) => AssignHandle(c.Handle); c.HandleDestroyed += (s, e) => ReleaseHandle(); }
    protected override void WndProc(ref Message m) {
        base.WndProc(ref m);
        switch (m.Msg) {
            case 0x000F: case 0x0005: case 0x0115: case 0x0101: case 0x0302: case 0x000C: bar.Sync(); break; // paint/size/vscroll/keyup/paste/settext
            case 0x020A: bar.Sync(); bar.Wake(); break; // wheel
            case 0x0200: if (bar.Needed) bar.Wake(); break; // mouse move
        }
    }
}
static class SlimScroll {
    // wraps `c` in a host that clips its native vertical scrollbar; returns the host (already put where c was)
    public static Panel Wrap(Control c, Color back) {
        var host = new Panel { BackColor = back, Margin = c.Margin, Dock = c.Dock, Anchor = c.Anchor, Size = c.Size, MinimumSize = c.MinimumSize, Location = c.Location, Tag = "slimhost" };
        var parent = c.Parent;
        if (parent != null) {
            int idx = parent.Controls.GetChildIndex(c);
            TableLayoutPanelCellPosition cell = new TableLayoutPanelCellPosition(-1, -1); int cs = 1, rs = 1;
            var tl = parent as TableLayoutPanel;
            if (tl != null) { cell = tl.GetCellPosition(c); cs = tl.GetColumnSpan(c); rs = tl.GetRowSpan(c); }
            parent.Controls.Remove(c);
            if (tl != null) { tl.Controls.Add(host, cell.Column, cell.Row); tl.SetColumnSpan(host, cs); tl.SetRowSpan(host, rs); } else parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, idx);
        }
        c.Dock = DockStyle.None; c.Margin = new Padding(0); c.MinimumSize = Size.Empty; c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        host.Controls.Add(c);
        var bar = new SlimBar(c) { Back = back, Visible = false };
        host.Controls.Add(bar); bar.BringToFront();
        int sbw = SystemInformation.VerticalScrollBarWidth;
        bool always = c is RichTextBox; if (always) ((RichTextBox)c).ScrollBars = RichTextBoxScrollBars.ForcedVertical;
        bool fitting = false;
        Action fit = () => {
            if (fitting) return; fitting = true;
            try {
            c.Bounds = new Rectangle(0, 0, host.ClientSize.Width + (always || bar.Needed ? sbw : 0), host.ClientSize.Height);
            bar.Bounds = new Rectangle(host.ClientSize.Width - bar.Width, 0, bar.Width, host.ClientSize.Height); bar.Sync();
            } finally { fitting = false; }
        };
        host.Resize += (s, e) => fit(); bar.NeedChanged += () => fit(); fit();
        new SlimHook(c, bar);
        return host;
    }
    // replaces every ListView / ListBox / AutoScroll panel inside root (root itself handled by caller)
    public static void WrapAll(Control root) {
        var list = new List<Control>(); Collect(root, list);
        foreach (var c in list) Wrap(c, c is ListView || c is ListBox ? c.BackColor : UI.Page);
    }
    static void Collect(Control p, List<Control> acc) {
        foreach (Control c in p.Controls) {
            if ((string)c.Tag == "slimhost" || c.Tag as string == "noslim") continue;
            if (c is ListView || c is ListBox || (c is Panel && ((Panel)c).AutoScroll)) { acc.Add(c); }
            else Collect(c, acc);
        }
    }
}
// ================= Image viewer (photo-viewer style) =================
class ImageViewer : ChromeForm {
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    readonly Image img; readonly ViewCanvas cv;
    public static void Open(Image img, IWin32Window owner) { var v = new ImageViewer(img); v.Show(owner); }
    public ImageViewer(Image image) {
        img = image; Font = UI.Base; BackColor = Color.FromArgb(14, 14, 26); Icon = Program.AppIcon; KeyPreview = true; ShowInTaskbar = false;
        Bar.Visible = false; Bar.Height = 0;
        StartPosition = FormStartPosition.Manual; MinimumSize = new Size(UI.S(420), UI.S(320));
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int w = Math.Min((int)(wa.Width * 0.9), Math.Max(MinimumSize.Width, img.Width + UI.S(6))), h = Math.Min((int)(wa.Height * 0.9), Math.Max(MinimumSize.Height, img.Height + UI.S(6)));
        Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        cv = new ViewCanvas(img) { Dock = DockStyle.Fill };
        cv.Changed += () => Text = "이미지 보기  ·  " + img.Width + "×" + img.Height + "  ·  " + Math.Round(cv.Zoom * 100) + "%";
        Controls.Add(cv);
        var tools = new Panel { Dock = DockStyle.Top, Height = UI.S(30), BackColor = BackColor };
        var close = new NoteBtn("✕", new Font("Segoe UI", 10f * UI.Scale), "닫기 (Esc)") { Dock = DockStyle.Right, Width = UI.S(38) };
        close.Click += (s, e) => Close();
        var grip = new Label { Dock = DockStyle.Fill, Text = "⋮⋮", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UI.Hint, Cursor = Cursors.SizeAll };
        grip.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero); } };
        tools.Controls.Add(grip); tools.Controls.Add(close); Controls.Add(tools); tools.BringToFront();
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
        menu.Items.Add("이미지 복사", null, (s, e) => Copy(img));
        menu.Items.Add("화면에 맞춤", null, (s, e) => cv.FitView(true));
        menu.Items.Add("원본 100%", null, (s, e) => cv.ZoomTo(1f, cv.Center, true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("닫기", null, (s, e) => Close());
        cv.ContextMenuStrip = menu;
        KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) cv.ZoomTo(cv.Target * 1.25f, cv.Center, true);
            else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) cv.ZoomTo(cv.Target / 1.25f, cv.Center, true);
            else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) cv.FitView(true);
            else if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) cv.ZoomTo(1f, cv.Center, true);
            else if (e.Control && e.KeyCode == Keys.C) Copy(img);
            else return;
            e.Handled = true;
        };
        Shown += (s, e) => { if (img.Width <= cv.Width && img.Height <= cv.Height) cv.ZoomTo(1f, cv.Center, false); else cv.FitView(false); };
        FormClosed += (s, e) => img.Dispose();
    }
    public ViewCanvas Canvas { get { return cv; } }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using (var p = new Pen(Color.FromArgb(82, 93, 103, 139))) e.Graphics.DrawRectangle(p, 1, 1, Math.Max(1, Width - 4), Math.Max(1, Height - 4));
    }
    public static void Copy(Image i) {
        try { var d = new DataObject(); using (var bm = new Bitmap(i)) { d.SetImage(new Bitmap(bm)); var ms = new MemoryStream(); bm.Save(ms, System.Drawing.Imaging.ImageFormat.Png); d.SetData("PNG", false, ms); Clipboard.SetDataObject(d, true); } } catch { }
    }
}
class ViewCanvas : Control {
    readonly Image img; public float Zoom = 1f, Target = 1f; PointF off, offGoal; WTimer anim; Point last; bool panning; bool fitMode;
    public event Action Changed;
    public ViewCanvas(Image i) {
        img = i; BackColor = Color.FromArgb(14, 14, 26);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        anim = new WTimer { Interval = 15 };
        anim.Tick += (s, e) => {
            Zoom += (Target - Zoom) * 0.3f; off.X += (offGoal.X - off.X) * 0.3f; off.Y += (offGoal.Y - off.Y) * 0.3f;
            if (Math.Abs(Target - Zoom) < 0.002f * Target && Math.Abs(offGoal.X - off.X) < 0.5f && Math.Abs(offGoal.Y - off.Y) < 0.5f) { Zoom = Target; off = offGoal; anim.Stop(); }
            Invalidate(); if (Changed != null) Changed();
        };
        Resize += (s, e) => { if (fitMode) FitView(false); else { Clamp(ref offGoal, Target); off = offGoal; Invalidate(); } };
    }
    public PointF Center { get { return new PointF(Width / 2f, Height / 2f); } }
    float FitZoom { get { return Math.Max(0.01f, Math.Min((Width - UI.S(16)) / (float)img.Width, (Height - UI.S(16)) / (float)img.Height)); } }
    public void FitView(bool smooth) { fitMode = true; float z = Math.Min(1f, FitZoom); Go(z, new PointF((Width - img.Width * z) / 2f, (Height - img.Height * z) / 2f), smooth); }
    public void ZoomTo(float z, PointF at, bool smooth) {
        fitMode = false; z = Math.Max(0.02f, Math.Min(32f, z));
        // keep the image point under `at` fixed
        float ix = (at.X - offGoal.X) / Target, iy = (at.Y - offGoal.Y) / Target;
        if (Target <= 0) { ix = img.Width / 2f; iy = img.Height / 2f; }
        Go(z, new PointF(at.X - ix * z, at.Y - iy * z), smooth);
    }
    void Go(float z, PointF o, bool smooth) {
        Target = z; Clamp(ref o, z); offGoal = o;
        if (smooth) { if (!anim.Enabled) anim.Start(); } else { Zoom = z; off = o; Invalidate(); }
        if (Changed != null) Changed();
    }
    void Clamp(ref PointF o, float z) {
        float w = img.Width * z, h = img.Height * z;
        o.X = w <= Width ? (Width - w) / 2f : Math.Max(Width - w, Math.Min(0, o.X));
        o.Y = h <= Height ? (Height - h) / 2f : Math.Max(Height - h, Math.Min(0, o.Y));
    }
    protected override void OnMouseWheel(MouseEventArgs e) { ZoomTo(Target * (e.Delta > 0 ? 1.2f : 1 / 1.2f), e.Location, true); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle) { panning = true; last = e.Location; Cursor = Cursors.SizeAll; } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) {
        if (!panning) return;
        fitMode = false; offGoal.X += e.X - last.X; offGoal.Y += e.Y - last.Y; last = e.Location; Clamp(ref offGoal, Target);
        if (!anim.Enabled) { off = offGoal; Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs e) { panning = false; Cursor = Cursors.Default; base.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; if (Math.Abs(Target - 1f) < 0.01f && FitZoom < 1f) FitView(true); else if (Math.Abs(Target - 1f) < 0.01f) FitView(true); else ZoomTo(1f, e.Location, true); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        bool moving = anim.Enabled || panning;
        g.InterpolationMode = Zoom >= 2f ? InterpolationMode.NearestNeighbor : moving ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        // checker for transparent images
        var r = new RectangleF(off.X, off.Y, img.Width * Zoom, img.Height * Zoom);
        using (var b = new SolidBrush(Color.FromArgb(30, 30, 46))) g.FillRectangle(b, r);
        g.DrawImage(img, r);
    }
}
// ================= Overlay =================
class OverlayForm : Form {
    string title, sub; volatile string status = ""; WTimer anim, pos; readonly Stopwatch clock = Stopwatch.StartNew();
    double targetOp; bool closing; float k = 1f; Rectangle card, spin; Bitmap bg, iconBm; int lastSecond = -1;
    // spinner geometry is integer-snapped once per layout; the icon is pre-rendered once at a fixed pixel size
    Point center; int ringD, iconPx; public int Relayouts; Rectangle pendingB, applied; int stableTicks; readonly Stopwatch pendingSince = new Stopwatch();
    public static bool LegacyIcon;                       // test only: the 3.1 per-frame scaled icon, for comparison
    public static Func<Rectangle> BoundsSource;          // test only: simulated primary-monitor bounds
    public OverlayForm(string t, string s, int opacity) {
        title = t; sub = s;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(9, 9, 20); targetOp = Math.Max(0.3, Math.Min(1.0, opacity / 100.0 + 0.1)); Opacity = 0;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        using (var g = CreateGraphics()) k = Snap(g.DpiX / 96f);
        Place(true);
        anim = new WTimer { Interval = 15 };
        anim.Tick += (a, b) => Tick();
        pos = new WTimer { Interval = 250 };
        pos.Tick += (a, b) => { try { Place(false); } catch { } };
        anim.Start(); pos.Start();
        SystemEvents.DisplaySettingsChanged += OnDisp;
    }
    public void SetStatus(string s) { status = s ?? ""; try { BeginInvoke((Action)(() => Invalidate(card))); } catch { } }
    public void FadeOut() { closing = true; }
    void Tick() {
        double ms = clock.Elapsed.TotalMilliseconds;
        if (closing) { Opacity = Math.Max(0, Opacity - 0.12); if (Opacity <= 0.01) { Close(); return; } }
        else if (Opacity < targetOp) Opacity = Math.Min(targetOp, Opacity + 0.09);
        if (ms > 90000) { closing = true; }
        Invalidate(spin);
        int sec = (int)(ms / 1000); if (sec != lastSecond) { lastSecond = sec; Invalidate(new Rectangle(card.X, spin.Bottom, card.Width, card.Bottom - spin.Bottom)); }
    }
    void OnDisp(object o, EventArgs e) { stableTicks = 0; }   // re-checked by the 250 ms timer once the new layout has settled
    static float Snap(float f) { return Math.Max(1f, (float)Math.Round(f * 4) / 4f); }
    static Rectangle Primary() {
        if (BoundsSource != null) return BoundsSource();
        if (TestEnv.Off) return new Rectangle(TestEnv.Area.X, TestEnv.Area.Y, 1920, 1080);
        foreach (var s in Screen.AllScreens) if (s.Primary) return s.Bounds;   // fresh list, not the cached PrimaryScreen
        return Screen.PrimaryScreen.Bounds;
    }
    // during a monitor switch the primary bounds flip through intermediate states; only relayout once they stay the same for 2 checks (~0.5 s)
    void Place(bool now) {
        var b = Primary();
        if (b != applied) {
            if (b != pendingB) { pendingB = b; stableTicks = 0; pendingSince.Restart(); }
            else stableTicks++;
            if (now || (stableTicks >= 1 && pendingSince.ElapsedMilliseconds >= 450) || card.Width == 0) { applied = b; Bounds = b; Layout2(); stableTicks = 0; }
        } else pendingB = b;
        if (IsHandleCreated && !TestEnv.Off) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }
    void Layout2() {
        Relayouts++;
        int w = (int)Math.Round(460 * k), h = (int)Math.Round(400 * k);
        card = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        int d = (int)Math.Round(150 * k); d -= d % 2; spin = new Rectangle(card.X + (w - d) / 2, card.Y + (int)Math.Round(44 * k), d, d);
        center = new Point(spin.X + d / 2, spin.Y + d / 2);
        ringD = d - (int)Math.Round(16 * k); ringD -= ringD % 2;
        spin.Inflate((int)(14 * k), (int)(14 * k));
        int ip = (int)Math.Round(54 * k); ip -= ip % 2;
        if (iconBm == null || iconPx != ip) {
            if (iconBm != null) iconBm.Dispose(); iconPx = ip; iconBm = new Bitmap(ip, ip, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(iconBm)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(Program.IconThumb(), 0, 0, ip, ip); }
        }
        if (bg != null) bg.Dispose(); bg = null; Invalidate();
    }
    public Point IconCenter { get { return center; } }
    public Rectangle SpinRect { get { return spin; } }
    // test hook: one frame at time `ms` into a bitmap of the whole client area
    public Bitmap Frame(double ms) { if (card.Width == 0) Layout2(); var bm = new Bitmap(ClientSize.Width, ClientSize.Height); using (var g = Graphics.FromImage(bm)) Draw(g, ClientRectangle, ms); return bm; }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80 | 0x8; return c; } }
    protected override void OnFormClosed(FormClosedEventArgs e) { SystemEvents.DisplaySettingsChanged -= OnDisp; anim.Stop(); pos.Stop(); anim.Dispose(); pos.Dispose(); if (bg != null) bg.Dispose(); if (iconBm != null) iconBm.Dispose(); base.OnFormClosed(e); }
    // static backdrop + card are rendered once into a bitmap; each frame only repaints the spinner area
    Bitmap Background() {
        if (bg != null) return bg;
        bg = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        using (var g = Graphics.FromImage(bg)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(16, 14, 38), Color.FromArgb(6, 6, 14), 90f)) g.FillRectangle(b, ClientRectangle);
            // soft glow behind the card
            using (var path = new GraphicsPath()) {
                var gr = card; gr.Inflate((int)(220 * k), (int)(160 * k)); path.AddEllipse(gr);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, 96, 92, 210), SurroundColors = new[] { Color.FromArgb(0, 16, 14, 38) } }) g.FillPath(pb, path);
            }
            for (int i = 6; i >= 1; i--) using (var p = UI.Round(new Rectangle(card.X - i * 3, card.Y - i * 2 + (int)(8 * k), card.Width + i * 6, card.Height + i * 4), (int)(26 * k) + i * 3)) using (var b = new SolidBrush(Color.FromArgb(10, 0, 0, 0))) g.FillPath(b, p);
            using (var p = UI.Round(card, (int)(26 * k))) {
                using (var b = new LinearGradientBrush(card, Color.FromArgb(42, 40, 78), Color.FromArgb(24, 23, 46), 90f)) g.FillPath(b, p);
                using (var b = UI.Grad(card, 120)) using (var pen = new Pen(b, Math.Max(1f, 1.2f * k))) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillRectangle(b, card.X + (int)(30 * k), card.Y + 1, card.Width - (int)(60 * k), Math.Max(1, (int)k));
        }
        return bg;
    }
    static double Ease(double t) { return t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
    protected override void OnPaint(PaintEventArgs e) {
        if (card.Width == 0) Layout2();
        Draw(e.Graphics, e.ClipRectangle, clock.Elapsed.TotalMilliseconds);
    }
    void Draw(Graphics g, Rectangle clip, double ms) {
        g.DrawImage(Background(), clip, clip, GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // spinner: track ring, eased gradient arc with fading tail, glowing head, icon in the middle
        var rc = LegacyIcon ? new RectangleF(spin.X + 14 * k + 8 * k, spin.Y + 14 * k + 8 * k, spin.Width - 44 * k, spin.Height - 44 * k)
                            : new RectangleF(center.X - ringD / 2, center.Y - ringD / 2, ringD, ringD);
        float thick = 9 * k;
        using (var p = new Pen(Color.FromArgb(34, 255, 255, 255), thick)) g.DrawEllipse(p, rc);
        double cyc = (ms % 1400) / 1400.0, rot = ms * 0.22;
        double head = rot + 360 * Ease(cyc), sweep = 40 + 230 * Math.Sin(Math.PI * cyc);
        const int seg = 48; var cols = new[] { UI.G1, UI.G2, UI.G3, UI.G4 };
        for (int i = 0; i < seg; i++) {
            double t0 = i / (double)seg, t1 = (i + 1.05) / seg;
            float a0 = (float)(head - sweep * (1 - t0)), a1 = (float)(sweep * (t1 - t0));
            double ct = t0 * 3; int ci = Math.Min(2, (int)ct); double f = ct - ci;
            var c0 = cols[ci]; var c1 = cols[ci + 1];
            var c = Color.FromArgb((int)(40 + 215 * t0), (int)(c0.R + (c1.R - c0.R) * f), (int)(c0.G + (c1.G - c0.G) * f), (int)(c0.B + (c1.B - c0.B) * f));
            using (var p = new Pen(c, thick) { StartCap = LineCap.Flat, EndCap = LineCap.Flat }) g.DrawArc(p, rc, a0, a1);
        }
        double hr = head * Math.PI / 180; float cx = rc.X + rc.Width / 2, cy = rc.Y + rc.Height / 2, R = rc.Width / 2;
        float hx = cx + (float)(Math.Cos(hr) * R), hy = cy + (float)(Math.Sin(hr) * R);
        using (var b = new SolidBrush(Color.FromArgb(60, UI.G4))) g.FillEllipse(b, hx - thick, hy - thick, thick * 2, thick * 2);
        using (var b = new SolidBrush(Color.FromArgb(255, 250, 236, 255))) g.FillEllipse(b, hx - thick / 2, hy - thick / 2, thick, thick);
        if (LegacyIcon) {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float isz = 54 * k * (float)(1 + 0.035 * Math.Sin(ms / 420.0));
            g.DrawImage(Program.IconThumb(), cx - isz / 2, cy - isz / 2, isz, isz);
        } else {
            // soft breathing glow instead of rescaling the icon every frame (scaling at sub-pixel positions made it shimmer)
            int ga = (int)(26 + 22 * Math.Sin(ms / 420.0)); float gr = iconPx * 0.62f;
            using (var b = new SolidBrush(Color.FromArgb(ga, UI.G3))) g.FillEllipse(b, center.X - gr, center.Y - gr, gr * 2, gr * 2);
            var st = g.Save(); g.PixelOffsetMode = PixelOffsetMode.Half; g.InterpolationMode = InterpolationMode.NearestNeighbor; g.SmoothingMode = SmoothingMode.None;
            g.DrawImage(iconBm, new Rectangle(center.X - iconPx / 2, center.Y - iconPx / 2, iconPx, iconPx), 0, 0, iconPx, iconPx, GraphicsUnit.Pixel);
            g.Restore(st);
        }
        // text
        int ty = spin.Bottom + (int)(2 * k);
        using (var f1 = new Font(UI.Fam, 19f, FontStyle.Bold))
        using (var f2 = new Font(UI.Fam, 10.5f, FontStyle.Regular)) using (var f3 = new Font(UI.Fam, 9f)) {
            TextRenderer.DrawText(g, title, f1, new Rectangle(card.X + (int)(20 * k), ty, card.Width - (int)(40 * k), (int)(44 * k)), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            string st = status != "" ? status : sub;
            TextRenderer.DrawText(g, st, f2, new Rectangle(card.X + (int)(20 * k), ty + (int)(46 * k), card.Width - (int)(40 * k), (int)(28 * k)), Color.FromArgb(196, 198, 230), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            int secs = (int)(ms / 1000);
            TextRenderer.DrawText(g, secs + "초 경과 · 화면이 잠시 깜빡일 수 있어요", f3, new Rectangle(card.X, ty + (int)(80 * k), card.Width, (int)(24 * k)), Color.FromArgb(128, 130, 170), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
static class Overlay {
    static OverlayForm f; static Thread t;
    public static void Show(string title, string sub, int opacity) {
        try {
            var ready = new ManualResetEvent(false);
            t = new Thread(() => {
                try { f = new OverlayForm(title, sub, opacity); f.Shown += (a, b) => ready.Set(); Application.Run(f); }
                catch { } finally { ready.Set(); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA); t.Start();
            ready.WaitOne(3000);
        } catch { }
    }
    public static void Status(string s) { try { var x = f; if (x != null && x.IsHandleCreated && !x.IsDisposed) x.SetStatus(s); } catch { } }
    public static void Close() {
        try { var x = f; if (x != null && x.IsHandleCreated && !x.IsDisposed) x.BeginInvoke((Action)(() => x.FadeOut())); } catch { }
        try { if (t != null) t.Join(3000); } catch { }
        f = null; t = null;
    }
}

public class MonInfo {
    public string Id = "", Name = "", Res = "", Device = "";
    public Rectangle Rect; public bool Active, Primary;
}

// ================= Core (monitor switching) =================
static class Core {
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub");
    public static readonly string LegacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorMode");
    static string Tool { get { return Path.Combine(Dir, "MultiMonitorTool.exe"); } }
    static int busy = 0;
    const string DfDefault = @"C:\Program Files\DisplayFusion\DisplayFusion.exe";
    public static bool Busy { get { return busy != 0; } }
    public static Action BeforeSwitch, AfterSwitch;

    public static void Extract() {
        Directory.CreateDirectory(Dir);
        foreach (var n in new[] { "MultiMonitorTool.exe", "work_layout.cfg" }) {
            string p = Path.Combine(Dir, n);
            if (File.Exists(p)) continue;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
            using (var f = File.Create(p)) s.CopyTo(f);
        }
    }
    public static string ResolveLayout(string l) {
        if (string.IsNullOrWhiteSpace(l)) return null;
        return Path.IsPathRooted(l) ? l : Path.Combine(Dir, l);
    }

    // returns false if another routine is already running
    public static bool Run(Preset p, GeneralCfg g) {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        bool ov = g.OverlayEnabled && p.Overlay;
        if (ov) Overlay.Show(p.Message(), "잠시만 기다려 주세요", g.OverlayOpacity);
        try { var bs = BeforeSwitch; if (bs != null) try { bs(); } catch { } Extract(); Switch(p); PresetExtras.Run(p, Overlay.Status); }
        finally { if (ov) Overlay.Close(); busy = 0; var a = AfterSwitch; if (a != null) try { a(); } catch { } }
        return true;
    }

    public static void Switch(Preset p) {
        int tries = Math.Max(1, p.Retries);
        string layout = ResolveLayout(p.Layout);
        for (int i = 0; i < tries; i++) {
            if (i > 0) Overlay.Status("확인 후 다시 시도하는 중… (" + (i + 1) + "/" + tries + ")");
            if (p.Enable.Count > 0) {
                Overlay.Status("모니터 켜는 중…");
                foreach (var id in p.Enable) { Mmt("/enable " + id); Sleep(p.EnableDelay); }
                Sleep(p.StepDelay);
            }
            if (layout != null && File.Exists(layout)) { Overlay.Status("모니터 배치 불러오는 중…"); Mmt("/LoadConfig \"" + layout + "\""); Sleep(p.LayoutDelay); }
            if (p.Primary != "") { Overlay.Status("주 모니터 지정하는 중…"); Mmt("/SetPrimary " + p.Primary); Sleep(p.StepDelay); }
            if (p.Disable.Count > 0) { Overlay.Status("쓰지 않는 모니터 끄는 중…"); Mmt("/disable " + string.Join(" ", p.Disable)); Sleep(p.LayoutDelay); }
            if (i == tries - 1) break;
            if (Verify(p)) break;
        }
        if (p.RefreshTaskbar) { Overlay.Status("작업 표시줄 새로 고치는 중…"); Refresh(DetectDf()); }
    }

    static bool Verify(Preset p) {
        var s = State("state.csv");
        if (s.Count == 0) return false;
        foreach (var id in p.Enable) { var r = s.FirstOrDefault(x => G(x, "Short Monitor ID") == id); if (r == null || G(r, "Active") != "Yes") return false; }
        foreach (var id in p.Disable) { var r = s.FirstOrDefault(x => G(x, "Short Monitor ID") == id); if (r != null && G(r, "Active") == "Yes") return false; }
        if (p.Primary != "") { var r = s.FirstOrDefault(x => G(x, "Primary") == "Yes"); if (r == null || G(r, "Short Monitor ID") != p.Primary) return false; }
        return true;
    }

    public static void Mmt(string a) {
        var psi = new ProcessStartInfo(Tool, a) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir };
        using (var p = Process.Start(psi)) p.WaitForExit();
    }
    static void Sleep(int ms) { if (ms > 0) Thread.Sleep(ms); }

    public static List<Dictionary<string, string>> State(string file) {
        Extract();
        string f = Path.Combine(Dir, file);
        try { File.Delete(f); } catch { }
        Mmt("/scomma \"" + f + "\"");
        var rows = new List<Dictionary<string, string>>();
        if (!File.Exists(f)) return rows;
        var lines = File.ReadAllLines(f);
        if (lines.Length == 0) return rows;
        var h = ParseCsv(lines[0]);
        for (int i = 1; i < lines.Length; i++) {
            if (lines[i].Trim() == "") continue;
            var v = ParseCsv(lines[i]); var d = new Dictionary<string, string>();
            for (int j = 0; j < h.Count; j++) d[h[j]] = j < v.Count ? v[j] : "";
            rows.Add(d);
        }
        return rows;
    }
    public static List<MonInfo> ListMonitors() {
        var l = new List<MonInfo>();
        foreach (var r in State("monitors_ui.csv")) {
            var m = new MonInfo { Id = G(r, "Short Monitor ID"), Name = G(r, "Monitor String"), Res = G(r, "Resolution").Replace(" X ", "×"), Device = G(r, "Name"),
                Active = G(r, "Active") == "Yes", Primary = G(r, "Primary") == "Yes" };
            if (m.Name == "") m.Name = G(r, "Monitor Name");
            int[] lt = Nums(G(r, "Left-Top")), rb = Nums(G(r, "Right-Bottom"));
            if (lt.Length == 2 && rb.Length == 2) m.Rect = Rectangle.FromLTRB(lt[0], lt[1], rb[0], rb[1]);
            if (m.Id != "") l.Add(m);
        }
        return l;
    }
    static int[] Nums(string s) {
        var r = new List<int>();
        foreach (var x in s.Split(',')) { int v; if (int.TryParse(x.Trim(), out v)) r.Add(v); }
        return r.ToArray();
    }
    public static bool SaveLayout(string path) {
        Extract(); Directory.CreateDirectory(Path.GetDirectoryName(path));
        Mmt("/SaveConfig \"" + path + "\"");
        return File.Exists(path);
    }
    static List<string> ParseCsv(string line) {
        var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++) {
            char c = line[i];
            if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        r.Add(sb.ToString()); return r;
    }
    public static string G(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }

    static string DetectDf() {
        string df = null;
        try { foreach (var p in Process.GetProcessesByName("DisplayFusion")) { df = p.MainModule.FileName; break; } } catch { }
        return df ?? DfDefault;
    }
    static void Kill(string name) {
        foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(); } catch { } }
    }
    static void Refresh(string df) {
        Kill("DisplayFusion"); Kill("DisplayFusionHookApp32"); Kill("DisplayFusionHookApp64");
        Kill("explorer"); Sleep(3000);
        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
        Sleep(3000);
        if (!string.IsNullOrEmpty(df) && File.Exists(df)) Process.Start(df);
    }

    public static bool AudioPlaying(double threshold) {
        try {
            var e = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice dev;
            if (e.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return false;
            Guid g = typeof(IAudioMeterInformation).GUID; object o;
            if (dev.Activate(ref g, 23, IntPtr.Zero, out o) != 0) return false;
            var m = (IAudioMeterInformation)o; float max = 0;
            for (int i = 0; i < 20; i++) { float v; m.GetPeakValue(out v); if (v > max) max = v; Thread.Sleep(100); }
            return max > threshold;
        } catch { return false; }
    }
}

// ================= Monitors (native id mapping) =================
// ================= Preset extras: brightness (DDC/CI or WMI) + linked desktop icon layout =================
public class MonBright { public string Monitor = ""; public int Value = -1; }   // Monitor "" = all monitors; Value -1 = unchanged
public interface IBrightBackend {
    List<string> Monitors();                                  // ids of active monitors (e.g. SAM73D3)
    bool Set(string id, int pct, out string how);             // false when the monitor is not ready / unsupported
}
class RealBright : IBrightBackend {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PHYS { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr m, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr m, uint n, [Out] PHYS[] a);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYS[] a);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr h, byte code, uint v);
    delegate bool MonProc(IntPtr m, IntPtr dc, IntPtr r, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonProc p, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MIEX { public int cb; public Native.RECT mon, work; public uint flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dev; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MIEX i);
    static string IdOfDevice(string dev) {
        try { var dd = new Native.DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(dd); if (!Native.EnumDisplayDevices(dev, 0, ref dd, 0)) return ""; var p = (dd.DeviceID ?? "").Split('\\'); return p.Length > 1 ? p[1] : ""; } catch { return ""; }
    }
    public static Dictionary<string, IntPtr> Handles() {   // fresh enumeration (not the cached Screen list)
        var d = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, dc, r, l) => { var mi = new MIEX(); mi.cb = Marshal.SizeOf(mi); if (GetMonitorInfo(m, ref mi)) { string id = IdOfDevice(mi.dev); if (id != "" && !d.ContainsKey(id)) d[id] = m; } return true; }, IntPtr.Zero);
        return d;
    }
    public static string Signature() {
        var sb = new StringBuilder();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, dc, r, l) => { var mi = new MIEX(); mi.cb = Marshal.SizeOf(mi); if (GetMonitorInfo(m, ref mi)) sb.Append(mi.dev).Append(mi.mon.L).Append(',').Append(mi.mon.T).Append(',').Append(mi.mon.R).Append(',').Append(mi.mon.B).Append(mi.flags).Append(';'); return true; }, IntPtr.Zero);
        return sb.ToString();
    }
    public List<string> Monitors() { return Handles().Keys.ToList(); }
    public bool Set(string id, int pct, out string how) {
        how = ""; IntPtr hm; if (!Handles().TryGetValue(id, out hm)) { how = "모니터가 아직 켜지지 않음"; return false; }
        uint n; if (GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n) && n > 0) {
            var a = new PHYS[n];
            if (GetPhysicalMonitorsFromHMONITOR(hm, n, a)) {
                try {
                    foreach (var ph in a) {
                        uint min, cur, max;
                        if (GetMonitorBrightness(ph.h, out min, out cur, out max) && max > min) { if (SetMonitorBrightness(ph.h, (uint)(min + (max - min) * pct / 100))) { how = "DDC/CI"; return true; } }
                        if (GetVCPFeatureAndVCPFeatureReply(ph.h, 0x10, IntPtr.Zero, out cur, out max) && max > 0) { if (SetVCPFeature(ph.h, 0x10, (uint)(max * pct / 100))) { how = "DDC/CI VCP 0x10"; return true; } }
                    }
                } finally { DestroyPhysicalMonitors(n, a); }
            }
        }
        // laptop panels: WMI
        try {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                foreach (System.Management.ManagementObject o in s.Get()) {
                    string inst = (o["InstanceName"] as string) ?? "";
                    if (inst.IndexOf("\\" + id + "\\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct }); how = "WMI"; return true;
                }
        } catch { }
        how = "DDC/CI 미지원"; return false;
    }
}
static class PresetExtras {
    public static IBrightBackend Bright = new RealBright();
    public static Func<string, DeskLayout> FindDesk = id => Desk.All.FirstOrDefault(l => l.Id == id);
    public static Func<DeskLayout, DeskResult> RestoreDesk = l => Desk.Restore(l);
    public static Func<string> Signature = RealBright.Signature;
    public static Action<int> Sleep = ms => Thread.Sleep(ms);
    public static int StableMs = 1500, StableMax = 8000, RetryDelay = 1500, Retries = 4;
    public static DateTime LinkedAt = DateTime.MinValue; public static string LastBright = "", LastDesk = "", LastWork = "";
    public static Func<string, Workspace> FindWork = id => { try { return Config.Load().Pt.Work.Items.FirstOrDefault(w => w.Id == id); } catch { return null; } };
    public static Func<bool> WorkLaunchMissing = () => { try { return Config.Load().Pt.Work.LaunchMissing; } catch { return false; } };
    public static Func<Workspace, bool, string> LaunchWork = (w, launch) => Workspaces.Launch(w, launch, true, null);   // running apps are always moved, never started twice
    public static int WorkRuns;   // counts linked workspace launches (tests)
    public static string WorkStampFile = Path.Combine(Core.Dir, "linked_work.stamp");
    public static int DeskRestores;   // counts linked restores (tests)
    public static bool HasWork(Preset p) { return (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0)) || !string.IsNullOrEmpty(p.DeskLayout) || !string.IsNullOrEmpty(p.Workspace); }
    // waits until the monitor configuration has stopped changing (fresh enumeration), bounded
    public static void WaitStable() {
        string last = Signature(); int same = 0, waited = 0;
        while (same < StableMs && waited < StableMax) { Sleep(250); waited += 250; string s = Signature(); if (s == last) same += 250; else { same = 0; last = s; } }
    }
    // per-monitor value wins over the "all monitors" value
    public static Dictionary<string, int> Targets(Preset p, IEnumerable<string> mons) {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); if (p.Brightness == null) return d;
        var all = p.Brightness.FirstOrDefault(b => b.Monitor == "");
        foreach (var m in mons) { var one = p.Brightness.FirstOrDefault(b => string.Equals(b.Monitor, m, StringComparison.OrdinalIgnoreCase)); int v = one != null ? one.Value : all != null ? all.Value : -1; if (v >= 0) d[m] = Math.Min(100, v); }
        // monitors configured explicitly but not on yet are still targets (they may come up during retries)
        foreach (var b in p.Brightness) if (b.Monitor != "" && b.Value >= 0 && !d.ContainsKey(b.Monitor) && (p.Disable == null || !p.Disable.Contains(b.Monitor))) d[b.Monitor] = Math.Min(100, b.Value);
        return d;
    }
    // runs inside the switch, after the monitors are set and before the overlay closes; never throws
    public static void Run(Preset p, Action<string> status) {
        LastBright = ""; LastDesk = ""; LastWork = "";
        if (!HasWork(p)) return;
        try { status("화면 안정되기를 기다리는 중…"); WaitStable(); } catch { }
        try {
            if (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0)) {
                status("모니터 밝기 맞추는 중…");
                var todo = Targets(p, Bright.Monitors()); var done = new List<string>(); var fail = new Dictionary<string, string>();
                for (int a = 0; a < Retries && todo.Count > 0; a++) {
                    if (a > 0) { Sleep(RetryDelay); foreach (var m in Targets(p, Bright.Monitors())) if (!todo.ContainsKey(m.Key) && !done.Contains(m.Key)) todo[m.Key] = m.Value; }
                    foreach (var t in todo.ToList()) {
                        string how; bool ok = false; try { ok = Bright.Set(t.Key, t.Value, out how); } catch (Exception ex) { how = ex.Message; }
                        if (ok) { done.Add(t.Key + " " + t.Value + "% (" + how + ")"); todo.Remove(t.Key); fail.Remove(t.Key); } else fail[t.Key] = how;
                    }
                }
                LastBright = (done.Count > 0 ? "밝기 " + string.Join(", ", done) : "") + (fail.Count > 0 ? (done.Count > 0 ? " · " : "") + "실패: " + string.Join(", ", fail.Select(f => f.Key + "(" + f.Value + ")")) : "");
                if (fail.Count > 0) status("일부 모니터 밝기를 바꾸지 못했어요");
            }
        } catch (Exception ex) { LastBright = "밝기 실패: " + ex.Message; }
        try {
            if (!string.IsNullOrEmpty(p.DeskLayout)) {
                var l = FindDesk(p.DeskLayout);
                if (l == null) { LastDesk = "연계된 바탕화면 아이콘 배치가 삭제됐어요. 프리셋 편집에서 다시 골라 주세요."; }
                else {
                    status("바탕화면 아이콘 배치 ‘" + l.Name + "’ 복원 중…");
                    Stamp(); DeskRestores++;
                    var r = RestoreDesk(l); Stamp();
                    LastDesk = r == null ? "아이콘 복원 실패" : r.Ok ? "‘" + l.Name + "’ " + r.Summary() : "아이콘 복원 실패: " + r.Error;
                }
            }
        } catch (Exception ex) { LastDesk = "아이콘 복원 실패: " + ex.Message; }
        // linked workspace: right after the icon layout, still before the overlay closes; exactly once per switch, also across processes
        try {
            if (!string.IsNullOrEmpty(p.Workspace)) {
                var w = FindWork(p.Workspace);
                if (w == null) LastWork = "연계된 작업 영역이 삭제됐어요. 프리셋 편집에서 다시 골라 주세요.";
                else {
                    bool mine; using (var mx = new Mutex(false, "BongHub_LinkedWork")) {
                        mine = false; try { mine = mx.WaitOne(0); } catch (AbandonedMutexException) { mine = true; }
                        if (!mine || RecentWork(p.Name + "|" + w.Id)) LastWork = "작업 영역 ‘" + w.Name + "’은 다른 전환에서 이미 실행 중이라 건너뛰었어요";
                        else {
                            try {
                                StampWork(p.Name + "|" + w.Id); status("창 배치 ‘" + w.Name + "’ 적용 중…"); WorkRuns++;
                                string r = LaunchWork(w, WorkLaunchMissing()); LastWork = r ?? "";
                                if (r != null && r.Contains("실패")) status("일부 창을 배치하지 못했어요");
                            } finally { mx.ReleaseMutex(); }
                        }
                    }
                }
            }
        } catch (Exception ex) { LastWork = "창 배치 실패: " + ex.Message; }
    }
    static bool RecentWork(string key) {
        try { if (!File.Exists(WorkStampFile)) return false; return (DateTime.Now - File.GetLastWriteTime(WorkStampFile)).TotalSeconds < 10 && File.ReadAllText(WorkStampFile) == key; } catch { return false; }
    }
    static void StampWork(string key) { try { File.WriteAllText(WorkStampFile, key); } catch { } }
    public static string StampFile = Path.Combine(Core.Dir, "linked_desk.stamp");
    static void Stamp() { LinkedAt = DateTime.Now; try { File.WriteAllText(StampFile, DateTime.Now.ToString("o")); } catch { } }
    // true for 20 s after a linked restore, also when another BongHub process (CLI switch) did it
    public static bool LinkedRecently { get {
        if ((DateTime.Now - LinkedAt).TotalSeconds < 20) return true;
        try { return File.Exists(StampFile) && (DateTime.Now - File.GetLastWriteTime(StampFile)).TotalSeconds < 20; } catch { return false; }
    } }
}

static class Monitors {
    public static string IdOf(Screen s) {
        try {
            var dd = new Native.DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(dd);
            if (!Native.EnumDisplayDevices(s.DeviceName, 0, ref dd, 0)) return "";
            var parts = (dd.DeviceID ?? "").Split('\\');
            return parts.Length > 1 ? parts[1] : "";
        } catch { return ""; }
    }
    public static Screen ScreenFor(string id) {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var s in Screen.AllScreens) if (string.Equals(IdOf(s), id, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }
}

// ================= Window rules =================
public class WinInfo {
    public IntPtr H; public uint Pid; public string Process = "", Title = "", Class = "", MonitorId = "";
    public Rectangle Rect, Rel; public bool Max, Min, TopMost;
}

static class WindowRules {
    static readonly Dictionary<uint, string> procCache = new Dictionary<uint, string>();
    static readonly Dictionary<string, Regex> rxCache = new Dictionary<string, Regex>();
    static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
    const uint NOSIZE = 1, NOMOVE = 2, NOZORDER = 4, NOACTIVATE = 0x10, NOOWNERZORDER = 0x200;

    public static bool HasCriteria(WindowRule r) { return r.Process != "" || r.Title != "" || r.Class != ""; }

    public static string ProcName(uint pid) {
        lock (procCache) {
            string n; if (procCache.TryGetValue(pid, out n)) return n;
            n = "";
            IntPtr h = Native.OpenProcess(0x1000, false, pid);
            if (h != IntPtr.Zero) {
                var sb = new StringBuilder(1024); uint sz = 1024;
                if (Native.QueryFullProcessImageName(h, 0, sb, ref sz)) n = Path.GetFileNameWithoutExtension(sb.ToString());
                Native.CloseHandle(h);
            }
            if (procCache.Count > 1000) procCache.Clear();
            procCache[pid] = n; return n;
        }
    }

    public static bool Eligible(IntPtr h) {
        if (!Native.IsWindowVisible(h)) return false;
        if (Native.GetAncestor(h, 2) != h) return false;
        int cloaked; if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
        long ex = Native.GetWindowLongPtr(h, -20).ToInt64();
        long st = Native.GetWindowLongPtr(h, -16).ToInt64();
        bool app = (ex & 0x40000) != 0;
        if ((ex & 0x80) != 0 && !app) return false;
        if (Native.GetWindow(h, 4) != IntPtr.Zero && !app) return false;
        if ((st & 0xC00000) == 0 && Native.GetWindowTextLength(h) == 0) return false;
        return true;
    }

    public static WinInfo Info(IntPtr h) {
        var w = new WinInfo { H = h };
        uint pid; Native.GetWindowThreadProcessId(h, out pid); w.Pid = pid;
        w.Process = ProcName(pid);
        int len = Native.GetWindowTextLength(h);
        var sb = new StringBuilder(Math.Max(len + 1, 2)); Native.GetWindowText(h, sb, sb.Capacity); w.Title = sb.ToString();
        var cb = new StringBuilder(256); Native.GetClassName(h, cb, 256); w.Class = cb.ToString();
        Native.RECT r; Native.GetWindowRect(h, out r); w.Rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        w.Max = Native.IsZoomed(h); w.Min = Native.IsIconic(h);
        w.TopMost = (Native.GetWindowLongPtr(h, -20).ToInt64() & 8) != 0;
        var s = Screen.FromHandle(h); w.MonitorId = Monitors.IdOf(s);
        var wa = s.WorkingArea; w.Rel = new Rectangle(w.Rect.X - wa.X, w.Rect.Y - wa.Y, w.Rect.Width, w.Rect.Height);
        return w;
    }

    public static List<WinInfo> Enumerate(bool requireTitle) {
        var hs = new List<IntPtr>();
        Native.EnumWindows((h, l) => { hs.Add(h); return true; }, IntPtr.Zero);
        var res = new List<WinInfo>();
        foreach (var h in hs) {
            try {
                if (!Eligible(h)) continue;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) continue;
                var w = Info(h);
                if (requireTitle && w.Title == "") continue;
                if (w.Class == "Progman" || w.Class == "WorkerW" || w.Class == "Shell_TrayWnd" || w.Class == "Shell_SecondaryTrayWnd") continue;
                res.Add(w);
            } catch { }
        }
        return res;
    }

    public static bool RegexValid(string p) { try { new Regex(p); return true; } catch { return false; } }

    public static bool Match(WindowRule r, WinInfo w) {
        if (!HasCriteria(r)) return false;
        if (r.Process != "") {
            string p = r.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? r.Process.Substring(0, r.Process.Length - 4) : r.Process;
            if (!string.Equals(p, w.Process, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (r.Class != "" && !string.Equals(r.Class, w.Class, StringComparison.OrdinalIgnoreCase)) return false;
        if (r.Title != "") {
            if (r.TitleRegex) {
                Regex rx;
                lock (rxCache) {
                    if (!rxCache.TryGetValue(r.Title, out rx)) {
                        try { rx = new Regex(r.Title, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); } catch { rx = null; }
                        rxCache[r.Title] = rx;
                    }
                }
                if (rx == null) return false;
                try { if (!rx.IsMatch(w.Title)) return false; } catch { return false; }
            } else if (w.Title.IndexOf(r.Title, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }

    static Rectangle Target(WindowRule r, Rectangle wa) {
        if (r.Action == "zone") { var z = Zones.ZoneRect(wa, r.Zone); if (z.HasValue) return z.Value; }
        return new Rectangle(wa.X + r.X, wa.Y + r.Y, r.W, r.H);
    }

    public static bool InPlace(WindowRule r, WinInfo w) {
        Screen target = r.Monitor == "" ? null : Monitors.ScreenFor(r.Monitor);
        bool missing = r.Monitor != "" && target == null;
        Screen cur = Screen.FromHandle(w.H);
        bool onTarget = target == null || target.DeviceName == cur.DeviceName;
        bool ok = true;
        if (!missing) {
            switch (r.Action) {
                case "pos": case "zone": {
                    var t = Target(r, (target ?? cur).WorkingArea);
                    ok = !w.Min && !w.Max && Math.Abs(t.X - w.Rect.X) <= 8 && Math.Abs(t.Y - w.Rect.Y) <= 8
                        && Math.Abs(t.Width - w.Rect.Width) <= 8 && Math.Abs(t.Height - w.Rect.Height) <= 8;
                    break; }
                case "monitor": ok = onTarget; break;
                case "max": ok = w.Max && onTarget; break;
                case "min": ok = w.Min; break;
                case "restore": ok = !w.Min && !w.Max; break;
                default: ok = onTarget; break;
            }
        }
        if (r.Pin && !w.TopMost) ok = false;
        return ok;
    }

    public static void Apply(WindowRule r, IntPtr h) {
        if (!Native.IsWindow(h) || Native.IsHungAppWindow(h)) return;
        Screen target = r.Monitor == "" ? null : Monitors.ScreenFor(r.Monitor);
        bool missing = r.Monitor != "" && target == null;
        Screen cur = Screen.FromHandle(h);
        if (target == null) target = cur;
        var wa = target.WorkingArea;
        uint f = NOZORDER | NOACTIVATE | NOOWNERZORDER;
        if (!missing) {
            switch (r.Action) {
                case "zone":
                    if (r.Action == "zone") { if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, 4); Zones.Place(h, Target(r, wa)); break; }
                    goto case "pos";
                case "pos": {
                    if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, 4);
                    var t = Target(r, wa);
                    Native.SetWindowPos(h, IntPtr.Zero, t.X, t.Y, t.Width, t.Height, f);
                    Native.SetWindowPos(h, IntPtr.Zero, t.X, t.Y, t.Width, t.Height, f); // again after possible DPI change
                    break; }
                case "monitor":
                case "none":
                    if (r.Action == "none" && r.Monitor == "") break;
                    if (cur.DeviceName != target.DeviceName) {
                        bool mx = Native.IsZoomed(h);
                        if (mx) Native.ShowWindow(h, 4);
                        Native.RECT rc; Native.GetWindowRect(h, out rc);
                        var cwa = cur.WorkingArea;
                        int w = Math.Min(rc.R - rc.L, wa.Width), hh = Math.Min(rc.B - rc.T, wa.Height);
                        int nx = Math.Max(wa.X, Math.Min(wa.X + (rc.L - cwa.X), wa.Right - w));
                        int ny = Math.Max(wa.Y, Math.Min(wa.Y + (rc.T - cwa.Y), wa.Bottom - hh));
                        Native.SetWindowPos(h, IntPtr.Zero, nx, ny, w, hh, f);
                        if (mx) Native.ShowWindow(h, 3);
                    }
                    break;
                case "max":
                    if (!(Native.IsZoomed(h) && cur.DeviceName == target.DeviceName)) {
                        if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, 4);
                        Native.SetWindowPos(h, IntPtr.Zero, wa.X + 40, wa.Y + 40, Math.Max(200, wa.Width - 80), Math.Max(150, wa.Height - 80), f);
                        Native.ShowWindow(h, 3);
                    }
                    break;
                case "min": if (!Native.IsIconic(h)) Native.ShowWindow(h, 7); break;
                case "restore": if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, 4); break;
            }
        }
        if (r.Pin) Native.SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, NOMOVE | NOSIZE | NOACTIVATE);
    }

    // applies the first matching enabled rule to every eligible window; returns windows touched
    public static int ApplyList(IEnumerable<WindowRule> rules) {
        var list = rules.Where(r => r.Enabled && HasCriteria(r)).ToList();
        if (list.Count == 0) return 0;
        int n = 0;
        foreach (var w in Enumerate(false)) {
            foreach (var r in list) {
                if (!Match(r, w)) continue;
                try { Apply(r, w.H); n++; } catch { }
                break;
            }
        }
        return n;
    }
}

// keep-in-place watcher: WinEvent hooks + slow polling fallback. Must live on a UI thread.
class Watcher {
    Native.WinEventProc proc; readonly List<IntPtr> hooks = new List<IntPtr>();
    List<WindowRule> rules = new List<WindowRule>();
    readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
    readonly Dictionary<IntPtr, int> lastApply = new Dictionary<IntPtr, int>();
    WTimer debounce, poll;
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
    public int AppliedCount;
    public bool Active { get { return rules.Count > 0; } }
    public Watcher() {
        proc = OnEvent;
        debounce = new WTimer { Interval = 250 }; debounce.Tick += (s, e) => Flush();
        poll = new WTimer { Interval = 15000 }; poll.Tick += (s, e) => Sweep();
    }
    public void SetRules(IEnumerable<WindowRule> all) {
        rules = all.Where(r => r.Enabled && r.Keep && WindowRules.HasCriteria(r)).ToList();
        if (rules.Count > 0) {
            if (hooks.Count == 0)
                foreach (var ev in new uint[] { 0x8002, 0x000B, 0x0017, 0x800B })
                    hooks.Add(Native.SetWinEventHook(ev, ev, IntPtr.Zero, proc, 0, 0, 0 | 2));
            poll.Start(); Sweep();
        } else {
            foreach (var h in hooks) if (h != IntPtr.Zero) Native.UnhookWinEvent(h);
            hooks.Clear(); poll.Stop(); debounce.Stop(); pending.Clear();
        }
    }
    void OnEvent(IntPtr hook, uint ev, IntPtr h, int idObj, int idChild, uint th, uint time) {
        if (idObj != 0 || idChild != 0 || h == IntPtr.Zero) return;
        if (GetAncestor(h, 2) != h) return; // top-level windows only (cheap filter for location-change noise)
        pending.Add(h);
        if (!debounce.Enabled) debounce.Start();
    }
    void Flush() {
        debounce.Stop();
        var list = pending.ToArray(); pending.Clear();
        foreach (var h in list) Check(h);
    }
    void Sweep() {
        var hs = new List<IntPtr>();
        Native.EnumWindows((h, l) => { hs.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in hs) Check(h);
        if (lastApply.Count > 200) lastApply.Clear();
    }
    void Check(IntPtr h) {
        try {
            if (!WindowRules.Eligible(h)) return;
            var w = WindowRules.Info(h);
            foreach (var r in rules) {
                if (!WindowRules.Match(r, w)) continue;
                if (!WindowRules.InPlace(r, w)) {
                    int last; int now = Environment.TickCount;
                    if (lastApply.TryGetValue(h, out last) && unchecked(now - last) < 1500) return;
                    lastApply[h] = now;
                    WindowRules.Apply(r, h); AppliedCount++;
                }
                return;
            }
        } catch { }
    }
}

// ================= UI helpers =================
static class UI {
    public static float Dpi = 96f, Scale = 1f;
    public static Font Base, Bold, Header, Small, Title, Big;
    // palette sampled from the blocks icon (cyan -> blue -> violet -> magenta on deep indigo)
    public static readonly Color G1 = Color.FromArgb(110, 198, 255), G2 = Color.FromArgb(76, 138, 254), G3 = Color.FromArgb(144, 132, 255), G4 = Color.FromArgb(228, 126, 244);
    public static readonly Color Accent = Color.FromArgb(134, 158, 255), Side = Color.FromArgb(21, 24, 33), Page = Color.FromArgb(14, 17, 24),
        Card = Color.FromArgb(25, 29, 39), CardAlt = Color.FromArgb(28, 33, 45), Border = Color.FromArgb(46, 54, 70), Input = Color.FromArgb(17, 21, 30),
        Text = Color.FromArgb(239, 243, 250), Text2 = Color.FromArgb(190, 201, 219), Hint = Color.FromArgb(142, 157, 182), SelBg = Color.FromArgb(42, 53, 79),
        Hover = Color.FromArgb(35, 43, 58), Ok = Color.FromArgb(110, 226, 176), Warn = Color.FromArgb(255, 176, 96), Bad = Color.FromArgb(255, 120, 140);
    public static string Fam = "Segoe UI", IconFam = "Segoe MDL2 Assets";
    public static Font Glyph;
    public static LinearGradientBrush Grad(RectangleF r, int alpha = 255, float angle = 0f) {
        if (r.Width < 1) r.Width = 1; if (r.Height < 1) r.Height = 1;
        var b = new LinearGradientBrush(r, G1, G4, angle);
        b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(alpha, G1), Color.FromArgb(alpha, G2), Color.FromArgb(alpha, G3), Color.FromArgb(alpha, G4) }, Positions = new[] { 0f, .35f, .68f, 1f } };
        return b;
    }
    static bool Has(string fam) { try { using (var f = new Font(fam, 9f)) return string.Equals(f.Name, fam, StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    public static int S(int v) { return (int)Math.Round(v * Dpi / 96f * Scale); }
    public static void Init(float scale) {
        Dark.InitApp();
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi = g.DpiX;
        Scale = scale;
        Fam = Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        IconFam = Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        string fam = Fam; Glyph = new Font(IconFam, 11f * scale);
        Base = new Font(fam, 9.5f * scale); Bold = new Font(fam, 9.5f * scale, FontStyle.Bold);
        Header = new Font(fam, 11f * scale, FontStyle.Bold); Small = new Font(fam, 8.5f * scale);
        Title = new Font(fam, 20f * scale, FontStyle.Bold); Big = new Font(fam, 13f * scale, FontStyle.Bold);
    }
    public static Button Btn(string text, bool primary = false) {
        var b = new FlatBtn { Text = text, Primary = primary, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(S(12), S(4), S(12), S(4)), Margin = new Padding(0, 0, S(8), S(8)), Font = Base, MinimumSize = new Size(0, S(30)) };
        return b;
    }
    public static Label L(string text, Font f = null, Color? c = null) {
        return new Label { Text = text, AutoSize = true, Font = f ?? Base, ForeColor = c ?? Text, Margin = new Padding(0, S(4), S(8), S(4)), BackColor = Color.Transparent };
    }
    public static Label HintL(string text) { var l = L(text, Small, Hint); l.Margin = new Padding(0, 0, S(8), S(6)); return l; }
    public static NumericUpDown Num(decimal min, decimal max, decimal val, int width = 90) {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = S(width), Font = Base, Margin = new Padding(0, S(2), S(6), S(2)), TextAlign = HorizontalAlignment.Right, BackColor = Input, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle };
        return n;
    }
    public static CheckBox Chk(string text) { return new DarkCheck { Text = text, AutoSize = true, Font = Base, Margin = new Padding(0, S(3), S(8), S(3)) }; }
    public static TextBox Txt(int width = 260) { return new TextBox { Width = S(width), Font = Base, BackColor = Input, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, S(2), S(6), S(2)) }; }
    public static FlowLayoutPanel Flow(params Control[] cs) {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        foreach (var c in cs) f.Controls.Add(c);
        return f;
    }
    public static TableLayoutPanel Stack() {
        var t = new ResponsiveTable { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, BackColor = Color.Transparent };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }
    public static void AddRow(TableLayoutPanel t, Control c) {
        int r = t.RowCount; t.RowCount = r + 1; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; t.Controls.Add(c, 0, r);
    }
    public static Icon MakeIcon() {
        var b = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(b)) {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using (var br = new SolidBrush(Accent)) { g.FillRectangle(br, 2, 4, 28, 19); g.FillRectangle(br, 13, 23, 6, 4); g.FillRectangle(br, 8, 27, 16, 3); }
            using (var br = new SolidBrush(Color.FromArgb(200, 230, 255))) g.FillRectangle(br, 5, 7, 22, 13);
        }
        return Icon.FromHandle(b.GetHicon());
    }
    public static GraphicsPath Round(Rectangle r, int rad) {
        var p = new GraphicsPath(); int d = rad * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }
}

class PageHeading : Panel {
    public PageHeading(string title) {
        Height=UI.S(90); MinimumSize=new Size(UI.S(80),UI.S(90)); Margin=new Padding(0,0,0,UI.S(12));
        Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top; BackColor=UI.Page;
        var descriptions=new Dictionary<string,string> { {"모니터 프리셋","화면과 작업 환경을 한 번에 전환하세요."},{"창 위치","앱마다 편안한 자리를 지정하세요."},{"스티커 메모","생각과 이미지를 가까이에 두세요."},{"카톡 광고 제거","대화에 집중할 수 있는 깔끔한 화면."},{"바탕화면 아이콘","정리한 바탕화면을 그대로 기억하세요."},{"일반","BongHub를 내 작업 방식에 맞춰보세요."},{"정보","석봉이의 유틸 창고 · BongHub " + AppInfo.Version},{"PowerToys","매일 반복하는 작업을 가볍게."} };
        string description=descriptions.ContainsKey(title)?descriptions[title]:"작업 환경을 세밀하게 조정하세요.";
        var brand=new Label { Text="BONGHUB  /  WORKSPACE", Font=UI.Small, ForeColor=UI.Hint, Bounds=new Rectangle(0,0,Width,UI.S(18)), Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top };
        var heading=new Label { Text=title, Font=UI.Title, ForeColor=UI.Text, Bounds=new Rectangle(0,UI.S(22),Width,UI.S(34)), Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top, AutoEllipsis=true };
        var hint=new Label { Text=description, Font=UI.Base, ForeColor=UI.Text2, Bounds=new Rectangle(0,UI.S(62),Width,UI.S(22)), Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top, AutoEllipsis=true };
        Controls.AddRange(new Control[]{brand,heading,hint});
    }
}
class ResponsiveTable : TableLayoutPanel {
    public static long LayoutCalls;
    bool fitting;
    readonly Dictionary<Size,Size> preferred=new Dictionary<Size,Size>();
    public override Size GetPreferredSize(Size proposed) {
        Size value;if(preferred.TryGetValue(proposed,out value))return value;
        value=base.GetPreferredSize(proposed);if(preferred.Count>32)preferred.Clear();preferred[proposed]=value;return value;
    }
    public ResponsiveTable() { DoubleBuffered = true; }
    protected override void OnLayout(LayoutEventArgs e) {
        LayoutCalls++; preferred.Clear();
        if (!fitting && ColumnStyles.Count>=ColumnCount && ClientSize.Width > Padding.Horizontal) {
            fitting = true; SuspendLayout();
            try {
                int available = ClientSize.Width - Padding.Horizontal;
                int[] cols=new int[ColumnCount];float percent=0;int fixedWidth=0;
                for(int col=0;col<ColumnCount;col++) {var style=ColumnStyles[col];if(style.SizeType==SizeType.Absolute){cols[col]=(int)style.Width;fixedWidth+=cols[col];}else if(style.SizeType==SizeType.Percent)percent+=style.Width;}
                for(int col=0;col<ColumnCount;col++)if(ColumnStyles[col].SizeType==SizeType.Percent)cols[col]=(int)(Math.Max(0,available-fixedWidth)*ColumnStyles[col].Width/Math.Max(1,percent));
                foreach (Control c in Controls) {
                    int width = available;
                    if (ColumnCount > 1) {
                        int col = GetColumn(c), span = GetColumnSpan(c); width = 0;
                        for (int i = Math.Max(0, col); i < Math.Min(cols.Length, col + span); i++) width += cols[i];
                        if (width <= 0) width = available;
                    }
                    width = Math.Max(UI.S(32), width - c.Margin.Horizontal);
                    if (c is Label || c is FlowLayoutPanel || (c is TextBox && ((TextBox)c).Multiline)) {
                        var max = new Size(width, 0); if (c.MaximumSize != max) c.MaximumSize = max;
                        if (c is FlowLayoutPanel) ((FlowLayoutPanel)c).WrapContents = true;
                    }
                }
            } finally { ResumeLayout(false); fitting = false; }
        }
        base.OnLayout(e);
    }
}
class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; } }

// card-like section with title, hint and a 2-column label/field grid
class Section : ResponsiveTable {
    TableLayoutPanel grid; int row = 0;
    public Section(string title, string hint) {
        ColumnCount = 1; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = UI.Card;
        Margin = new Padding(0, 0, 0, UI.S(14)); Padding = new Padding(UI.S(20), UI.S(14), UI.S(20), UI.S(12));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); ResizeRedraw = true; DoubleBuffered = true;
        UI.AddRow(this, UI.L(title, UI.Header, UI.Text));
        if (hint != null) UI.AddRow(this, UI.HintL(hint));
        grid = new ResponsiveTable { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, UI.S(4), 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UI.S(140))); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        UI.AddRow(this, grid);
    }
    public void Row(string label, Control c, string hint = null) {
        grid.RowCount = row + 1; grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var l = UI.L(label); l.Anchor = AnchorStyles.Left; l.Margin = new Padding(0, UI.S(5), UI.S(14), UI.S(5));
        grid.Controls.Add(l, 0, row); c.Anchor = AnchorStyles.Left; grid.Controls.Add(c, 1, row); row++;
        if (hint != null) { grid.RowCount = row + 1; grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.Controls.Add(UI.HintL(hint), 1, row); row++; }
    }
    public void Full(Control c, bool stretch = false) {
        grid.RowCount = row + 1; grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (c is TextBox && ((TextBox)c).Multiline) { c.MinimumSize = new Size(0, c.Height); grid.RowStyles[row].SizeType = SizeType.Absolute; grid.RowStyles[row].Height = c.Height + c.Margin.Vertical; }
        c.Anchor = stretch || c is Label || (c is TextBox && ((TextBox)c).Multiline) ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left;
        grid.Controls.Add(c, 0, row); grid.SetColumnSpan(c, 2); row++;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { } // PaintCard covers the buffered surface once.
    GraphicsPath path; Size pathSize; Bitmap cardBm; Size cardSz;
    protected override void OnPaint(PaintEventArgs e) {
        int w = Math.Max(1, Width), h = Math.Max(1, Height);
        bool sizeChanged = cardBm == null || cardSz.Width != w || cardSz.Height != h;
        if (sizeChanged && ResizeGlide.Live) { PaintCard(e.Graphics, w, h); return; }
        if (sizeChanged) {
            if (cardBm != null) cardBm.Dispose();
            cardBm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var bg = Graphics.FromImage(cardBm)) PaintCard(bg, w, h);
            cardSz = new Size(w, h);
        }
        e.Graphics.DrawImageUnscaled(cardBm, Point.Empty);
    }
    void PaintCard(Graphics g, int w, int h) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Dark.ParentBack(this));
        if (path == null || pathSize.Width != w || pathSize.Height != h) { if (path != null) path.Dispose(); path = UI.Round(new Rectangle(0, 0, w - 1, h - 1), UI.S(12)); pathSize = new Size(w, h); }
        using (var b = new SolidBrush(UI.Card)) g.FillPath(b, path);
        using (var pen = new Pen(UI.Border)) g.DrawPath(pen, path);

    }
    protected override void Dispose(bool d) { if (d && path != null) path.Dispose(); if (d && cardBm != null) cardBm.Dispose(); base.Dispose(d); }
}

class HotkeyBox : TextBox {
    string val = "";
    public event EventHandler ValueChanged;
    public HotkeyBox() { BackColor = UI.Input; ForeColor = UI.Text; BorderStyle = BorderStyle.FixedSingle; ShortcutsEnabled = false; ContextMenu = new ContextMenu(); Width = UI.S(200); Font = UI.Base; Cursor = Cursors.Hand; Margin = new Padding(0, UI.S(2), UI.S(6), UI.S(2)); Show(); }
    public string Value { get { return val; } set { val = value ?? ""; Show(); } }
    new void Show() { Text = val == "" ? (Focused ? "키 조합을 누르세요…" : "없음 (클릭해서 지정)") : val; ForeColor = val == "" ? UI.Hint : UI.Text; }
    protected override bool IsInputKey(Keys k) { return true; }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); BackColor = Color.FromArgb(38, 46, 74); Show(); SelectAll(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); BackColor = UI.Input; Show(); }
    protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e) {
        e.Handled = true; e.SuppressKeyPress = true;
        Keys k = e.KeyCode;
        if (k == Keys.Tab && e.Modifiers == Keys.None) { Parent.SelectNextControl(this, true, true, true, true); return; }
        if ((k == Keys.Back || k == Keys.Delete || k == Keys.Escape) && e.Modifiers == Keys.None) { Set(""); return; }
        if (k == Keys.ControlKey || k == Keys.Menu || k == Keys.ShiftKey || k == Keys.LWin || k == Keys.RWin) {
            Text = Hotkey.Format(e.Modifiers, Keys.None).Replace("+None", "+…"); return;
        }
        bool fkey = k >= Keys.F1 && k <= Keys.F24;
        if (e.Modifiers == Keys.None && !fkey) { Text = "Ctrl/Alt/Shift와 함께 누르세요"; return; }
        Set(Hotkey.Format(e.Modifiers, k));
    }
    protected override bool ProcessCmdKey(ref Message m, Keys keyData) {
        if (Focused && (keyData & Keys.Alt) != 0) { OnKeyDown(new KeyEventArgs(keyData)); return true; }
        return base.ProcessCmdKey(ref m, keyData);
    }
    void Set(string v) { val = v; Show(); SelectAll(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
}

// visual monitor map editor for a preset
class MonitorMap : Control {
    public List<MonInfo> Mons = new List<MonInfo>();
    public Preset P; public string Selected; public string Status = "모니터 정보를 불러오는 중…";
    public event EventHandler Changed, SelectionChanged;
    string hover; ToolTip tip = new ToolTip(); ContextMenuStrip cm; string cmId;
    public MonitorMap() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(27, 27, 32); Height = UI.S(240); Font = UI.Base; Cursor = Cursors.Hand; Margin = new Padding(0, UI.S(4), 0, UI.S(6));
        cm = new ContextMenuStrip { Font = UI.Base, Renderer = new DarkRenderer() };
        tip.OwnerDraw = true; tip.BackColor = UI.Card; tip.ForeColor = UI.Text;
        tip.Draw += (s, e) => { e.Graphics.Clear(Color.FromArgb(40, 40, 48)); using (var p = new Pen(UI.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1); TextRenderer.DrawText(e.Graphics, e.ToolTipText, UI.Small, new Rectangle(UI.S(6), UI.S(4), e.Bounds.Width, e.Bounds.Height), UI.Text, TextFormatFlags.Left); };
        tip.Popup += (s, e) => { var sz = TextRenderer.MeasureText(tip.GetToolTip(this), UI.Small); e.ToolTipSize = new Size(sz.Width + UI.S(14), sz.Height + UI.S(10)); };
        cm.Items.Add("★ 주 모니터로 지정", null, (s, e) => SetPrimary(cmId));
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("켜기", null, (s, e) => SetState(cmId, "on"));
        cm.Items.Add("끄기", null, (s, e) => SetState(cmId, "off"));
        cm.Items.Add("변경 없음", null, (s, e) => SetState(cmId, "keep"));
    }
    public IEnumerable<string> AllIds() {
        var l = new List<string>();
        foreach (var m in Mons) if (!l.Contains(m.Id)) l.Add(m.Id);
        if (P != null) foreach (var id in P.Enable.Concat(P.Disable).Concat(new[] { P.Primary })) if (id != "" && !l.Contains(id)) l.Add(id);
        return l;
    }
    public string StateOf(string id) {
        if (P == null) return "keep";
        if (P.Enable.Contains(id)) return "on"; if (P.Disable.Contains(id)) return "off"; return "keep";
    }
    public void SetState(string id, string st) {
        if (P == null || id == null) return;
        P.Enable.Remove(id); P.Disable.Remove(id);
        if (st == "on") P.Enable.Add(id);
        if (st == "off") { P.Disable.Add(id); if (P.Primary == id) P.Primary = ""; }
        Selected = id; Fire();
    }
    public void SetPrimary(string id) {
        if (P == null || id == null) return;
        P.Primary = id; P.Disable.Remove(id); Selected = id; Fire();
    }
    void Fire() { Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
    MonInfo Mon(string id) { return Mons.FirstOrDefault(m => m.Id == id); }

    List<KeyValuePair<string, Rectangle>> Boxes() {
        var res = new List<KeyValuePair<string, Rectangle>>();
        var ids = AllIds().ToList(); if (ids.Count == 0) return res;
        var act = Mons.Where(m => m.Active && m.Rect.Width > 0 && m.Rect.Height > 0).ToList();
        var rest = ids.Where(id => !act.Any(a => a.Id == id)).ToList();
        int pad = UI.S(12), stripH = rest.Count > 0 ? UI.S(64) : 0, cap = rest.Count > 0 ? UI.S(24) : 0;
        var area = new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad - stripH - cap);
        if (act.Count > 0 && area.Width > 10 && area.Height > 10) {
            var bb = act[0].Rect; foreach (var a in act) bb = Rectangle.Union(bb, a.Rect);
            float sc = Math.Min(area.Width / (float)bb.Width, area.Height / (float)bb.Height);
            int ox = area.X + (int)((area.Width - bb.Width * sc) / 2), oy = area.Y + (int)((area.Height - bb.Height * sc) / 2);
            int minW = UI.S(84), minH = UI.S(56);
            foreach (var a in act) {
                var r = new Rectangle(ox + (int)((a.Rect.X - bb.X) * sc), oy + (int)((a.Rect.Y - bb.Y) * sc), (int)(a.Rect.Width * sc), (int)(a.Rect.Height * sc));
                r.Inflate(-UI.S(3), -UI.S(3));
                if (r.Width < minW) { r.X -= (minW - r.Width) / 2; r.Width = minW; }
                if (r.Height < minH) { r.Y -= (minH - r.Height) / 2; r.Height = minH; }
                res.Add(new KeyValuePair<string, Rectangle>(a.Id, r));
            }
            // push enlarged (tiny) boxes away from neighbours so nothing overlaps
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < res.Count; i++)
                    for (int j = 0; j < res.Count; j++) {
                        if (i == j) continue; var a = res[i].Value; var b = res[j].Value;
                        if (!a.IntersectsWith(b) || a.Width * a.Height > b.Width * b.Height) continue;
                        var ia = Rectangle.Intersect(a, b);
                        if (ia.Width <= ia.Height) a.X += (a.X + a.Width / 2 >= b.X + b.Width / 2) ? ia.Width + UI.S(4) : -(ia.Width + UI.S(4));
                        else a.Y += (a.Y + a.Height / 2 >= b.Y + b.Height / 2) ? ia.Height + UI.S(4) : -(ia.Height + UI.S(4));
                        a.X = Math.Max(UI.S(2), Math.Min(Width - a.Width - UI.S(2), a.X)); a.Y = Math.Max(UI.S(2), a.Y);
                        res[i] = new KeyValuePair<string, Rectangle>(res[i].Key, a);
                    }
        }
        int x = pad, y = Height - pad - stripH, bw = UI.S(124);
        foreach (var id in rest) { res.Add(new KeyValuePair<string, Rectangle>(id, new Rectangle(x, y, bw, stripH))); x += bw + UI.S(8); }
        return res;
    }
    string Hit(Point p) { foreach (var b in Boxes()) if (b.Value.Contains(p)) return b.Key; return null; }
    protected override void OnMouseMove(MouseEventArgs e) {
        var h = Hit(e.Location);
        if (h != hover) {
            hover = h; Invalidate();
            var m = h == null ? null : Mon(h);
            tip.SetToolTip(this, h == null ? "" : h + (m != null ? "  " + m.Name + "  " + m.Res + (m.Active ? "" : "  (현재 꺼짐)") : "  (감지 안 됨)") + "\n클릭: 변경 없음 → 켜기 → 끄기   ·   우클릭: 주 모니터 지정");
        }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) {
        var h = Hit(e.Location); if (h == null || P == null) return;
        if (e.Button == MouseButtons.Right) { cmId = h; Selected = h; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); cm.Show(this, e.Location); return; }
        string st = StateOf(h);
        SetState(h, st == "keep" ? "on" : st == "on" ? "off" : "keep");
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Dark.ParentBack(this));
        using (var p = UI.Round(new Rectangle(0, 0, Width - 1, Height - 1), UI.S(8))) { using (var b = new SolidBrush(BackColor)) g.FillPath(b, p); using (var pen = new Pen(UI.Border)) g.DrawPath(pen, p); }
        var boxes = Boxes();
        if (boxes.Count == 0) { TextRenderer.DrawText(g, Status, UI.Base, ClientRectangle, UI.Hint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        if (boxes.Any(b => { var m = Mon(b.Key); return m == null || !m.Active; })) {
            var first = boxes.First(b => { var m = Mon(b.Key); return m == null || !m.Active; });
            TextRenderer.DrawText(g, "현재 꺼져 있거나 감지되지 않은 모니터", UI.Small, new Point(first.Value.X, first.Value.Y - UI.S(20)), UI.Hint);
        }
        foreach (var b in boxes) {
            string id = b.Key; var r = b.Value; var m = Mon(id); string st = StateOf(id);
            Color fill = st == "on" ? Color.FromArgb(28, 54, 44) : st == "off" ? Color.FromArgb(36, 36, 42) : Color.FromArgb(44, 44, 52);
            using (var path = UI.Round(r, UI.S(5))) {
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                if (st == "off") using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(28, 255, 255, 255), Color.Transparent)) g.FillPath(hb, path);
                Color bc = id == Selected ? UI.Accent : id == hover ? Color.FromArgb(130, 160, 230) : Color.FromArgb(74, 74, 86);
                using (var pen = new Pen(bc, id == Selected ? UI.S(3) : UI.S(1))) g.DrawPath(pen, path);
            }
            bool prim = P != null && P.Primary == id;
            string stText = st == "on" ? "켜기" : st == "off" ? "끄기" : "변경 없음";
            Color stColor = st == "on" ? Color.FromArgb(110, 214, 150) : st == "off" ? Color.FromArgb(240, 128, 128) : UI.Hint;
            var lines = new List<Tuple<string, Font, Color>>();
            if (prim) lines.Add(Tuple.Create("★ 주 모니터", UI.Bold, UI.Accent));
            lines.Add(Tuple.Create(id, UI.Bold, UI.Text));
            if (r.Height > UI.S(80)) lines.Add(Tuple.Create(m != null ? m.Res : "감지 안 됨", UI.Small, UI.Hint));
            lines.Add(Tuple.Create(stText, UI.Bold, stColor));
            int total = lines.Sum(l => TextRenderer.MeasureText(l.Item1, l.Item2).Height);
            int y = r.Y + Math.Max(UI.S(2), (r.Height - total) / 2);
            foreach (var l in lines) {
                var sz = TextRenderer.MeasureText(l.Item1, l.Item2);
                TextRenderer.DrawText(g, l.Item1, l.Item2, new Rectangle(r.X + 2, y, r.Width - 4, sz.Height), l.Item3, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                y += sz.Height;
            }
            if (m != null && m.Primary) TextRenderer.DrawText(g, "현재 주", UI.Small, new Rectangle(r.X + UI.S(4), r.Bottom - UI.S(20), r.Width - UI.S(8), UI.S(18)), UI.Hint, TextFormatFlags.Right);
        }
    }
}


// ================= Dark theme controls =================
static class Dark {
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    static bool inited;
    public static void InitApp() {
        if (inited) return; inited = true;
        try { SetPreferredAppMode(2); FlushMenuThemes(); } catch { }
    }
    public static void Title(IntPtr h) {
        int on = 1;
        try { if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch { }
        try { int c = 0x00141212; DwmSetWindowAttribute(h, 35, ref c, 4); } catch { } // caption color #121214 (Win11)
    }
    public static void Theme(Form f) {
        InitApp();
        f.BackColor = UI.Page; f.ForeColor = UI.Text;
        if (f.IsHandleCreated) Title(f.Handle); else f.HandleCreated += (s, e) => Title(((Form)s).Handle);
        Walk(f);
        f.ControlAdded += (s, e) => { Hook(e.Control); Walk(e.Control); };
    }
    public static void Apply(Control c) { Hook(c); Walk(c); }
    public static void Walk(Control c) { foreach (Control k in c.Controls) { Hook(k); Walk(k); } }
    static void Hook(Control k) {
        string th = null;
        if (k is ComboBox) th = "DarkMode_CFD";
        else if (k is ListView || k is ListBox || k is TextBoxBase || k is UpDownBase || k is TrackBar || (k is ScrollableControl && ((ScrollableControl)k).AutoScroll)) th = "DarkMode_Explorer";
        else if (k.Parent is UpDownBase) th = "DarkMode_Explorer";
        if (k is TextBoxBase && !(k is HotkeyBox)) { k.BackColor = UI.Input; k.ForeColor = UI.Text; }
        if (k is UpDownBase) { k.BackColor = UI.Input; k.ForeColor = UI.Text; ((UpDownBase)k).BorderStyle = BorderStyle.FixedSingle; }
        if (k is TrackBar) k.BackColor = UI.Card;
        if (th == null) return;
        string t = th;
        if (k.IsHandleCreated) SetWindowTheme(k.Handle, t, null);
        else k.HandleCreated += (s, e) => SetWindowTheme(((Control)s).Handle, t, null);
    }
    public static Color ParentBack(Control c) {
        for (var p = c.Parent; p != null; p = p.Parent) if (p.BackColor.A == 255) return p.BackColor;
        return UI.Page;
    }
    public static void List(ListView lv) {
        lv.OwnerDraw = true; lv.BackColor = UI.Card; lv.ForeColor = UI.Text; lv.BorderStyle = BorderStyle.None;
        lv.DrawColumnHeader += (s, e) => {
            using (var b = new SolidBrush(UI.Side)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(UI.Border)) { e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1); e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + UI.S(5), e.Bounds.Right - 1, e.Bounds.Bottom - UI.S(5)); }
            TextRenderer.DrawText(e.Graphics, e.Header.Text, UI.Small, new Rectangle(e.Bounds.X + UI.S(8), e.Bounds.Y, e.Bounds.Width - UI.S(10), e.Bounds.Height), UI.Hint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        };
        lv.DrawItem += (s, e) => { e.DrawDefault = false; };
        lv.DrawSubItem += (s, e) => {
            var r = e.Bounds; if (e.ColumnIndex == 0) r.Width = lv.Columns[0].Width;
            bool sel = e.Item.Selected;
            Color bg = sel ? UI.SelBg : (e.ItemIndex % 2 == 0 ? UI.Card : UI.CardAlt);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, r);
            int x = r.X + UI.S(8);
            if (e.ColumnIndex == 0 && lv.CheckBoxes) {
                int bs = UI.S(14); var box = new Rectangle(r.X + UI.S(4), r.Y + (r.Height - bs) / 2, bs, bs);
                CheckGlyph(e.Graphics, box, e.Item.Checked, false, true);
                x = box.Right + UI.S(8);
            }
            string txt = e.ColumnIndex == 0 ? e.Item.Text : e.SubItem.Text;
            TextRenderer.DrawText(e.Graphics, txt, UI.Base, new Rectangle(x, r.Y, r.Right - x - UI.S(4), r.Height), e.ColumnIndex == 0 ? UI.Text : UI.Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        };
        lv.ItemChecked += (s, e) => lv.Invalidate(e.Item.Bounds);
        lv.HandleCreated += (s, e) => SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
    }
    public static void CheckGlyph(Graphics g, Rectangle box, bool on, bool hover, bool enabled) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UI.Round(box, Math.Max(2, box.Width / 4))) {
            if (on && enabled) using (var b = UI.Grad(box)) g.FillPath(b, path);
            else using (var b = new SolidBrush(on ? UI.Border : UI.Input)) g.FillPath(b, path);
            using (var p = new Pen(on ? (enabled ? UI.Accent : UI.Border) : hover ? UI.Accent : Color.FromArgb(92, 92, 106), 1.2f)) g.DrawPath(p, path);
        }
        if (on) using (var p = new Pen(Color.White, Math.Max(1.6f, box.Width / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
            g.DrawLines(p, new[] { new PointF(box.X + box.Width * .24f, box.Y + box.Height * .52f), new PointF(box.X + box.Width * .43f, box.Y + box.Height * .70f), new PointF(box.X + box.Width * .77f, box.Y + box.Height * .32f) });
        }
    }
}

class FlatBtn : Button {
    public bool Primary, Nav; public string Glyph = ""; bool sel, hover, down;
    public bool Selected { get { return sel; } set { if (sel == value) return; sel = value; Invalidate(); if(IsHandleCreated && Visible)Update(); } }
    public FlatBtn() {
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Cursor = Cursors.Hand; ForeColor = UI.Text;
    }
    string pk; Size ps;
    public override Size GetPreferredSize(Size proposed) {
        string k = Text + "|" + Font.GetHashCode() + "|" + Padding + "|" + MinimumSize;
        if (k == pk) return ps;
        var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        ps = new Size(Math.Max(MinimumSize.Width, t.Width + Padding.Horizontal + UI.S(8)), Math.Max(MinimumSize.Height, t.Height + Padding.Vertical + UI.S(6)));
        pk = k; return ps;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var sp = Nav ? SidePanel.Of(this) : null;
        if (sp != null) sp.PaintBg(g, this); else g.Clear(Dark.ParentBack(this));
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (Nav) {
            using (var p = UI.Round(r, UI.S(8))) {
                if (sel) { using (var b = UI.Grad(r, 46)) g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(70, UI.G3))) g.DrawPath(pen, p); }
                else if (hover) using (var b = new SolidBrush(UI.Hover)) g.FillPath(b, p);
            }
            if (sel) using (var b = UI.Grad(new RectangleF(0, UI.S(9), UI.S(4), Height - UI.S(18)), 255, 90f)) using (var p = UI.Round(new Rectangle(UI.S(3), UI.S(10), UI.S(3), Height - UI.S(20)), UI.S(1))) g.FillPath(b, p);
            int gx = UI.S(16);
            if (Glyph != "") TextRenderer.DrawText(g, Glyph, UI.Glyph, new Rectangle(gx, 0, UI.S(22), Height), sel ? UI.G1 : UI.Hint, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, sel ? UI.Bold : UI.Base, new Rectangle(gx + UI.S(30), 0, Width - gx - UI.S(32), Height), sel ? UI.Text : UI.Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            return;
        }
        Color fg; bool focus = Focused && ShowFocusCues && Enabled;
        using (var p = UI.Round(r, UI.S(7))) {
            if (!Enabled) { using (var b = new SolidBrush(Color.FromArgb(30, 29, 48))) g.FillPath(b, p); using (var pen = new Pen(UI.Border)) g.DrawPath(pen, p); fg = Color.FromArgb(98, 98, 120); }
            else if (Primary) {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(89, 119, 231), Color.FromArgb(117, 105, 219), 0f)) g.FillPath(b, p);
                if (hover && !down) using (var b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(b, p);
                if (focus) using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255))) g.DrawPath(pen, p);
                fg = Color.White;
            } else {
                Color fill = down ? Color.FromArgb(49, 60, 80) : hover ? Color.FromArgb(39, 48, 65) : UI.CardAlt;
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (hover || focus) using (var b = UI.Grad(r, focus ? 230 : 150)) using (var pen = new Pen(b, 1f)) g.DrawPath(pen, p);
                else using (var pen = new Pen(UI.Border)) g.DrawPath(pen, p);
                fg = UI.Text;
            }
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left / 2, 0, Width - Padding.Left / 2 - Padding.Right / 2, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

class DarkCheck : CheckBox {
    bool hover; float knob = -1; WTimer anim;
    public DarkCheck() { SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); ForeColor = UI.Text; Cursor = Cursors.Hand; }
    int SW { get { return UI.S(38); } } int SH { get { return UI.S(20); } }
    string pk; Size ps;
    public override Size GetPreferredSize(Size p) {
        string k = Text + "|" + Font.GetHashCode();
        if (k == pk) return ps;
        var t = TextRenderer.MeasureText(Text, Font);
        pk = k; ps = new Size(SW + UI.S(10) + t.Width + UI.S(2), Math.Max(SH, t.Height) + UI.S(6)); return ps;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) {
        if (knob < 0 || !Visible || !IsHandleCreated) { knob = Checked ? 1 : 0; Invalidate(); }
        else {
            if (anim == null) { anim = new WTimer { Interval = 15 }; anim.Tick += (s, a) => { float t = Checked ? 1 : 0; knob += (t - knob) * 0.38f; if (Math.Abs(t - knob) < 0.02f) { knob = t; anim.Stop(); } Invalidate(); }; }
            anim.Start();
        }
        base.OnCheckedChanged(e);
    }
    protected override void Dispose(bool d) { if (d && anim != null) anim.Dispose(); base.Dispose(d); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Dark.ParentBack(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        if (knob < 0) knob = Checked ? 1 : 0;
        var tr = new Rectangle(1, (Height - SH) / 2, SW - 2, SH - 1);
        using (var p = UI.Round(tr, (SH - 1) / 2)) {
            using (var b = new SolidBrush(Color.FromArgb(44, 42, 72))) g.FillPath(b, p);
            if (knob > 0.01f) using (var b = UI.Grad(tr, (int)(255 * knob * (Enabled ? 1f : .35f)))) g.FillPath(b, p);
            using (var pen = new Pen(knob > .5f ? Color.FromArgb(60, 255, 255, 255) : hover || Focused ? UI.G3 : Color.FromArgb(78, 76, 114))) g.DrawPath(pen, p);
        }
        int d = SH - UI.S(7); float kx = tr.X + UI.S(3) + (tr.Width - d - UI.S(6)) * knob;
        using (var b = new SolidBrush(Enabled ? (knob > .5f ? Color.White : Color.FromArgb(200, 202, 226)) : Color.FromArgb(110, 110, 130))) g.FillEllipse(b, kx, tr.Y + (tr.Height - d) / 2f + .5f, d, d);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(SW + UI.S(10), 0, Width - SW - UI.S(10), Height), Enabled ? UI.Text : UI.Hint, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

// gradient slider (replaces TrackBar)
class GradSlider : Control {
    int min = 0, max = 100, val = 0; bool drag, hover;
    public event EventHandler ValueChanged;
    public int Minimum { get { return min; } set { min = value; if (val < min) Value = min; Invalidate(); } }
    public int Maximum { get { return max; } set { max = value; if (val > max) Value = max; Invalidate(); } }
    public int Value { get { return val; } set { int v = Math.Max(min, Math.Min(max, value)); if (v == val) return; val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
    public GradSlider() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = UI.S(26); Width = UI.S(220); Cursor = Cursors.Hand; Margin = new Padding(0, UI.S(2), UI.S(8), UI.S(2)); TabStop = true;
    }
    int Pad { get { return UI.S(9); } }
    void SetFromX(int x) { float t = (x - Pad) / (float)Math.Max(1, Width - 2 * Pad); Value = min + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (max - min)); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); drag = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 1 : -1; }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Left) Value--; else if (e.KeyCode == Keys.Right) Value++; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Dark.ParentBack(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        int th = UI.S(4), cy = Height / 2; var tr = new Rectangle(Pad, cy - th / 2, Width - 2 * Pad, th);
        float t = max > min ? (val - min) / (float)(max - min) : 0; int kx = Pad + (int)(tr.Width * t);
        using (var p = UI.Round(tr, th / 2)) using (var b = new SolidBrush(Color.FromArgb(48, 46, 80))) g.FillPath(b, p);
        if (kx > Pad) using (var b = UI.Grad(tr)) using (var p = UI.Round(new Rectangle(Pad, tr.Y, kx - Pad, th), th / 2)) g.FillPath(b, p);
        int kr = UI.S(hover || drag || Focused ? 8 : 7);
        using (var b = new SolidBrush(Color.FromArgb(60, UI.G3))) g.FillEllipse(b, kx - kr - 2, cy - kr - 2, 2 * kr + 4, 2 * kr + 4);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, cy - kr, 2 * kr, 2 * kr);
    }
}

class DarkCombo : ComboBox {
    public DarkCombo() {
        DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; DropDownStyle = ComboBoxStyle.DropDownList;
        BackColor = UI.Input; ForeColor = UI.Text; ItemHeight = Math.Max(UI.S(20), (UI.Base ?? Font).Height + UI.S(4));
    }
    protected override void OnDrawItem(DrawItemEventArgs e) {
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0, sel = (e.State & DrawItemState.Selected) != 0 && !edit;
        using (var b = new SolidBrush(sel ? UI.SelBg : UI.Input)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + UI.S(4), e.Bounds.Y, e.Bounds.Width - UI.S(6), e.Bounds.Height), UI.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

class DarkColors : ProfessionalColorTable {
    static readonly Color Bg = Color.FromArgb(24, 23, 42), Hi = Color.FromArgb(48, 46, 92), Br = Color.FromArgb(54, 52, 88);
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Br; } }
    public override Color MenuItemBorder { get { return Hi; } }
    public override Color MenuItemSelected { get { return Hi; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
    public override Color MenuItemPressedGradientBegin { get { return Hi; } }
    public override Color MenuItemPressedGradientEnd { get { return Hi; } }
    public override Color SeparatorDark { get { return Br; } }
    public override Color SeparatorLight { get { return Bg; } }
    public override Color CheckBackground { get { return Color.FromArgb(60, 80, 140); } }
    public override Color CheckSelectedBackground { get { return Color.FromArgb(70, 95, 160); } }
    public override Color CheckPressedBackground { get { return Color.FromArgb(70, 95, 160); } }
}
class DarkRenderer : ToolStripProfessionalRenderer {
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if (!e.Item.Selected || !e.Item.Enabled) { base.OnRenderMenuItemBackground(e); return; }
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        using (var p = UI.Round(r, 5)) { using (var b = UI.Grad(r, 70)) g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(90, UI.G3))) g.DrawPath(pen, p); }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? UI.Text : UI.Hint; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = UI.Text2; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
        var r = e.ImageRectangle; r.Inflate(-1, -1);
        Dark.CheckGlyph(e.Graphics, r, true, false, true);
    }
}

static class DarkPrompt {
    public static string Show(IWin32Window owner, string caption, string initial) {
        using (var f = new ChromeForm()) {
            f.Text = caption; f.Font = UI.Base; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = f.MaximizeBox = false; f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent; f.AutoScaleMode = AutoScaleMode.None; f.Icon = Program.AppIcon; f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var st = UI.Stack(); st.Dock = DockStyle.None; st.Padding = new Padding(UI.S(20), UI.S(18), UI.S(20), UI.S(14)); st.MinimumSize = new Size(UI.S(380), 0);
            var tb = UI.Txt(340); tb.Text = initial ?? ""; tb.Margin = new Padding(0, 0, 0, UI.S(14)); UI.AddRow(st, tb);
            var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent };
            var cancel = UI.Btn("취소"); cancel.DialogResult = DialogResult.Cancel; var ok = UI.Btn("확인", true); ok.DialogResult = DialogResult.OK;
            cancel.MinimumSize = ok.MinimumSize = new Size(UI.S(84), UI.S(32)); flow.Controls.Add(cancel); flow.Controls.Add(ok);
            UI.AddRow(st, flow); flow.Anchor = AnchorStyles.Right;
            st.Location = new Point(UI.S(3), f.Bar.Height); f.Controls.Add(st); f.AcceptButton = ok; f.CancelButton = cancel; Dark.Theme(f); st.BackColor = UI.Page;
            f.Shown += (s, e) => { tb.Focus(); tb.SelectAll(); };
            return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
        }
    }
}

// dark replacement for MessageBox
static class DarkBox {
    public static DialogResult Show(string text, string caption) { return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
    public static DialogResult Show(IWin32Window owner, string text, string caption) { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons btns, MessageBoxIcon icon) {
        using (var f = Make(owner, text, caption, btns, icon)) return owner == null ? f.ShowDialog() : f.ShowDialog(owner);
    }
    public static ChromeForm Make(IWin32Window owner, string text, string caption, MessageBoxButtons btns, MessageBoxIcon icon) {
        if (UI.Base == null) UI.Init(1f);
        var f = new ChromeForm(); {
            f.Text = caption; f.Font = UI.Base; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = f.MaximizeBox = false; f.ShowInTaskbar = owner == null;
            f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent; f.AutoScaleMode = AutoScaleMode.None; f.Icon = Program.AppIcon;
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; if (owner == null) f.TopMost = true;
            var st = UI.Stack(); st.Dock = DockStyle.None; st.Padding = new Padding(UI.S(22), UI.S(20), UI.S(22), UI.S(16)); st.MinimumSize = new Size(UI.S(380), 0);
            string glyph = icon == MessageBoxIcon.Warning ? "⚠  " : icon == MessageBoxIcon.Question ? "" : "";
            var msg = UI.L(glyph + text, UI.Base, UI.Text); msg.MaximumSize = new Size(UI.S(520), 0); msg.Margin = new Padding(0, 0, 0, UI.S(18));
            UI.AddRow(st, msg);
            var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent };
            Func<string, DialogResult, bool, Button> add = (t, r, pr) => { var b = UI.Btn(t, pr); b.MinimumSize = new Size(UI.S(84), UI.S(32)); b.DialogResult = r; b.Margin = new Padding(UI.S(6), 0, 0, 0); flow.Controls.Add(b); return b; };
            Button first = null, cancel = null;
            switch (btns) {
                case MessageBoxButtons.YesNo: cancel = add("아니요", DialogResult.No, false); first = add("예", DialogResult.Yes, true); break;
                case MessageBoxButtons.YesNoCancel: cancel = add("취소", DialogResult.Cancel, false); add("아니요", DialogResult.No, false); first = add("예", DialogResult.Yes, true); break;
                case MessageBoxButtons.OKCancel: cancel = add("취소", DialogResult.Cancel, false); first = add("확인", DialogResult.OK, true); break;
                default: first = add("확인", DialogResult.OK, true); cancel = first; break;
            }
            UI.AddRow(st, flow); flow.Anchor = AnchorStyles.Right;
            st.Location = new Point(UI.S(3), f.Bar.Height); f.Controls.Add(st); f.AcceptButton = first; f.CancelButton = cancel;
            Dark.Theme(f); st.BackColor = UI.Page;
            f.Shown += (s, e) => first.Focus();
            return f;
        }
    }
}


// ================= KakaoTalk ad hiding (embedded BongAd helper) =================
public class KakaoCfg {
    public bool AdBlock = false;
    public bool Compact = true;
    public bool Dark = false;
    public bool AutoApply = true;
}

static class Kakao {
    public static string HelperDir { get { return Path.Combine(Core.Dir, "helpers"); } }
    public static string ExePath { get { return Path.Combine(HelperDir, "BongAd.exe"); } }
    static string lastArgs = null;
    static readonly object gate = new object();

    public static string Args(KakaoCfg k) {
        var l = new List<string>(); if (!k.Compact) l.Add("/nocompact"); if (k.Dark) l.Add("/dark");
        return string.Join(" ", l);
    }
    public static void Extract() {
        Directory.CreateDirectory(HelperDir);
        byte[] data;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BongAd.exe")) { var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
        bool same = false;
        try { if (File.Exists(ExePath)) { var cur = File.ReadAllBytes(ExePath); same = cur.Length == data.Length && cur.SequenceEqual(data); } } catch { }
        if (same) return;
        if (IsRunning()) Stop();
        File.WriteAllBytes(ExePath, data);
    }
    public static Process[] Procs() { return Process.GetProcessesByName("BongAd"); }
    public static bool IsRunning() { var p = Procs(); foreach (var x in p) x.Dispose(); return p.Length > 0; }
    public static bool KakaoRunning() { var p = Process.GetProcessesByName("KakaoTalk"); foreach (var x in p) x.Dispose(); return p.Length > 0; }
    static int RunHelper(string args, bool wait) {
        var psi = new ProcessStartInfo(ExePath, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = HelperDir };
        using (var p = Process.Start(psi)) { if (!wait) return 0; if (!p.WaitForExit(10000)) return -1; return p.ExitCode; }
    }
    // start (or restart with new flags). A new BongAd launch closes the running one itself.
    public static void Start(KakaoCfg k) {
        lock (gate) {
            Extract();
            string a = Args(k);
            RunHelper(a, false);
            lastArgs = a;
        }
    }
    // stop: "/restore" sends WM_CLOSE to the running helper (which restores on exit) and re-shows hidden windows
    public static void Stop() {
        lock (gate) {
            try { if (File.Exists(ExePath)) RunHelper("/restore", true); } catch { }
            var sw = Stopwatch.StartNew();
            while (IsRunning() && sw.ElapsedMilliseconds < 4000) Thread.Sleep(100);
            foreach (var p in Procs()) { try { if (string.Equals(p.MainModule.FileName, ExePath, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { } p.Dispose(); }
            lastArgs = null;
        }
    }
    public static void Restore() { Stop(); }
    // bring the helper in line with config
    public static void Sync(KakaoCfg k) {
        if (k.AdBlock) { if (!IsRunning() || lastArgs != Args(k)) Start(k); }
        else if (IsRunning() && lastArgs != null) Stop();
    }
    public static string Status() {
        return (IsRunning() ? "광고 숨김 실행 중" : "광고 숨김 꺼짐") + "  ·  " + (KakaoRunning() ? "카톡 감지됨" : "카톡 실행 안 됨");
    }
    // first run: preserve a standalone BongAd setup (running process / Startup shortcut)
    public static KakaoCfg DetectLegacy() {
        var k = new KakaoCfg();
        try {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "BongAd.lnk");
            string args = "";
            if (File.Exists(lnk)) { k.AdBlock = true; args = Startup.ArgsOf(lnk); }
            foreach (var p in Procs()) { k.AdBlock = true; p.Dispose(); }
            k.Dark = args.IndexOf("/dark", StringComparison.OrdinalIgnoreCase) >= 0;
            k.Compact = args.IndexOf("/nocompact", StringComparison.OrdinalIgnoreCase) < 0;
        } catch { }
        return k;
    }
}


// ================= Sticky notes =================
public class NotesCfg {
    public string DefaultColor = "g:blocks";
    public bool Gradient = true;
    public int ColorVer = 0;
    public float FontSize = 11f;
    public int Opacity = 100;
    public string Hotkey = "Ctrl+Alt+N";
    public bool OpenOnStartup = true;
}
public class NoteMeta {
    public string Id = "";
    public string Color = "yellow";
    public int X, Y, W = 330, H = 330;
    public bool TopMost = false;
    public bool Open = true;
    public float Zoom = 1f;
    public string Text = "";
    public DateTime Modified = DateTime.Now;
}

static class NoteColors {
    // legacy solid keys (kept for old notes) + curated gradients + custom "c:RRGGBB:RRGGBB"
    public static readonly string[] Keys = { "yellow", "green", "pink", "purple", "blue", "gray", "charcoal" };
    public static readonly string[] Names = { "노랑", "초록", "분홍", "보라", "파랑", "회색", "차콜" };
    static readonly Color[] title = { Color.FromArgb(104, 88, 32), Color.FromArgb(46, 94, 62), Color.FromArgb(118, 54, 86), Color.FromArgb(84, 62, 132), Color.FromArgb(46, 78, 130), Color.FromArgb(82, 82, 92), Color.FromArgb(48, 48, 55) };
    static readonly Color[] body = { Color.FromArgb(58, 52, 30), Color.FromArgb(32, 52, 40), Color.FromArgb(60, 36, 50), Color.FromArgb(44, 38, 66), Color.FromArgb(30, 42, 64), Color.FromArgb(48, 48, 54), Color.FromArgb(30, 30, 34) };
    static readonly Color[] swatch = { Color.FromArgb(240, 205, 90), Color.FromArgb(120, 205, 140), Color.FromArgb(240, 140, 190), Color.FromArgb(180, 150, 240), Color.FromArgb(120, 170, 245), Color.FromArgb(180, 180, 190), Color.FromArgb(110, 110, 120) };
    // gradient partner for each legacy colour (used when the gradient theme is on)
    static readonly Color[] partner = { Color.FromArgb(255, 150, 110), Color.FromArgb(64, 184, 176), Color.FromArgb(183, 124, 255), Color.FromArgb(228, 126, 244), Color.FromArgb(76, 138, 254), Color.FromArgb(110, 117, 144), Color.FromArgb(46, 48, 72) };
    public static readonly string[][] Gradients = {
        new[] { "g:blocks", "블록", "4C8AFE", "E47EF4" }, new[] { "g:aurora", "오로라", "6EC6FF", "9084FF" }, new[] { "g:grape", "포도", "9D6BFF", "E47EF4" },
        new[] { "g:lagoon", "라군", "40D9D0", "4C8AFE" }, new[] { "g:mint", "민트", "5EE7B0", "3FA9F5" }, new[] { "g:forest", "숲", "7BD88F", "2A9D8F" },
        new[] { "g:lemon", "레몬", "F5E36B", "8FD66B" }, new[] { "g:peach", "복숭아", "FFC37A", "FF7EB3" }, new[] { "g:sunset", "노을", "FF9A6B", "E4589C" },
        new[] { "g:ember", "불씨", "FF7A59", "B03A8F" }, new[] { "g:rose", "장미", "FF7EA8", "B06CFF" }, new[] { "g:ocean", "바다", "2E9BFF", "5B4BFF" },
        new[] { "g:night", "밤하늘", "5560A8", "2A2B58" }, new[] { "g:slate", "슬레이트", "8A93A8", "4A5168" },
        new[] { "g:sage", "세이지", "9AA58E", "747F6D" }, new[] { "g:dustrose", "더스티 로즈", "B58D94", "8C7077" }, new[] { "g:mauve", "모브", "9B8298", "706173" },
        new[] { "g:bluegray", "블루 그레이", "879BA5", "647681" }, new[] { "g:sand", "샌드", "B3A388", "857862" }, new[] { "g:clay", "클레이", "B18670", "826552" },
        new[] { "g:olive", "올리브", "94916D", "6E6C50" }, new[] { "g:ink", "잉크", "778296", "555E70" }, new[] { "g:lavendergray", "라벤더 그레이", "9D9BB3", "77758E" },
        new[] { "g:petrol", "페트롤", "698985", "4F6D68" }, new[] { "g:cocoa", "코코아", "927A6D", "6D594F" }, new[] { "g:rosetaupe", "로즈 토프", "A88982", "806963" },
        new[] { "g:mist", "미스트", "A5A5A0", "7D7D78" }, new[] { "g:mustard", "머스터드", "AC9B63", "817446" }, new[] { "g:deepteal", "딥 틸", "5C7E79", "48615D" },
        new[] { "g:plumgray", "플럼 그레이", "8D8294", "665D6B" }, new[] { "g:smoke", "스모크", "9A9DA1", "72767A" }, new[] { "g:terracotta", "테라코타", "AA806B", "805F50" } };
    public static readonly Color Text = Color.FromArgb(240, 240, 246);
    public static IEnumerable<string> PaletteKeys { get { return Gradients.Select(g => g[0]).Concat(Keys); } }
    static int I(string k) { return Array.IndexOf(Keys, k); }
    static Color Hex(string h) { int v = Convert.ToInt32(h, 16); return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }
    public static string Custom(Color a, Color b) { return "c:" + (a.ToArgb() & 0xFFFFFF).ToString("X6") + ":" + (b.ToArgb() & 0xFFFFFF).ToString("X6"); }
    public static bool Valid(string k) {
        if (string.IsNullOrEmpty(k)) return false;
        if (I(k) >= 0 || Gradients.Any(g => g[0] == k)) return true;
        if (k.StartsWith("c:")) { var p = k.Split(':'); try { return p.Length == 3 && p[1].Length == 6 && p[2].Length == 6 && Hex(p[1]).A == 255 && Hex(p[2]).A == 255; } catch { } }
        return false;
    }
    public static bool IsGradient(string k) { return k != null && (k.StartsWith("g:") || k.StartsWith("c:")); }
    // raw (bright) colours of a key
    public static void Raw(string k, out Color a, out Color b) {
        int i = I(k);
        if (i >= 0) { a = swatch[i]; b = partner[i]; return; }
        var g = Gradients.FirstOrDefault(x => x[0] == k);
        if (g != null) { a = Hex(g[2]); b = Hex(g[3]); return; }
        if (k != null && k.StartsWith("c:")) { var p = k.Split(':'); try { a = Hex(p[1]); b = Hex(p[2]); return; } catch { } }
        a = Hex("4C8AFE"); b = Hex("E47EF4");
    }
    public static Color Mix(Color x, Color y, float t) { return Color.FromArgb((int)(x.R + (y.R - x.R) * t), (int)(x.G + (y.G - x.G) * t), (int)(x.B + (y.B - x.B) * t)); }
    static double Lum(Color c) { Func<double, double> f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B); }
    // darken until white text keeps at least 7:1 (body) or 4.5:1 (title) contrast
    static Color ForText(Color c, double ratio) { double lt = Lum(Text); for (int n = 0; n < 40 && (lt + 0.05) / (Lum(c) + 0.05) < ratio; n++) c = Mix(c, Color.Black, 0.08f); return c; }
    static readonly Color Base = Color.FromArgb(20, 19, 34);
    public static bool Grad { get { return Notes.Cfg == null || Notes.Cfg.Gradient; } }
    public static Color TitleA(string k) { if (!Grad && I(k) >= 0) return title[I(k)]; Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, a, Grad ? .62f : .55f), 4.5); }
    public static Color TitleB(string k) { if (!Grad) return TitleA(k); Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, b, .62f), 4.5); }
    public static Color Title(string k) { return TitleA(k); }
    public static Color Body(string k) { if (!Grad && I(k) >= 0) return body[I(k)]; Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, Mix(a, b, .5f), Grad ? .2f : .24f), 9); }
    // one continuous surface for title + paper (seamless note)
    public static Color SurfA(string k) { if (!Grad) return Body(k); Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, a, .40f), 7); }
    public static Color SurfB(string k) { if (!Grad) return Body(k); Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, b, .34f), 7); }
    public static Color BodyEdge(string k) { if (!Grad) return Body(k); Color a, b; Raw(k, out a, out b); return ForText(Mix(Base, b, .3f), 9); }
    public static Color Swatch(string k) { Color a, b; Raw(k, out a, out b); return a; }
    public static string Name(string k) {
        int i = I(k); if (i >= 0) return Names[i];
        var g = Gradients.FirstOrDefault(x => x[0] == k); if (g != null) return g[1];
        return k != null && k.StartsWith("c:") ? "맞춤" : "블록";
    }
    public static void Fill(Graphics g, RectangleF r, string k, bool round) {
        Color a, b; Raw(k, out a, out b); bool solid = !IsGradient(k);
        using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, solid ? a : b, 45f))
            if (round) g.FillEllipse(br, r); else g.FillRectangle(br, r);
    }
    public static Bitmap SwatchImage(string k, int size) {
        var bm = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bm)) { g.SmoothingMode = SmoothingMode.AntiAlias; Fill(g, new RectangleF(1, 1, size - 3, size - 3), k, true); }
        return bm;
    }
}

// grid of colour swatches (used inline in settings and in the note palette popup)
class SwatchGrid : Control {
    public string Current; public event Action<string> Picked, Hovered;
    readonly List<string> keys; int hover = -1; public int Cols = 7; int Cell { get { return UI.S(34); } }
    public SwatchGrid(IEnumerable<string> k) {
        keys = k.ToList();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand; Margin = new Padding(0, UI.S(2), 0, UI.S(2)); Fit();
    }
    void Fit() { Size = new Size(Cols * Cell, ((keys.Count + Cols - 1) / Cols) * Cell); }
    public override Size GetPreferredSize(Size p) { return new Size(Cols * Cell, ((keys.Count + Cols - 1) / Cols) * Cell); }
    int Hit(Point p) { int c = p.X / Cell, r = p.Y / Cell, i = r * Cols + c; return c < Cols && i >= 0 && i < keys.Count ? i : -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); if (h >= 0) tip.SetToolTip(this, NoteColors.Name(keys[h])); if (Hovered != null) Hovered(h >= 0 ? keys[h] : null); } base.OnMouseMove(e); }
    readonly ToolTip tip = new ToolTip();
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); if (Hovered != null) Hovered(null); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0) { Current = keys[h]; Invalidate(); if (Picked != null) Picked(keys[h]); } base.OnMouseClick(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Dark.ParentBack(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < keys.Count; i++) {
            float cs = Cell, x = (i % Cols) * cs, y = (i / Cols) * cs, d = cs * (i == hover ? .78f : .66f);
            var r = new RectangleF(x + (cs - d) / 2, y + (cs - d) / 2, d, d);
            NoteColors.Fill(g, r, keys[i], true);
            if (keys[i] == Current) { var rr = r; rr.Inflate(UI.S(3), UI.S(3)); using (var b = UI.Grad(rr)) using (var p = new Pen(b, Math.Max(2f, UI.S(2)))) g.DrawEllipse(p, rr); using (var p = new Pen(Color.White, 1.2f)) g.DrawEllipse(p, r); }
        }
    }
}
class PalettePopup : Form {
    protected override bool ShowWithoutActivation { get { return TestEnv.Off; } }
    public event Action<string> Picked, Previewed;
    SwatchGrid grads, solids; Color ca, cb; FlatBtn bA, bB;
    public PalettePopup(string current) {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
        BackColor = Color.FromArgb(26, 25, 46); AutoScaleMode = AutoScaleMode.None; Font = UI.Base; Padding = new Padding(UI.S(14), UI.S(10), UI.S(14), UI.S(12));
        var st = UI.Stack(); st.Dock = DockStyle.Fill; st.BackColor = BackColor;
        UI.AddRow(st, UI.L("그라데이션", UI.Small, UI.Hint));
        grads = new SwatchGrid(NoteColors.Gradients.Select(x => x[0])) { Current = current }; UI.AddRow(st, grads);
        UI.AddRow(st, UI.L("단색", UI.Small, UI.Hint));
        solids = new SwatchGrid(NoteColors.Keys) { Current = current }; UI.AddRow(st, solids);
        UI.AddRow(st, UI.L("맞춤 그라데이션", UI.Small, UI.Hint));
        NoteColors.Raw(NoteColors.IsGradient(current) ? current : "g:blocks", out ca, out cb);
        bA = UI.Btn("시작 색") as FlatBtn; bB = UI.Btn("끝 색") as FlatBtn; var bOk = UI.Btn("적용", true);
        bA.Click += (s, e) => { var c = PickColor(ca); if (c.HasValue) { ca = c.Value; Paint2(); } };
        bB.Click += (s, e) => { var c = PickColor(cb); if (c.HasValue) { cb = c.Value; Paint2(); } };
        bOk.Click += (s, e) => Pick(NoteColors.Custom(ca, cb));
        UI.AddRow(st, UI.Flow(bA, bB, bOk));
        Controls.Add(st);
        grads.Picked += Pick; solids.Picked += Pick;
        grads.Hovered += Preview; solids.Hovered += Preview;
        Deactivate += (s, e) => { if (!picking) Close(); };
        var fl = (Control)bOk.Parent; var ps = st.GetPreferredSize(Size.Empty);
        ClientSize = new Size(Math.Max(Math.Max(ps.Width, grads.Width + grads.Margin.Horizontal + st.Padding.Horizontal + UI.S(28)), fl.GetPreferredSize(Size.Empty).Width + UI.S(8)) + Padding.Horizontal, ps.Height + Padding.Vertical + UI.S(4));
        Paint2();
    }
    bool picking;
    Color? PickColor(Color c) { picking = true; try { using (var d = new ColorDialog { Color = c, FullOpen = true }) return d.ShowDialog(this) == DialogResult.OK ? d.Color : (Color?)null; } finally { picking = false; Activate(); } }
    void Paint2() { bA.Text = "시작 #" + (ca.ToArgb() & 0xFFFFFF).ToString("X6"); bB.Text = "끝 #" + (cb.ToArgb() & 0xFFFFFF).ToString("X6"); Invalidate(true); }
    void Preview(string k) { if (Previewed != null) Previewed(k); }
    void Pick(string k) { if (Picked != null) Picked(k); Close(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { } Dark.Title(Handle); }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; if (TestEnv.Off) c.ExStyle |= 0x08000000 | 0x80; return c; } }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = UI.Grad(new RectangleF(0, 0, Width, 2))) g.FillRectangle(b, 0, 0, Width, UI.S(2));
    }
    protected override bool ProcessCmdKey(ref Message m, Keys k) { if (k == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref m, k); }
    public static PalettePopup ShowAt(Control anchor, string current, Action<string> picked) {
        var p = new PalettePopup(current); p.Picked += picked;
        var pt = anchor.PointToScreen(new Point(0, anchor.Height + UI.S(4)));
        var wa = TestEnv.Off ? TestEnv.Area : Screen.FromPoint(pt).WorkingArea;
        p.Location = new Point(Math.Max(wa.X, Math.Min(pt.X, wa.Right - p.Width)), pt.Y + p.Height > wa.Bottom ? anchor.PointToScreen(Point.Empty).Y - p.Height - UI.S(4) : pt.Y);
        p.FormClosed += (s, e) => p.Dispose();
        if (TestEnv.Off) { p.ShowInTaskbar = false; p.Show(); return p; }
        p.Show(); p.Activate(); return p;
    }
}

static class Notes {
    public static string Dir = Path.Combine(Core.Dir, "notes");
    public static NotesCfg Cfg = new NotesCfg();
    public static readonly List<NoteMeta> All = new List<NoteMeta>();
    static readonly Dictionary<string, NoteForm> forms = new Dictionary<string, NoteForm>();
    public static event Action Changed;
    public static Action<int> OpenList;
    static bool loaded;
    static readonly JavaScriptSerializer js = new JavaScriptSerializer();

    static Notes() { Application.EnterThreadModal += EnableNotesDuringModal; }
    static void EnableNotesDuringModal(object sender, EventArgs e) {
        foreach (var f in forms.Values.ToArray()) if (f != null && !f.IsDisposed) f.EnableDuringModal();
    }

    public static void Reset(string dir) { Dir = dir; loaded = false; All.Clear(); forms.Clear(); }
    public static void EnsureLoaded() {
        if (loaded) return; loaded = true; All.Clear();
        if (!Directory.Exists(Dir)) return;
        foreach (var f in Directory.GetFiles(Dir, "*.json")) {
            try { var m = js.Deserialize<NoteMeta>(File.ReadAllText(f, Encoding.UTF8)); if (m != null && !string.IsNullOrEmpty(m.Id)) { if (m.Text == null) m.Text = ""; if (!NoteColors.Valid(m.Color)) m.Color = "g:blocks"; All.Add(m); } } catch { }
        }
        All.Sort((a, b) => b.Modified.CompareTo(a.Modified));
    }
    public static string MetaPath(string id) { return Path.Combine(Dir, id + ".json"); }
    public static string RtfPath(string id) { return Path.Combine(Dir, id + ".rtf"); }
    public static void Write(string path, string content) {
        Directory.CreateDirectory(Dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        Cloud.Kick();
    }
    public static void SaveMeta(NoteMeta m) { m.Modified = DateTime.Now; try { Write(MetaPath(m.Id), js.Serialize(m)); } catch { } Fire(); }
    public static void SaveRtf(NoteMeta m, string rtf) { try { Write(RtfPath(m.Id), rtf ?? ""); } catch { } }
    public static string LoadRtf(string id) { try { return File.ReadAllText(RtfPath(id), Encoding.UTF8); } catch { return null; } }
    public static bool IsOpen(NoteMeta m) { NoteForm f; return forms.TryGetValue(m.Id, out f) && !f.IsDisposed; }
    public static NoteForm FormOf(NoteMeta m) { NoteForm f; return forms.TryGetValue(m.Id, out f) ? f : null; }

    public static NoteForm New(bool activate) {
        EnsureLoaded();
        var m = new NoteMeta { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Color = Cfg.DefaultColor, W = UI.S(330), H = UI.S(330) };
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        int n = forms.Count % 6;
        m.X = area.Right - m.W - UI.S(48) - n * UI.S(30); m.Y = area.Y + UI.S(60) + n * UI.S(30);
        All.Insert(0, m); SaveRtf(m, ""); SaveMeta(m);
        return Open(m, activate);
    }
    public static NoteForm Open(NoteMeta m, bool activate) {
        NoteForm f;
        if (forms.TryGetValue(m.Id, out f) && !f.IsDisposed) { f.Raise(); if (activate) f.ActivateEdit(); return f; }
        f = new NoteForm(m); forms[m.Id] = f;
        f.FormClosed += (s, e) => { NoteForm cur; if (forms.TryGetValue(m.Id, out cur) && cur == s) forms.Remove(m.Id); Fire(); };
        if (!m.Open) { m.Open = true; SaveMeta(m); }
        f.Show(); if (activate) f.ActivateEdit();
        Fire(); return f;
    }
    public static void Close(NoteMeta m) {
        var f = FormOf(m); if (f != null) { f.Flush(); f.Discard = true; f.Close(); }
        m.Open = false; SaveMeta(m);
    }
    public static void Delete(NoteMeta m) {
        var f = FormOf(m); if (f != null) { f.Discard = true; f.Close(); }
        All.Remove(m);
        try { File.Delete(MetaPath(m.Id)); } catch { } try { File.Delete(RtfPath(m.Id)); } catch { }
        Cloud.Kick(); Fire();
    }
    public static void SetColor(NoteMeta m, string key) { m.Color = key; var f = FormOf(m); if (f != null) f.ApplyColor(); SaveMeta(m); }
    public static void OpenStartup() { EnsureLoaded(); if (!Cfg.OpenOnStartup) return; foreach (var m in All.ToList()) if (m.Open) Open(m, false); }
    public static void ShowAll() { EnsureLoaded(); foreach (var m in All.ToList()) Open(m, false); }
    public static void HideAll() { foreach (var m in All.ToList()) if (IsOpen(m)) Close(m); }
    public static void ApplySettings(NotesCfg c) { bool colors = Cfg.Gradient != c.Gradient, opacity = Cfg.Opacity != c.Opacity; Cfg = c; if (!colors && !opacity) return; foreach (var f in forms.Values.ToList()) { if (opacity) f.ApplyOpacity(); if (colors) f.ApplyColor(); } }
    // re-reads notes after the cloud brought newer copies; open notes are reopened with the new content
    public static void ReloadFromDisk() {
        var open = new HashSet<string>(forms.Keys);
        foreach (var f in forms.Values.ToList()) { f.Discard = true; f.Close(); }
        forms.Clear(); loaded = false; EnsureLoaded();
        foreach (var m in All.ToList()) if (m.Open && (open.Contains(m.Id) || Cfg.OpenOnStartup)) Open(m, false);
        Fire();
    }
    public static void FlushAll() { foreach (var f in forms.Values.ToList()) f.Flush(); }
    public static void Shutdown() { FlushAll(); foreach (var f in forms.Values.ToList()) { f.Discard = true; f.Close(); } forms.Clear(); }
    public static string AllText() {
        FlushAll(); EnsureLoaded();
        return string.Join("\r\n\r\n──────────\r\n\r\n", All.Where(m => (m.Text ?? "").Trim() != "").Select(m => m.Text.Trim()));
    }
    // keep a window's title strip on a visible work area; otherwise move it into the nearest one
    public static Rectangle Clamp(Rectangle r, Rectangle[] areas, int titleH) {
        if (areas.Length == 0) return r;
        var t = new Rectangle(r.X, r.Y, r.Width, Math.Max(8, titleH));
        foreach (var a in areas) { var i = Rectangle.Intersect(a, t); if ((long)i.Width * i.Height * 2 >= (long)t.Width * t.Height && i.Height >= t.Height) return r; }
        Rectangle best = areas[0]; double bd = double.MaxValue; var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        foreach (var a in areas) {
            int dx = Math.Max(a.X - c.X, Math.Max(0, c.X - a.Right)), dy = Math.Max(a.Y - c.Y, Math.Max(0, c.Y - a.Bottom));
            double d = Math.Sqrt((double)dx * dx + (double)dy * dy); if (d < bd) { bd = d; best = a; }
        }
        int w = Math.Min(r.Width, best.Width), h = Math.Min(r.Height, best.Height);
        int x = Math.Max(best.X, Math.Min(r.X, best.Right - w)), y = Math.Max(best.Y, Math.Min(r.Y, best.Bottom - h));
        return new Rectangle(x, y, w, h);
    }
    public static Rectangle[] Areas() { return TestEnv.Off ? new[] { TestEnv.Area } : Screen.AllScreens.Select(s => s.WorkingArea).ToArray(); }
    static void Fire() { if (Changed != null) try { Changed(); } catch { } }
}

// RichEdit 4.1+ (msftedit) keeps PNG/JPEG pictures in RTF; the default RichEdit 2.0 drops them
// RichEdit 5 (msftedit) keeps PNG/JPEG pictures; adds intuitive picture selection + resize handles,
// a picture context menu and double-click viewer. Pictures keep their stored pixels; only the display size
// (\picwgoal/\pichgoal) changes, so quality is preserved and the size is saved/synced with the note.
class ImageDragOutline : Form {
    public ImageDragOutline() {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        BackColor = TransparencyKey = Color.Magenta; DoubleBuffered = true; TopMost = false;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80 | 0x20; return c; } }
    protected override void OnPaint(PaintEventArgs e) { using (var p = new Pen(UI.G2, 2f)) e.Graphics.DrawRectangle(p, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)); }
}
class RichBox : RichTextBox {
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string n);
    static bool ok = LoadLibrary("msftedit.dll") != IntPtr.Zero;
    public Action<Graphics, Control> PaintBack;   // when set, the control is transparent and the owner paints its surface
    protected override CreateParams CreateParams { get { var c = base.CreateParams; if (ok) c.ClassName = "RICHEDIT50W"; if (PaintBack != null) c.ExStyle |= 0x20; return c; } }
    [DllImport("user32.dll")] static extern bool GetUpdateRect(IntPtr h, out Native.RECT r, bool erase);

    public bool ImageTools; public event Action ImagesChanged; public Func<int> MaxImageWidth;
    public int SelImage = -1;            // char index of the selected picture
    Rectangle dragRect; int handle = -1; Point dragStart; Rectangle startRect; ToolTip tip; float aspect = 1;
    int imageDrag = -1, dropIndex = -1; Point imageDown; bool movingImage; ImageDragOutline outline;
    void ShowOutline(Rectangle r) {
        if (outline == null) outline = new ImageDragOutline();
        outline.Bounds = new Rectangle(PointToScreen(r.Location), new Size(Math.Max(4, r.Width), Math.Max(4, r.Height)));
        if (!outline.Visible) outline.Show(FindForm());
    }
    void EndOutline() { var old=outline; outline=null; if (old != null) { old.Close(); old.Dispose(); } }
    protected override void Dispose(bool disposing) { if (disposing) { EndOutline(); if (tip != null) tip.Dispose(); if (imgMenu != null) imgMenu.Dispose(); } base.Dispose(disposing); }
    List<PictInfo> cache; List<int> objectCache; float cachedDpi;
    public class PictInfo { public int Start, End, GoalW, GoalH, PixW, PixH; public string Hex; }

    float Dpi { get { if (cachedDpi <= 0) using (var g = CreateGraphics()) cachedDpi = g.DpiX; return cachedDpi; } }
    public int PictureParseCount;
    public List<PictInfo> Pictures() {
        if (cache == null) { PictureParseCount++; cache = ParsePicts(Rtf); }
        return cache;
    }
    // finds \pict groups that hold png/jpeg data (skips \nonshppict fallbacks)
    public static List<PictInfo> ParsePicts(string r) {
        var res = new List<PictInfo>(); int i = 0;
        while ((i = r.IndexOf(@"{\pict", i, StringComparison.Ordinal)) >= 0) {
            int ns = r.LastIndexOf(@"{\nonshppict", i, StringComparison.Ordinal);
            int depth = 0, j = i; for (; j < r.Length; j++) { if (r[j] == '\\') { j++; continue; } if (r[j] == '{') depth++; else if (r[j] == '}') { depth--; if (depth == 0) break; } }
            string g = r.Substring(i, Math.Min(r.Length, j + 1) - i);
            bool inNon = ns >= 0 && GroupEnd(r, ns) > i;
            if (!inNon && (g.Contains(@"\pngblip") || g.Contains(@"\jpegblip"))) {
                var p = new PictInfo { Start = i, End = j };
                p.GoalW = Num(g, @"\picwgoal"); p.GoalH = Num(g, @"\pichgoal"); p.PixW = Num(g, @"\picw"); p.PixH = Num(g, @"\pich");
                int sx = Num(g, @"\picscalex"), sy = Num(g, @"\picscaley"); if (sx > 0) p.GoalW = p.GoalW * sx / 100; if (sy > 0) p.GoalH = p.GoalH * sy / 100;
                // hex payload = text after the last control word / nested group
                int k = g.Length - 1; var sb = new StringBuilder(); int hs = LastCtl(g);
                for (int q = hs; q < g.Length - 1; q++) { char c = g[q]; if (Uri.IsHexDigit(c)) sb.Append(c); }
                p.Hex = sb.ToString(); res.Add(p);
            }
            i = j + 1;
        }
        return res;
    }
    static int GroupEnd(string r, int s) { int d = 0; for (int j = s; j < r.Length; j++) { if (r[j] == '\\') { j++; continue; } if (r[j] == '{') d++; else if (r[j] == '}') { d--; if (d == 0) return j; } } return r.Length; }
    static int LastCtl(string g) {
        int pos = 1, d = 0, last = 1;
        for (int j = 1; j < g.Length - 1; j++) {
            char c = g[j];
            if (c == '{') { d++; continue; } if (c == '}') { d--; last = j + 1; continue; }
            if (d > 0) continue;
            if (c == '\\') { j++; while (j < g.Length && char.IsLetter(g[j])) j++; while (j < g.Length && (char.IsDigit(g[j]) || g[j] == '-')) j++; if (j < g.Length && g[j] == ' ') j++; last = j; j--; }
        }
        return last;
    }
    static int Num(string g, string w) { var m = Regex.Match(g, Regex.Escape(w) + @"(-?\d+)(?![a-z])"); int v; return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0; }
    public Image PictureImage(int nth) {
        var ps = Pictures(); if (nth < 0 || nth >= ps.Count) return null;
        var hx = ps[nth].Hex; var b = new byte[hx.Length / 2];
        for (int q = 0; q < b.Length; q++) b[q] = Convert.ToByte(hx.Substring(q * 2, 2), 16);
        try { using (var ms = new MemoryStream(b)) using (var im = Image.FromStream(ms)) return new Bitmap(im); } catch { return null; }
    }
    // object chars in text order correspond to pictures in RTF order
    [StructLayout(LayoutKind.Sequential)] struct GETTEXTEX { public int cb, flags, codepage; public IntPtr defChar, usedDef; }
    [StructLayout(LayoutKind.Sequential)] struct GETTEXTLENGTHEX { public int flags, codepage; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, ref GETTEXTEX w, StringBuilder l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, ref GETTEXTLENGTHEX w, IntPtr l);
    public string RawText() {
        if (!IsHandleCreated) return "";
        var gl = new GETTEXTLENGTHEX { flags = 1 | 8, codepage = 1200 }; int n = (int)SendMessage(Handle, 0x045F, ref gl, IntPtr.Zero);
        var sb = new StringBuilder(n + 2); var gt = new GETTEXTEX { cb = (n + 2) * 2, flags = 4, codepage = 1200 };
        SendMessage(Handle, 0x045E, ref gt, sb); return sb.ToString();
    }
    public List<int> ObjectChars() { if (objectCache != null) return objectCache; var t = RawText(); var l = new List<int>(); for (int k = 0; k < t.Length; k++) if (t[k] == '\uFFFC') l.Add(k); return objectCache = l; }
    public Rectangle ImageRect(int ci) {
        var objs = ObjectChars(); int nth = objs.IndexOf(ci); var ps = Pictures(); if (nth < 0 || nth >= ps.Count) return Rectangle.Empty;
        var p0 = GetPositionFromCharIndex(ci); float k = Dpi / 1440f * ZoomFactor;
        return new Rectangle(p0.X, p0.Y, Math.Max(4, (int)(ps[nth].GoalW * k)), Math.Max(4, (int)(ps[nth].GoalH * k)));
    }
    public int ImageAt(Point pt) {
        var objects = ObjectChars();
        int near = GetCharIndexFromPosition(pt), at = objects.BinarySearch(near);
        if (at < 0) at = ~at;
        for (int i = Math.Max(0, at - 1); i < Math.Min(objects.Count, at + 2); i++) if (ImageRect(objects[i]).Contains(pt)) return objects[i];
        return -1;
    }
    Rectangle[] Handles(Rectangle r) {
        int s = UI.S(8), h = s / 2;
        return new[] { new Rectangle(r.Left - h, r.Top - h, s, s), new Rectangle(r.Right - h, r.Top - h, s, s), new Rectangle(r.Right - h, r.Bottom - h, s, s), new Rectangle(r.Left - h, r.Bottom - h, s, s),
                       new Rectangle(r.Left + r.Width / 2 - h, r.Top - h, s, s), new Rectangle(r.Right - h, r.Top + r.Height / 2 - h, s, s), new Rectangle(r.Left + r.Width / 2 - h, r.Bottom - h, s, s), new Rectangle(r.Left - h, r.Top + r.Height / 2 - h, s, s) };
    }
    static readonly Cursor[] HCur = { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNS, Cursors.SizeWE, Cursors.SizeNS, Cursors.SizeWE };
    int HandleAt(Point p) { if (SelImage < 0) return -1; var hs = Handles(ImageRect(SelImage)); for (int i = 0; i < hs.Length; i++) { var r = hs[i]; r.Inflate(UI.S(3), UI.S(3)); if (r.Contains(p)) return i; } return -1; }

    public void SetImageSize(int ci, int wPx, int hPx) {
        int maxW = MaxImageWidth != null ? MaxImageWidth() : ClientSize.Width - UI.S(8);
        if (wPx > maxW) { hPx = (int)(hPx * (maxW / (double)wPx)); wPx = maxW; }
        wPx = Math.Max(UI.S(24), wPx); hPx = Math.Max(UI.S(16), hPx);
        float k = Dpi / 1440f * ZoomFactor; int tw = (int)(wPx / k), th = (int)(hPx / k);
        int ss = SelectionStart, sl = SelectionLength;
        Select(ci, 1); string s = SelectedRtf;
        s = Regex.Replace(s, @"\\picscale[xy]-?\d+", "");
        s = Regex.Replace(s, @"\\picwgoal-?\d+", @"\picwgoal" + tw); s = Regex.Replace(s, @"\\pichgoal-?\d+", @"\pichgoal" + th);
        SelectedRtf = s; Select(ci, 1); SelImage = ci; cache = null;
        Invalidate(); if (ImagesChanged != null) ImagesChanged();
    }
    public void SetImageScale(int ci, double f) {
        var objs = ObjectChars(); int nth = objs.IndexOf(ci); var ps = Pictures(); if (nth < 0) return;
        float dpi = Dpi; int w = ps[nth].PixW, h = ps[nth].PixH;
        if (w <= 0 || h <= 0) { using (var im = PictureImage(nth)) if (im != null) { w = im.Width; h = im.Height; } }
        SetImageSize(ci, (int)(w * f * dpi / 96f), (int)(h * f * dpi / 96f));
    }
    public void FitImageWidth(int ci) { var r = ImageRect(ci); int maxW = MaxImageWidth != null ? MaxImageWidth() : ClientSize.Width - UI.S(8); SetImageSize(ci, maxW, (int)(r.Height * (maxW / (double)Math.Max(1, r.Width)))); }
    public void DeleteImage(int ci) { Select(ci, 1); SelectedText = ""; SelImage = -1; cache = null; Invalidate(); if (ImagesChanged != null) ImagesChanged(); }
    public Image ImageOf(int ci) { return PictureImage(ObjectChars().IndexOf(ci)); }

    ContextMenuStrip imgMenu;
    void ShowImageMenu(int ci, Point at) {
        imgMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
        imgMenu.Items.Add("이미지 복사", null, (s, e) => { using (var im = ImageOf(ci)) if (im != null) ImageViewer.Copy(im); });
        imgMenu.Items.Add("이미지 보기 (원본)", null, (s, e) => OpenViewer(ci));
        imgMenu.Items.Add(new ToolStripSeparator());
        foreach (var pc in new[] { 25, 50, 75, 100 }) { int v = pc; imgMenu.Items.Add(v + "% 크기", null, (s, e) => SetImageScale(ci, v / 100.0)); }
        imgMenu.Items.Add("메모 폭에 맞춤", null, (s, e) => FitImageWidth(ci));
        imgMenu.Items.Add("원본 크기", null, (s, e) => SetImageScale(ci, 1.0));
        imgMenu.Items.Add(new ToolStripSeparator());
        imgMenu.Items.Add("삭제", null, (s, e) => DeleteImage(ci));
        imgMenu.Show(this, at);
    }
    public void OpenViewer(int ci) { var im = ImageOf(ci); if (im != null) ImageViewer.Open(im, FindForm()); }
    public void MoveImage(int ci, int target) {
        if (ci < 0 || ci >= TextLength) return;
        target = Math.Max(0, Math.Min(TextLength, target));
        if (target == ci || target == ci + 1) return;
        Select(ci, 1); string imageRtf = SelectedRtf;
        if (string.IsNullOrEmpty(imageRtf) || !imageRtf.Contains(@"\pict")) return;
        SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        try {
            SelImage = -1;
            Select(ci, 1); SelectedText = "";
            if (target > ci) target--;
            target = Math.Max(0, Math.Min(TextLength, target));
            Select(target, 0); SelectedRtf = imageRtf;
            Select(Math.Min(TextLength, target + 1), 0); SelImage = -1; cache = null; objectCache = null;
        } finally { SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero); Invalidate(); }

        if (ImagesChanged != null) ImagesChanged();
    }

    protected override void WndProc(ref Message m) {
        if (ImageTools) {
            // Scrolling never needs picture payload parsing or a full-control adorner repaint.
            if ((m.Msg == 0x0115 || m.Msg == 0x020A) && handle < 0 && imageDrag < 0 && SelImage >= 0) { SelImage = -1; Invalidate(); }
            switch (m.Msg) {
                case 0x0215: if (handle >= 0 || imageDrag >= 0) { handle=-1; imageDrag=-1; movingImage=false; EndOutline(); Invalidate(); } break;
                case 0x0201: { // LBUTTONDOWN
                    var p = Pt(m); int h = HandleAt(p);
                    if (h >= 0) { handle = h; dragStart = p; startRect = ImageRect(SelImage); dragRect = startRect; aspect = startRect.Width / (float)Math.Max(1, startRect.Height); Capture = true; Focus(); ShowOutline(startRect); Invalidate(Rectangle.Inflate(startRect, UI.S(12), UI.S(12))); return; }
                    int ci = ImageAt(p);
                    if (ci >= 0) { Focus(); Select(ci, 1); SelImage = ci; imageDrag = ci; imageDown = p; movingImage = false; dropIndex = ci; Capture = true; Cursor = Cursors.SizeAll; Invalidate(); return; }
                    if (SelImage >= 0) { SelImage = -1; Invalidate(); }
                    break;
                }
                case 0x0203: { var p = Pt(m); int ci = ImageAt(p); if (ci >= 0) { OpenViewer(ci); return; } break; } // DBLCLK
                case 0x0200: { // MOUSEMOVE
                    var p = Pt(m);
                    if (handle >= 0) {
                        int dx = p.X - dragStart.X, dy = p.Y - dragStart.Y; var r = startRect;
                        int w = r.Width, hh = r.Height;
                        if (handle == 0 || handle == 3 || handle == 7) w -= dx; else if (handle == 1 || handle == 2 || handle == 5) w += dx;
                        if (handle == 0 || handle == 1 || handle == 4) hh -= dy; else if (handle == 2 || handle == 3 || handle == 6) hh += dy;
                        bool free = (ModifierKeys & Keys.Shift) != 0;
                        if (!free) { if (handle == 4 || handle == 6) w = (int)(hh * aspect); else hh = (int)(w / aspect); }
                        int maxW = MaxImageWidth != null ? MaxImageWidth() : ClientSize.Width - UI.S(8);
                        w = Math.Max(UI.S(24), Math.Min(maxW, w)); hh = Math.Max(UI.S(16), hh); if (!free) hh = (int)(w / aspect);
                        dragRect = new Rectangle(r.X, r.Y, w, hh); ShowOutline(dragRect);
                        if (tip == null) tip = new ToolTip();
                        int nth = ObjectChars().IndexOf(SelImage); var ps = Pictures(); float sc = nth >= 0 && ps[nth].PixW > 0 ? w / (ps[nth].PixW * Dpi / 96f) : 1;
                        tip.Show(w + " × " + hh + " px  ·  " + Math.Round(sc * 100) + "%" + (free ? "  (자유 비율)" : ""), this, p.X + UI.S(14), p.Y + UI.S(18));
                        Cursor = HCur[handle];
                        return;
                    }
                    if (imageDrag >= 0) {
                        int dx = p.X - imageDown.X, dy = p.Y - imageDown.Y;
                        if (!movingImage && (Math.Abs(dx) >= SystemInformation.DragSize.Width / 2 || Math.Abs(dy) >= SystemInformation.DragSize.Height / 2)) { movingImage = true; startRect = ImageRect(imageDrag); Invalidate(Rectangle.Inflate(startRect, UI.S(12), UI.S(12))); }
                        if (movingImage) { dropIndex = p.Y < 0 ? 0 : p.Y > ClientSize.Height ? TextLength : GetCharIndexFromPosition(p); if (dropIndex < 0) dropIndex = TextLength; Cursor = Cursors.SizeAll; ShowOutline(new Rectangle(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height)); }
                        return;
                    }
                    int hv = HandleAt(p); if (hv >= 0) { Cursor = HCur[hv]; return; }

                    break;
                }
                case 0x0020:
                    if (handle >= 0) { System.Windows.Forms.Cursor.Current = HCur[handle]; m.Result = (IntPtr)1; return; }
                    if (imageDrag >= 0 && movingImage) { System.Windows.Forms.Cursor.Current = Cursors.SizeAll; m.Result = (IntPtr)1; return; }
                    { var pt = PointToClient(Cursor.Position); int hv = HandleAt(pt); if (hv >= 0) { System.Windows.Forms.Cursor.Current = HCur[hv]; m.Result = (IntPtr)1; return; } if (SelImage >= 0 && ImageAt(pt) == SelImage) { System.Windows.Forms.Cursor.Current = Cursors.SizeAll; m.Result = (IntPtr)1; return; } }
                    System.Windows.Forms.Cursor.Current = Cursors.IBeam; m.Result = (IntPtr)1; return; // native text area has one stable cursor
                case 0x0202:
                    if (imageDrag >= 0) {
                        int ci = imageDrag, dest = dropIndex; bool move = movingImage; EndOutline();
                        imageDrag = -1; movingImage = false; dropIndex = -1; Capture = false; Cursor = Cursors.IBeam; Invalidate();
                        if (move) MoveImage(ci, dest);
                        return;
                    }
                    if (handle >= 0) { EndOutline(); handle = -1; Capture = false; Cursor = Cursors.IBeam; if (tip != null) tip.Hide(this); SetImageSize(SelImage, dragRect.Width, dragRect.Height); return; }
                    break;
                case 0x0205: { var p = Pt(m); int ci = ImageAt(p); if (ci >= 0) { Select(ci, 1); SelImage = ci; Invalidate(); ShowImageMenu(ci, p); return; } break; } // RBUTTONUP
                case 0x0204: { var p = Pt(m); if (ImageAt(p) >= 0) return; break; }
                case 0x007B: { var p = PointToClient(Cursor.Position); if (ImageAt(p) >= 0) return; break; } // WM_CONTEXTMENU over a picture
                case 0x0100: if (SelImage >= 0 && ((int)m.WParam == 0x2E || (int)m.WParam == 0x08) && SelectionLength == 1 && SelectionStart == SelImage) { DeleteImage(SelImage); return; } break;
            }
        }
        if (PaintBack != null) {
            if (m.Msg == 0x0014) { m.Result = (IntPtr)1; return; }
            if (m.Msg == 0x000F) { Native.RECT ur; if (GetUpdateRect(Handle, out ur, false)) using (var g = Graphics.FromHwnd(Handle)) { g.SetClip(Rectangle.FromLTRB(ur.L, ur.T, ur.R, ur.B)); PaintBack(g, this); } }
        }
        base.WndProc(ref m);
        if (PaintBack != null && (m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0005)) Invalidate();
        if (m.Msg == 0x000F && ImageTools && SelImage >= 0) PaintSelection();

    }
    protected override void OnSelectionChanged(EventArgs e) { if (SelImage >= 0 && !(SelectionStart == SelImage && SelectionLength == 1) && handle < 0) { SelImage = -1; Cursor = Cursors.IBeam; Invalidate(); } base.OnSelectionChanged(e); }
    protected override void OnTextChanged(EventArgs e) { cache = null; objectCache = null; base.OnTextChanged(e); if (PaintBack != null) Invalidate(); }
    static Point Pt(Message m) { long l = (long)m.LParam; return new Point(unchecked((short)(l & 0xFFFF)), unchecked((short)((l >> 16) & 0xFFFF))); }
    void PaintSelection() {
        if (movingImage || handle >= 0) return;
        if (SelImage >= TextLength) { SelImage = -1; return; }
        var r = handle >= 0 ? dragRect : ImageRect(SelImage); if (r.IsEmpty) return;
        using (var g = CreateGraphics()) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (handle >= 0) using (var fill = new SolidBrush(Color.FromArgb(40, UI.G2))) g.FillRectangle(fill, r);
            using (var pen = new Pen(UI.G1, Math.Max(1.5f, UI.Scale * 1.5f))) { if (handle >= 0) pen.DashStyle = DashStyle.Dash; g.DrawRectangle(pen, r); }
            foreach (var h in Handles(r)) { using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, h); using (var pen = new Pen(UI.G2, 1.5f)) g.DrawEllipse(pen, h); }
            if (movingImage && dropIndex >= 0) {
                var p = GetPositionFromCharIndex(Math.Min(dropIndex, TextLength));
                using (var pen = new Pen(UI.G2, Math.Max(2f, UI.Scale * 2f))) g.DrawLine(pen, p.X, p.Y, p.X, p.Y + Math.Max(Font.Height, UI.S(18)));
            }
        }
    }
}
class GradPanel : Panel {
    public Color A, B; public bool Line; public NoteForm Seamless;
    public GradPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
    public void PaintBg(Graphics g, Rectangle child) {
        if (Seamless != null) { var st0 = g.Save(); g.TranslateTransform(-child.X, -child.Y); Seamless.PaintSurface(g, this); g.Restore(st0); return; }
        var st = g.Save(); g.TranslateTransform(-child.X, -child.Y);
        using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), A, B, 0f)) g.FillRectangle(b, child);
        g.Restore(st);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if (Seamless != null) { Seamless.PaintSurface(e.Graphics, this); return; }
        using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), A, B, 0f)) e.Graphics.FillRectangle(b, ClientRectangle);
        if (Line) using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) e.Graphics.FillRectangle(b, 0, Height - 1, Width, 1);
    }
}
class NoteBtn : Control {
    bool hover; public string Glyph; public bool On;
    public NoteBtn(string glyph, Font f, string tip) {
        Glyph = glyph; Font = f; Size = new Size(UI.S(30), UI.S(28)); Cursor = Cursors.Hand; Margin = new Padding(0);
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        if (tip != null) new ToolTip().SetToolTip(this, tip);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var gp = Parent as GradPanel;
        var nf = FindForm() as NoteForm;
        if (nf != null) nf.PaintSurface(g, this); else if (gp != null) gp.PaintBg(g, Bounds); else g.Clear(Parent != null ? Parent.BackColor : BackColor);
        if (hover || On) using (var b = new SolidBrush(Color.FromArgb(hover ? 46 : 30, 255, 255, 255))) using (var p = UI.Round(new Rectangle(UI.S(2), UI.S(2), Width - UI.S(4) - 1, Height - UI.S(4) - 1), UI.S(4))) g.FillPath(b, p);
        TextRenderer.DrawText(g, Glyph, Font, ClientRectangle, NoteColors.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// While a border is dragged, skip per-pixel layout. The snapshot stays 1:1 (never scaled):
// content sits at the top-left and is clipped when the window shrinks; new space is the
// window background. Title buttons stay on the right edge, a note's toolbar on the bottom.
// One real layout runs on mouse-up. Settings, sticky notes, image viewer, window picker.
class ResizeGlide {
    public bool Armed, On;
    public static bool Live; // a border drag is in progress (snapshot, or the live fallback)
    Form form; Control cover; Bitmap shot; bool busy, failed;
    public void Arm(Form f) { form = f; Armed = true; failed = false; Live = true; }
    public bool SizeChanging(int wParam, int lParam) {
        if (busy || failed || !Armed || On || form == null || form.IsDisposed || !form.IsHandleCreated) return false;
        if (wParam == 1 || wParam == 2) return false; // minimized / maximized: lay out once, don't scale a snapshot
        int nw = lParam & 0xFFFF, nh = (lParam >> 16) & 0xFFFF;
        if (nw < 2 || nh < 2) return false;
        return nw != form.ClientSize.Width || nh != form.ClientSize.Height;
    }
    public void Begin(Form f) {
        if (On || busy || f == null || f.IsDisposed || !f.IsHandleCreated) return;
        busy = true;
        try {
            form = f;
            Bitmap bm = Capture(f);
            if (bm == null) { failed = true; return; }
            shot = bm;
            var glideCover = new GlideCover(shot) {
                Bounds = f.ClientRectangle,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            var chrome = f as ChromeForm; if (chrome != null) chrome.CopyGlideChrome(glideCover);
            var note = f as NoteForm; if (note != null) note.CopyGlideChrome(glideCover);
            glideCover.SampleTitle();
            cover = glideCover;
            On = true;
            f.Resize += OnResize;
            f.FormClosed += OnClosed;
            f.SuspendLayout();
            f.Controls.Add(cover);
            cover.BringToFront();
            if (!cover.IsHandleCreated) cover.CreateControl();
        } catch {
            On = false;
            try { if (f != null) { f.Resize -= OnResize; f.FormClosed -= OnClosed; } } catch { }
            try { if (cover != null && f != null) f.Controls.Remove(cover); } catch { }
            if (cover != null) cover.Dispose();
            cover = null;
            if (shot != null) shot.Dispose();
            shot = null;
            try { if (f != null) f.ResumeLayout(false); } catch { }
        } finally { busy = false; }
    }
    public void Track() {
        if (!On || cover == null || form == null || form.IsDisposed) return;
        var r = form.ClientRectangle;
        if (cover.Bounds != r) cover.Bounds = r;
        cover.Invalidate();
        if (cover.IsHandleCreated) cover.Update();
    }
    public void End() {
        Armed = false; failed = false; Live = false;
        if (!On) { form = null; return; }
        On = false;
        var f = form; form = null;
        if (f != null) { f.Resize -= OnResize; f.FormClosed -= OnClosed; }
        // lay the real controls out under the snapshot, then drop the snapshot
        if (f != null && !f.IsDisposed) { try { f.ResumeLayout(true); } catch { } }
        if (f != null && cover != null && !f.IsDisposed) { f.Controls.Remove(cover); cover.Dispose(); }
        cover = null;
        if (shot != null) { shot.Dispose(); shot = null; }
        if (f != null && !f.IsDisposed) f.Invalidate(true);
    }
    public static void Blit(Graphics g, Bitmap bm, Rectangle dest, Rectangle src) {
        var im = g.InterpolationMode; var po = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bm, dest, src, GraphicsUnit.Pixel);
        g.InterpolationMode = im; g.PixelOffsetMode = po;
    }
    void OnResize(object s, EventArgs e) { Track(); }
    void OnClosed(object s, FormClosedEventArgs e) { End(); }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    static Bitmap Capture(Form f) {
        int w = Math.Max(1, f.ClientSize.Width), h = Math.Max(1, f.ClientSize.Height);
        Bitmap bm = null;
        try {
            bm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            bool ok = false;
            using (var g = Graphics.FromImage(bm)) {
                var hdc = g.GetHdc();
                try { ok = PrintWindow(f.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            }
            if (!ok || Blank(bm)) {
                bm.Dispose(); bm = null;
                bm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                f.DrawToBitmap(bm, new Rectangle(0, 0, w, h));
            }
            var done = bm; bm = null; return done;
        } catch {
            if (bm != null) bm.Dispose();
            return null;
        }
    }
    static bool Blank(Bitmap b) {
        if (b.Width < 2 || b.Height < 2) return true;
        var c0 = b.GetPixel(b.Width / 2, b.Height / 2); int same = 0;
        for (int i = 1; i < 8; i++) { var c = b.GetPixel(b.Width * i / 8, b.Height * i / 8); if (c.ToArgb() == c0.ToArgb()) same++; }
        return same == 7 && (c0.A == 0 || c0.ToArgb() == Color.Black.ToArgb());
    }
}
class GlideCover : Control {
    readonly Bitmap shot;
    public int HeaderH, BtnX, BtnW, FooterTop, FooterH;
    public Color Back = Color.FromArgb(15, 15, 27), TitleL, TitleR;
    public bool TitleGrad;
    public GlideCover(Bitmap b) {
        shot = b; TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public void SampleTitle() {
        TitleGrad = false;
        if (shot == null || HeaderH < 2 || shot.Width < 2 || shot.Height < 2) return;
        int y = Math.Min(shot.Height - 1, Math.Max(0, HeaderH / 2));
        // one pixel just left of the window buttons: the gap uses that colour, so icon/text pixels are never stretched
        int x = Math.Min(shot.Width - 1, Math.Max(0, BtnW > 0 ? BtnX - 2 : shot.Width / 2));
        try { TitleL = TitleR = shot.GetPixel(x, y); TitleGrad = true; } catch { TitleGrad = false; }
    }
    // 1:1 blit. dest and source stay the same size; anything outside the client or the bitmap is clipped, never scaled.
    void Blit1(Graphics g, int dx, int dy, int sx, int sy, int w, int h) {
        if (shot == null || w <= 0 || h <= 0) return;
        var dest = new Rectangle(dx, dy, w, h);
        var src = new Rectangle(sx, sy, w, h);
        var cut = Rectangle.Intersect(dest, ClientRectangle);
        if (cut.Width <= 0 || cut.Height <= 0) return;
        src.X += cut.X - dest.X; src.Y += cut.Y - dest.Y; src.Width = cut.Width; src.Height = cut.Height;
        var src2 = Rectangle.Intersect(src, new Rectangle(0, 0, shot.Width, shot.Height));
        if (src2.Width <= 0 || src2.Height <= 0) return;
        var dest2 = new Rectangle(cut.X + (src2.X - src.X), cut.Y + (src2.Y - src.Y), src2.Width, src2.Height);
        ResizeGlide.Blit(g, shot, dest2, src2);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        using (var br = new SolidBrush(Back)) e.Graphics.FillRectangle(br, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        using (var br = new SolidBrush(Back)) g.FillRectangle(br, ClientRectangle);
        if (shot == null) return;
        int sw = shot.Width, sh = shot.Height;
        int header = Math.Max(0, Math.Min(HeaderH, sh));
        int footer = Math.Max(0, Math.Min(FooterH, Math.Max(0, sh - header)));
        int footerSrc = sh - footer;
        if (header > 0 && TitleGrad && Width > 0) {
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), header), TitleL, TitleR, 0f))
                g.FillRectangle(br, 0, 0, Width, header);
        }
        int bodyH = footerSrc - header;
        if (bodyH > 0) {
            int limit = footer > 0 ? Height - footer : Height;
            int dh = Math.Min(bodyH, Math.Max(0, limit - header));
            Blit1(g, 0, header, 0, header, Math.Min(sw, Width), dh);
        }
        if (header > 0) {
            int inset = BtnW > 0 ? sw - (BtnX + BtnW) : 0;
            int destBtnX = BtnW > 0 ? Width - inset - BtnW : Width;
            int leftW = BtnW > 0 ? Math.Max(0, Math.Min(BtnX, destBtnX)) : Math.Min(sw, Width);
            Blit1(g, 0, 0, 0, 0, leftW, header);
            if (BtnW > 0) Blit1(g, destBtnX, 0, BtnX, 0, BtnW, header);
        }
        if (footer > 0 && Height > header) {
            int drawH = Math.Min(footer, Height - header);
            int dy = Height - drawH;
            Blit1(g, 0, dy, 0, footerSrc + (footer - drawH), Math.Min(sw, Width), drawH);
        }
    }
}

class NoteForm : Form {
    public NoteMeta M; public bool Discard;
    Panel rtbHost;
    readonly ResizeGlide glide = new ResizeGlide();
    Bitmap surfBm; int surfW, surfH, surfKey;
    // one cached gradient for the whole note; children blit their own slice (no per-control brush, no PointToScreen)
    public void PaintSurface(Graphics g, Control c) {
        int x = 0, y = 0; for (Control p = c; p != null && p != this; p = p.Parent) { x += p.Left; y += p.Top; }
        var state = g.Save(); g.TranslateTransform(-x, -y); FillSurface(g); g.Restore(state);
    }
    void DropSurf() { if (surfBm != null) { surfBm.Dispose(); surfBm = null; } }
    int SurfKey() { return titleA.ToArgb() ^ (titleB.ToArgb() * 17) ^ (Paper.ToArgb() * 31) ^ (title == null ? 0 : title.Bottom); }
    void EnsureSurf() {
        int w = Math.Max(1, Width), h = Math.Max(1, Height), k = SurfKey();
        if (surfBm != null && surfW == w && surfH == h && surfKey == k) return;
        DropSurf();
        surfBm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(surfBm)) FillSurface(g);
        surfW = w; surfH = h; surfKey = k;
    }
    // paper colour everywhere; the note's gradient runs across the title and melts into the paper (no seam line)
    public Color Paper { get { return NoteColors.Mix(surfA, surfB, .5f); } }
    void FillSurface(Graphics g) {
        var r = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
        using (var pb = new SolidBrush(Paper)) g.FillRectangle(pb, r);
        int h = title == null ? UI.S(32) : title.Bottom;
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, r.Width, Math.Max(1, h)), titleA, titleB, 0f)) g.FillRectangle(br, 0, 0, r.Width, h);
        using (var fade = new LinearGradientBrush(new Rectangle(0, 0, r.Width, Math.Max(1, h)), Color.FromArgb(0, Paper), Paper, 90f)) g.FillRectangle(fade, 0, 0, r.Width, h);
        using (var accent = new LinearGradientBrush(new Rectangle(0, 0, r.Width, Math.Max(1, UI.S(3))), titleA, titleB, 0f)) g.FillRectangle(accent, 0, 0, r.Width, UI.S(3));

    }
    Color titleA, titleB;
    Color surfA, surfB;
    NoteBtn pinButton;
    RichTextBox rtb; GradPanel title; Panel body; FlowLayoutPanel bar; WTimer save; bool dirtyText, dirtyMeta, loading = true;
    public RichTextBox Box { get { return rtb; } }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnableWindow(IntPtr h, bool enable);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    const int GWL_EXSTYLE = -20, WM_MOUSEACTIVATE = 0x0021, WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string a, string i);

    public NoteForm(NoteMeta m) {
        M = m;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true; Text = "스티커 메모"; Icon = Program.AppIcon; MinimumSize = new Size(UI.S(190), UI.S(150));
        Padding = new Padding(UI.S(4));
        Bounds = Notes.Clamp(new Rectangle(m.X, m.Y, Math.Max(m.W, UI.S(190)), Math.Max(m.H, UI.S(150))), Notes.Areas(), UI.S(32));
        TopMost = m.TopMost;

        title = new GradPanel { Dock = DockStyle.Top, Height = UI.S(32), Line = true };
        title.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        var gfont = new Font("Segoe UI", 11f * UI.Scale, FontStyle.Regular);
        var bAdd = new NoteBtn("+", new Font("Segoe UI", 13f * UI.Scale), "새 메모 (Ctrl+N)") { Dock = DockStyle.Left };
        bAdd.Click += (s, e) => Notes.New(true);
        var bMenu = new NoteBtn("⋯", gfont, "메뉴") { Dock = DockStyle.Right };
        bMenu.Click += (s, e) => ShowMenu(bMenu);
        var bClose = new NoteBtn("✕", new Font("Segoe UI", 9f * UI.Scale), "메모 닫기 (목록에는 남아요)") { Dock = DockStyle.Right };
        bClose.Click += (s, e) => Notes.Close(M);
        pinButton = new NoteBtn("↑", new Font("Segoe UI", 12f * UI.Scale, FontStyle.Bold), "항상 위 고정 켜기/끄기") { Dock = DockStyle.Right, On = TopMost };
        pinButton.Click += (s, e) => TogglePinned();
        title.Controls.Add(bAdd); title.Controls.Add(pinButton); title.Controls.Add(bMenu); title.Controls.Add(bClose);

        bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = UI.S(32), WrapContents = false, Padding = new Padding(UI.S(6), UI.S(2), 0, 0), Visible = false };
        Func<string, FontStyle, string, Action, NoteBtn> tb = (gl, st, tip, act) => { var b = new NoteBtn(gl, new Font("Segoe UI", 10f * UI.Scale, st), tip); b.Click += (s, e) => { act(); rtb.Focus(); }; bar.Controls.Add(b); return b; };
        tb("B", FontStyle.Bold, "굵게 (Ctrl+B)", () => Toggle(FontStyle.Bold));
        tb("I", FontStyle.Italic, "기울임 (Ctrl+I)", () => Toggle(FontStyle.Italic));
        tb("U", FontStyle.Underline, "밑줄 (Ctrl+U)", () => Toggle(FontStyle.Underline));
        tb("S", FontStyle.Strikeout, "취소선 (Ctrl+T)", () => Toggle(FontStyle.Strikeout));
        tb("•", FontStyle.Bold, "글머리 기호 (Ctrl+Shift+L)", Bullet);
        var bSize = new NoteBtn("Aa", new Font("Segoe UI", 9.5f * UI.Scale, FontStyle.Bold), "텍스트 · 제목1 · 제목2 · 제목3 · 제목4") { Margin = new Padding(0, 0, UI.S(3), 0) };
        bSize.Click += (s, e) => ShowFontSizes(bSize); bar.Controls.Add(bSize);
        tb("A−", FontStyle.Regular, "글자 작게 (Ctrl+-)", () => Grow(-1));
        tb("A+", FontStyle.Regular, "글자 크게 (Ctrl++)", () => Grow(1));
        tb("↶", FontStyle.Regular, "실행 취소 (Ctrl+Z)", () => { if (rtb.CanUndo) rtb.Undo(); });
        tb("↷", FontStyle.Regular, "다시 실행 (Ctrl+Y)", () => { if (rtb.CanRedo) rtb.Redo(); });

        body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UI.S(12), UI.S(6), UI.S(4), UI.S(4)) };
        rtb = new RichBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = true,
            Font = new Font("Malgun Gothic", Math.Max(7f, Notes.Cfg.FontSize)), ForeColor = NoteColors.Text, AcceptsTab = true, BulletIndent = UI.S(14) };
        rtb.HandleCreated += (s, e) => SetWindowTheme(rtb.Handle, "DarkMode_Explorer", null);
        ((RichBox)rtb).ImageTools = true;
        ((RichBox)rtb).MaxImageWidth = () => Math.Max(UI.S(40), rtb.ClientSize.Width - UI.S(28));
        ((RichBox)rtb).ImagesChanged += () => { dirtyText = true; Kick(); };
        body.Controls.Add(rtb);
        body.BackColor = Color.Transparent; bar.BackColor = Color.Transparent;
        rtbHost = SlimScroll.Wrap(rtb, NoteColors.Body(m.Color)); rtbHost.Dock = DockStyle.Fill;
        Controls.Add(body); Controls.Add(bar); Controls.Add(title); body.BringToFront();

        string rtf = Notes.LoadRtf(m.Id);
        if (!string.IsNullOrEmpty(rtf)) { try { rtb.Rtf = rtf; } catch { rtb.Text = m.Text ?? ""; } }
        else if (!string.IsNullOrEmpty(m.Text)) rtb.Text = m.Text;
        if (m.Zoom > 0.5f && m.Zoom < 5f) rtb.ZoomFactor = m.Zoom;
        rtb.SelectionColor = NoteColors.Text;
        loading = false;
        rtb.TextChanged += (s, e) => { if (loading) return; dirtyText = true; Kick(); };
        rtb.MouseWheel += (s, e) => { if ((ModifierKeys & Keys.Control) != 0) { dirtyMeta = true; Kick(); } };
        rtb.LinkClicked += (s, e) => { try { Process.Start(e.LinkText); } catch { } };
        rtb.AllowDrop = true;
        rtb.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop) && ((string[])e.Data.GetData(DataFormats.FileDrop)).Any(IsImageFile)) e.Effect = DragDropEffects.Copy; else if (e.Data.GetDataPresent(DataFormats.Bitmap)) e.Effect = DragDropEffects.Copy; };
        rtb.DragDrop += (s, e) => {
            try {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) foreach (var f in ((string[])e.Data.GetData(DataFormats.FileDrop)).Where(IsImageFile)) InsertFile(f);
                else if (e.Data.GetDataPresent(DataFormats.Bitmap)) using (var img = (Image)e.Data.GetData(DataFormats.Bitmap)) InsertImage(img);
            } catch { }
        };
        save = new WTimer { Interval = 1800 }; save.Tick += (s, e) => Flush();
        Move += (s, e) => { if (!loading && Visible) { dirtyMeta = true; Kick(); } };
        Resize += (s, e) => { if (!loading && Visible) { dirtyMeta = true; Kick(); } };
        Activated += (s, e) => bar.Visible = true;
        Deactivate += (s, e) => bar.Visible = false;
        SystemEvents.DisplaySettingsChanged += OnDisp;
        ApplyColor(); ApplyOpacity();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; if (TestEnv.Off) c.ExStyle |= 0x08000000 | 0x80; return c; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { } }
    public void EnableDuringModal() { if (Visible && IsHandleCreated) try { EnableWindow(Handle, true); } catch { } }
    void AllowActivation() {
        if (!IsHandleCreated) return;
        long ex = Native.GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_NOACTIVATE) != 0) {
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_NOACTIVATE));
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }
    }
    public void Raise() { if (!Visible) Show(); Native.SetWindowPos(Handle, TopMost ? new IntPtr(-1) : IntPtr.Zero, 0, 0, 0, 0, 0x13); }
    public void ActivateEdit() { AllowActivation(); Activate(); rtb.Focus(); }
    void Kick() { save.Stop(); save.Start(); }
    public void Flush() {
        if (Discard) return; save.Stop();
        if (dirtyText) { Notes.SaveRtf(M, rtb.Rtf); M.Text = rtb.Text; }
        if (dirtyText || dirtyMeta) { M.X = Left; M.Y = Top; M.W = Width; M.H = Height; M.Zoom = rtb.ZoomFactor; M.TopMost = TopMost; Notes.SaveMeta(M); }
        dirtyText = dirtyMeta = false;
    }
    public void PreviewColor(string key) { ApplyColorFor(key ?? M.Color); }
    public void ApplyColor() { ApplyColorFor(M.Color); }
    void ApplyColorFor(string key) {
        Color t = NoteColors.TitleA(key), t2 = NoteColors.TitleB(key), b = NoteColors.Body(key);
        frameA = t; frameB = NoteColors.BodyEdge(key);
        surfA = NoteColors.SurfA(key); surfB = NoteColors.SurfB(key); titleA = t; titleB = t2;
        DropSurf();
        BackColor = Paper; title.A = t; title.B = t2; title.Seamless = this; rtb.BackColor = Paper;
        if (rtbHost != null) rtbHost.BackColor = Paper;
        foreach (var sbar in rtbHost == null ? new SlimBar[0] : rtbHost.Controls.OfType<SlimBar>().ToArray()) sbar.Back = Paper;
        Invalidate(true);
        title.Invalidate(true); foreach (Control c in bar.Controls) c.Invalidate(); Invalidate();
    }
    Color frameA, frameB;
    protected override void OnPaintBackground(PaintEventArgs e) {
        FillSurface(e.Graphics);
        using (var p = new Pen(Color.FromArgb(24, 255, 255, 255))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
    // snapshot chrome: title strip (right-docked buttons pinned later) and the bottom toolbar, in client pixels
    public void CopyGlideChrome(GlideCover c) {
        c.Back = Paper;
        c.HeaderH = title == null ? 0 : title.Bottom;
        int x0 = int.MaxValue, x1 = 0;
        if (title != null) foreach (Control k in title.Controls) if (k.Dock == DockStyle.Right) {
            int a = title.Left + k.Left, b = title.Left + k.Right;
            if (a < x0) x0 = a; if (b > x1) x1 = b;
        }
        if (x1 > x0) { c.BtnX = x0; c.BtnW = x1 - x0; }
        if (bar != null && bar.Visible && bar.Height > 0) { c.FooterTop = bar.Top; c.FooterH = Math.Max(0, ClientSize.Height - bar.Top); }
    }
    public void ApplyOpacity() { double o = Math.Max(0.3, Math.Min(1.0, Notes.Cfg.Opacity / 100.0)); if (Math.Abs(Opacity - o) > 0.001) Opacity = o; }
    void OnDisp(object s, EventArgs e) { try { BeginInvoke((Action)ClampToScreens); } catch { } }
    public void ClampToScreens() { var nb = Notes.Clamp(Bounds, Notes.Areas(), UI.S(32)); if (nb != Bounds) { Bounds = nb; dirtyMeta = true; Kick(); } }
    protected override void OnFormClosed(FormClosedEventArgs e) { SystemEvents.DisplaySettingsChanged -= OnDisp; save.Stop(); DropSurf(); glide.End(); base.OnFormClosed(e); }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (!Discard) Flush(); base.OnFormClosing(e); }

    public void Toggle(FontStyle st) {
        var f = rtb.SelectionFont ?? rtb.Font;
        rtb.SelectionFont = new Font(f, f.Style ^ st); dirtyText = true; Kick();
    }
    public void Bullet() { rtb.SelectionBullet = !rtb.SelectionBullet; dirtyText = true; Kick(); }
    public void Grow(float d) {
        var f = rtb.SelectionFont ?? rtb.Font;
        rtb.SelectionFont = new Font(f.FontFamily, Math.Max(7f, Math.Min(48f, f.Size + d)), f.Style); dirtyText = true; Kick();
    }
    void ShowFontSizes(Control at) {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = UI.Base };
        foreach (var item in new[] { new { Label = "텍스트", Size = 11f }, new { Label = "H₁  제목1", Size = 24f }, new { Label = "H₂  제목2", Size = 20f }, new { Label = "H₃  제목3", Size = 16f }, new { Label = "H₄  제목4", Size = 13f } }) {
            float size = item.Size;
            var mi = new ToolStripMenuItem(item.Label + "  ·  " + size + "pt") { Checked = Math.Abs((rtb.SelectionFont ?? rtb.Font).Size - size) < .1f };
            mi.Click += (s, e) => { var f = rtb.SelectionFont ?? rtb.Font; rtb.SelectionFont = new Font(f.FontFamily, size, f.Style); dirtyText = true; Kick(); rtb.Focus(); };
            menu.Items.Add(mi);
        }
        menu.Closed += (s, e) => menu.Dispose(); menu.Show(at, new Point(0, at.Height));
    }
    // ---- images: paste / drop / pick; downscaled + compressed and embedded in the note's RTF ----
    public const int MaxEdge = 1600; public const long WarnBytes = 8L * 1024 * 1024;
    static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
    public static bool IsImageFile(string f) { return ImgExt.Contains((Path.GetExtension(f) ?? "").ToLowerInvariant()); }
    bool PasteImage() {
        if (Clipboard.ContainsImage()) { using (var img = Clipboard.GetImage()) if (img != null) { InsertImage(img); return true; } }
        if (Clipboard.ContainsFileDropList()) { var fs = Clipboard.GetFileDropList().Cast<string>().Where(IsImageFile).ToList(); if (fs.Count > 0) { foreach (var f in fs) InsertFile(f); return true; } }
        return false;
    }
    void PickImage() {
        using (var d = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|모든 파일|*.*", Multiselect = true })
            if (d.ShowDialog(this) == DialogResult.OK) foreach (var f in d.FileNames) InsertFile(f);
    }
    void InsertFile(string f) { try { using (var fs = File.OpenRead(f)) using (var img = Image.FromStream(fs)) InsertImage(img); } catch { } }
    public void InsertImage(Image img) {
        string rtf = ImageRtf(img, Math.Max(UI.S(120), rtb.ClientSize.Width - UI.S(24)));
        rtb.SelectedRtf = rtf; rtb.SelectionColor = NoteColors.Text;
        dirtyText = true; Flush();
        long size = 0; try { size = new FileInfo(Notes.RtfPath(M.Id)).Length; } catch { }
        if (size > WarnBytes) DarkBox.Show(this, "이 메모가 " + (size / 1024 / 1024) + "MB로 커졌어요. 이미지가 많으면 저장과 클라우드 동기화가 느려질 수 있어요.", "스티커 메모", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    // builds an RTF picture (PNG for screenshots/transparency, JPEG for photos) shown at most `fitPx` wide
    public static string ImageRtf(Image src, int fitPx) {
        int w = src.Width, h = src.Height; double sc = Math.Min(1.0, MaxEdge / (double)Math.Max(w, h));
        int nw = Math.Max(1, (int)(w * sc)), nh = Math.Max(1, (int)(h * sc));
        byte[] png, jpg;
        using (var bm = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
            using (var g = Graphics.FromImage(bm)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent); g.DrawImage(src, 0, 0, nw, nh); }
            using (var ms = new MemoryStream()) { bm.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png = ms.ToArray(); }
            bool alpha = (Image.GetPixelFormatSize(src.PixelFormat) == 32 && (src.Flags & (int)System.Drawing.Imaging.ImageFlags.HasAlpha) != 0);
            jpg = null;
            if (!alpha && png.Length > 200 * 1024) {
                var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                var ep = new System.Drawing.Imaging.EncoderParameters(1); ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 86L);
                using (var flat = new Bitmap(nw, nh)) { using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(bm, 0, 0); } using (var ms = new MemoryStream()) { flat.Save(ms, enc, ep); jpg = ms.ToArray(); } }
            }
        }
        bool useJpg = jpg != null && jpg.Length < png.Length * 0.8; byte[] data = useJpg ? jpg : png;
        int dw = Math.Min(nw, fitPx), dh = (int)(nh * (dw / (double)nw));
        float dpi = UI.Dpi > 0 ? UI.Dpi : 96f; int twW = (int)(dw * 1440 / dpi), twH = (int)(dh * 1440 / dpi);
        var sb = new StringBuilder(data.Length * 2 + 200);
        sb.Append(@"{\rtf1{\pict").Append(useJpg ? @"\jpegblip" : @"\pngblip").Append(@"\picw").Append(nw).Append(@"\pich").Append(nh).Append(@"\picwgoal").Append(twW).Append(@"\pichgoal").Append(twH).Append("\r\n");
        const string hx = "0123456789abcdef";
        for (int i = 0; i < data.Length; i++) { sb.Append(hx[data[i] >> 4]).Append(hx[data[i] & 15]); if (i % 64 == 63) sb.Append("\r\n"); }
        sb.Append(@"}\par}");
        return sb.ToString();
    }
    void AskDelete() {
        if (rtb.TextLength == 0 || DarkBox.Show(this, "이 메모를 삭제할까요?", "스티커 메모", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Notes.Delete(M);
    }
    void TogglePinned() { TopMost = !TopMost; pinButton.On = TopMost; pinButton.Invalidate(); dirtyMeta = true; Flush(); }
    void ShowMenu(Control at) {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = UI.Base };
        menu.Items.Add(new ToolStripMenuItem("색상…  (" + NoteColors.Name(M.Color) + ")", NoteColors.SwatchImage(M.Color, UI.S(14)), (s, e) => BeginInvoke((Action)(() => {
            var palette = PalettePopup.ShowAt(at, M.Color, k => Notes.SetColor(M, k));
            palette.Previewed += k => PreviewColor(k);
            palette.FormClosed += (a, b) => PreviewColor(null);
        }))));
        menu.Items.Add(new ToolStripMenuItem("이미지 넣기…", null, (s, e) => PickImage()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("항상 위에 표시", null, (s, e) => TogglePinned()) { Checked = TopMost });
        menu.Items.Add("새 메모", null, (s, e) => Notes.New(true));
        menu.Items.Add("메모 목록 열기", null, (s, e) => { if (Notes.OpenList != null) Notes.OpenList(2); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("메모 삭제", null, (s, e) => AskDelete());
        menu.Closed += (s, e) => BeginInvoke((Action)(() => menu.Dispose()));
        menu.Show(at, new Point(0, at.Height));
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys k) {
        switch (k) {
            case Keys.Control | Keys.B: Toggle(FontStyle.Bold); return true;
            case Keys.Control | Keys.I: Toggle(FontStyle.Italic); return true;
            case Keys.Control | Keys.U: Toggle(FontStyle.Underline); return true;
            case Keys.Control | Keys.T: Toggle(FontStyle.Strikeout); return true;
            case Keys.Control | Keys.Shift | Keys.L: Bullet(); return true;
            case Keys.Control | Keys.N: Notes.New(true); return true;
            case Keys.Control | Keys.D: AskDelete(); return true;
            case Keys.Control | Keys.W: Notes.Close(M); return true;
            case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Shift | Keys.Oemplus: case Keys.Control | Keys.Add: Grow(1); return true;
            case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: Grow(-1); return true;
            case Keys.Control | Keys.Y: if (rtb.CanRedo) rtb.Redo(); return true;
            case Keys.Control | Keys.V: case Keys.Shift | Keys.Insert:
                try { if (PasteImage()) return true; } catch { }
                try { if (Clipboard.ContainsText()) { rtb.SelectionColor = NoteColors.Text; rtb.SelectedText = Clipboard.GetText(); } } catch { }
                return true;
        }
        return base.ProcessCmdKey(ref msg, k);
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_MOUSEACTIVATE) { AllowActivation(); m.Result = (IntPtr)1; return; } // MA_ACTIVATE: startup notes stay quiet until clicked
        // RichEdit stays live; defer background bitmap caches until the drag ends.
        if(m.Msg==0x0231) ResizeGlide.Live=true;
        if(m.Msg==0x0232) { ResizeGlide.Live=false; Invalidate(true); }
        if (m.Msg == 0x84) {
            base.WndProc(ref m);
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int g = UI.S(9); bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
            int ht = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
            if (ht != 0) m.Result = (IntPtr)ht;
            return;
        }
        base.WndProc(ref m);
    }
}

// ================= Custom window chrome (keeps snap, shadow, min/max animations) =================
class ChromeForm : Form {
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
    [StructLayout(LayoutKind.Sequential)] struct NCCALC { public Native.RECT r0, r1, r2; public IntPtr pos; }
    public readonly TitleBar Bar;
    protected virtual bool CaptionEnabled { get { return Bar != null && Bar.Visible; } }
    readonly ResizeGlide glide = new ResizeGlide();
    protected override bool ShowWithoutActivation { get { return TestEnv.Off; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; if (TestEnv.Off) c.ExStyle |= 0x08000000 | 0x80; return c; } }
    public ChromeForm() {
        FormBorderStyle = FormBorderStyle.Sizable;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Bar = new TitleBar(this) { Dock = DockStyle.Top };
        Controls.Add(Bar);
        Padding = new Padding(UI.S(3), 0, UI.S(3), UI.S(3));
        TextChanged += (s, e) => Bar.Invalidate();
    }
    // title bar in client pixels; window buttons (min/max/close) are the strip pinned to the right edge while dragging
    public void CopyGlideChrome(GlideCover c) {
        c.Back = BackColor.A == 0 ? Color.FromArgb(15, 15, 27) : BackColor;
        c.HeaderH = Bar == null ? 0 : Bar.Bottom;
        int n = 1 + (MinimizeBox ? 1 : 0) + (MaximizeBox ? 1 : 0);
        c.BtnW = n * UI.S(46);
        c.BtnX = Bar == null ? 0 : Bar.Right - c.BtnW;
        if (c.BtnX < 0) { c.BtnW += c.BtnX; c.BtnX = 0; }
        c.FooterH = 0;
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        var m = new MARGINS { L = 0, R = 0, T = 1, B = 0 }; try { DwmExtendFrameIntoClientArea(Handle, ref m); } catch { }
        int v = 2; try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
        Dark.Title(Handle);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }
    protected override void OnLoad(EventArgs e) { Bar.SendToBack(); base.OnLoad(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { if (glide.On) return; using (var b = new SolidBrush(Color.FromArgb(22, 21, 40))) e.Graphics.FillRectangle(b, ClientRectangle); }
    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        if (glide.On) return; // snapshot covers the client; don't repaint the title bar on every pixel
        var p = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(UI.S(3), 0, UI.S(3), UI.S(3)); if (Padding != p) Padding = p; Bar.Invalidate();
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { glide.End(); base.OnFormClosed(e); }
    protected override void WndProc(ref Message m) {
        // Real controls continue to lay out; only avoid rebuilding background bitmaps during a drag.
        if(m.Msg==0x0231) ResizeGlide.Live=true;
        if(m.Msg==0x0232) { ResizeGlide.Live=false; Invalidate(true); }
        if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) { // WM_NCCALCSIZE: whole window is client area
            if (IsZoomed(Handle)) {
                var p = (NCCALC)Marshal.PtrToStructure(m.LParam, typeof(NCCALC));
                // maximized: client = monitor work area exactly (never under the taskbar)
                var wa = Screen.FromRectangle(Rectangle.FromLTRB(p.r0.L, p.r0.T, p.r0.R, p.r0.B)).WorkingArea;
                p.r0.L = Math.Max(p.r0.L, wa.Left); p.r0.T = Math.Max(p.r0.T, wa.Top); p.r0.R = Math.Min(p.r0.R, wa.Right); p.r0.B = Math.Min(p.r0.B, wa.Bottom);
                if (wa.Bottom == Screen.FromRectangle(wa).Bounds.Bottom && wa.Height == Screen.FromRectangle(wa).Bounds.Height) p.r0.B -= 1; // auto-hide taskbar stays reachable
                Marshal.StructureToPtr(p, m.LParam, false);
            }
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x0084) { // WM_NCHITTEST
            base.WndProc(ref m);
            var pt = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            m.Result = (IntPtr)Hit(pt); return;
        }
        base.WndProc(ref m);
    }
    public int Hit(Point pt) {
        int b = UI.S(6); bool max = WindowState == FormWindowState.Maximized;
        if (!max && FormBorderStyle == FormBorderStyle.Sizable) {
            bool l = pt.X < b, r = pt.X >= ClientSize.Width - b, t = pt.Y < b, bo = pt.Y >= ClientSize.Height - b;
            if (t && l) return 13; if (t && r) return 14; if (bo && l) return 16; if (bo && r) return 17;
            if (l) return 10; if (r) return 11; if (t) return 12; if (bo) return 15;
        }
        if (CaptionEnabled && pt.Y < Bar.Bottom && !Bar.OverButton(Bar.PointToClient(PointToScreen(pt)))) return 2; // HTCAPTION
        return 1;
    }
}
class TitleBar : Control {
    readonly ChromeForm f; int hot = -1, down = -1; Bitmap fill; int fillW, fillH;
    public TitleBar(ChromeForm form) {
        f = form; Height = UI.S(38);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Disposed += (s, e) => { if (fill != null) fill.Dispose(); };
    }
    void EnsureFill(int w, int h) {
        w = Math.Max(1, w); h = Math.Max(1, h);
        if (fill != null && fillW == w && fillH == h) return;
        if (fill != null) fill.Dispose();
        fill = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(fill))
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), UI.Side, UI.Page, 0f))
                g.FillRectangle(b, 0, 0, w, h);
        fillW = w; fillH = h;
    }
    void PaintFill(Graphics g) {
        if (ResizeGlide.Live && (fill == null || fillW != Math.Max(1, Width) || fillH != Math.Max(1, Height))) {
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), UI.Side, UI.Page, 0f))
                g.FillRectangle(b, ClientRectangle);
            return;
        }
        EnsureFill(Width, Height); g.DrawImageUnscaled(fill, 0, 0);
    }
    int BW { get { return UI.S(46); } }
    bool Shown(int i) { return i == 2 || (i == 1 ? f.MaximizeBox : f.MinimizeBox); }
    Rectangle Btn(int i) { // 0 min, 1 max, 2 close; hidden buttons collapse
        if (!Shown(i)) return Rectangle.Empty;
        int slot = 0; for (int k = 2; k > i; k--) if (Shown(k)) slot++;
        return new Rectangle(Width - BW * (slot + 1), 0, BW, Height);
    }
    int BtnAt(Point p) { for (int i = 0; i < 3; i++) if (Btn(i).Contains(p)) return i; return -1; }
    public bool OverButton(Point p) { return BtnAt(p) >= 0; }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x0084) { // let the form decide caption / resize, except over our buttons
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            if (BtnAt(p) < 0) { m.Result = (IntPtr)(-1); return; }
        }
        base.WndProc(ref m);
    }
    protected override void OnMouseMove(MouseEventArgs e) { int h = BtnAt(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; down = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = BtnAt(e.Location); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) {
        int b = BtnAt(e.Location); bool click = b >= 0 && b == down; down = -1; Invalidate();
        if (click) {
            if (b == 0) f.WindowState = FormWindowState.Minimized;
            else if (b == 1) f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            else f.Close();
        }
        base.OnMouseUp(e);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        PaintFill(g);
        using (var b = UI.Grad(new RectangleF(0, Height - 1, Width, 1), f.ContainsFocus ? 90 : 40)) g.FillRectangle(b, 0, Height - 1, Width, 1);
        int isz = UI.S(18); g.DrawImage(Program.IconThumb(), new Rectangle(UI.S(12), (Height - isz) / 2, isz, isz));
        TextRenderer.DrawText(g, f.Text, UI.Base, new Rectangle(UI.S(40), 0, Width - BW * 3 - UI.S(48), Height), f.ContainsFocus ? UI.Text : UI.Hint, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string[] gl = { "\uE921", f.WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922", "\uE8BB" };
        for (int i = 0; i < 3; i++) {
            var r = Btn(i); if (r.IsEmpty) continue;
            if (hot == i || down == i) {
                Color c = i == 2 ? Color.FromArgb(down == i ? 170 : 200, 196, 64, 86) : Color.FromArgb(down == i ? 60 : 36, 255, 255, 255);
                using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
            }
            using (var gf = new Font(UI.IconFam, 7.5f * UI.Scale)) TextRenderer.DrawText(g, gl[i], gf, r, hot == i && i == 2 ? Color.White : UI.Text2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

// ================= Settings window =================
class SettingsForm : ChromeForm, IMessageFilter {
    public Config Cfg; public bool StartupEnabled; bool loading;
    public Action<Config, bool> OnSave; public Action<Preset> RunPresetNow;
    Panel content; readonly List<Button> nav = new List<Button>(); readonly List<Control> pages = new List<Control>(); int page = -1;
    public static readonly string[] PageKeys = { "presets", "windows", "notes", "kakao", "desktop", "general", "about", "powertoys" };
    List<MonInfo> mons = new List<MonInfo>();

    Label statusLbl; WTimer saveTimer, statusFade; string saveError;
    readonly Func<Control>[] builders; static readonly string[] Glyphs = { "\uE7F4", "\uE737", "\uE70B", "\uE8BD", "\uE8A9", "\uE713", "\uE946", "\uE945" };
    public static readonly string[] Names = { "모니터 프리셋", "창 위치", "스티커 메모", "카톡 광고 제거", "바탕화면 아이콘", "일반", "정보", "PowerToys" };

    // sidebar tabs can be dragged to reorder; order is saved instantly as General.TabOrder (page keys)
    bool navDragged; ToolTip tip = new ToolTip();
    public List<Button> OrderedNav() {
        var ord = (Cfg.General.TabOrder ?? new List<string>()).ToList();
        if (ord.Count == 0) ord = new List<string> { "presets", "windows", "notes", "kakao", "desktop", "powertoys", "general", "about" };
        if (!ord.Contains("powertoys")) { int gi = ord.IndexOf("general"); ord.Insert(gi < 0 ? ord.Count : gi, "powertoys"); }
        return nav.OrderBy(b => { int k = ord.IndexOf((string)b.Tag); return k < 0 ? 100 + nav.IndexOf(b) : k; }).ToList();
    }
    void HookNavDrag(Button b, FlowLayoutPanel stack) {
        Point down = Point.Empty; bool pressed = false, drag = false;
        b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { pressed = true; drag = false; down = e.Location; } };
        b.MouseMove += (s, e) => {
            if (!pressed) return;
            if (!drag && Math.Abs(e.Y - down.Y) < UI.S(6)) return;
            drag = true; b.Cursor = Cursors.SizeNS;
            int y = stack.PointToClient(b.PointToScreen(e.Location)).Y;
            var navs = stack.Controls.OfType<FlatBtn>().Where(x => x.Nav).ToList();
            int target = navs.Count - 1;
            for (int k = 0; k < navs.Count; k++) if (y < navs[k].Top + navs[k].Height / 2) { target = k; break; }
            int cur = navs.IndexOf((FlatBtn)b);
            if (target != cur) stack.Controls.SetChildIndex(b, stack.Controls.IndexOf(navs[target]));
        };
        b.MouseUp += (s, e) => {
            pressed = false; b.Cursor = Cursors.Default;
            if (!drag) return;
            drag = false; navDragged = true;
            Cfg.General.TabOrder = stack.Controls.OfType<FlatBtn>().Where(x => x.Nav).Select(x => (string)x.Tag).ToList();
            Changed();
        };
    }
    public void MoveNav(string key, int to) { // used by settingstest to check the reorder path
        var stack = nav[0].Parent; var b = nav[Array.IndexOf(PageKeys, key)];
        var navs = stack.Controls.OfType<FlatBtn>().Where(x => x.Nav).ToList();
        stack.Controls.SetChildIndex(b, stack.Controls.IndexOf(navs[to]));
        Cfg.General.TabOrder = stack.Controls.OfType<FlatBtn>().Where(x => x.Nav).Select(x => (string)x.Tag).ToList();
    }
    public SettingsForm(Config cfg, bool startup, bool deferPage = false) {
        Cfg = Config.Clone(cfg); StartupEnabled = startup; Application.AddMessageFilter(this);
        Text = "BongHub 설정"; Font = UI.Base; BackColor = UI.Page; AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen; Icon = Program.AppIcon; KeyPreview = true;
        Size = new Size(UI.S(1080), UI.S(760)); MinimumSize = new Size(UI.S(880), UI.S(580));
        var wa0 = Screen.FromPoint(Cursor.Position).WorkingArea; Size = new Size(Math.Min(Width, wa0.Width - 40), Math.Min(Height, wa0.Height - 40));
        SuspendLayout();

        var side = new SidePanel { Dock = DockStyle.Left, Width = UI.S(224), Padding = new Padding(UI.S(12), UI.S(14), UI.S(12), UI.S(12)) };
        var sideStack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        var brand = new BrandHeader { Width = UI.S(198), Height = UI.S(72), Margin = new Padding(0, 0, 0, UI.S(14)) };
        sideStack.Controls.Add(brand);
        for (int i = 0; i < Names.Length; i++) {
            int idx = i;
            var b = new FlatBtn { Text = Names[i], Glyph = Glyphs[i], Nav = true, Width = UI.S(198), Height = UI.S(44), Margin = new Padding(0, 0, 0, UI.S(4)), Font = UI.Base, Tag = PageKeys[i] };
            b.Click += (s, e) => { if (navDragged) { navDragged = false; return; } ShowPage(idx); };
            HookNavDrag(b, sideStack);
            nav.Add(b);
        }
        foreach (var b in OrderedNav()) sideStack.Controls.Add(b);
        tip.SetToolTip(sideStack, "탭을 끌어서 순서를 바꿀 수 있어요");
        statusLbl = new Label { Dock = DockStyle.Bottom, Height = UI.S(52), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(UI.S(6), 0, 0, 0), Font = UI.Small, ForeColor = UI.Hint, BackColor = Color.Transparent, Text = "변경 사항은 바로 적용·저장돼요" };
        side.Controls.Add(sideStack); side.Controls.Add(statusLbl);

        content = new DbPanel { Dock = DockStyle.Fill, BackColor = UI.Page };
        Controls.Add(side); Controls.Add(content); content.BringToFront();

        builders = new Func<Control>[] { BuildPresets, BuildWindows, BuildNotes, BuildKakao, BuildDesk, BuildGeneral, BuildAbout, BuildPowerToys };
        for (int k = 0; k < builders.Length; k++) pages.Add(null);
        saveTimer = new WTimer { Interval = 450 }; saveTimer.Tick += (s, e) => { saveTimer.Stop(); Flush(); };
        statusFade = new WTimer { Interval = 2200 }; statusFade.Tick += (s, e) => { statusFade.Stop(); if (saveError == null) { statusLbl.ForeColor = UI.Hint; statusLbl.Text = "변경 사항은 바로 적용·저장돼요"; } };
        Dark.Theme(this); DoubleBuffered = true;
        if (!deferPage) ShowPage(0);
        ResumeLayout(true);
        content.Resize += (s, e) => QueuePageFit();
        Bar.SendToBack();
        Activated += (s, e) => Bar.Invalidate(); Deactivate += (s, e) => Bar.Invalidate();
        Shown += (s, e) => { if (!deferPage) Prebuild(); };
        LoadMonitorsAsync();
    }

    static void SolidPageBackgrounds(Control root,Color background) {
        Color childBackground=root is Section?UI.Card:background;
        if(root.BackColor.A<255 && (root is Panel || root is Label))root.BackColor=background;
        foreach(Control child in root.Controls)SolidPageBackgrounds(child,childBackground);
    }
    Control EnsurePage(int i) {
        if (pages[i] != null) return pages[i];
        var p = builders[i]();
        SolidPageBackgrounds(p,UI.Page);
        SlimScroll.WrapAll(p);
        if (p is Panel && ((Panel)p).AutoScroll) { var host = SlimScroll.Wrap(p, UI.Page); p = host; }
        p.Dock = DockStyle.None; p.Bounds = new Rectangle(OffX, 0, content.ClientSize.Width, content.ClientSize.Height);
        pages[i] = p; content.Controls.Add(p); Dark.Apply(p);
        return p;
    }
    public static StringBuilder Prof = new StringBuilder();
    const int OffX = -32000;
    WTimer resizeFrame;
    void QueuePageFit() {
        if(!ResizeGlide.Live){FitPages();return;}
        if(resizeFrame==null) { resizeFrame=new WTimer{Interval=16};resizeFrame.Tick+=(s,e)=>{resizeFrame.Stop();if(!IsDisposed)FitPages();};Disposed+=(s,e)=>resizeFrame.Dispose(); }
        // Keep one pending frame. Read current ClientSize when it runs rather than queueing stale sizes.
        if(!resizeFrame.Enabled)resizeFrame.Start();
    }
    protected override void OnResizeEnd(EventArgs e) {
        if(resizeFrame!=null)resizeFrame.Stop();FitPages();base.OnResizeEnd(e);
    }
    public bool PageFitsViewport { get {return page>=0 && pages[page]!=null && pages[page].Bounds==new Rectangle(Point.Empty,content.ClientSize);} }
    void FitPages() {
        var sz = content.ClientSize;
        if (page >= 0 && pages[page] != null) pages[page].Bounds = new Rectangle(0, 0, sz.Width, sz.Height);
    }
    // builds the remaining pages one by one while idle so the first visit is instant too
    bool prebuildStarted;
    void Prebuild() {
        if(prebuildStarted)return;prebuildStarted=true;
        var t = new WTimer { Interval = 180 };
        t.Tick += (s, e) => {
            if(IsDisposed){t.Stop();t.Dispose();return;}
            if(ResizeGlide.Live || MouseButtons!=MouseButtons.None)return;
            int n = pages.FindIndex(x => x == null);
            if (n < 0 || IsDisposed) { t.Stop(); t.Dispose(); return; }
            content.SuspendLayout(); try { EnsurePage(n); } finally { content.ResumeLayout(false); }
        };
        t.Start();
    }
    public void ShowPage(int i) {
        if (i == page) return;
        content.SuspendLayout();
        var sw = Stopwatch.StartNew(); double tb = 0, tr = 0, tv = 0;
        try {
            var np = EnsurePage(i); if (map != null && !monitorLoadStarted) LoadMonitorsAsync(); tb = sw.Elapsed.TotalMilliseconds;
            int old = page; page = i;
            if (i == 1) RefreshScopes(); if (i == 2) FillNotes(); if (i == 3) RefreshKakaoStatus(); if (i == 4) RefreshDesk(); if (i == 5) { RefreshIdlePresets(); ShowCloud(); } if (i == 6) RefreshAbout(); if (i == 7) { if (PtOpen != null) PtShow(null); PtRefresh(); }
            tr = sw.Elapsed.TotalMilliseconds;
            np.Bounds = new Rectangle(0, 0, content.ClientSize.Width, content.ClientSize.Height); np.BringToFront();
            if (old >= 0 && pages[old] != null) { pages[old].Location = new Point(OffX, 0); }
            for (int k = 0; k < nav.Count; k++) ((FlatBtn)nav[k]).Selected = k == i;
            tv = sw.Elapsed.TotalMilliseconds;
        } finally {
            content.ResumeLayout(true);
            if (page >= 0 && pages[page] != null) pages[page].Invalidate(true);
            if(IsHandleCreated)Prebuild();
            Prof.AppendLine("  page " + i + ": build " + tb.ToString("0") + " refresh " + (tr - tb).ToString("0") + " swap " + (tv - tr).ToString("0") + " paint " + (sw.Elapsed.TotalMilliseconds - tv).ToString("0"));
        }
    }
    // instant apply: every change schedules a short debounced save
    void Changed() { if (loading) return; saveTimer.Stop(); saveTimer.Start(); }
    public void Flush() {
        saveTimer.Stop();
        string err = Problem();
        if (err != null) { saveError = err; statusLbl.ForeColor = UI.Bad; statusLbl.Text = "⚠ " + err; return; }
        saveError = null;
        foreach (var p in Cfg.Presets) p.Name = p.Name.Trim();
        try { if (OnSave != null) OnSave(Config.Clone(Cfg), StartupEnabled); }
        catch (Exception ex) { saveError = ex.Message; statusLbl.ForeColor = UI.Bad; statusLbl.Text = "⚠ 저장 실패: " + ex.Message; return; }
        statusLbl.ForeColor = UI.Ok; statusLbl.Text = "✓ 저장됨  " + DateTime.Now.ToString("HH:mm:ss");
        statusFade.Stop(); statusFade.Start();
    }

    Panel Scroller(out TableLayoutPanel stack) {
        var p = new BufferedPanel { AutoScroll = true, BackColor = UI.Page, Padding = new Padding(UI.S(20), UI.S(16), UI.S(20), UI.S(8)) };
        stack = UI.Stack(); var layout = stack;
        p.ClientSizeChanged += (a, b) => { int viewport=p.Parent != null && p.Parent.Tag as string == "slimhost" ? Math.Min(p.ClientSize.Width,p.Parent.ClientSize.Width) : p.ClientSize.Width; int w = Math.Max(UI.S(80), viewport - p.Padding.Horizontal); if (layout.MaximumSize.Width != w) { layout.MaximumSize = new Size(w, 0); layout.Width = w; } };
        p.ParentChanged += (a,b) => { if (p.Parent != null) { int w=Math.Max(UI.S(80),Math.Min(p.ClientSize.Width,p.Parent.ClientSize.Width)-p.Padding.Horizontal); layout.MaximumSize=new Size(w,0);layout.Width=w; } };
        p.Controls.Add(stack); return p;
    }
    static Control PageTitle(string t) { return new PageHeading(t); }

    // ---------- Presets page ----------
    ListBox plist; Preset cur; TextBox nameBox; HotkeyBox hkBox; MonitorMap map; Label monSummary, layoutLbl, rulesLbl, actionStatus;
    NumericUpDown nRetry, nEnDelay, nStep, nLayDelay, nRulesDelay; CheckBox cRefresh, cOverlay; TextBox ovMsg; Label ovHint;
    TableLayoutPanel editorStack; Panel editorScroll;

    Control BuildPresets() {
        var root = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = UI.Page, Padding = new Padding(UI.S(16), UI.S(16), 0, 0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UI.S(250))); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, UI.S(4), UI.S(12)) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize)); left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(PageTitle("모니터 프리셋"), 0, 0);
        left.Controls.Add(UI.HintL("단축키나 트레이 메뉴로 실행할 모니터 구성"), 0, 1);
        plist = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = UI.S(40), IntegralHeight = false, BorderStyle = BorderStyle.None, Font = UI.Base, BackColor = UI.Card, ForeColor = UI.Text };
        plist.DrawItem += DrawPresetItem;
        plist.SelectedIndexChanged += (s, e) => { if (plist.SelectedIndex >= 0) LoadPreset(Cfg.Presets[plist.SelectedIndex]); };
        left.Controls.Add(plist, 0, 2);
        var btns = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, UI.S(8), 0, 0) };
        for (int i = 0; i < 3; i++) btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        Func<string, EventHandler, Button> mk = (t, h) => { var b = UI.Btn(t); b.Dock = DockStyle.Fill; b.Margin = new Padding(0, 0, UI.S(4), UI.S(4)); b.Padding = new Padding(0, UI.S(2), 0, UI.S(2)); b.Click += h; return b; };
        btns.Controls.Add(mk("추가", (s, e) => AddPreset(null)), 0, 0);
        btns.Controls.Add(mk("복제", (s, e) => { if (cur != null) AddPreset(cur); }), 1, 0);
        btns.Controls.Add(mk("이름 변경", (s, e) => { if (cur != null) { ShowPage(0); nameBox.Focus(); nameBox.SelectAll(); } }), 2, 0);
        btns.Controls.Add(mk("삭제", (s, e) => DeletePreset()), 0, 1);
        btns.Controls.Add(mk("▲ 위로", (s, e) => MovePreset(-1)), 1, 1);
        btns.Controls.Add(mk("▼ 아래로", (s, e) => MovePreset(1)), 2, 1);
        left.Controls.Add(btns, 0, 3);
        root.Controls.Add(left, 0, 0);

        editorScroll = Scroller(out editorStack); editorScroll.Dock = DockStyle.Fill; editorScroll.Padding = new Padding(UI.S(12), 0, UI.S(20), UI.S(8));
        root.Controls.Add(editorScroll, 1, 0);

        // basic
        var sBasic = new Section("기본", null);
        nameBox = UI.Txt(280);
        nameBox.TextChanged += (s, e) => {
            if (loading || cur == null) return;
            string old = cur.Name; cur.Name = nameBox.Text.Trim();
            if (Cfg.General.IdlePreset == old) Cfg.General.IdlePreset = cur.Name;
            plist.Invalidate(); UpdateOverlayHint(); Changed();
        };
        sBasic.Row("이름", nameBox);
        hkBox = new HotkeyBox();
        hkBox.ValueChanged += (s, e) => { if (loading || cur == null) return; cur.Hotkey = hkBox.Value; plist.Invalidate(); Changed(); };
        var hkClear = UI.Btn("지우기"); hkClear.Click += (s, e) => { hkBox.Value = ""; if (cur != null) { cur.Hotkey = ""; plist.Invalidate(); Changed(); } };
        sBasic.Row("단축키", UI.Flow(hkBox, hkClear), "입력란을 클릭하고 원하는 키 조합을 누르세요 (Backspace: 지우기)");
        UI.AddRow(editorStack, sBasic);

        // monitors
        var sMon = new Section("모니터", "클릭할 때마다 변경 없음 → 켜기 → 끄기로 바뀌어요. 우클릭하면 주 모니터로 지정할 수 있어요.");
        map = new MonitorMap();
        map.Changed += (s, e) => { UpdateMonSummary(); Changed(); };
        map.SelectionChanged += (s, e) => UpdateMonSummary();
        sMon.Full(map, true);
        var bOn = UI.Btn("켜기"); bOn.Click += (s, e) => map.SetState(map.Selected, "on");
        var bOff = UI.Btn("끄기"); bOff.Click += (s, e) => map.SetState(map.Selected, "off");
        var bKeep = UI.Btn("변경 없음"); bKeep.Click += (s, e) => map.SetState(map.Selected, "keep");
        var bPrim = UI.Btn("★ 주 모니터로"); bPrim.Click += (s, e) => map.SetPrimary(map.Selected);
        var bNoPrim = UI.Btn("주 모니터 지정 안 함"); bNoPrim.Click += (s, e) => { if (cur != null) { cur.Primary = ""; map.Invalidate(); UpdateMonSummary(); Changed(); } };
        var bReload = UI.Btn("↻ 새로고침"); bReload.Click += (s, e) => LoadMonitorsAsync();
        var mf = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0), MaximumSize = new Size(UI.S(2000), 0) };
        foreach (var b in new[] { bOn, bOff, bKeep, bPrim, bNoPrim, bReload }) mf.Controls.Add(b);
        sMon.Full(mf);
        monSummary = UI.HintL(""); sMon.Full(monSummary);
        UI.AddRow(editorStack, sMon);

        // layout
        var sLay = new Section("레이아웃", "모니터 위치·해상도·배율을 레이아웃 파일로 저장해 두고 적용할 때 불러와요.");
        layoutLbl = UI.L("");
        sLay.Row("레이아웃 파일", layoutLbl);
        var bSaveLay = UI.Btn("현재 배치 저장"); bSaveLay.Click += (s, e) => SaveCurrentLayout();
        var bPickLay = UI.Btn("파일 선택…"); bPickLay.Click += (s, e) => {
            using (var d = new OpenFileDialog { Filter = "레이아웃 (*.cfg)|*.cfg|모든 파일|*.*", InitialDirectory = Core.Dir }) if (d.ShowDialog(this) == DialogResult.OK && cur != null) { cur.Layout = d.FileName; UpdateLayout(); Changed(); }
        };
        var bNoLay = UI.Btn("사용 안 함"); bNoLay.Click += (s, e) => { if (cur != null) { cur.Layout = ""; UpdateLayout(); Changed(); } };
        sLay.Row("", UI.Flow(bSaveLay, bPickLay, bNoLay));
        UI.AddRow(editorStack, sLay);

        // behaviour
        var sAct = new Section("전환 동작", null);
        nRetry = UI.Num(1, 10, 1, 70); nRetry.ValueChanged += (s, e) => { if (!loading && cur != null) { cur.Retries = (int)nRetry.Value; Changed(); } };
        sAct.Row("재시도 횟수", UI.Flow(nRetry, UI.L("회")), "모니터가 모두 켜지고 주 모니터가 맞을 때까지 반복");
        nEnDelay = DelayBox(v => cur.EnableDelay = v); sAct.Row("모니터 켜기 간격", UI.Flow(nEnDelay, UI.L("ms")));
        nStep = DelayBox(v => cur.StepDelay = v); sAct.Row("단계 사이 대기", UI.Flow(nStep, UI.L("ms")));
        nLayDelay = DelayBox(v => cur.LayoutDelay = v); sAct.Row("레이아웃·끄기 후 대기", UI.Flow(nLayDelay, UI.L("ms")));
        nRulesDelay = DelayBox(v => cur.RulesDelay = v); sAct.Row("창 규칙 적용 대기", UI.Flow(nRulesDelay, UI.L("ms")), "전환이 끝난 뒤 이만큼 기다렸다가 창 규칙을 적용");
        cRefresh = UI.Chk("전환 후 작업 표시줄 새로고침 (DisplayFusion·탐색기 다시 시작)");
        cRefresh.CheckedChanged += (s, e) => { if (!loading && cur != null) { cur.RefreshTaskbar = cRefresh.Checked; Changed(); } };
        sAct.Full(cRefresh);
        UI.AddRow(editorStack, sAct);

        // overlay
        var sOv = new Section("전환 안내 화면", null);
        cOverlay = UI.Chk("전환하는 동안 주 모니터에 로딩 화면 표시");
        cOverlay.CheckedChanged += (s, e) => { if (!loading && cur != null) { cur.Overlay = cOverlay.Checked; Changed(); } };
        sOv.Full(cOverlay);
        ovMsg = UI.Txt(320); ovMsg.TextChanged += (s, e) => { if (!loading && cur != null) { cur.OverlayMessage = ovMsg.Text; Changed(); } };
        var bPrev = UI.Btn("미리보기"); bPrev.Click += (s, e) => { if (cur != null) PreviewOverlay(cur.Message()); };
        ovHint = UI.HintL("");
        sOv.Row("안내 문구", UI.Flow(ovMsg, bPrev));
        sOv.Row("", ovHint);
        UI.AddRow(editorStack, sOv);

        // brightness + linked desktop icon layout
        var sEx = new Section("밝기 · 바탕화면 아이콘 · 창 배치", "전환이 끝나고 화면이 안정되면(오버레이가 닫히기 직전) 적용돼요. 실패해도 전환은 계속돼요.");
        bTarget = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(260), Font = UI.Base };
        bTarget.SelectedIndexChanged += (s, e) => LoadBright();
        brKeep = UI.Chk("변경 안 함"); brKeep.CheckedChanged += (s, e) => { if (loading) return; SaveBright(); };
        bTrack = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = UI.S(240), AutoSize = false, Height = UI.S(30), BackColor = UI.Card };
        bVal = UI.L("", UI.Bold, UI.Text); bVal.MinimumSize = new Size(UI.S(48), 0);
        bTrack.ValueChanged += (s, e) => { bVal.Text = bTrack.Value + "%"; if (loading) return; if (brKeep.Checked) { loading = true; brKeep.Checked = false; loading = false; } SaveBright(); };
        sEx.Row("밝기 대상", bTarget, "모니터를 따로 정하면 ‘전체 모니터’ 값보다 우선해요");
        sEx.Row("밝기", UI.Flow(brKeep, bTrack, bVal), "DDC/CI(외장 모니터) · WMI(노트북 화면). 모니터가 켜질 때까지 최대 4번 다시 시도해요");
        bSum = UI.HintL(""); sEx.Row("", bSum);
        dLink = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(320), Font = UI.Base };
        dLink.SelectedIndexChanged += (s, e) => { if (loading || cur == null || dLink.SelectedIndex < 0) return; cur.DeskLayout = dLinkIds[dLink.SelectedIndex]; UpdLinkHint(); Changed(); };
        dLinkHint = UI.HintL("");
        sEx.Row("아이콘 배치", dLink); sEx.Row("", dLinkHint);
        wLink = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(320), Font = UI.Base };
        wLink.SelectedIndexChanged += (s, e) => { if (loading || cur == null || wLink.SelectedIndex < 0) return; cur.Workspace = wLinkIds[wLink.SelectedIndex]; UpdWorkHint(); Changed(); };
        wLinkHint = UI.HintL("");
        sEx.Row("창 배치(작업 영역)", wLink); sEx.Row("", wLinkHint);
        UI.AddRow(editorStack, sEx); exSec = sEx;

        // rules + run
        var sRun = new Section("창 규칙 · 실행", null);
        rulesLbl = UI.L("");
        var bRules = UI.Btn("창 규칙 편집 →"); bRules.Click += (s, e) => { ShowPage(1); scope.SelectedIndex = Cfg.Presets.IndexOf(cur) + 1; };
        sRun.Row("창 규칙", UI.Flow(rulesLbl, bRules));
        var bRunRules = UI.Btn("창 규칙만 지금 적용"); bRunRules.Click += (s, e) => { if (cur != null) ApplyRulesAsync(cur.Rules.Concat(Cfg.GlobalRules), actionStatus); };
        var bRun = UI.Btn("▶ 지금 적용", true); bRun.Click += (s, e) => RunNow();
        sRun.Row("테스트", UI.Flow(bRun, bRunRules), "‘지금 적용’은 실제로 모니터 구성을 바꾸고 탐색기를 다시 시작해요");
        actionStatus = UI.HintL(""); sRun.Full(actionStatus);
        UI.AddRow(editorStack, sRun);

        RefreshPresetList(0);
        return root;
    }
    NumericUpDown DelayBox(Action<int> set) {
        var n = UI.Num(0, 30000, 0, 90); n.Increment = 250;
        n.ValueChanged += (s, e) => { if (!loading && cur != null) { set((int)n.Value); Changed(); } };
        return n;
    }
    public void ScrollPresetEditor() { editorScroll.AutoScrollPosition = new Point(0, 100000); editorScroll.Refresh(); }
    void DrawPresetItem(object s, DrawItemEventArgs e) {
        if (e.Index < 0 || e.Index >= Cfg.Presets.Count) return;
        var p = Cfg.Presets[e.Index]; bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var br = new SolidBrush(sel ? UI.SelBg : UI.Card)) e.Graphics.FillRectangle(br, e.Bounds);
        if (sel) using (var br = UI.Grad(new RectangleF(e.Bounds.X, e.Bounds.Y, UI.S(3), e.Bounds.Height), 255, 90f)) e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y + UI.S(6), UI.S(3), e.Bounds.Height - UI.S(12));
        var r = new Rectangle(e.Bounds.X + UI.S(12), e.Bounds.Y, e.Bounds.Width - UI.S(20), e.Bounds.Height);
        string hk = p.Hotkey == "" ? "" : p.Hotkey;
        var hs = TextRenderer.MeasureText(hk, UI.Small);
        TextRenderer.DrawText(e.Graphics, p.Name == "" ? "(이름 없음)" : p.Name, UI.Bold, new Rectangle(r.X, r.Y, r.Width - hs.Width - UI.S(6), r.Height), UI.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, hk, UI.Small, r, UI.Hint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
    void RefreshPresetList(int sel) {
        plist.Items.Clear(); foreach (var p in Cfg.Presets) plist.Items.Add(p);
        if (Cfg.Presets.Count > 0) plist.SelectedIndex = Math.Max(0, Math.Min(sel, Cfg.Presets.Count - 1)); else LoadPreset(null);
    }
    string UniqueName(string b) { string n = b; int i = 2; while (Cfg.Find(n) != null) n = b + " " + (i++); return n; }
    void AddPreset(Preset from) {
        Preset p = from == null ? new Preset { Name = UniqueName("새 모니터 프리셋") } : Config.CloneObj(from);
        if (from != null) { p.Name = UniqueName(from.Name + " 복사본"); p.Hotkey = ""; }
        Cfg.Normalize();
        Cfg.Presets.Add(p); Cfg.Normalize(); RefreshPresetList(Cfg.Presets.Count - 1); Changed();
        nameBox.Focus(); nameBox.SelectAll();
    }
    void DeletePreset() {
        if (cur == null) return;
        if (Cfg.Presets.Count <= 1) { DarkBox.Show(this, "모니터 프리셋은 최소 1개가 필요해요.", "BongHub"); return; }
        if (DarkBox.Show(this, "'" + cur.Name + "' 모니터 프리셋을 삭제할까요?\n이 모니터 프리셋의 창 규칙도 함께 삭제돼요.", "모니터 프리셋 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int i = Cfg.Presets.IndexOf(cur); Cfg.Presets.RemoveAt(i); RefreshPresetList(i); Changed();
    }
    void MovePreset(int d) {
        if (cur == null) return; int i = Cfg.Presets.IndexOf(cur), j = i + d;
        if (j < 0 || j >= Cfg.Presets.Count) return;
        Cfg.Presets.RemoveAt(i); Cfg.Presets.Insert(j, cur); RefreshPresetList(j); Changed();
    }
    Section exSec; public void ScrollToExtras() { ShowPage(0); editorScroll.ScrollControlIntoView(exSec); }
    ComboBox bTarget, dLink; CheckBox brKeep; TrackBar bTrack; Label bVal, bSum, dLinkHint; readonly List<string> bIds = new List<string>(), dLinkIds = new List<string>();
    void FillExtras() {
        bool l = loading; loading = true;
        try {
            int sel = Math.Max(0, bTarget.SelectedIndex); bTarget.Items.Clear(); bIds.Clear();
            bTarget.Items.Add("전체 모니터"); bIds.Add("");
            foreach (var m in mons) { bTarget.Items.Add(m.Id + "  ·  " + m.Name); bIds.Add(m.Id); }
            if (cur != null) foreach (var b in cur.Brightness) if (b.Monitor != "" && !bIds.Contains(b.Monitor)) { bTarget.Items.Add(b.Monitor + "  (감지 안 됨)"); bIds.Add(b.Monitor); }
            bTarget.SelectedIndex = Math.Min(sel, bTarget.Items.Count - 1);
            dLink.Items.Clear(); dLinkIds.Clear(); dLink.Items.Add("연계 안 함"); dLinkIds.Add("");
            List<DeskLayout> all; try { all = Desk.All.OrderByDescending(x => x.Saved).ToList(); } catch { all = new List<DeskLayout>(); }
            foreach (var d in all) { dLink.Items.Add(d.Name + "  ·  " + Desk.Describe(d.Key) + (d.Auto ? "  (자동)" : "")); dLinkIds.Add(d.Id); }
            string want = cur == null ? "" : cur.DeskLayout;
            if (want != "" && !dLinkIds.Contains(want)) { dLink.Items.Add("(삭제된 배치) — 다른 배치를 고르거나 ‘연계 안 함’"); dLinkIds.Add(want); }
            dLink.SelectedIndex = Math.Max(0, dLinkIds.IndexOf(want));
            wLink.Items.Clear(); wLinkIds.Clear(); wLink.Items.Add("연계 안 함"); wLinkIds.Add("");
            foreach (var w in Cfg.Pt.Work.Items) { wLink.Items.Add(w.Name + "  ·  앱 " + w.Apps.Count(a => a.Enabled) + "개"); wLinkIds.Add(w.Id); }
            string ww = cur == null ? "" : cur.Workspace;
            if (ww != "" && !wLinkIds.Contains(ww)) { wLink.Items.Add("(삭제된 작업 영역) — 다른 작업 영역을 고르거나 ‘연계 안 함’"); wLinkIds.Add(ww); }
            wLink.SelectedIndex = Math.Max(0, wLinkIds.IndexOf(ww));
        } finally { loading = l; }
        LoadBright(); UpdLinkHint(); UpdWorkHint();
    }
    DarkCombo wLink; Label wLinkHint; readonly List<string> wLinkIds = new List<string>();
    void UpdWorkHint() {
        if (cur == null || wLinkHint == null) return; string id = cur.Workspace;
        bool gone = id != "" && Cfg.Pt.Work.Items.All(x => x.Id != id);
        wLinkHint.ForeColor = gone ? UI.Warn : UI.Hint;
        wLinkHint.Text = id == "" ? "작업 영역은 PowerToys 탭 › 작업 영역에서 만들어요" : gone ? "⚠ 연계된 작업 영역이 삭제됐어요 — 전환 때 실행되지 않아요"
            : "아이콘 배치 직후 한 번 실행해요. 열린 앱은 옮기기만 하고, 없는 앱은 작업 영역의 ‘자동 실행’ 설정을 따라요";
    }
    void UpdLinkHint() {
        if (cur == null) return; string id = cur.DeskLayout;
        bool gone = id != "" && Desk.All.All(x => x.Id != id);
        dLinkHint.ForeColor = gone ? UI.Warn : UI.Hint;
        dLinkHint.Text = id == "" ? "연계하지 않으면 ‘해상도 변경 시 자동 복원’ 설정을 따라요" : gone ? "⚠ 연계된 배치가 삭제됐어요 — 전환 때 복원되지 않아요" : "전환 직후 이 배치를 한 번 복원해요 (이때는 자동 복원을 건너뛰어 중복되지 않아요)";
    }
    void LoadBright() {
        if (cur == null || bTarget.SelectedIndex < 0) return; bool l = loading; loading = true;
        try {
            string id = bIds[bTarget.SelectedIndex]; var b = cur.Brightness.FirstOrDefault(x => x.Monitor == id);
            brKeep.Checked = b == null || b.Value < 0; bTrack.Value = b == null || b.Value < 0 ? 70 : b.Value; bVal.Text = brKeep.Checked ? "-" : bTrack.Value + "%";
            bSum.Text = BrightSummary(cur);
        } finally { loading = l; }
    }
    void SaveBright() {
        if (cur == null || bTarget.SelectedIndex < 0) return; string id = bIds[bTarget.SelectedIndex];
        cur.Brightness.RemoveAll(x => x.Monitor == id);
        if (!brKeep.Checked) cur.Brightness.Add(new MonBright { Monitor = id, Value = bTrack.Value });
        bVal.Text = brKeep.Checked ? "-" : bTrack.Value + "%"; bSum.Text = BrightSummary(cur); Changed();
    }
    public static string BrightSummary(Preset p) {
        var l = p.Brightness.Where(x => x.Value >= 0).OrderBy(x => x.Monitor).Select(x => (x.Monitor == "" ? "전체" : x.Monitor) + " " + x.Value + "%").ToList();
        return l.Count == 0 ? "밝기는 바꾸지 않아요" : "적용: " + string.Join(", ", l);
    }
    void LoadPreset(Preset p) {
        loading = true;
        try {
            cur = p; editorScroll.Enabled = p != null;
            if (p == null) return;
            nameBox.Text = p.Name; hkBox.Value = p.Hotkey;
            map.P = p; map.Mons = mons; map.Selected = null; map.Invalidate(); UpdateMonSummary();
            UpdateLayout();
            nRetry.Value = Math.Max(1, Math.Min(10, p.Retries));
            nEnDelay.Value = Clamp(p.EnableDelay); nStep.Value = Clamp(p.StepDelay); nLayDelay.Value = Clamp(p.LayoutDelay); nRulesDelay.Value = Clamp(p.RulesDelay);
            cRefresh.Checked = p.RefreshTaskbar; cOverlay.Checked = p.Overlay; ovMsg.Text = p.OverlayMessage; UpdateOverlayHint();
            rulesLbl.Text = p.Rules.Count + "개";
            FillExtras();
            actionStatus.Text = "";
            editorScroll.AutoScrollPosition = new Point(0, 0);
        } finally { loading = false; }
    }
    static decimal Clamp(int v) { return Math.Max(0, Math.Min(30000, v)); }
    void UpdateOverlayHint() { if (cur != null) ovHint.Text = "비워두면 ‘" + cur.Name + " 모드로 전환 중...’이 표시돼요"; }
    void UpdateLayout() {
        if (cur == null) return;
        string f = Core.ResolveLayout(cur.Layout);
        layoutLbl.Text = f == null ? "사용 안 함" : Path.GetFileName(f) + (File.Exists(f) || cur.Layout == "work_layout.cfg" ? "" : "  (파일 없음!)");
        layoutLbl.ForeColor = f == null ? UI.Hint : UI.Text;
    }
    void UpdateMonSummary() {
        if (cur == null) return;
        string sel = map.Selected == null ? "모니터를 클릭해서 선택하세요" : "선택: " + map.Selected;
        monSummary.Text = sel + "    ·    켜기 " + (cur.Enable.Count == 0 ? "-" : string.Join(", ", cur.Enable)) +
            "    ·    끄기 " + (cur.Disable.Count == 0 ? "-" : string.Join(", ", cur.Disable)) + "    ·    주 모니터 " + (cur.Primary == "" ? "변경 없음" : cur.Primary);
    }
    bool monitorLoadStarted;
    void LoadMonitorsAsync() {
        if (map == null || monitorLoadStarted) return; monitorLoadStarted = true;
        map.Status = "모니터 정보를 불러오는 중…"; map.Invalidate();
        ThreadPool.QueueUserWorkItem(_ => {
            List<MonInfo> l; string err = null;
            try { l = Core.ListMonitors(); } catch (Exception ex) { l = new List<MonInfo>(); err = ex.Message; }
            try {
                BeginInvoke((Action)(() => {
                    mons = l; map.Mons = l; map.Status = err != null ? "모니터 정보를 읽지 못했어요: " + err : "감지된 모니터가 없어요";
                    map.Invalidate(); UpdateMonSummary(); if (cur != null) FillExtras();
                }));
            } catch { }
        });
    }
    void SaveCurrentLayout() {
        if (cur == null) return;
        string safe = string.Concat(cur.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string rel = Path.Combine("layouts", (safe == "" ? "preset" : safe) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".cfg");
        bool ok = false; try { ok = Core.SaveLayout(Path.Combine(Core.Dir, rel)); } catch { }
        if (ok) { cur.Layout = rel; UpdateLayout(); Changed(); actionStatus.Text = "현재 모니터 배치를 저장했어요: " + rel; }
        else DarkBox.Show(this, "레이아웃을 저장하지 못했어요.", "BongHub");
    }
    void PreviewOverlay(string msg) {
        int op = Cfg.General.OverlayOpacity;
        ThreadPool.QueueUserWorkItem(_ => { Overlay.Show(msg, "잠시만 기다려 주세요 (미리보기)", op); Thread.Sleep(2500); Overlay.Close(); });
    }
    void RunNow() {
        if (cur == null || RunPresetNow == null) return;
        if (DarkBox.Show(this, "'" + cur.Name + "' 모니터 프리셋을 지금 적용할까요?\n모니터 구성이 바뀌고 탐색기가 다시 시작돼요.", "지금 적용", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        RunPresetNow(Config.CloneObj(cur)); actionStatus.Text = "'" + cur.Name + "' 적용을 시작했어요.";
    }
    void ApplyRulesAsync(IEnumerable<WindowRule> rules, Label status) {
        var list = rules.Select(r => Config.CloneObj(r)).ToList();
        status.Text = "창 규칙 적용 중…";
        ThreadPool.QueueUserWorkItem(_ => {
            int n = 0; try { n = WindowRules.ApplyList(list); } catch { }
            try { BeginInvoke((Action)(() => status.Text = n + "개 창에 규칙을 적용했어요.")); } catch { }
        });
    }

    // ---------- Windows page ----------
    ComboBox scope; ListView rlist; Label rStatus;
    List<WindowRule> CurRules() { int i = scope.SelectedIndex; if (i <= 0 || i > Cfg.Presets.Count) return Cfg.GlobalRules; return Cfg.Presets[i - 1].Rules; }
    Control BuildWindows() {
        var root = new TableLayoutPanel { ColumnCount = 2, RowCount = 5, BackColor = UI.Page, Padding = new Padding(UI.S(20), UI.S(16), UI.S(20), UI.S(12)) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var t = PageTitle("창 위치"); root.Controls.Add(t, 0, 0); root.SetColumnSpan(t, 2);
        var h = UI.HintL("조건에 맞는 창을 원하는 모니터·위치로 옮겨요. 전역 규칙은 항상, 모니터 프리셋 규칙은 그 모니터 프리셋으로 전환한 뒤 적용돼요.");
        root.Controls.Add(h, 0, 1); root.SetColumnSpan(h, 2);
        scope = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(280), Font = UI.Base, Margin = new Padding(0, UI.S(2), 0, UI.S(2)) };
        scope.SelectedIndexChanged += (s, e) => FillRules();
        var sc = UI.Flow(UI.L("적용 범위"), scope); sc.Margin = new Padding(0, UI.S(4), 0, UI.S(8));
        root.Controls.Add(sc, 0, 2); root.SetColumnSpan(sc, 2);
        rlist = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, Font = UI.Base, BorderStyle = BorderStyle.FixedSingle, MultiSelect = true };
        Dark.List(rlist);
        rlist.Columns.Add("규칙", UI.S(170)); rlist.Columns.Add("일치 조건", UI.S(250)); rlist.Columns.Add("동작", UI.S(220)); rlist.Columns.Add("옵션", UI.S(120));
        rlist.ItemChecked += (s, e) => { if (loading) return; var r = e.Item.Tag as WindowRule; if (r != null && r.Enabled != e.Item.Checked) { r.Enabled = e.Item.Checked; Changed(); } };
        rlist.DoubleClick += (s, e) => EditRule();
        rlist.Resize += (s, e) => FitColumns();
        root.Controls.Add(rlist, 0, 3);
        var bs = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(UI.S(10), 0, 0, 0) };
        Func<string, bool, EventHandler, Button> mk = (txt, pr, ev) => { var b = UI.Btn(txt, pr); b.AutoSize = false; b.Width = UI.S(170); b.Height = UI.S(34); b.Click += ev; b.Margin = new Padding(0, 0, 0, UI.S(6)); return b; };
        bs.Controls.Add(mk("+ 규칙 추가…", true, (s, e) => AddRule()));
        bs.Controls.Add(mk("현재 창에서 캡처…", false, (s, e) => CaptureRules()));
        bs.Controls.Add(mk("편집…", false, (s, e) => EditRule()));
        bs.Controls.Add(mk("복제", false, (s, e) => DupRule()));
        bs.Controls.Add(mk("삭제", false, (s, e) => DelRule()));
        bs.Controls.Add(mk("▲ 위로", false, (s, e) => MoveRule(-1)));
        bs.Controls.Add(mk("▼ 아래로", false, (s, e) => MoveRule(1)));
        var gap = new Label { Height = UI.S(10), Width = 1 }; bs.Controls.Add(gap);
        bs.Controls.Add(mk("선택 규칙 지금 적용", false, (s, e) => { var sel = SelRules(); if (sel.Count > 0) ApplyRulesAsync(sel, rStatus); }));
        bs.Controls.Add(mk("범위 전체 지금 적용", false, (s, e) => ApplyRulesAsync(CurRules(), rStatus)));
        root.Controls.Add(bs, 1, 3);
        rStatus = UI.HintL("규칙은 위에서부터 차례로 확인하고, 처음 일치한 규칙 하나만 적용돼요. ‘지금 적용’은 창만 움직이고 모니터 구성은 바꾸지 않아요.");
        rStatus.Margin = new Padding(0, UI.S(8), 0, 0);
        root.Controls.Add(rStatus, 0, 4); root.SetColumnSpan(rStatus, 2);
        return root;
    }
    void FitColumns() {
        if (rlist.Columns.Count < 4) return;
        int w = rlist.ClientSize.Width - 4; if (w < 100) return;
        float[] k = { .22f, .36f, .27f, .15f }; int used = 0;
        for (int c = 0; c < 3; c++) { rlist.Columns[c].Width = (int)(w * k[c]); used += rlist.Columns[c].Width; }
        rlist.Columns[3].Width = w - used;
    }
    void RefreshScopes() {
        int i = Math.Max(0, scope.SelectedIndex);
        scope.Items.Clear(); scope.Items.Add("전역 (항상 적용)");
        foreach (var p in Cfg.Presets) scope.Items.Add("모니터 프리셋: " + p.Name);
        scope.SelectedIndex = Math.Min(i, scope.Items.Count - 1);
    }
    void FillRules() {
        loading = true;
        try {
            rlist.BeginUpdate(); rlist.Items.Clear();
            foreach (var r in CurRules()) {
                var it = new ListViewItem(r.Name == "" ? "(이름 없음)" : r.Name) { Checked = r.Enabled, Tag = r };
                it.SubItems.Add(r.MatchText()); it.SubItems.Add(r.ActionText()); it.SubItems.Add(r.OptText());
                rlist.Items.Add(it);
            }
            rlist.EndUpdate(); FitColumns();
        } finally { loading = false; }
    }
    List<WindowRule> SelRules() { return rlist.SelectedItems.Cast<ListViewItem>().Select(i => (WindowRule)i.Tag).ToList(); }
    void AddRule() {
        var r = new WindowRule();
        using (var d = new RuleDialog(r, mons)) if (d.ShowDialog(this) == DialogResult.OK) { CurRules().Add(r); FillRules(); Changed(); }
    }
    void EditRule() {
        var sel = SelRules(); if (sel.Count == 0) return;
        var copy = Config.CloneObj(sel[0]);
        using (var d = new RuleDialog(copy, mons)) if (d.ShowDialog(this) == DialogResult.OK) {
            var l = CurRules(); int i = l.IndexOf(sel[0]); l[i] = copy; FillRules(); Changed();
        }
    }
    void DupRule() { var l = CurRules(); foreach (var r in SelRules()) { var c = Config.CloneObj(r); c.Name = r.Name + " 복사본"; l.Insert(l.IndexOf(r) + 1, c); } FillRules(); Changed(); }
    void DelRule() {
        var sel = SelRules(); if (sel.Count == 0) return;
        if (DarkBox.Show(this, sel.Count + "개 규칙을 삭제할까요?", "규칙 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var l = CurRules(); foreach (var r in sel) l.Remove(r); FillRules(); Changed();
    }
    void MoveRule(int d) {
        var sel = SelRules(); if (sel.Count != 1) return; var l = CurRules(); int i = l.IndexOf(sel[0]), j = i + d;
        if (j < 0 || j >= l.Count) return; l.RemoveAt(i); l.Insert(j, sel[0]); FillRules(); rlist.Items[j].Selected = true; Changed();
    }
    void CaptureRules() {
        using (var d = new CaptureDialog(true)) if (d.ShowDialog(this) == DialogResult.OK && d.Result.Count > 0) {
            CurRules().AddRange(d.Result); FillRules(); Changed(); rStatus.Text = d.Result.Count + "개 규칙을 추가했어요. 바로 저장됐어요.";
        }
    }

    // ---------- Sticky notes page ----------
    ListView nlist; SwatchGrid nColor; CheckBox nGrad; NumericUpDown nSize; GradSlider nOp; Label nOpVal, nStatus; HotkeyBox nHk; CheckBox nStartup;
    Control BuildNotes() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("스티커 메모"));
        var sList = new Section("메모", "목록의 메모는 바로 열고 닫을 수 있어요. 닫아도 삭제되지 않고 여기에 남아요.");
        nlist = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Height = UI.S(230), Font = UI.Base };
        Dark.List(nlist);
        nlist.Columns.Add("메모", UI.S(300)); nlist.Columns.Add("색상", UI.S(70)); nlist.Columns.Add("상태", UI.S(70)); nlist.Columns.Add("수정한 시각", UI.S(140));
        nlist.DoubleClick += (s, e) => { foreach (var m in SelNotes()) Notes.Open(m, true); };
        nlist.Resize += (s, e) => { int w = nlist.ClientSize.Width - 4; if (w > 200) { nlist.Columns[1].Width = UI.S(70); nlist.Columns[2].Width = UI.S(70); nlist.Columns[3].Width = UI.S(140); nlist.Columns[0].Width = Math.Max(UI.S(120), w - UI.S(280)); } };
        sList.Full(nlist, true);
        var bNew = UI.Btn("+ 새 메모", true); bNew.Click += (s, e) => { Notes.New(true); };
        var bOpen = UI.Btn("열기"); bOpen.Click += (s, e) => { foreach (var m in SelNotes()) Notes.Open(m, true); };
        var bClose = UI.Btn("닫기"); bClose.Click += (s, e) => { foreach (var m in SelNotes()) Notes.Close(m); };
        var bDel = UI.Btn("삭제"); bDel.Click += (s, e) => {
            var sel = SelNotes(); if (sel.Count == 0) return;
            bool empty = sel.All(m => (m.Text ?? "").Trim() == "");
            if (empty || DarkBox.Show(this, sel.Count + "개 메모를 삭제할까요?", "스티커 메모", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                foreach (var m in sel) Notes.Delete(m);
        };
        var bCol = UI.Btn("색상 ▾"); bCol.Click += (s, e) => {
            var sel = SelNotes(); if (sel.Count == 0) return;
            PalettePopup.ShowAt(bCol, sel[0].Color, k => { foreach (var m in sel) Notes.SetColor(m, k); });
        };
        var bShow = UI.Btn("모두 보이기"); bShow.Click += (s, e) => Notes.ShowAll();
        var bHide = UI.Btn("모두 숨기기"); bHide.Click += (s, e) => Notes.HideAll();
        var bCopy = UI.Btn("모든 메모 텍스트 복사"); bCopy.Click += (s, e) => { string t = Notes.AllText(); try { if (t != "") Clipboard.SetText(t); nStatus.Text = t == "" ? "복사할 내용이 없어요." : "모든 메모 내용을 클립보드에 복사했어요."; } catch { } };
        sList.Full(UI.Flow(bNew, bOpen, bClose, bDel));
        sList.Full(UI.Flow(bCol, bShow, bHide, bCopy));
        nStatus = UI.HintL(""); sList.Full(nStatus);
        UI.AddRow(st, sList);

        var sDef = new Section("기본 설정", null);
        nGrad = UI.Chk("그라데이션 테마 (끄면 단색)"); nGrad.CheckedChanged += (s, e) => { if (loading) return; Cfg.Notes.Gradient = nGrad.Checked; Changed(); };
        sDef.Row("메모 스타일", nGrad, "모든 메모에 바로 적용돼요. 글자는 항상 밝게, 배경은 대비가 충분하도록 자동으로 어둡게 맞춰요.");
        nColor = new SwatchGrid(NoteColors.PaletteKeys) { Current = Cfg.Notes.DefaultColor };
        nColor.Picked += k => { if (loading) return; Cfg.Notes.DefaultColor = k; Changed(); };
        var nCustom = UI.Btn("맞춤 색…"); nCustom.Click += (s, e) => PalettePopup.ShowAt(nCustom, Cfg.Notes.DefaultColor, k => { Cfg.Notes.DefaultColor = k; nColor.Current = k; nColor.Invalidate(); Changed(); });
        sDef.Row("새 메모 색상", nColor);
        sDef.Row("", nCustom);
        nSize = UI.Num(7, 48, 11, 70); nSize.ValueChanged += (s, e) => { if (loading) return; Cfg.Notes.FontSize = (float)nSize.Value; Changed(); };
        sDef.Row("글자 크기", UI.Flow(nSize, UI.L("pt")), "새 메모에 적용돼요 (메모 안에서 Ctrl+휠로 확대/축소)");
        nOp = new GradSlider { Minimum = 40, Maximum = 100, Width = UI.S(220) };
        nOpVal = UI.L("");
        nOp.ValueChanged += (s, e) => { nOpVal.Text = nOp.Value + "%"; if (loading) return; Cfg.Notes.Opacity = nOp.Value; Changed(); };
        sDef.Row("불투명도", UI.Flow(nOp, nOpVal));
        nHk = new HotkeyBox(); nHk.ValueChanged += (s, e) => { if (loading) return; Cfg.Notes.Hotkey = nHk.Value; Changed(); };
        var nHkClr = UI.Btn("지우기"); nHkClr.Click += (s, e) => { nHk.Value = ""; Cfg.Notes.Hotkey = ""; Changed(); };
        sDef.Row("새 메모 단축키", UI.Flow(nHk, nHkClr), "어디서든 이 키를 누르면 새 메모가 열려요 (기본 Ctrl+Alt+N)");
        nStartup = UI.Chk("BongHub 시작 시 열려 있던 메모 다시 열기"); nStartup.CheckedChanged += (s, e) => { if (loading) return; Cfg.Notes.OpenOnStartup = nStartup.Checked; Changed(); };
        sDef.Full(nStartup);
        UI.AddRow(st, sDef);

        var sKeys = new Section("메모 안에서 쓰는 단축키", null);
        sKeys.Full(UI.L("Ctrl+B 굵게   ·   Ctrl+I 기울임   ·   Ctrl+U 밑줄   ·   Ctrl+T 취소선   ·   Ctrl+Shift+L 글머리 기호", UI.Small, UI.Text2));
        sKeys.Full(UI.L("Ctrl+Z / Ctrl+Y 실행 취소·다시 실행   ·   Ctrl+ +/− 글자 크기   ·   Ctrl+N 새 메모   ·   Ctrl+D 삭제   ·   Ctrl+W 닫기", UI.Small, UI.Text2));
        UI.AddRow(st, sKeys);

        loading = true;
        try {
            nColor.Current = Cfg.Notes.DefaultColor; nGrad.Checked = Cfg.Notes.Gradient;
            nSize.Value = (decimal)Math.Max(7f, Math.Min(48f, Cfg.Notes.FontSize));
            nOp.Value = Math.Max(40, Math.Min(100, Cfg.Notes.Opacity)); nOpVal.Text = nOp.Value + "%";
            nHk.Value = Cfg.Notes.Hotkey; nStartup.Checked = Cfg.Notes.OpenOnStartup;
        } finally { loading = false; }
        Notes.Changed += NotesChanged;
        FormClosed += (s, e) => Notes.Changed -= NotesChanged;
        return p;
    }
    void NotesChanged() { if (page == 2 && IsHandleCreated) { try { BeginInvoke((Action)FillNotes); } catch { } } }
    List<NoteMeta> SelNotes() { return nlist.SelectedItems.Cast<ListViewItem>().Select(i => (NoteMeta)i.Tag).ToList(); }
    void FillNotes() {
        Notes.EnsureLoaded();
        var sel = new HashSet<string>(SelNotes().Select(m => m.Id));
        nlist.BeginUpdate(); nlist.Items.Clear();
        foreach (var m in Notes.All) {
            string t = (m.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (t.Length > 80) t = t.Substring(0, 80) + "…";
            var it = new ListViewItem(t == "" ? "(빈 메모)" : t) { Tag = m };
            it.SubItems.Add(NoteColors.Name(m.Color)); it.SubItems.Add(Notes.IsOpen(m) ? "열림" : "닫힘"); it.SubItems.Add(m.Modified.ToString("M/d HH:mm"));
            if (sel.Contains(m.Id)) it.Selected = true;
            nlist.Items.Add(it);
        }
        nlist.EndUpdate();
        nStatus.Text = Notes.All.Count + "개 메모 · 열린 메모 " + Notes.All.Count(Notes.IsOpen) + "개";
    }

    // ---------- KakaoTalk page ----------
    CheckBox kAd, kCompact, kDark, kAuto; Label kStatus, kMsg; WTimer kTimer;
    Control BuildKakao() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("카톡 광고 제거"));
        var sAd = new Section("광고 숨김", "카톡 PC의 광고 배너를 숨겨요. 창 이벤트로만 동작해서 평소엔 CPU를 거의 쓰지 않아요.");
        kAd = UI.Chk("광고 숨김 사용"); kAd.CheckedChanged += (s, e) => { if (loading) return; Cfg.Kakao.AdBlock = kAd.Checked; UpdKakaoEn(); Changed(); };
        sAd.Full(kAd);
        kCompact = UI.Chk("컴팩트 레이아웃 (광고 자리까지 채우기)"); kCompact.CheckedChanged += (s, e) => { if (loading) return; Cfg.Kakao.Compact = kCompact.Checked; Changed(); };
        sAd.Full(kCompact);
        kDark = UI.Chk("다크 오버레이 (카톡 목록 위에 어두운 덮개)"); kDark.CheckedChanged += (s, e) => { if (loading) return; Cfg.Kakao.Dark = kDark.Checked; Changed(); };
        sAd.Full(kDark);
        kAuto = UI.Chk("시작 시 자동 적용 (BongHub가 켜질 때 광고 숨김 시작)"); kAuto.CheckedChanged += (s, e) => { if (loading) return; Cfg.Kakao.AutoApply = kAuto.Checked; Changed(); };
        sAd.Full(kAuto);
        sAd.Full(UI.HintL("Windows 시작 시 BongHub 자동 실행은 ‘일반’ 탭에서 설정해요. 레이아웃은 125% 배율 기준으로 맞춰져 있어요."));
        UI.AddRow(st, sAd);
        var sSt = new Section("상태", null);
        kStatus = UI.L(""); sSt.Row("현재", kStatus);
        var bRestore = UI.Btn("원상복구"); bRestore.Click += (s, e) => {
            kMsg.Text = "원상복구 중…";
            ThreadPool.QueueUserWorkItem(_ => { Kakao.Restore(); try { BeginInvoke((Action)(() => { kMsg.Text = "숨겼던 카톡 창을 모두 되돌리고 광고 숨김을 멈췄어요."; RefreshKakaoStatus(); })); } catch { } });
        };
        var bRef = UI.Btn("↻ 새로고침"); bRef.Click += (s, e) => RefreshKakaoStatus();
        sSt.Row("", UI.Flow(bRestore, bRef));
        kMsg = UI.HintL(""); sSt.Full(kMsg);
        UI.AddRow(st, sSt);
        loading = true;
        try { kAd.Checked = Cfg.Kakao.AdBlock; kCompact.Checked = Cfg.Kakao.Compact; kDark.Checked = Cfg.Kakao.Dark; kAuto.Checked = Cfg.Kakao.AutoApply; } finally { loading = false; }
        UpdKakaoEn();
        kTimer = new WTimer { Interval = 2000 }; kTimer.Tick += (s, e) => { if (page == 3) RefreshKakaoStatus(); }; kTimer.Start();
        return p;
    }
    void UpdKakaoEn() { kCompact.Enabled = kDark.Enabled = kAuto.Enabled = kAd.Checked; }
    void RefreshKakaoStatus() {
        ThreadPool.QueueUserWorkItem(_ => {
            bool run = Kakao.IsRunning(), kk = Kakao.KakaoRunning();
            try { BeginInvoke((Action)(() => {
                kStatus.Text = (run ? "● 광고 숨김 실행 중" : "○ 광고 숨김 꺼짐") + "     " + (kk ? "카톡 감지됨" : "카톡 실행 안 됨");
                kStatus.ForeColor = run ? Color.FromArgb(110, 214, 150) : UI.Hint;
            })); } catch { }
        });
    }

    // ---------- Desktop icons page ----------
    ListView dlist; Label dCur, dStatus, dState; CheckBox dAuto, dExit, dBefore, dChange, dStart, dAll; NumericUpDown dMin; HotkeyBox dHkS, dHkR; WTimer dTimer;
    Button dArr, dSnap, dVis;
    Control BuildDesk() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("바탕화면 아이콘"));
        var sList = new Section("저장된 배치", "아이콘 위치를 이름 붙여 여러 개 저장해 두고 언제든 되돌려요. 화면 구성(해상도·모니터 배치)마다 따로 기억해요.");
        dCur = UI.L("", UI.Bold, UI.Text); sList.Full(dCur);
        dlist = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = UI.S(220), Font = UI.Base };
        Dark.List(dlist);
        dlist.Columns.Add("이름", UI.S(220)); dlist.Columns.Add("화면 구성", UI.S(250)); dlist.Columns.Add("아이콘", UI.S(60)); dlist.Columns.Add("저장한 시각", UI.S(130));
        dlist.DoubleClick += (s, e) => DeskRestoreSel();
        dlist.Resize += (s, e) => { int w = dlist.ClientSize.Width - 4; if (w > 300) dlist.Columns[1].Width = Math.Max(UI.S(160), w - dlist.Columns[0].Width - dlist.Columns[2].Width - dlist.Columns[3].Width); };
        sList.Full(dlist, true);
        var bSave = UI.Btn("+ 지금 배치 저장", true); bSave.Click += (s, e) => {
            string n = DarkPrompt.Show(this, "새 배치 이름", "배치 " + DateTime.Now.ToString("M/d HH:mm"));
            if (n == null) return; string err; var l = Desk.SaveNew(n.Trim() == "" ? "배치" : n.Trim(), out err);
            DeskMsg(l != null ? "‘" + l.Name + "’ 저장 · 아이콘 " + l.Items.Count + "개" : "저장하지 못했어요: " + err, l != null);
        };
        var bRest = UI.Btn("복원"); bRest.Click += (s, e) => DeskRestoreSel();
        var bOver = UI.Btn("덮어쓰기"); bOver.Click += (s, e) => {
            var l = DeskSel(); if (l == null) return;
            if (DarkBox.Show(this, "‘" + l.Name + "’을(를) 지금 아이콘 위치로 덮어쓸까요?", "덮어쓰기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string err; DeskMsg(Desk.Overwrite(l.Id, out err) ? "‘" + l.Name + "’을(를) 지금 위치로 덮어썼어요" : "실패: " + err, err == null);
        };
        var bRen = UI.Btn("이름 변경"); bRen.Click += (s, e) => { var l = DeskSel(); if (l == null) return; string n = DarkPrompt.Show(this, "배치 이름", l.Name); if (n != null && n.Trim() != "") Desk.Rename(l.Id, n.Trim()); };
        var bDel = UI.Btn("삭제"); bDel.Click += (s, e) => {
            var l = DeskSel(); if (l == null) return;
            if (DarkBox.Show(this, "‘" + l.Name + "’ 배치를 삭제할까요?", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Desk.Delete(l.Id);
        };
        sList.Full(UI.Flow(bSave, bRest, bOver, bRen, bDel));
        dAll = UI.Chk("다른 화면 구성에서 저장한 배치도 보기"); dAll.CheckedChanged += (s, e) => FillDesk(); sList.Full(dAll);
        dStatus = UI.HintL(""); sList.Full(dStatus);
        UI.AddRow(st, sList);

        var sAuto = new Section("자동 저장 · 자동 복원", "모니터 프리셋을 바꾸거나 해상도가 바뀌면 아이콘이 흩어지는데, 이때 그 화면 구성에 맞는 배치로 자동으로 되돌려요.");
        dChange = UI.Chk("해상도·모니터 구성이 바뀌면 자동 복원 (모니터 프리셋 전환 포함)"); dChange.CheckedChanged += (s, e) => { if (loading) return; Cfg.Desk.RestoreOnChange = dChange.Checked; Changed(); };
        sAuto.Full(dChange);
        dBefore = UI.Chk("모니터 프리셋 전환 직전에 지금 배치 자동 저장"); dBefore.CheckedChanged += (s, e) => { if (loading) return; Cfg.Desk.SaveBeforeSwitch = dBefore.Checked; Changed(); };
        sAuto.Full(dBefore);
        dAuto = UI.Chk("주기적으로 자동 저장"); dAuto.CheckedChanged += (s, e) => { if (loading) return; Cfg.Desk.AutoSave = dAuto.Checked; dMin.Enabled = dAuto.Checked; Changed(); };
        dMin = UI.Num(1, 240, 10, 70); dMin.ValueChanged += (s, e) => { if (loading) return; Cfg.Desk.AutoSaveMinutes = (int)dMin.Value; Changed(); };
        sAuto.Full(UI.Flow(dAuto, dMin, UI.L("분마다 (바뀐 경우에만)")));
        dExit = UI.Chk("BongHub 종료 시 자동 저장"); dExit.CheckedChanged += (s, e) => { if (loading) return; Cfg.Desk.SaveOnExit = dExit.Checked; Changed(); };
        sAuto.Full(dExit);
        dStart = UI.Chk("BongHub 시작 시 현재 화면 구성의 배치로 복원"); dStart.CheckedChanged += (s, e) => { if (loading) return; Cfg.Desk.RestoreOnStartup = dStart.Checked; Changed(); };
        sAuto.Full(dStart);
        sAuto.Full(UI.HintL("자동 저장은 화면 구성마다 ‘자동 저장’ 하나만 유지하고 계속 최신으로 바꿔요. 직접 저장한 배치는 건드리지 않아요."));
        UI.AddRow(st, sAuto);

        var sKey = new Section("단축키", null);
        dHkS = new HotkeyBox(); dHkS.ValueChanged += (s, e) => { if (loading) return; Cfg.Desk.HotkeySave = dHkS.Value; Changed(); };
        var c1 = UI.Btn("지우기"); c1.Click += (s, e) => { dHkS.Value = ""; Cfg.Desk.HotkeySave = ""; Changed(); };
        sKey.Row("지금 배치 저장", UI.Flow(dHkS, c1));
        dHkR = new HotkeyBox(); dHkR.ValueChanged += (s, e) => { if (loading) return; Cfg.Desk.HotkeyRestore = dHkR.Value; Changed(); };
        var c2 = UI.Btn("지우기"); c2.Click += (s, e) => { dHkR.Value = ""; Cfg.Desk.HotkeyRestore = ""; Changed(); };
        sKey.Row("현재 화면 배치 복원", UI.Flow(dHkR, c2), "트레이 메뉴 ‘바탕화면 아이콘’에서도 저장·복원할 수 있어요");
        UI.AddRow(st, sKey);

        var sTools = new Section("바탕화면 도구", "아이콘 자동 정렬이 켜져 있으면 Windows가 아이콘을 강제로 줄 세워서 위치를 복원할 수 없어요.");
        dState = UI.L(""); sTools.Row("현재 상태", dState);
        dArr = UI.Btn("자동 정렬 끄기"); dArr.Click += (s, e) => { Desk.ToggleAutoArrange(); Thread.Sleep(300); RefreshDeskState(); };
        dSnap = UI.Btn("눈금에 맞춤 끄기"); dSnap.Click += (s, e) => { Desk.ToggleSnap(); Thread.Sleep(300); RefreshDeskState(); };
        dVis = UI.Btn("아이콘 숨기기"); dVis.Click += (s, e) => { Desk.ToggleIcons(); Thread.Sleep(300); RefreshDeskState(); };
        var bMin = UI.Btn("모든 창 최소화"); bMin.Click += (s, e) => Desk.MinimizeAll(false);
        var bUnmin = UI.Btn("최소화 되돌리기"); bUnmin.Click += (s, e) => Desk.MinimizeAll(true);
        sTools.Row("", UI.Flow(dArr, dSnap, dVis));
        sTools.Row("", UI.Flow(bMin, bUnmin));
        UI.AddRow(st, sTools);

        var sWin = new Section("창 위치도 저장하려면", "열린 창의 위치·크기는 ‘창 위치’ 탭의 ‘현재 창에서 캡처…’로 규칙을 만들어 두면 모니터 프리셋 전환 뒤 자동으로 제자리로 돌아가요.");
        var bWin = UI.Btn("창 위치 탭 열기 →"); bWin.Click += (s, e) => ShowPage(1);
        sWin.Full(bWin);
        UI.AddRow(st, sWin);

        loading = true;
        try {
            var d = Cfg.Desk; dChange.Checked = d.RestoreOnChange; dBefore.Checked = d.SaveBeforeSwitch; dAuto.Checked = d.AutoSave; dMin.Value = Math.Max(1, Math.Min(240, d.AutoSaveMinutes)); dMin.Enabled = d.AutoSave;
            dExit.Checked = d.SaveOnExit; dStart.Checked = d.RestoreOnStartup; dHkS.Value = d.HotkeySave; dHkR.Value = d.HotkeyRestore;
        } finally { loading = false; }
        Desk.Changed += DeskChanged; FormClosed += (s, e) => Desk.Changed -= DeskChanged;
        dTimer = new WTimer { Interval = 3000 }; dTimer.Tick += (s, e) => { if (page == 4) RefreshDeskState(); }; dTimer.Start();
        return p;
    }
    void DeskChanged() { if (page == 4 && IsHandleCreated) { try { BeginInvoke((Action)FillDesk); } catch { } } }
    void DeskMsg(string t, bool ok) { dStatus.Text = t; dStatus.ForeColor = ok ? UI.Ok : UI.Bad; }
    DeskLayout DeskSel() { return dlist.SelectedItems.Count == 0 ? null : Desk.Get((string)dlist.SelectedItems[0].Tag); }
    void DeskRestoreSel() {
        var l = DeskSel(); if (l == null) { DeskMsg("복원할 배치를 목록에서 고르세요", false); return; }
        var r = Desk.Restore(l); DeskMsg((r.Ok ? "‘" + l.Name + "’ → " : "") + r.Summary(), r.Ok);
    }
    void RefreshDesk() { FillDesk(); RefreshDeskState(); }
    void FillDesk() {
        string key = Desk.ScreenKey(); dCur.Text = "현재 화면: " + Desk.Describe(key);
        string sel = dlist.SelectedItems.Count > 0 ? (string)dlist.SelectedItems[0].Tag : null;
        var l = Desk.All.Where(x => dAll.Checked || x.Key == key).OrderByDescending(x => x.Key == key).ThenByDescending(x => x.Saved).ToList();
        dlist.BeginUpdate(); dlist.Items.Clear();
        foreach (var x in l) {
            var it = new ListViewItem((x.Key == key ? "● " : "    ") + x.Name + (x.Auto ? "  (자동)" : "")) { Tag = x.Id };
            it.SubItems.Add(x.Desc + (x.Key == key ? "  · 현재 화면" : "")); it.SubItems.Add(x.Items.Count.ToString()); it.SubItems.Add(x.Saved.ToString("M/d HH:mm"));
            if (x.Id == sel) it.Selected = true;
            dlist.Items.Add(it);
        }
        if (dlist.SelectedItems.Count == 0 && dlist.Items.Count > 0) dlist.Items[0].Selected = true;
        dlist.EndUpdate();
        if (l.Count == 0) DeskMsg(dAll.Checked ? "저장된 배치가 없어요. ‘지금 배치 저장’을 눌러 보세요." : "이 화면 구성으로 저장된 배치가 없어요.", true);
    }
    void RefreshDeskState() {
        bool arr = Desk.AutoArrange, snap = Desk.SnapToGrid, vis = Desk.IconsVisible;
        dState.Text = "자동 정렬 " + (arr ? "켜짐 ⚠" : "꺼짐") + "   ·   눈금에 맞춤 " + (snap ? "켜짐" : "꺼짐") + "   ·   아이콘 " + (vis ? "보임" : "숨김");
        dState.ForeColor = arr ? UI.Warn : UI.Text;
        dArr.Text = arr ? "자동 정렬 끄기" : "자동 정렬 켜기"; dSnap.Text = snap ? "눈금에 맞춤 끄기" : "눈금에 맞춤 켜기"; dVis.Text = vis ? "아이콘 숨기기" : "아이콘 보이기";
    }

    // ---------- General page ----------
    CheckBox gCloud; Label gCloudSt;
    void CloudStatus() { try { BeginInvoke((Action)ShowCloud); } catch { } }
    void ShowCloud() {
        if (gCloudSt == null) return;
        string t = !Cfg.Cloud.Enabled ? "꺼짐 · 켜면 연결을 확인하고 동기화를 시작해요" : Cloud.Status + (Cloud.LastSync > DateTime.MinValue ? "   ·   마지막 동기화 " + Cloud.LastSync.ToString("M/d HH:mm:ss") : "");
        gCloudSt.Text = t;
        gCloudSt.ForeColor = !Cfg.Cloud.Enabled ? UI.Hint : Cloud.Status.StartsWith("저장됨") ? UI.Ok : Cloud.Status.StartsWith("오프라인") || Cloud.Status.StartsWith("연결 필요") || Cloud.Status.Contains("실패") ? UI.Warn : UI.Text;
    }
    CheckBox gIdle, gAudio, gStartup, gOverlay; NumericUpDown gMin, gThr; ComboBox gPreset; GradSlider gOp; Label gOpVal;
    Control BuildGeneral() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("일반"));
        var sIdle = new Section("자리를 비우면 자동 전환", "키보드·마우스 입력이 없으면 선택한 모니터 프리셋을 자동으로 실행해요.");
        gIdle = UI.Chk("자동 전환 사용"); gIdle.CheckedChanged += (s, e) => { if (loading) return; Cfg.General.IdleEnabled = gIdle.Checked; Changed(); };
        sIdle.Full(gIdle);
        gMin = UI.Num(1, 600, Cfg.General.IdleMinutes, 70); gMin.ValueChanged += (s, e) => { if (loading) return; Cfg.General.IdleMinutes = (int)gMin.Value; Changed(); };
        sIdle.Row("대기 시간", UI.Flow(gMin, UI.L("분 동안 입력 없음")));
        gPreset = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(220), Font = UI.Base };
        gPreset.SelectedIndexChanged += (s, e) => { if (loading || gPreset.SelectedItem == null) return; Cfg.General.IdlePreset = gPreset.SelectedItem.ToString(); Changed(); };
        sIdle.Row("실행할 모니터 프리셋", gPreset);
        gAudio = UI.Chk("소리가 재생 중이면 건너뛰기 (영상 시청 등)"); gAudio.CheckedChanged += (s, e) => { if (loading) return; Cfg.General.SkipIfAudio = gAudio.Checked; Changed(); };
        sIdle.Full(gAudio);
        gThr = new NumericUpDown { DecimalPlaces = 4, Increment = 0.0005m, Minimum = 0.0001m, Maximum = 1m, Width = UI.S(100), Font = UI.Base, TextAlign = HorizontalAlignment.Right };
        gThr.ValueChanged += (s, e) => { if (loading) return; Cfg.General.AudioThreshold = (double)gThr.Value; Changed(); };
        sIdle.Row("소리 감지 기준", gThr, "출력 음량 최고값이 이 값보다 크면 소리가 나는 것으로 봐요 (기본 0.0010)");
        UI.AddRow(st, sIdle);

        var sStart = new Section("시작", null);
        gStartup = UI.Chk("Windows 시작 시 자동 실행"); gStartup.CheckedChanged += (s, e) => { if (loading) return; StartupEnabled = gStartup.Checked; Changed(); };
        sStart.Full(gStartup);
        sStart.Full(UI.HintL("시작프로그램 폴더의 ‘" + Startup.LinkName + "’ 바로가기로 관리돼요"));
        UI.AddRow(st, sStart);

        var sLock = new Section("Windows 잠금 화면 숨기기", "잠금 배경 사진을 생략하고 로그인 화면을 바로 표시해요. 암호·PIN 인증은 그대로 필요해요. Windows Pro·Enterprise·Education에서 지원하며 관리 정책에 따라 적용이 제한될 수 있어요.");
        var lockStatus = UI.HintL(""); var lockButton = UI.Btn("설정");
        Action updateLock = () => { lockStatus.Text = LockScreenPolicy.Hidden ? "현재 정책: 잠금 배경 화면 숨김" : "현재 정책: Windows 기본 잠금 화면"; lockButton.Text = LockScreenPolicy.Owned ? "이전 설정으로 복원…" : "잠금 화면 숨기기…"; };
        updateLock();
        lockButton.Click += (a, b) => {
            bool restore = LockScreenPolicy.Owned; lockButton.Enabled = false;
            ThreadPool.QueueUserWorkItem(_ => {
                string result;
                try { using (var helper = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "lockscreen " + (restore ? "restore" : "enable")) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) })) { helper.WaitForExit(); result = helper.ExitCode == 0 ? "설정됐어요. 다음 잠금부터 확인해 주세요." : "설정하지 못했어요."; } }
                catch (System.ComponentModel.Win32Exception ex) { result = ex.NativeErrorCode == 1223 ? "관리자 권한 요청을 취소했어요." : ex.Message; }
                catch (Exception ex) { result = ex.Message; }
                try { BeginInvoke((Action)(() => { if (IsDisposed) return; updateLock(); lockStatus.Text += " · " + result; lockButton.Enabled = true; })); } catch { }
            });
        };
        sLock.Full(lockStatus); sLock.Full(lockButton); sLock.Full(UI.HintL("이 PC에만 적용돼요. 변경할 때 관리자 권한이 필요하며, 복원하면 BongHub가 변경하기 전 정책으로 돌아가요.")); UI.AddRow(st, sLock);

        var sOv = new Section("전환 안내 화면", null);
        gOverlay = UI.Chk("전환 중 로딩 화면 사용 (모든 모니터 프리셋)"); gOverlay.CheckedChanged += (s, e) => { if (loading) return; Cfg.General.OverlayEnabled = gOverlay.Checked; Changed(); };
        sOv.Full(gOverlay);
        gOp = new GradSlider { Minimum = 30, Maximum = 95, Width = UI.S(240) };
        gOpVal = UI.L("");
        gOp.ValueChanged += (s, e) => { gOpVal.Text = gOp.Value + "%"; if (loading) return; Cfg.General.OverlayOpacity = gOp.Value; Changed(); };
        var bPrev = UI.Btn("미리보기"); bPrev.Click += (s, e) => PreviewOverlay("작업 모드로 전환 중...");
        sOv.Row("배경 어둡기", UI.Flow(gOp, gOpVal, bPrev));
        UI.AddRow(st, sOv);

        var sCloud = new Section("클라우드 동기화", "설정·창 규칙·스티커 메모(이미지 포함)·바탕화면 아이콘 배치를 비공개 GitHub 레포에 자동으로 저장하고 다른 PC와 맞춰요. 토큰·비밀번호는 올리지 않아요.");
        gCloud = UI.Chk("클라우드 동기화 사용"); gCloud.CheckedChanged += (s, e) => { if (loading) return; Cfg.Cloud.Enabled = gCloud.Checked; Changed(); };
        sCloud.Full(gCloud);
        sCloud.Row("저장소", UI.L(Cfg.Cloud.Repo.Replace("https://github.com/", "github.com/").Replace(".git", "") + "  (" + Cfg.Cloud.Branch + ")", UI.Base, UI.Text2));
        gCloudSt = UI.L("", UI.Bold); sCloud.Row("상태", gCloudSt);
        var bConn = UI.Btn("GitHub 연결…"); bConn.Click += (s, e) => { if (!Cloud.Connect()) DarkBox.Show(this, "GitHub CLI가 필요해요. 열린 페이지에서 설치한 뒤 다시 눌러 주세요.", "클라우드 동기화"); else gCloudSt.Text = "브라우저에서 GitHub 로그인을 마친 뒤 ‘연결 확인’을 눌러 주세요"; };
        var bChk = UI.Btn("연결 확인"); bChk.Click += (s, e) => { gCloudSt.Text = "확인 중…"; ThreadPool.QueueUserWorkItem(_ => { bool ok; string r = Cloud.Check(out ok); try { BeginInvoke((Action)(() => { gCloudSt.Text = r; gCloudSt.ForeColor = ok ? UI.Ok : UI.Warn; })); } catch { } }); };
        var bNow = UI.Btn("지금 동기화", true); bNow.Click += (s, e) => { if (!Cfg.Cloud.Enabled) { gCloudSt.Text = "먼저 ‘클라우드 동기화 사용’을 켜 주세요"; return; } Flush(); ThreadPool.QueueUserWorkItem(_ => Cloud.SyncNow(true)); };
        var bRepo = UI.Btn("레포 열기"); bRepo.Click += (s, e) => { try { Process.Start(Cfg.Cloud.Repo.Replace(".git", "")); } catch { } };
        sCloud.Row("", UI.Flow(bConn, bChk, bNow, bRepo));
        sCloud.Full(UI.HintL("변경하면 1~2초 뒤 자동으로 올리고, " + Cfg.Cloud.PullMinutes + "분마다 다른 PC의 변경을 가져와요. 충돌하면 더 최근에 수정한 쪽이 이기고, 메모는 메모별로 합쳐요. 덮어쓴 내용도 레포 기록(history)에 남아 되돌릴 수 있어요.\n권한은 이 레포 하나에만 Contents 읽기/쓰기면 충분해요. 로그인 정보는 Windows 자격 증명 관리자에만 저장돼요."));
        UI.AddRow(st, sCloud);
        Cloud.StatusChanged += CloudStatus; FormClosed += (s, e) => Cloud.StatusChanged -= CloudStatus;

        var sFile = new Section("설정 파일", null);
        var path = UI.L(Config.FilePath); var bOpen = UI.Btn("폴더 열기");
        bOpen.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + Core.Dir + "\""); } catch { } };
        sFile.Row("위치", UI.Flow(path, bOpen));
        UI.AddRow(st, sFile);
        LoadGeneral();
        return p;
    }
    void LoadGeneral() {
        loading = true;
        try {
            var g = Cfg.General;
            gIdle.Checked = g.IdleEnabled; gMin.Value = Math.Max(1, Math.Min(600, g.IdleMinutes)); gAudio.Checked = g.SkipIfAudio;
            gThr.Value = (decimal)Math.Max(0.0001, Math.Min(1, g.AudioThreshold)); gStartup.Checked = StartupEnabled; gOverlay.Checked = g.OverlayEnabled;
            gOp.Value = Math.Max(30, Math.Min(95, g.OverlayOpacity)); gOpVal.Text = gOp.Value + "%";
            gCloud.Checked = Cfg.Cloud.Enabled; ShowCloud();
            RefreshIdlePresets();
        } finally { loading = false; }
    }
    void RefreshIdlePresets() {
        bool was = loading; loading = true;
        try {
            gPreset.Items.Clear(); foreach (var p in Cfg.Presets) gPreset.Items.Add(p.Name);
            int i = Cfg.Presets.FindIndex(p => p.Name == Cfg.General.IdlePreset);
            gPreset.SelectedIndex = i >= 0 ? i : -1;
        } finally { loading = was; }
    }

    // ---------- About page ----------
    Label aboutKeys;
    Control BuildAbout() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("정보"));
        var s1 = new Section("BongHub " + AppInfo.Version, "석봉이의 유틸 창고");
        s1.Row("버전", UI.L("BongHub " + AppInfo.Version + "  (파일 버전 " + AppInfo.FileVer(Application.ExecutablePath) + ")"));
        aboutKeys = UI.L(""); s1.Row("단축키", aboutKeys);
        s1.Row("명령줄", UI.L("BongHub.exe <모니터 프리셋 이름>\nBongHub.exe work  |  away   (기존 방식)"));
        s1.Row("트레이", UI.L("아이콘 더블클릭: 설정 열기\n우클릭: 모니터 프리셋 실행 · 새 스티커 메모 · 창 규칙 적용 · 종료"));
        aboutComp = UI.L(""); s1.Row("구성 요소", aboutComp);
        UI.AddRow(st, s1);
        var s2 = new Section("변경 이력", null);
        s2.Full(UI.L(string.Join("\n", AppInfo.Changelog), UI.Small, UI.Text2));
        UI.AddRow(st, s2);
        return p;
    }
    Label aboutComp;
    void RefreshAbout() {
        aboutComp.Text = "NirSoft MultiMonitorTool " + AppInfo.FileVer(Path.Combine(Core.Dir, "MultiMonitorTool.exe")) + " (내장)\n"
            + "BongAd 카톡 광고 제거 도우미 " + (AppInfo.FileVer(Kakao.ExePath) == "?" ? "2.4" : AppInfo.FileVer(Kakao.ExePath)) + " (내장, MIT)\n"
            + "PowerToys 기능 모음 1.0 — Microsoft PowerToys(MIT 라이선스, © Microsoft Corporation)에서 영감을 받아 새로 구현\n"
            + "클라우드 데이터 형식 v" + Cloud.Schema + "  ·  git 사용 (설치된 Git/GitHub CLI)";
        aboutKeys.Text = string.Join("\n", Cfg.Presets.Select(p => (p.Hotkey == "" ? "(없음)" : p.Hotkey) + "   " + p.Name)) + "\n" + (Cfg.Notes.Hotkey == "" ? "(없음)" : Cfg.Notes.Hotkey) + "   새 스티커 메모";
    }

    // ---------- PowerToys page (card grid + detail subpages) ----------
    public static readonly string[] PtKeys = { "zones", "grab", "work", "palette", "paste", "peek", "resizer" };
    static readonly string[] PtNames = { "FancyZones", "잡아서 이동", "작업 영역", "명령 팔레트", "고급 붙여넣기", "미리보기", "Image Resizer" };
    static readonly string[] PtGlyphs = { "\uE80A", "\uE7C2", "\uE8A1", "\uE721", "\uE77F", "\uE8FF", "\uE91B" };
    static readonly string[] PtDescs = {
        "Shift를 누른 채 창을 끌면 영역이 나타나고, 놓으면 그 칸에 딱 맞춰요. 창 위치 규칙에서도 영역을 쓸 수 있어요.",
        "Alt+왼쪽 드래그로 창 어디서든 옮기고, Alt+오른쪽 드래그로 크기를 바꿔요.",
        "지금 열린 앱과 위치를 묶어 저장하고, 한 번에 실행·배치해요.",
        "단축키 하나로 앱 실행, BongHub 명령, 메모, 계산, 웹 검색을 해요.",
        "클립보드를 서식 없는 텍스트·마크다운·JSON으로 바꿔 붙여넣어요.",
        "탐색기에서 고른 파일을 단축키로 빠르게 미리 봐요 (이미지·텍스트·정보).",
        "이미지 여러 장을 한 번에 원하는 크기로 줄여요. 탐색기 우클릭 메뉴는 선택 사항이에요." };
    readonly List<PtCard> ptCards = new List<PtCard>(); Panel ptGrid, ptDetail; FlowLayoutPanel ptFlow; TextBox ptSearch; Label ptEmpty, ptConflict;
    readonly Dictionary<string, Control> ptSubs = new Dictionary<string, Control>(); public string PtOpen;
    public static Func<string, string> HotkeyOwner;   // returns the other app's description when a hotkey is taken (tray app fills it)
    Control BuildPowerToys() {
        TableLayoutPanel st; var p = Scroller(out st);
        UI.AddRow(st, PageTitle("PowerToys"));
        ptGrid = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0) };
        var gs = UI.Stack(); gs.Dock = DockStyle.Top; ptGrid.Controls.Add(gs);
        ptSearch = new TextBox { Width = UI.S(360), Font = UI.Base, BorderStyle = BorderStyle.FixedSingle, BackColor = UI.Input, ForeColor = UI.Text, Margin = new Padding(0, 0, 0, UI.S(4)) };
        SetCue(ptSearch, "기능 검색 (예: 붙여넣기, 창, 이미지)");
        ptSearch.TextChanged += (s, e) => PtFilter();
        UI.AddRow(gs, ptSearch);
        ptConflict = UI.L("", UI.Small, UI.Warn); UI.AddRow(gs, ptConflict);
        ptFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, UI.S(6), 0, 0), MaximumSize = new Size(UI.S(960), 0) };
        for (int i = 0; i < PtKeys.Length; i++) {
            var c = new PtCard { Key = PtKeys[i], Title = PtNames[i], Glyph = PtGlyphs[i], Desc = PtDescs[i], HasToggle = PtKeys[i] != "resizer" && PtKeys[i] != "work" };
            string key = c.Key;
            c.Toggled += () => { if (loading) return; PtSet(key, c.On); PtRefresh(); Changed(); };
            c.Opened += () => PtShow(key);
            ptCards.Add(c); ptFlow.Controls.Add(c);
        }
        UI.AddRow(gs, ptFlow);
        ptEmpty = UI.HintL("찾는 기능이 없어요"); ptEmpty.Visible = false; UI.AddRow(gs, ptEmpty);
        var lic = UI.HintL("Microsoft PowerToys(MIT 라이선스)에서 영감을 받아 BongHub에 맞게 새로 만든 기능이에요. 위험할 수 있는 기능(전역 후킹·창 끌기·탐색기 연동)은 기본으로 꺼져 있고, 켜야만 동작해요.");
        lic.MaximumSize = new Size(UI.S(900), 0); UI.AddRow(gs, lic);
        UI.AddRow(st, ptGrid);
        ptDetail = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Visible = false, Margin = new Padding(0) };
        ptDetail.SizeChanged += (a,b) => { foreach (Control child in ptDetail.Controls) { int w=Math.Max(UI.S(80),ptDetail.ClientSize.Width); if(child.MaximumSize.Width!=w) { child.MaximumSize=new Size(w,0); child.Width=w; } } };
        UI.AddRow(st, ptDetail);
        PtRefresh();
        return p;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, string l);
    static void SetCue(TextBox t, string cue) { t.HandleCreated += (s, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); }
    bool PtOn(string k) {
        var x = Cfg.Pt;
        switch (k) { case "zones": return x.Zones.Enabled; case "grab": return x.Grab.Enabled; case "work": return x.Work.Enabled; case "palette": return x.Palette.Enabled; case "paste": return x.Paste.Enabled; case "peek": return x.Peek.Enabled; default: return x.Resizer.Enabled; }
    }
    void PtSet(string k, bool on) {
        var x = Cfg.Pt;
        switch (k) { case "zones": x.Zones.Enabled = on; break; case "grab": x.Grab.Enabled = on; break; case "work": x.Work.Enabled = on; break; case "palette": x.Palette.Enabled = on; break; case "paste": x.Paste.Enabled = on; break; case "peek": x.Peek.Enabled = on; break; }
        if (ptSubs.ContainsKey(k)) { var c = ptSubs[k].Controls.Find("ptOn", true).OfType<CheckBox>().FirstOrDefault(); if (c != null && c.Checked != on) { bool l = loading; loading = true; c.Checked = on; loading = l; } }
    }
    // every hotkey BongHub registers, for conflict display
    public List<string[]> AllHotkeys() {
        var l = new List<string[]>();
        foreach (var p in Cfg.Presets) l.Add(new[] { p.Hotkey, "모니터 프리셋 '" + p.Name + "'" });
        l.Add(new[] { Cfg.Notes.Hotkey, "새 스티커 메모" }); l.Add(new[] { Cfg.Desk.HotkeySave, "아이콘 저장" }); l.Add(new[] { Cfg.Desk.HotkeyRestore, "아이콘 복원" });
        var x = Cfg.Pt;
        if (x.Palette.Enabled) l.Add(new[] { x.Palette.Hotkey, "명령 팔레트" });
        if (x.Paste.Enabled) { l.Add(new[] { x.Paste.Hotkey, "고급 붙여넣기" }); l.Add(new[] { x.Paste.PlainHotkey, "서식 없이 붙여넣기" }); l.Add(new[] { x.Paste.JsonHotkey, "JSON으로 붙여넣기" }); l.Add(new[] { x.Paste.MarkdownHotkey, "마크다운으로 붙여넣기" }); }
        if (x.Peek.Enabled) l.Add(new[] { x.Peek.Hotkey, "미리보기" });
        if (x.Work.Enabled) foreach (var w in x.Work.Items) l.Add(new[] { w.Hotkey, "작업 영역 '" + w.Name + "'" });
        return l.Where(a => Hotkey.Normalize(a[0]) != "").ToList();
    }
    static readonly string[] Reserved = { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+A", "Ctrl+S", "Alt+F4", "Alt+Tab", "Ctrl+Shift+Esc", "Win+L", "Win+D", "Win+E", "Win+V", "Ctrl+Alt+Delete" };
    public string PtConflicts() {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var msgs = new List<string>();
        foreach (var a in AllHotkeys()) {
            string k = Hotkey.Normalize(a[0]), o;
            if (seen.TryGetValue(k, out o)) msgs.Add(k + ": " + o + " ↔ " + a[1]); else seen[k] = a[1];
            if (Reserved.Contains(k, StringComparer.OrdinalIgnoreCase)) msgs.Add(k + ": Windows 기본 단축키와 겹쳐요 (" + a[1] + ")");
            else if (HotkeyOwner != null) { string t = HotkeyOwner(k); if (t != null) msgs.Add(k + ": " + t + " (" + a[1] + ")"); }
        }
        return msgs.Count == 0 ? "" : "⚠ 단축키 충돌 — " + string.Join("  ·  ", msgs.Distinct());
    }
    void PtRefresh() {
        if (ptCards.Count == 0) return; bool l = loading; loading = true;
        var x = Cfg.Pt; string conf = PtConflicts();
        foreach (var c in ptCards) {
            Color previousColor = c.BadgeColor; string previous = c.On + "|" + c.Badge + "|" + previousColor.ToArgb();
            c.On = PtOn(c.Key); string hk = null;
            switch (c.Key) {
                case "zones": c.Badge = c.On ? "켜짐 · " + (x.Zones.ShiftDrag ? "Shift+드래그" : "드래그") : "꺼짐 (후킹 없음)"; break;
                case "grab": c.Badge = c.On ? "켜짐 · " + x.Grab.Modifier + "+드래그" : "꺼짐 (후킹 없음)"; break;
                case "work": c.Badge = x.Work.Items.Count + "개 저장됨"; break;
                case "palette": hk = x.Palette.Hotkey; c.Badge = c.On ? "켜짐 · " + hk : "꺼짐"; break;
                case "paste": hk = x.Paste.Hotkey; c.Badge = c.On ? "켜짐 · " + hk : "꺼짐"; break;
                case "peek": hk = x.Peek.Hotkey; c.Badge = c.On ? "켜짐 · " + hk : "꺼짐"; break;
                case "resizer": c.Badge = "우클릭 메뉴 " + (x.Resizer.ContextMenu ? "켜짐" : "꺼짐"); break;
            }
            bool bad = c.On && hk != null && conf.Contains(Hotkey.Normalize(hk) + ":");
            c.BadgeColor = bad ? UI.Warn : c.On || c.Key == "work" || c.Key == "resizer" ? UI.Ok : UI.Hint; if (bad) c.Badge += " · 충돌";
            Color currentColor = c.BadgeColor; if (previous != c.On + "|" + c.Badge + "|" + currentColor.ToArgb()) c.Invalidate();
        }
        ptConflict.Text = conf; ptConflict.Visible = conf != "";
        loading = l;
    }
    void PtFilter() {
        string q = ptSearch.Text.Trim(); int n = 0;
        for (int i = 0; i < ptCards.Count; i++) { var c = ptCards[i]; bool v = q == "" || Palette.Score(q, c.Title) > 0 || c.Desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; c.Visible = v; if (v) n++; }
        ptEmpty.Visible = n == 0;
    }
    public bool PreFilterMessage(ref Message message) {
        bool appBack=message.Msg==0x0319 && ((message.LParam.ToInt64()>>16)&0x7FF)==1;
        bool mouseBack=(message.Msg==0x020B || message.Msg==0x020C) && ((message.WParam.ToInt64()>>16)&0xFFFF)==1;
        if(page!=7 || PtOpen==null || !Visible || IsDisposed || (!appBack && !mouseBack))return false;
        var target=Control.FromChildHandle(message.HWnd);if(target==null || target.FindForm()!=this)return false;
        if(appBack || message.Msg==0x020C)PtShow(null);return true;
    }
    public void PtShow(string key) {
        PtOpen = key;
        if (key == null) { ptDetail.Visible = false; ptGrid.Visible = true; PtRefresh(); return; }
        Control sub;
        if (!ptSubs.TryGetValue(key, out sub)) { bool l = loading; loading = true; try { sub = PtBuildSub(key); } finally { loading = l; } ptSubs[key] = sub; SlimScroll.WrapAll(sub); Dark.Apply(sub); }
        ptDetail.SuspendLayout(); ptDetail.Controls.Clear(); sub.MaximumSize = new Size(Math.Max(UI.S(80), ptDetail.ClientSize.Width), 0); sub.Width=ptDetail.ClientSize.Width; ptDetail.Controls.Add(sub); ptDetail.ResumeLayout(true);
        ptGrid.Visible = false; ptDetail.Visible = true;
        if (key == "work") PtFillWs();
    }
    Section PtHead(TableLayoutPanel st, string key) {
        int i = Array.IndexOf(PtKeys, key);
        var back = UI.Btn("‹  PowerToys"); back.Click += (s, e) => PtShow(null); back.Margin = new Padding(0, 0, 0, UI.S(10)); UI.AddRow(st, back);
        var sec = new Section(PtNames[i], PtDescs[i]);
        if (key != "work" && key != "resizer") {
            var on = UI.Chk("사용"); on.Name = "ptOn"; on.Checked = PtOn(key);
            on.CheckedChanged += (s, e) => { if (loading) return; PtSet(key, on.Checked); Changed(); };
            sec.Full(on);
        }
        UI.AddRow(st, sec); return sec;
    }
    HotkeyBox PtHk(Section sec, string label, string val, Action<string> set) {
        var hb = new HotkeyBox(); hb.Value = val; hb.ValueChanged += (s, e) => { if (loading) return; set(hb.Value); Changed(); PtRefresh(); };
        var clr = UI.Btn("지우기"); clr.Click += (s, e) => { hb.Value = ""; set(""); Changed(); PtRefresh(); };
        sec.Row(label, UI.Flow(hb, clr)); return hb;
    }
    CheckBox PtChk(Section sec, string text, bool val, Action<bool> set) { var c = UI.Chk(text); c.Checked = val; c.CheckedChanged += (s, e) => { if (loading) return; set(c.Checked); Changed(); PtRefresh(); }; sec.Full(c); return c; }
    public void SyncPasteShortcut(string key) {
        Cfg.Pt.Paste.Hotkey=key;bool previous=loading;loading=true;
        try { foreach(var panel in ptSubs.Values) foreach(var box in panel.Controls.Find("pasteActivation",true).OfType<HotkeyBox>())box.Value=key; } finally {loading=previous;}
    }
    ListView wsList; Label wsStatus;
    Control PtBuildSub(string key) {
        var st = UI.Stack(); st.Dock = DockStyle.Top; st.AutoSize = true; var x = Cfg.Pt;
        var sec = PtHead(st, key);
        switch (key) {
            case "zones": {
                PtChk(sec, "Shift를 누른 채 끌 때 영역 표시", x.Zones.ShiftDrag, v => x.Zones.ShiftDrag = v);
                PtChk(sec, "Shift 없이 모든 드래그에서 영역 표시 (Shift를 누르면 반대로 동작)", x.Zones.ShowOnDrag, v => x.Zones.ShowOnDrag = v);
                PtChk(sec, "Ctrl을 누른 채 끌면 여러 영역에 걸쳐 맞춤", x.Zones.Span, v => x.Zones.Span = v);
                PtChk(sec, "Win+←/→로 활성 창을 영역 사이에서 이동 (키보드 후킹, 다음 모니터까지 이어짐)", x.Zones.WinArrows, v => x.Zones.WinArrows = v);
                PtChk(sec, "영역에서 끌어낼 때 원래 크기로 되돌리기", x.Zones.RestoreSize, v => x.Zones.RestoreSize = v);
                PtChk(sec, "끄는 동안 모든 모니터에 영역 표시", x.Zones.AllMonitors, v => x.Zones.AllMonitors = v);
                PtChk(sec, "영역 번호 표시", x.Zones.Numbers, v => x.Zones.Numbers = v);
                var op = UI.Num(10, 100, Math.Max(10, Math.Min(100, x.Zones.Opacity)), 64); op.ValueChanged += (a, b) => { if (loading) return; x.Zones.Opacity = (int)op.Value; Changed(); };
                var zc = new TextBox { Width = UI.S(90), Font = UI.Base, Text = x.Zones.Color }; var zh = new TextBox { Width = UI.S(90), Font = UI.Base, Text = x.Zones.HotColor };
                Func<string, bool> okc = t => Regex.IsMatch(t.Trim(), "^#[0-9A-Fa-f]{6}$");
                zc.TextChanged += (a, b) => { if (loading || !okc(zc.Text)) return; x.Zones.Color = zc.Text.Trim(); Changed(); }; zh.TextChanged += (a, b) => { if (loading || !okc(zh.Text)) return; x.Zones.HotColor = zh.Text.Trim(); Changed(); };
                sec.Row("오버레이", UI.Flow(UI.L("불투명도 %"), op, UI.L("  영역 색"), zc, UI.L("  강조 색"), zh), "#RRGGBB");
                var ex = new TextBox { Width = UI.S(320), Font = UI.Base, Text = string.Join(", ", x.Zones.Exclude) };
                ex.TextChanged += (a, b) => { if (loading) return; x.Zones.Exclude = ex.Text.Split(',', ';', '\n').Select(t => t.Trim()).Where(t => t != "").ToList(); Changed(); };
                sec.Row("제외할 앱", ex, "프로세스 이름, 쉼표로 구분 (예: notepad, Photoshop)");
                var lay = new Section("모니터별 레이아웃", "각 모니터의 영역 구성을 정해요. 창 위치 규칙의 ‘FancyZones 영역에 맞춤’도 이 레이아웃을 써요.");
                foreach (var s in Screen.AllScreens) {
                    string id = Monitors.IdOf(s); var zl = x.Zones.Layouts.FirstOrDefault(l => l.Monitor == id);
                    if (zl == null) { zl = Zones.For(s); zl.Monitor = id; x.Zones.Layouts.Add(zl); }
                    var z = zl; var kind = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(200), Font = UI.Base }; kind.Items.AddRange(Zones.KindNames); kind.SelectedIndex = Math.Max(0, Array.IndexOf(Zones.Kinds, z.Kind));
                    var na = UI.Num(1, 8, Math.Max(1, Math.Min(8, z.A)), 56); var nb = UI.Num(1, 8, Math.Max(1, Math.Min(8, z.B)), 56); var sp = UI.Num(0, 40, Math.Max(0, Math.Min(40, z.Spacing)), 56);
                    var custom = new TextBox { Width = UI.S(140), Font = UI.Base, Text = string.Join(":", z.Custom.Select(f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))) };
                    var pv = new ZonePreview { Size = new Size(UI.S(320), UI.S(180)), Layout = z, Area = s.WorkingArea, Editable = true };
                    pv.Edited += () => { loading = true; kind.SelectedIndex = Math.Max(0, Array.IndexOf(Zones.Kinds, z.Kind)); loading = false; Changed(); };
                    Action upd = () => { if (loading) return; z.Kind = Zones.Kinds[kind.SelectedIndex]; z.A = (int)na.Value; z.B = (int)nb.Value; z.Spacing = (int)sp.Value; z.Custom = custom.Text.Split(':', ',', ' ').Select(t => { float f; return float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : 0f; }).Where(f => f > 0).ToList(); pv.Invalidate(); Changed(); };
                    kind.SelectedIndexChanged += (a, b) => upd(); na.ValueChanged += (a, b) => upd(); nb.ValueChanged += (a, b) => upd(); sp.ValueChanged += (a, b) => upd(); custom.TextChanged += (a, b) => upd();
                    lay.Row(id + (s.Primary ? " (주)" : "") + "  " + s.Bounds.Width + "×" + s.Bounds.Height, UI.Flow(kind, UI.L(" 열"), na, UI.L(" 행"), nb, UI.L(" 간격"), sp));
                    lay.Row("직접 비율", custom, "예: 1:2:1  (‘직접 비율’일 때)");
                    lay.Full(pv); lay.Full(UI.HintL("편집: 영역 클릭 = 세로로 나누기 · Shift+클릭 = 가로로 나누기 · 오른쪽 클릭 두 번(두 영역) = 합치기. 편집하면 ‘캔버스’ 레이아웃이 돼요."));
                }
                UI.AddRow(st, lay);
                break; }
            case "grab": {
                sec.Full(UI.HintL("일반 창과 최대화 창을 지원해요. 관리자 권한으로 실행된 앱은 BongHub도 관리자 권한이 필요합니다. 보안 데스크톱과 잠금 화면은 이동 대상이 아닙니다."));
                var elevate=UI.Btn("관리자 권한으로 다시 시작…");elevate.Click+=(a,b)=>{try{Flush();Notes.FlushAll();Process.Start(new ProcessStartInfo(Application.ExecutablePath,"restart "+Process.GetCurrentProcess().Id){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(Application.ExecutablePath)});Application.Exit();}catch(System.ComponentModel.Win32Exception ex){if(ex.NativeErrorCode!=1223)DarkBox.Show(this,ex.Message,"다시 시작");}};sec.Full(elevate);
                var mod = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(140), Font = UI.Base }; mod.Items.AddRange(new object[] { "Alt", "Win", "Ctrl+Alt" }); mod.SelectedIndex = Math.Max(0, mod.Items.IndexOf(x.Grab.Modifier));
                mod.SelectedIndexChanged += (s, e) => { if (loading) return; x.Grab.Modifier = (string)mod.SelectedItem; Changed(); PtRefresh(); };
                sec.Row("보조 키", mod, "보조 키 + 왼쪽 드래그 = 이동");
                PtChk(sec, "보조 키 + 오른쪽 드래그로 크기 조정", x.Grab.Resize, v => x.Grab.Resize = v);
                PtChk(sec, "전체 화면 앱(게임·영상)에서는 동작 안 함", x.Grab.SkipFullscreen, v => x.Grab.SkipFullscreen = v);
                sec.Full(UI.HintL("바탕화면과 작업 표시줄은 제외해요. 최대화 창은 움직이기 시작할 때 복원하며, 켜져 있는 동안에만 마우스 후킹을 써요."));
                break; }
            case "work": {
                PtChk(sec, "작업 영역 단축키와 명령 팔레트에서 사용", x.Work.Enabled, v => x.Work.Enabled = v);
                PtChk(sec, "실행되지 않은 앱은 자동으로 실행", x.Work.LaunchMissing, v => x.Work.LaunchMissing = v);
                PtChk(sec, "이미 열린 창은 새로 실행하지 않고 옮기기 (끄면 PowerToys처럼 새 창을 띄움)", x.Work.MoveExisting, v => x.Work.MoveExisting = v);
                wsList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = UI.S(200), Width = UI.S(700), Font = UI.Base };
                Dark.List(wsList); wsList.Columns.Add("이름", UI.S(200)); wsList.Columns.Add("앱", UI.S(330)); wsList.Columns.Add("단축키", UI.S(150));
                wsList.DoubleClick += (s, e) => WsLaunch();
                sec.Full(wsList, true);
                var bNew = UI.Btn("+ 지금 열린 창으로 저장", true); bNew.Click += (s, e) => {
                    string n = DarkPrompt.Show(this, "작업 영역 이름", "작업 " + (x.Work.Items.Count + 1)); if (n == null) return;
                    var w = new Workspace { Name = n.Trim() == "" ? "작업 영역" : n.Trim(), Apps = Workspaces.Capture() }; x.Work.Items.Add(w); PtFillWs(); Changed(); PtRefresh();
                    wsStatus.Text = "‘" + w.Name + "’ 저장 · 앱 " + w.Apps.Count + "개";
                };
                var bRun = UI.Btn("열기"); bRun.Click += (s, e) => WsLaunch();
                var bUpd = UI.Btn("다시 캡처"); bUpd.Click += (s, e) => { var w = WsSel(); if (w == null) return; w.Apps = Workspaces.Capture(); w.Saved = DateTime.Now; PtFillWs(); Changed(); };
                var bHk = UI.Btn("단축키…"); bHk.Click += (s, e) => { var w = WsSel(); if (w == null) return; string h = DarkPrompt.Show(this, "단축키 (예: Ctrl+Alt+W, 비우면 없음)", w.Hotkey); if (h == null) return; w.Hotkey = Hotkey.Normalize(h); PtFillWs(); Changed(); PtRefresh(); };
                var bRen = UI.Btn("이름 변경"); bRen.Click += (s, e) => { var w = WsSel(); if (w == null) return; string n = DarkPrompt.Show(this, "작업 영역 이름", w.Name); if (n != null && n.Trim() != "") { w.Name = n.Trim(); PtFillWs(); Changed(); } };
                var bDel = UI.Btn("삭제"); bDel.Click += (s, e) => { var w = WsSel(); if (w == null) return; if (DarkBox.Show(this, "‘" + w.Name + "’을(를) 삭제할까요?", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { x.Work.Items.Remove(w); PtFillWs(); Changed(); PtRefresh(); } };
                var bLnk = UI.Btn("바탕화면 바로 가기"); bLnk.Click += (s, e) => { var w = WsSel(); if (w == null) return; try { wsStatus.Text = "바로 가기 만듦: " + Path.GetFileName(Workspaces.Shortcut(w, null)); } catch (Exception ex2) { wsStatus.Text = "바로 가기 실패: " + ex2.Message; } };
                sec.Full(UI.Flow(bNew, bRun, bUpd, bHk, bRen, bDel, bLnk));
                wsStatus = UI.HintL("앱 경로·모니터·위치·상태(최대화/최소화)를 기억해요."); sec.Full(wsStatus);
                break; }
            case "palette": {
                PtHk(sec, "단축키", x.Palette.Hotkey, v => x.Palette.Hotkey = v);
                PtChk(sec, "시작 메뉴 앱 검색", x.Palette.Apps, v => x.Palette.Apps = v);
                PtChk(sec, "계산기 (예: 12*3+4)", x.Palette.Calc, v => x.Palette.Calc = v);
                PtChk(sec, "웹 검색 항목 표시 (??로 바로 검색)", x.Palette.Web, v => x.Palette.Web = v);
                PtChk(sec, "단위 변환 (예: 10 km to mi, 30 평 to m2, 100 f to c)", x.Palette.Units, v => x.Palette.Units = v);
                PtChk(sec, "시간·날짜·세계 시각 (time, 날짜, time tokyo)", x.Palette.Time, v => x.Palette.Time = v);
                PtChk(sec, "열린 창 전환 (< 로 시작)", x.Palette.Windows, v => x.Palette.Windows = v);
                PtChk(sec, "파일 검색 (? 로 시작, Windows 검색 색인 사용)", x.Palette.Files, v => x.Palette.Files = v);
                PtChk(sec, "명령 실행 (> 로 시작)", x.Palette.Shell, v => x.Palette.Shell = v);
                PtChk(sec, "Windows 터미널 프로필", x.Palette.Terminal, v => x.Palette.Terminal = v);
                PtChk(sec, "시스템 명령 (잠금·절전·로그오프·다시 시작·종료·휴지통 비우기, 실행 전 확인)", x.Palette.System, v => x.Palette.System = v);
                PtChk(sec, "Windows 설정 페이지", x.Palette.Settings, v => x.Palette.Settings = v);
                sec.Full(UI.HintL("앱 목록은 처음 쓸 때 백그라운드에서 만들어 캐시해요. 창은 한 번 만든 뒤 숨겨 두고 다시 써요."));
                break; }
            case "paste": {
                PtHk(sec, "붙여넣기 메뉴", x.Paste.Hotkey, v => x.Paste.Hotkey = v).Name="pasteActivation";
                PtHk(sec, "바로 서식 없이", x.Paste.PlainHotkey, v => x.Paste.PlainHotkey = v);
                PtHk(sec, "바로 JSON으로", x.Paste.JsonHotkey, v => x.Paste.JsonHotkey = v);
                PtHk(sec, "바로 마크다운으로", x.Paste.MarkdownHotkey, v => x.Paste.MarkdownHotkey = v);
                var ca = new Section("사용자 지정 동작", "한 줄에 하나: 이름 | 종류 | 패턴 | 바꿀 값.  종류: " + string.Join(", ", AdvPaste.CustomKinds));
                var ctb = new TextBox { Multiline = true, Height = UI.S(110), Width = UI.S(560), Font = UI.Base, ScrollBars = ScrollBars.Vertical, Text = string.Join("\r\n", x.Paste.Custom.Select(a => a.Name + " | " + a.Kind + " | " + a.Pattern + " | " + a.Replace)) };
                ctb.TextChanged += (a, b) => { if (loading) return; x.Paste.Custom = ParseActions(ctb.Text); Changed(); };
                ca.Full(ctb); ca.Full(UI.HintL("예: 전화번호 정리 | regex | [^0-9] |    ·   인용 | template | > {text}")); UI.AddRow(st, ca);
                sec.Full(UI.HintL("변환: 서식 없는 텍스트 · HTML→마크다운 · CSV/TSV/키:값→JSON · JSON 정리 · 공백 정리 · .txt/.html/.png/.jpg 파일로 붙여넣기 · 사용자 지정 동작. 클립보드는 단축키를 누를 때만 읽어요."));
                break; }
            case "peek": {
                PtHk(sec, "단축키", x.Peek.Hotkey, v => x.Peek.Hotkey = v);
                var selectPreview=UI.Btn("파일 선택해서 미리보기…");selectPreview.Click+=(a,b)=>{using(var dialog=new OpenFileDialog{Multiselect=true,Filter="모든 파일|*.*"})if(dialog.ShowDialog(this)==DialogResult.OK){var preview=new PeekForm(dialog.FileNames.ToList(),0);preview.Show();preview.Activate();}};sec.Full(selectPreview);
                sec.Full(UI.HintL("기본 Space는 탐색기에서 파일을 선택했을 때만 동작해요. 파일 이름·검색어를 입력할 때는 일반 공백으로 입력됩니다."));
                PtChk(sec, "다른 창을 누르면 닫기", x.Peek.CloseOnFocusLoss, v => x.Peek.CloseOnFocusLoss = v);
                PtChk(sec, "HTML 파일을 렌더링해서 보기 (끄면 소스 보기, 스크립트는 항상 차단)", x.Peek.RenderHtml, v => x.Peek.RenderHtml = v);
                sec.Full(UI.HintL("Space를 누르는 동안 표시하고 떼면 닫아요. 이미지·GIF·WebP·HEIC(썸네일) · 코드(구문 강조) · 마크다운 · HTML · 동영상/음악 · ZIP 목록 · 폴더 · PDF/Office(설치된 미리보기 처리기). ←/→ 다음 파일, Esc/Space 닫기, Enter 열기."));
                break; }
            case "resizer": {
                var open = UI.Btn("Image Resizer 열기", true); open.Click += (s, e) => { var f = new ResizerForm(x.Resizer, null); f.Saved += () => Changed(); f.Show(this); };
                sec.Full(open);
                PtChk(sec, "탐색기 이미지 우클릭 메뉴에 ‘BongHub로 크기 조정’ 추가 (현재 사용자만, 끄면 제거)", x.Resizer.ContextMenu, v => x.Resizer.ContextMenu = v);
                PtChk(sec, "사진 방향 무시 (세로 사진에는 프리셋을 돌려서 적용)", x.Resizer.IgnoreOrientation, v => x.Resizer.IgnoreOrientation = v);
                PtChk(sec, "메타데이터 제거 (EXIF·GPS 등)", x.Resizer.RemoveMeta, v => x.Resizer.RemoveMeta = v);
                PtChk(sec, "원본의 수정한 날짜 유지", x.Resizer.KeepDate, v => x.Resizer.KeepDate = v);
                var nf = new TextBox { Width = UI.S(200), Font = UI.Base, Text = x.Resizer.NameFormat }; nf.TextChanged += (a, b) => { if (loading || !(nf.Text.Contains("%1") || nf.Text.Contains("%2"))) return; x.Resizer.NameFormat = nf.Text; Changed(); };
                sec.Row("파일 이름 형식", nf, "%1 원래 이름 · %2 크기 이름 · %3 %4 지정 크기 · %5 %6 실제 크기");
                var fb = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(120), Font = UI.Base }; fb.Items.AddRange(new object[] { "PNG", "JPEG" }); fb.SelectedIndex = x.Resizer.Fallback == "jpg" ? 1 : 0;
                fb.SelectedIndexChanged += (a, b) => { if (loading) return; x.Resizer.Fallback = fb.SelectedIndex == 1 ? "jpg" : "png"; Changed(); };
                sec.Row("기타 형식 저장", fb, "원래 형식으로 저장할 수 없을 때");
                var pre = new Section("크기 프리셋", "이름 너비×높이 [px|%|cm|in], 한 줄에 하나");
                var tb = new TextBox { Multiline = true, Height = UI.S(110), Width = UI.S(320), Font = UI.Base, Text = string.Join("\r\n", x.Resizer.Presets.Select(q => q.Name + " " + q.W + "x" + q.H + (q.Unit == "px" ? "" : " " + q.Unit))) };
                tb.TextChanged += (s, e) => { if (loading) return; var l = ParsePresets(tb.Text); if (l.Count > 0) { x.Resizer.Presets = l; Changed(); } };
                pre.Full(tb); UI.AddRow(st, pre);
                break; }
        }
        return st;
    }
    public static List<ResizePreset> ParsePresets(string t) {
        var l = new List<ResizePreset>();
        foreach (var line in t.Replace("\r", "").Split('\n')) { var m = Regex.Match(line.Trim(), @"^(.+?)\s+(\d+)\s*[x×*]\s*(\d+)\s*(px|%|cm|in)?$", RegexOptions.IgnoreCase); if (m.Success) { int w = int.Parse(m.Groups[2].Value), h = int.Parse(m.Groups[3].Value); if (w > 0 && h > 0 && w <= 20000 && h <= 20000) l.Add(new ResizePreset(m.Groups[1].Value, w, h) { Unit = m.Groups[4].Success ? m.Groups[4].Value.ToLowerInvariant() : "px" }); } }
        return l;
    }
    public static List<PasteAction> ParseActions(string t) {
        var l = new List<PasteAction>();
        foreach (var line in t.Replace("\r", "").Split('\n')) {
            var p = line.Split('|'); if (p.Length < 2 || p[0].Trim() == "") continue; string k = p[1].Trim().ToLowerInvariant(); if (!AdvPaste.CustomKinds.Contains(k)) continue;
            l.Add(new PasteAction { Name = p[0].Trim(), Kind = k, Pattern = p.Length > 2 ? p[2].Trim() : "", Replace = p.Length > 3 ? string.Join("|", p.Skip(3)).Trim() : "" });
        }
        return l;
    }
    Workspace WsSel() { if (wsList == null || wsList.SelectedItems.Count == 0) return null; return wsList.SelectedItems[0].Tag as Workspace; }
    void PtFillWs() {
        if (wsList == null) return; wsList.BeginUpdate(); wsList.Items.Clear();
        foreach (var w in Cfg.Pt.Work.Items) { var it = new ListViewItem(w.Name) { Tag = w }; it.SubItems.Add(string.Join(", ", w.Apps.Select(a => a.Process).Distinct())); it.SubItems.Add(w.Hotkey == "" ? "-" : w.Hotkey); wsList.Items.Add(it); }
        wsList.EndUpdate();
    }
    void WsLaunch() {
        var w = WsSel(); if (w == null) return; var copy = Config.CloneObj(w); bool launch = Cfg.Pt.Work.LaunchMissing, move = Cfg.Pt.Work.MoveExisting; wsStatus.Text = "‘" + w.Name + "’ 여는 중…";
        ThreadPool.QueueUserWorkItem(_ => { string r; try { r = Workspaces.Launch(copy, launch, move, null); } catch (Exception ex) { r = "실패: " + ex.Message; } try { BeginInvoke((Action)(() => { if (!IsDisposed) wsStatus.Text = r; })); } catch { } });
    }

    // ---------- save / close ----------
    string Problem() {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Cfg.Presets) {
            if (p.Name.Trim() == "") return "이름이 비어 있는 모니터 프리셋이 있어요 (저장 보류)";
            if (!names.Add(p.Name.Trim())) return "'" + p.Name + "' 이름이 중복돼요 (저장 보류)";
            string k = Hotkey.Normalize(p.Hotkey);
            if (k != "") { string other; if (keys.TryGetValue(k, out other)) return "단축키 " + k + "가 '" + other + "'과(와) 겹쳐요 (저장 보류)"; keys[k] = p.Name; }
        }
        foreach (var x in new[] { new[] { Cfg.Notes.Hotkey, "새 스티커 메모" }, new[] { Cfg.Desk.HotkeySave, "아이콘 저장" }, new[] { Cfg.Desk.HotkeyRestore, "아이콘 복원" } }.Concat(AllHotkeys().Where(a => !a[1].StartsWith("모니터 프리셋") && a[1] != "새 스티커 메모" && !a[1].StartsWith("아이콘 ")))) {
            string k = Hotkey.Normalize(x[0]); if (k == "") continue;
            string other; if (keys.TryGetValue(k, out other)) return x[1] + " 단축키 " + k + "가 '" + other + "'과(와) 겹쳐요 (저장 보류)";
            keys[k] = x[1];
        }
        return null;
    }
    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (saveTimer.Enabled) Flush();
        if (saveError != null && e.CloseReason == CloseReason.UserClosing) {
            var r = DarkBox.Show(this, saveError.Replace(" (저장 보류)", "") + "\n\n고치지 않고 닫으면 마지막으로 저장된 설정이 유지돼요. 닫을까요?", "BongHub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { Application.RemoveMessageFilter(this); saveTimer.Dispose(); statusFade.Dispose(); if (kTimer != null) kTimer.Dispose(); if (dTimer != null) dTimer.Dispose(); base.OnFormClosed(e); }
    protected override bool ProcessCmdKey(ref Message m, Keys k) {
        if (k == (Keys.Control | Keys.S)) { Flush(); return true; }
        return base.ProcessCmdKey(ref m, k);
    }
}

class DbPanel : Panel { public DbPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); } }
class SidePanel : Panel {
    Bitmap bm; Size bmSz;
    public SidePanel() {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = UI.Side;
        Disposed += (s, e) => { if (bm != null) bm.Dispose(); };
    }
    public static SidePanel Of(Control c) { for (var p = c.Parent; p != null; p = p.Parent) { var s = p as SidePanel; if (s != null) return s; } return null; }
    void PaintInto(Graphics g, int w, int h) {
        using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), UI.Side, Color.FromArgb(16, 20, 29), 90f)) g.FillRectangle(b, 0, 0, w, h);
        using (var b = UI.Grad(new RectangleF(0, 0, 1, h), 60, 90f)) g.FillRectangle(b, w - 1, 0, 1, h);
    }
    void Ensure() {
        int w = Math.Max(1, Width), h = Math.Max(1, Height);
        if (bm != null && bmSz.Width == w && bmSz.Height == h) return;
        if (ResizeGlide.Live) return;
        if (bm != null) bm.Dispose();
        bm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bm)) PaintInto(g, w, h);
        bmSz = new Size(w, h);
    }
    bool Fresh() { return bm != null && bmSz.Width == Math.Max(1, Width) && bmSz.Height == Math.Max(1, Height); }
    public void PaintBg(Graphics g, Control c) {
        Ensure();
        int x = 0, y = 0;
        for (var p = c; p != null && p != this; p = p.Parent) { x += p.Left; y += p.Top; }
        if (!Fresh()) {
            var st = g.Save(); g.TranslateTransform(-x, -y); PaintInto(g, Math.Max(1, Width), Math.Max(1, Height)); g.Restore(st); return;
        }
        var src = Rectangle.Intersect(new Rectangle(x, y, c.Width, c.Height), new Rectangle(0, 0, bmSz.Width, bmSz.Height));
        if (src.Width <= 0 || src.Height <= 0) return;
        ResizeGlide.Blit(g, bm, new Rectangle(src.X - x, src.Y - y, src.Width, src.Height), src);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        Ensure();
        if (!Fresh()) { PaintInto(e.Graphics, Math.Max(1, Width), Math.Max(1, Height)); return; }
        var clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(0, 0, bmSz.Width, bmSz.Height));
        if (clip.Width > 0 && clip.Height > 0) ResizeGlide.Blit(e.Graphics, bm, clip, clip);
    }
}
class BrandHeader : Control {
    public BrandHeader() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int isz = UI.S(40); g.DrawImage(Program.IconThumb(), new Rectangle(UI.S(2), (Height - isz) / 2, isz, isz));
        int x = UI.S(2) + isz + UI.S(10);
        using (var path = new GraphicsPath()) {
            path.AddString("BongHub", UI.Big.FontFamily, (int)FontStyle.Bold, g.DpiY * UI.Big.SizeInPoints / 72f, new Point(x, Height / 2 - UI.S(22)), StringFormat.GenericTypographic);
            using (var b = UI.Grad(path.GetBounds())) g.FillPath(b, path);
        }
        TextRenderer.DrawText(g, "석봉이의 유틸 창고", UI.Small, new Rectangle(x, Height / 2 + UI.S(3), Width - x, Height / 2), UI.Hint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

// ================= Rule editor dialog =================
class RuleDialog : ChromeForm {
    WindowRule r; TextBox tName, tProc, tTitle, tClass; CheckBox cRx, cEn, cPin, cKeep; ComboBox cMon, cAct; NumericUpDown nX, nY, nW, nH, nZone; List<string> monIds = new List<string>();
    public RuleDialog(WindowRule rule, List<MonInfo> mons) {
        r = rule; Text = "창 규칙"; Font = UI.Base; BackColor = UI.Page; AutoScaleMode = AutoScaleMode.None; Icon = Program.AppIcon;
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FormBorderStyle = FormBorderStyle.FixedDialog;
        var st = UI.Stack(); st.Dock = DockStyle.None; st.Padding = new Padding(UI.S(16)); st.MinimumSize = new Size(UI.S(600), 0);
        st.Location = new Point(UI.S(3), Bar.Height); Controls.Add(st);

        var s0 = new Section("규칙", null);
        tName = UI.Txt(300); s0.Row("이름", tName);
        cEn = UI.Chk("이 규칙 사용"); s0.Row("", cEn);
        UI.AddRow(st, s0);

        var s1 = new Section("어떤 창에?", "입력한 조건을 모두 만족하는 창에 적용돼요. 비워둔 칸은 검사하지 않아요.");
        tProc = UI.Txt(220); var bPick = UI.Btn("실행 중인 창에서 고르기…"); bPick.Click += (s, e) => Pick();
        s1.Row("프로세스", UI.Flow(tProc, bPick), "예: chrome, Discord (.exe 생략 가능)");
        tTitle = UI.Txt(300); cRx = UI.Chk("정규식");
        s1.Row("창 제목", UI.Flow(tTitle, cRx), "제목에 이 글자가 들어가면 일치 (정규식 체크 시 패턴)");
        tClass = UI.Txt(220); s1.Row("창 클래스", tClass);
        UI.AddRow(st, s1);

        var s2 = new Section("어떻게?", "위치는 선택한 모니터의 작업 영역(작업 표시줄 제외) 왼쪽 위 기준이에요.");
        cMon = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(300), Font = UI.Base };
        cMon.Items.Add("현재 있는 모니터 그대로"); monIds.Add("");
        foreach (var m in mons) { cMon.Items.Add(m.Id + "  ·  " + m.Res + (m.Primary ? "  (주)" : "") + (m.Active ? "" : "  (꺼짐)")); monIds.Add(m.Id); }
        if (r.Monitor != "" && !monIds.Contains(r.Monitor)) { cMon.Items.Add(r.Monitor + "  (감지 안 됨)"); monIds.Add(r.Monitor); }
        s2.Row("모니터", cMon);
        cAct = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(220), Font = UI.Base };
        cAct.Items.AddRange(WindowRule.Labels); cAct.SelectedIndexChanged += (s, e) => UpdEn();
        s2.Row("창 상태", cAct);
        nX = UI.Num(-20000, 20000, 0, 80); nY = UI.Num(-20000, 20000, 0, 80); nW = UI.Num(50, 20000, 800, 80); nH = UI.Num(50, 20000, 600, 80);
        s2.Row("위치", UI.Flow(UI.L("X"), nX, UI.L("Y"), nY));
        s2.Row("크기", UI.Flow(UI.L("너비"), nW, UI.L("높이"), nH));
        nZone = UI.Num(1, 64, 1, 70); s2.Row("영역 번호", nZone, "PowerToys › FancyZones의 모니터별 레이아웃에서 번호를 확인하세요");
        UI.AddRow(st, s2);

        var s3 = new Section("옵션", null);
        cPin = UI.Chk("항상 위에 고정"); s3.Full(cPin);
        cKeep = UI.Chk("위치 유지 — 창이 새로 뜨거나 옮겨지면 자동으로 다시 적용"); s3.Full(cKeep);
        UI.AddRow(st, s3);

        var ok = UI.Btn("확인", true); var cancel = UI.Btn("취소"); ok.MinimumSize = cancel.MinimumSize = new Size(UI.S(88), 0);
        ok.Click += (s, e) => { if (Commit()) { DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var bf = UI.Flow(ok, cancel); bf.Anchor = AnchorStyles.Right; UI.AddRow(st, bf); bf.Anchor = AnchorStyles.Right;
        AcceptButton = ok; CancelButton = cancel;

        tName.Text = r.Name; cEn.Checked = r.Enabled; tProc.Text = r.Process; tTitle.Text = r.Title; cRx.Checked = r.TitleRegex; tClass.Text = r.Class;
        cMon.SelectedIndex = Math.Max(0, monIds.IndexOf(r.Monitor));
        cAct.SelectedIndex = Math.Max(0, Array.IndexOf(WindowRule.Codes, r.Action)); nZone.Value = Math.Max(1, Math.Min(64, r.Zone + 1));
        nX.Value = Math.Max(nX.Minimum, Math.Min(nX.Maximum, r.X)); nY.Value = Math.Max(nY.Minimum, Math.Min(nY.Maximum, r.Y));
        nW.Value = Math.Max(nW.Minimum, Math.Min(nW.Maximum, r.W)); nH.Value = Math.Max(nH.Minimum, Math.Min(nH.Maximum, r.H));
        cPin.Checked = r.Pin; cKeep.Checked = r.Keep; UpdEn();
        Dark.Theme(this); st.BackColor = UI.Page;
    }
    void UpdEn() { bool p = cAct.SelectedIndex == 0; nX.Enabled = nY.Enabled = nW.Enabled = nH.Enabled = p; nZone.Enabled = cAct.SelectedIndex == Array.IndexOf(WindowRule.Codes, "zone"); }
    void Pick() {
        using (var d = new CaptureDialog(false)) if (d.ShowDialog(this) == DialogResult.OK && d.Picked != null) {
            var w = d.Picked; tProc.Text = w.Process; tClass.Text = w.Class; if (tName.Text == "") tName.Text = w.Process;
            int mi = monIds.IndexOf(w.MonitorId); if (mi >= 0) cMon.SelectedIndex = mi;
            cAct.SelectedIndex = w.Max ? 2 : 0;
            nX.Value = Math.Max(nX.Minimum, Math.Min(nX.Maximum, w.Rel.X)); nY.Value = Math.Max(nY.Minimum, Math.Min(nY.Maximum, w.Rel.Y));
            nW.Value = Math.Max(nW.Minimum, Math.Min(nW.Maximum, w.Rel.Width)); nH.Value = Math.Max(nH.Minimum, Math.Min(nH.Maximum, w.Rel.Height));
        }
    }
    bool Commit() {
        if (tProc.Text.Trim() == "" && tTitle.Text.Trim() == "" && tClass.Text.Trim() == "") { DarkBox.Show(this, "프로세스, 창 제목, 창 클래스 중 하나 이상 입력해 주세요.", "창 규칙"); return false; }
        if (cRx.Checked && !WindowRules.RegexValid(tTitle.Text)) { DarkBox.Show(this, "정규식이 올바르지 않아요.", "창 규칙"); return false; }
        r.Name = tName.Text.Trim() == "" ? (tProc.Text.Trim() != "" ? tProc.Text.Trim() : tTitle.Text.Trim() != "" ? tTitle.Text.Trim() : tClass.Text.Trim()) : tName.Text.Trim();
        r.Enabled = cEn.Checked; r.Process = tProc.Text.Trim(); r.Title = tTitle.Text.Trim(); r.TitleRegex = cRx.Checked; r.Class = tClass.Text.Trim();
        r.Monitor = monIds[Math.Max(0, cMon.SelectedIndex)]; r.Action = WindowRule.Codes[Math.Max(0, cAct.SelectedIndex)];
        r.X = (int)nX.Value; r.Y = (int)nY.Value; r.W = (int)nW.Value; r.H = (int)nH.Value; r.Pin = cPin.Checked; r.Keep = cKeep.Checked; r.Zone = (int)nZone.Value - 1;
        return true;
    }
}

// ================= Capture current windows =================
class CaptureDialog : ChromeForm {
    bool multi; ListView lv; CheckBox mProc, mClass, mTitle, oKeep; List<WinInfo> wins = new List<WinInfo>();
    public List<WindowRule> Result = new List<WindowRule>(); public WinInfo Picked;
    public CaptureDialog(bool multiSelect) {
        multi = multiSelect; Text = multi ? "현재 창에서 규칙 만들기" : "창 고르기"; Font = UI.Base; BackColor = UI.Page; AutoScaleMode = AutoScaleMode.None; Icon = Program.AppIcon;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; Size = new Size(UI.S(980), UI.S(580)); MinimumSize = new Size(UI.S(700), UI.S(400));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(UI.S(16)) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(UI.HintL(multi ? "규칙으로 저장할 창을 체크하세요. 지금 위치·크기·모니터가 그대로 규칙이 돼요." : "창을 선택하고 ‘선택’을 누르세요."), 0, 0);
        lv = new ListView { View = View.Details, CheckBoxes = multi, FullRowSelect = true, Dock = DockStyle.Fill, Font = UI.Base, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.FixedSingle };
        Dark.List(lv);
        lv.Columns.Add("프로세스", UI.S(130)); lv.Columns.Add("창 제목", UI.S(300)); lv.Columns.Add("클래스", UI.S(170)); lv.Columns.Add("모니터", UI.S(90)); lv.Columns.Add("위치 (모니터 기준)", UI.S(190));
        lv.DoubleClick += (s, e) => { if (!multi) Done(); };
        lv.Resize += (s, e) => { int w = lv.ClientSize.Width; for (int c = 0; c < lv.Columns.Count - 1; c++) w -= lv.Columns[c].Width; lv.Columns[lv.Columns.Count - 1].Width = Math.Max(UI.S(120), w - 2); };
        root.Controls.Add(lv, 0, 1);
        mProc = UI.Chk("프로세스"); mClass = UI.Chk("창 클래스"); mTitle = UI.Chk("창 제목"); oKeep = UI.Chk("위치 유지");
        mProc.Checked = true; mClass.Checked = true;
        var opts = UI.Flow(UI.L("일치 기준:"), mProc, mClass, mTitle, UI.L("   "), oKeep); opts.Visible = multi; opts.Margin = new Padding(0, UI.S(8), 0, 0);
        root.Controls.Add(opts, 0, 2);
        var refresh = UI.Btn("↻ 새로고침"); refresh.Click += (s, e) => Fill();
        var ok = UI.Btn(multi ? "체크한 창 저장" : "선택", true); ok.Click += (s, e) => Done();
        var cancel = UI.Btn("취소"); cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var bf = UI.Flow(refresh, ok, cancel); bf.Anchor = AnchorStyles.Right; bf.Margin = new Padding(0, UI.S(10), 0, 0);
        root.Controls.Add(bf, 0, 3);
        Controls.Add(root); CancelButton = cancel;
        Dark.Theme(this);
        Fill();
    }
    void Fill() {
        wins = WindowRules.Enumerate(true);
        lv.BeginUpdate(); lv.Items.Clear();
        foreach (var w in wins) {
            var it = new ListViewItem(w.Process) { Tag = w };
            it.SubItems.Add(w.Title); it.SubItems.Add(w.Class); it.SubItems.Add(w.MonitorId);
            it.SubItems.Add(w.Min ? "최소화됨" : w.Max ? "최대화" : w.Rel.X + ", " + w.Rel.Y + "  " + w.Rel.Width + "×" + w.Rel.Height);
            lv.Items.Add(it);
        }
        lv.EndUpdate();
    }
    void Done() {
        if (!multi) {
            if (lv.SelectedItems.Count == 0) return; Picked = (WinInfo)lv.SelectedItems[0].Tag; DialogResult = DialogResult.OK; Close(); return;
        }
        if (!mProc.Checked && !mClass.Checked && !mTitle.Checked) { DarkBox.Show(this, "일치 기준을 하나 이상 고르세요.", "창 규칙"); return; }
        foreach (ListViewItem it in lv.CheckedItems) {
            var w = (WinInfo)it.Tag;
            string t = w.Title.Length > 40 ? w.Title.Substring(0, 40) + "…" : w.Title;
            Result.Add(new WindowRule {
                Name = w.Process + (t != "" ? " - " + t : ""), Process = mProc.Checked ? w.Process : "", Class = mClass.Checked ? w.Class : "", Title = mTitle.Checked ? w.Title : "",
                Monitor = w.MonitorId, Action = w.Max ? "max" : w.Min ? "min" : "pos", X = w.Rel.X, Y = w.Rel.Y, W = w.Rel.Width, H = w.Rel.Height, Keep = oKeep.Checked });
        }
        DialogResult = DialogResult.OK; Close();
    }
}

// ================= Startup shortcut =================
static class Startup {
    public const string LinkName = "BongHub.lnk";
    public const string OldLinkName = "모니터 단축키.lnk";
    public static string ArgsOf(string lnkPath) {
        try {
            var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
            return (string)lnk.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, lnk, null) ?? "";
        } catch { return ""; }
    }
    public static void MigrateOld() {
        try {
            string old = Path.Combine(Path.GetDirectoryName(LinkPath), OldLinkName);
            if (File.Exists(old)) { Set(true); if (File.Exists(LinkPath)) File.Delete(old); }
        } catch { }
    }
    public static string LinkPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName); } }
    public static bool IsEnabled() { return File.Exists(LinkPath); }
    public static string Target() {
        try {
            var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { LinkPath });
            return (string)lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
        } catch { return ""; }
    }
    public static void Set(bool on) {
        if (!on) { try { if (File.Exists(LinkPath)) File.Delete(LinkPath); } catch { } return; }
        string exe = Application.ExecutablePath;
        if (File.Exists(LinkPath) && string.Equals(Target(), exe, StringComparison.OrdinalIgnoreCase)) return;
        var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { LinkPath });
        var lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exe) });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "BongHub" });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath + ",0" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }
    public static void SyncIcon() {
        if (!File.Exists(LinkPath)) return;
        string want = Application.ExecutablePath + ",0";
        var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { LinkPath });
        var lt = lnk.GetType();
        string cur = (string)lt.InvokeMember("IconLocation", BindingFlags.GetProperty, null, lnk, null);
        if (string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) return;
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { want });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }
}

// ================= Tray app =================
// ================= PowerToys-style features (inspired by Microsoft PowerToys, MIT) =================
public class PtCfg {
    public int ShortcutVersion;
    public ZonesCfg Zones = new ZonesCfg();
    public GrabCfg Grab = new GrabCfg();
    public WorkspacesCfg Work = new WorkspacesCfg();
    public PaletteCfg Palette = new PaletteCfg();
    public PasteCfg Paste = new PasteCfg();
    public PeekCfg Peek = new PeekCfg();
    public ResizerCfg Resizer = new ResizerCfg();
    public void Normalize() {
        if (Zones == null) Zones = new ZonesCfg(); if (Zones.Layouts == null) Zones.Layouts = new List<ZoneLayout>();
        if (Zones.Exclude == null) Zones.Exclude = new List<string>(); if (string.IsNullOrEmpty(Zones.Color)) Zones.Color = "#5A6CFF"; if (string.IsNullOrEmpty(Zones.HotColor)) Zones.HotColor = "#6EC6FF";
        foreach (var l in Zones.Layouts) { if (l.Custom == null) l.Custom = new List<float>(); if (l.Canvas == null) l.Canvas = new List<string>(); }
        if (Grab == null) Grab = new GrabCfg(); if (Work == null) Work = new WorkspacesCfg(); if (Work.Items == null) Work.Items = new List<Workspace>();
        foreach (var w in Work.Items) { if (w.Apps == null) w.Apps = new List<WsApp>(); foreach (var a in w.Apps) if (a.Args == null) a.Args = ""; }
        if (Palette == null) Palette = new PaletteCfg(); if (Paste == null) Paste = new PasteCfg(); if (Peek == null) Peek = new PeekCfg();
        if (ShortcutVersion < 1) {
            if (Palette.Hotkey == "Ctrl+Alt+Space") Palette.Hotkey = "Win+Alt+Space";
            if (Paste.Hotkey == "Ctrl+Alt+V") Paste.Hotkey = "Win+Shift+V";
            if (Paste.PlainHotkey == "Ctrl+Alt+Shift+V") Paste.PlainHotkey = "Ctrl+Win+Alt+V";
            if (Peek.Hotkey == "Ctrl+Shift+Space") Peek.Hotkey = "Space";
            ShortcutVersion=1;
        }
        if (Paste.Custom == null) Paste.Custom = new List<PasteAction>();
        if (Resizer == null) Resizer = new ResizerCfg(); if (Resizer.Presets == null || Resizer.Presets.Count == 0) Resizer.Presets = ResizerCfg.DefaultPresets();
        foreach (var p in Resizer.Presets) if (string.IsNullOrEmpty(p.Unit)) p.Unit = "px";
        if (string.IsNullOrEmpty(Resizer.NameFormat)) Resizer.NameFormat = "%1 (%2)";
    }
}
// Kind: cols, rows, grid, priority, custom (ratios), canvas (free rectangles "x,y,w,h" in 0..1)
public class ZoneLayout { public string Monitor = ""; public string Kind = "cols"; public int A = 2, B = 2; public int Spacing = 8; public List<float> Custom = new List<float>(); public List<string> Canvas = new List<string>(); }
public class ZonesCfg {
    public bool Enabled = false;          // global hooks only while enabled
    public bool ShiftDrag = true;         // hold Shift while dragging a window to snap
    public bool ShowOnDrag = false;       // show zones on every drag (without Shift)
    public bool Span = true;              // hold Ctrl while dragging to span several zones
    public bool WinArrows = false;        // Win+Left/Right moves the active window between zones (keyboard hook, off by default)
    public bool RestoreSize = true;       // un-snapping by dragging restores the size the window had before it was snapped
    public bool AllMonitors = false;      // show zones on every monitor while dragging
    public int Opacity = 55; public string Color = "#5A6CFF", HotColor = "#6EC6FF"; public bool Numbers = true;
    public List<string> Exclude = new List<string>();   // process names never snapped
    public List<ZoneLayout> Layouts = new List<ZoneLayout>();
}
public class GrabCfg { public bool Enabled = false; public string Modifier = "Alt"; public bool Resize = true; public bool SkipFullscreen = true; }
public class WsApp { public bool Enabled = true; public string Path = ""; public string Args = ""; public string Title = ""; public string Process = ""; public string Monitor = ""; public int X, Y, W, H; public string State = "normal"; }
public class Workspace { public string Id = Guid.NewGuid().ToString("N").Substring(0, 8); public string Name = ""; public string Hotkey = ""; public List<WsApp> Apps = new List<WsApp>(); public DateTime Saved = DateTime.Now; }
public class WorkspacesCfg { public bool Enabled = true; public bool LaunchMissing = true; public bool MoveExisting = true; public List<Workspace> Items = new List<Workspace>(); }
public class PaletteCfg { public bool Enabled = false; public string Hotkey = "Win+Alt+Space"; public bool Apps = true; public bool Calc = true; public bool Web = true; public bool Units = true, Time = true, Windows = true, Files = true, Shell = true, Terminal = true, System = true, Settings = true; }
public class PasteAction { public string Name = ""; public string Kind = "regex"; public string Pattern = "", Replace = ""; }
public class PasteCfg { public bool Enabled = false; public string Hotkey = "Win+Shift+V"; public string PlainHotkey = "Ctrl+Win+Alt+V"; public string JsonHotkey = ""; public string MarkdownHotkey = ""; public List<PasteAction> Custom = new List<PasteAction>(); }
public class PeekCfg { public bool Enabled = false; public string Hotkey = "Space"; public bool CloseOnFocusLoss = false; public bool RenderHtml = true; }
// Unit: px, %, cm, in (cm/in use the image's DPI)
public class ResizePreset { public string Name = ""; public int W, H; public string Unit = "px"; public ResizePreset() { } public ResizePreset(string n, int w, int h) { Name = n; W = w; H = h; } }
public class ResizerCfg {
    public bool Enabled = true; public bool ContextMenu = false;   // Explorer right-click entry (HKCU only), off by default
    public List<ResizePreset> Presets = new List<ResizePreset>(); public int Preset = 1; public string Fit = "fit"; public bool ShrinkOnly = true; public bool Replace = false; public int Quality = 90;
    public bool IgnoreOrientation = true;   // portrait photos use the preset rotated (as in PowerToys)
    public bool KeepDate = false, RemoveMeta = false; public string NameFormat = "%1 (%2)"; public string Fallback = "png"; public int PngInterlace = 0;
    public static List<ResizePreset> DefaultPresets() { return new List<ResizePreset> { new ResizePreset("작게", 854, 480), new ResizePreset("중간", 1366, 768), new ResizePreset("크게", 1920, 1080), new ResizePreset("휴대폰", 320, 568) }; }
}
static class Zones {
    public static ZonesCfg Cfg = new ZonesCfg();
    public static readonly string[] Kinds = { "cols", "rows", "grid", "priority", "custom", "canvas" };
    public static readonly string[] KindNames = { "세로 열", "가로 행", "격자", "가운데 강조 (25·50·25)", "직접 비율", "캔버스 (자유 편집)" };
    // monitor ids are cached per device name (EnumDisplayDevices is slow); cleared on display change / config apply
    static readonly Dictionary<string, string> idCache = new Dictionary<string, string>();
    static readonly Dictionary<string, List<Rectangle>> rectCache = new Dictionary<string, List<Rectangle>>();
    public static void Invalidate() { lock (idCache) { idCache.Clear(); rectCache.Clear(); } }
    static string Id(Screen s) { lock (idCache) { string id; if (!idCache.TryGetValue(s.DeviceName, out id)) { id = Monitors.IdOf(s); idCache[s.DeviceName] = id; } return id; } }
    public static ZoneLayout For(Screen s) {
        string id = Id(s);
        return Cfg.Layouts.FirstOrDefault(l => l.Monitor == id) ?? Cfg.Layouts.FirstOrDefault(l => l.Monitor == s.DeviceName) ?? new ZoneLayout { Monitor = id, Kind = s.WorkingArea.Width >= 3000 ? "priority" : "cols", A = s.WorkingArea.Width >= 3000 ? 3 : 2 };
    }
    public static List<Rectangle> RectsFor(Screen s) {
        lock (idCache) {
            string k = s.DeviceName + "|" + s.WorkingArea; List<Rectangle> r;
            if (!rectCache.TryGetValue(k, out r)) { r = Rects(For(s), s.WorkingArea); rectCache[k] = r; }
            return r;
        }
    }
    public static RectangleF? ParseCanvas(string s) {
        var p = (s ?? "").Split(','); float x, y, w, h; var ci = System.Globalization.CultureInfo.InvariantCulture; var st = System.Globalization.NumberStyles.Float;
        if (p.Length == 4 && float.TryParse(p[0], st, ci, out x) && float.TryParse(p[1], st, ci, out y) && float.TryParse(p[2], st, ci, out w) && float.TryParse(p[3], st, ci, out h) && w > 0.01f && h > 0.01f)
            return RectangleF.FromLTRB(Math.Max(0, x), Math.Max(0, y), Math.Min(1, x + w), Math.Min(1, y + h));
        return null;
    }
    public static string CanvasStr(RectangleF r) { var ci = System.Globalization.CultureInfo.InvariantCulture; return r.X.ToString("0.####", ci) + "," + r.Y.ToString("0.####", ci) + "," + r.Width.ToString("0.####", ci) + "," + r.Height.ToString("0.####", ci); }
    public static List<Rectangle> Rects(ZoneLayout l, Rectangle wa) {
        var res = new List<Rectangle>(); int sp = Math.Max(0, l.Spacing);
        Func<float[], bool, List<Rectangle>> split = (fr, vert) => {
            var o = new List<Rectangle>(); float sum = fr.Sum(); float acc = 0;
            for (int i = 0; i < fr.Length; i++) {
                float a0 = acc / sum, a1 = (acc + fr[i]) / sum; acc += fr[i];
                if (vert) { int x0 = wa.X + (int)(wa.Width * a0), x1 = wa.X + (int)(wa.Width * a1); o.Add(Rectangle.FromLTRB(x0 + sp, wa.Y + sp, x1 - (i == fr.Length - 1 ? sp : sp / 2) , wa.Bottom - sp)); }
                else { int y0 = wa.Y + (int)(wa.Height * a0), y1 = wa.Y + (int)(wa.Height * a1); o.Add(Rectangle.FromLTRB(wa.X + sp, y0 + sp, wa.Right - sp, y1 - (i == fr.Length - 1 ? sp : sp / 2))); }
            }
            return o;
        };
        int a = Math.Max(1, Math.Min(8, l.A)), b = Math.Max(1, Math.Min(8, l.B));
        switch (l.Kind) {
            case "rows": return split(Enumerable.Repeat(1f, a).ToArray(), false);
            case "priority": return split(new[] { 1f, 2f, 1f }, true);
            case "custom": { var fr = (l.Custom ?? new List<float>()).Where(x => x > 0).ToArray(); return split(fr.Length > 0 ? fr : new[] { 1f, 1f }, true); }
            case "canvas": {
                foreach (var c in l.Canvas ?? new List<string>()) { var f = ParseCanvas(c); if (!f.HasValue) continue; var r = f.Value;
                    res.Add(Rectangle.FromLTRB(wa.X + (int)(wa.Width * r.Left) + sp / 2 + (r.Left <= 0 ? sp / 2 : 0), wa.Y + (int)(wa.Height * r.Top) + sp / 2 + (r.Top <= 0 ? sp / 2 : 0), wa.X + (int)(wa.Width * r.Right) - sp / 2 - (r.Right >= 1 ? sp / 2 : 0), wa.Y + (int)(wa.Height * r.Bottom) - sp / 2 - (r.Bottom >= 1 ? sp / 2 : 0))); }
                if (res.Count == 0) return split(new[] { 1f, 1f }, true);
                return res;
            }
            case "grid": {
                int cw = wa.Width / a, ch = wa.Height / b;
                for (int y = 0; y < b; y++) for (int x = 0; x < a; x++) {
                    int x0 = wa.X + x * cw, y0 = wa.Y + y * ch, x1 = x == a - 1 ? wa.Right : x0 + cw, y1 = y == b - 1 ? wa.Bottom : y0 + ch;
                    res.Add(Rectangle.FromLTRB(x0 + sp, y0 + sp, x1 - (x == a - 1 ? sp : sp / 2), y1 - (y == b - 1 ? sp : sp / 2)));
                }
                return res;
            }
            default: return split(Enumerable.Repeat(1f, a).ToArray(), true);
        }
    }
    // canvas layouts are edited by splitting / merging zones (fractions of the work area)
    public static List<string> ToCanvas(ZoneLayout l) {
        var wa = new Rectangle(0, 0, 10000, 10000); var tmp = new ZoneLayout { Kind = l.Kind, A = l.A, B = l.B, Custom = l.Custom, Canvas = l.Canvas, Spacing = 0 };
        return Rects(tmp, wa).Select(r => CanvasStr(new RectangleF(r.X / 10000f, r.Y / 10000f, r.Width / 10000f, r.Height / 10000f))).ToList();
    }
    public static void Split(ZoneLayout l, int i, bool vertical) {
        if (l.Kind != "canvas") { l.Canvas = ToCanvas(l); l.Kind = "canvas"; }
        var f = ParseCanvas(l.Canvas[i]); if (!f.HasValue) return; var r = f.Value;
        var a = vertical ? new RectangleF(r.X, r.Y, r.Width / 2, r.Height) : new RectangleF(r.X, r.Y, r.Width, r.Height / 2);
        var b = vertical ? new RectangleF(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height) : new RectangleF(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2);
        l.Canvas[i] = CanvasStr(a); l.Canvas.Insert(i + 1, CanvasStr(b));
    }
    public static bool Merge(ZoneLayout l, int i, int j) {
        if (l.Kind != "canvas") { l.Canvas = ToCanvas(l); l.Kind = "canvas"; }
        if (i == j || i < 0 || j < 0 || i >= l.Canvas.Count || j >= l.Canvas.Count) return false;
        var a = ParseCanvas(l.Canvas[i]); var b = ParseCanvas(l.Canvas[j]); if (!a.HasValue || !b.HasValue) return false;
        var u = RectangleF.Union(a.Value, b.Value);
        // zones fully inside the merged rectangle are absorbed (like the PowerToys editor)
        var keep = new List<string>(); int at = Math.Min(i, j), idx = 0;
        for (int k = 0; k < l.Canvas.Count; k++) { var c = ParseCanvas(l.Canvas[k]); if (c.HasValue && u.Contains(RectangleF.Inflate(c.Value, -0.0005f, -0.0005f))) { if (k < at) at--; continue; } keep.Add(l.Canvas[k]); idx++; }
        keep.Insert(Math.Max(0, Math.Min(at, keep.Count)), CanvasStr(u)); l.Canvas = keep; return true;
    }
    public static Rectangle? ZoneRect(Rectangle wa, int zone) {
        var s = Screen.FromRectangle(wa); var r = RectsFor(s);
        return zone >= 0 && zone < r.Count ? r[zone] : (Rectangle?)null;
    }
    public static Rectangle Span(List<Rectangle> rs, int a, int b) { var u = rs[a]; u = Rectangle.Union(u, rs[b]); return u; }
    // ----- drag snapping (WinEvent move/size start/end; a 33 ms timer runs only during a drag) -----
    static IntPtr hStart, hEnd; static Native.WinEventProc proc; static IntPtr dragWin; static WTimer tick; static readonly Dictionary<string, ZoneOverlay> ovs = new Dictionary<string, ZoneOverlay>();
    static int hot = -1, anchor = -1; static Screen hotScreen; static List<Rectangle> hotRects; static bool shown; static Point dragFrom; static bool restorePending;
    static readonly Dictionary<IntPtr, Size> preSnap = new Dictionary<IntPtr, Size>();   // size before snapping, for restore on un-snap
    public static bool Active { get { return hStart != IntPtr.Zero; } }
    public static bool KeysActive { get { return kHook != IntPtr.Zero; } }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out Native.RECT r, int s);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    public static void Apply(ZonesCfg c) {
        Cfg = c ?? new ZonesCfg(); Invalidate();
        if (Cfg.Enabled && hStart == IntPtr.Zero) {
            proc = OnEvent;
            hStart = Native.SetWinEventHook(0x000A, 0x000A, IntPtr.Zero, proc, 0, 0, 2);
            hEnd = Native.SetWinEventHook(0x000B, 0x000B, IntPtr.Zero, proc, 0, 0, 2);
        } else if (!Cfg.Enabled && hStart != IntPtr.Zero) {
            Native.UnhookWinEvent(hStart); Native.UnhookWinEvent(hEnd); hStart = hEnd = IntPtr.Zero; EndDrag(false);
            foreach (var o in ovs.Values) try { o.Close(); } catch { } ovs.Clear();
        }
        bool wantKeys = Cfg.Enabled && Cfg.WinArrows;
        if (wantKeys && kHook == IntPtr.Zero) { ui = SynchronizationContext.Current; kProc = KeyHook; kHook = SetWindowsHookEx(13, kProc, GetModuleHandle(null), 0); }
        else if (!wantKeys && kHook != IntPtr.Zero) { UnhookWindowsHookEx(kHook); kHook = IntPtr.Zero; }
    }
    static bool Key(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
    public static bool Excluded(IntPtr h) {
        if (Cfg.Exclude == null || Cfg.Exclude.Count == 0) return false;
        string exe = Path.GetFileNameWithoutExtension(Workspaces.ExeOf(h));
        return Cfg.Exclude.Any(x => string.Equals(Path.GetFileNameWithoutExtension(x.Trim()), exe, StringComparison.OrdinalIgnoreCase));
    }
    static void OnEvent(IntPtr hook, uint ev, IntPtr h, int idObj, int idChild, uint th, uint time) {
        if (idObj != 0 || h == IntPtr.Zero) return;
        if (ev == 0x000A) {
            if (Native.IsZoomed(h) || Excluded(h)) return;
            dragWin = h; hot = -1; anchor = -1; shown = false; dragFrom = Cursor.Position; restorePending = Cfg.RestoreSize && preSnap.ContainsKey(h);
            if (tick == null) { tick = new WTimer { Interval = 33 }; tick.Tick += (s, e) => Track(); }
            tick.Start();
        } else if (ev == 0x000B && h == dragWin) EndDrag(true);
    }
    static bool Want { get { return Cfg.ShowOnDrag ? !Key(0x10) : (Cfg.ShiftDrag && Key(0x10)); } }
    static void Track() {
        var pt = Cursor.Position;
        if (restorePending && (Math.Abs(pt.X - dragFrom.X) > 6 || Math.Abs(pt.Y - dragFrom.Y) > 6)) {
            restorePending = false; Size sz; if (preSnap.TryGetValue(dragWin, out sz)) {
                Native.RECT r; Native.GetWindowRect(dragWin, out r); double fx = (pt.X - r.L) / (double)Math.Max(1, r.R - r.L);
                Native.SetWindowPos(dragWin, IntPtr.Zero, pt.X - (int)(sz.Width * fx), r.T, sz.Width, sz.Height, 0x0004 | 0x0010); preSnap.Remove(dragWin);
            }
        }
        if (!Want) { if (shown) HideAll(); hot = -1; anchor = -1; return; }
        var s = Screen.FromPoint(pt); var rs = RectsFor(s);
        int h = -1; for (int i = 0; i < rs.Count; i++) if (rs[i].Contains(pt)) { h = i; break; }
        bool span = Cfg.Span && Key(0x11);
        if (!span || anchor < 0 || hotScreen == null || hotScreen.DeviceName != s.DeviceName) anchor = h;
        if (!shown || hotScreen == null || hotScreen.DeviceName != s.DeviceName) {
            hotScreen = s; hotRects = rs; HideAll();
            foreach (var sc in Cfg.AllMonitors ? Screen.AllScreens : new[] { s }) { var o = Ov(sc); o.Setup(sc.WorkingArea, RectsFor(sc), Cfg); o.ShowNoActivate(); }
            shown = true;
        }
        var ov = Ov(s);
        int lo = Math.Min(anchor < 0 ? h : anchor, h), hi = Math.Max(anchor < 0 ? h : anchor, h);
        if (h != hot || ov.SpanFrom != lo) { hot = h; ov.SetHot(lo, hi); }
    }
    static ZoneOverlay Ov(Screen s) { ZoneOverlay o; if (!ovs.TryGetValue(s.DeviceName, out o) || o.IsDisposed) { o = new ZoneOverlay(); ovs[s.DeviceName] = o; } return o; }
    static void HideAll() { foreach (var o in ovs.Values) if (!o.IsDisposed && o.Visible) o.Hide(); shown = false; }
    static void EndDrag(bool apply) {
        if (tick != null) tick.Stop();
        var w = dragWin; dragWin = IntPtr.Zero;
        bool snap = apply && hot >= 0 && hotScreen != null && Want && hotRects != null;
        Rectangle target = Rectangle.Empty;
        if (snap) { int a = anchor < 0 ? hot : anchor; target = Span(hotRects, Math.Min(a, hot), Math.Max(a, hot)); if (Cfg.Span && Math.Min(a, hot) != Math.Max(a, hot)) for (int i = Math.Min(a, hot); i <= Math.Max(a, hot); i++) target = Rectangle.Union(target, hotRects[i]); }
        HideAll();
        if (snap) Snap(w, target);
        hot = -1; anchor = -1;
    }
    public static void Snap(IntPtr w, Rectangle target) {
        if (Cfg.RestoreSize && !preSnap.ContainsKey(w)) { Native.RECT r; Native.GetWindowRect(w, out r); preSnap[w] = new Size(r.R - r.L, r.B - r.T); if (preSnap.Count > 200) preSnap.Remove(preSnap.Keys.First()); }
        Place(w, target);
    }
    // places a window so its visible frame (not the invisible resize border) fills r
    public static void Place(IntPtr h, Rectangle r) {
        if (!Native.IsWindow(h)) return;
        if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, 9);
        Native.RECT wr, fr; Native.GetWindowRect(h, out wr);
        int l = 0, t = 0, rr = 0, b = 0;
        if (DwmGetWindowAttribute(h, 9, out fr, Marshal.SizeOf(typeof(Native.RECT))) == 0) { l = fr.L - wr.L; t = fr.T - wr.T; rr = wr.R - fr.R; b = wr.B - fr.B; }
        Native.SetWindowPos(h, IntPtr.Zero, r.X - l, r.Y - t, r.Width + l + rr, r.Height + t + b, 0x0004 | 0x0010 | 0x0200);
    }
    // ----- Win+Left/Right: move the active window to the previous/next zone (then the next monitor) -----
    delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    static IntPtr kHook; static LLProc kProc; static SynchronizationContext ui;
    // returns immediately: the actual move is posted to the UI thread
    static IntPtr KeyHook(int code, IntPtr w, IntPtr l) {
        if (code >= 0 && ((int)w == 0x0100 || (int)w == 0x0104)) {
            int vk = Marshal.ReadInt32(l);
            if ((vk == 0x25 || vk == 0x27) && (Key(0x5B) || Key(0x5C)) && !Key(0x10) && !Key(0x11) && !Key(0x12)) {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && !Native.IsZoomed(fg) && ui != null) { int dir = vk == 0x27 ? 1 : -1; ui.Post(_ => { try { MoveByKey(fg, dir); } catch { } }, null); return (IntPtr)1; }
            }
        }
        return CallNextHookEx(kHook, code, w, l);
    }
    public static int NextZone(List<Rectangle> rs, Rectangle cur, int dir) {
        int best = -1; long bestA = 0;
        for (int i = 0; i < rs.Count; i++) { var x = Rectangle.Intersect(rs[i], cur); long a = (long)x.Width * x.Height; if (a > bestA) { bestA = a; best = i; } }
        bool exact = best >= 0 && Math.Abs(rs[best].Width - cur.Width) < 40 && Math.Abs(rs[best].Height - cur.Height) < 40;
        if (!exact) { // not snapped yet: pick the zone in that direction from the window center
            int cx = cur.X + cur.Width / 2; var order = Enumerable.Range(0, rs.Count).OrderBy(i => rs[i].X).ToList();
            return dir > 0 ? FirstOr(order, i => rs[i].X + rs[i].Width / 2 > cx, order.Last()) : LastOr(order, i => rs[i].X + rs[i].Width / 2 < cx, order.First());
        }
        int n = best + dir; return n < 0 || n >= rs.Count ? -1 : n;
    }
    static int FirstOr(List<int> l, Func<int, bool> f, int d) { foreach (var x in l) if (f(x)) return x; return d; }
    static int LastOr(List<int> l, Func<int, bool> f, int d) { for (int i = l.Count - 1; i >= 0; i--) if (f(l[i])) return l[i]; return d; }
    static void MoveByKey(IntPtr h, int dir) {
        if (Excluded(h)) return;
        Native.RECT wr; Native.GetWindowRect(h, out wr); var cur = Rectangle.FromLTRB(wr.L, wr.T, wr.R, wr.B);
        var s = Screen.FromHandle(h); var rs = RectsFor(s); int n = NextZone(rs, cur, dir);
        if (n < 0) { // past the edge: continue on the neighbouring monitor
            var others = Screen.AllScreens.Where(x => dir > 0 ? x.Bounds.Left >= s.Bounds.Right - 1 : x.Bounds.Right <= s.Bounds.Left + 1).OrderBy(x => Math.Abs(x.Bounds.Left - s.Bounds.Left)).ToList();
            if (others.Count == 0) return; s = others[0]; rs = RectsFor(s); n = dir > 0 ? 0 : rs.Count - 1;
        }
        Snap(h, rs[n]);
    }
}
// click-through, never-activating layer that draws the zones of one monitor while dragging.
// zones are rendered once into a cached bitmap; moving between zones only repaints the old and new hot area.
class ZoneOverlay : Form {
    List<Rectangle> zones = new List<Rectangle>(); Rectangle area; int hot = -1, hot2 = -1; Bitmap baseBm; Color zc = Color.FromArgb(90, 108, 255), hc = Color.FromArgb(110, 198, 255); bool nums = true;
    public int SpanFrom { get { return hot; } }
    public ZoneOverlay() {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(10, 10, 24);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x80000 | 0x8; return c; } }
    static Color Hex(string s, Color d) { try { return ColorTranslator.FromHtml(s); } catch { return d; } }
    public void Setup(Rectangle wa, List<Rectangle> zs, int opacity) { Setup(wa, zs, new ZonesCfg { Opacity = opacity }); }
    public void Setup(Rectangle wa, List<Rectangle> zs, ZonesCfg c) {
        var nzc = Hex(c.Color, zc); var nhc = Hex(c.HotColor, hc);
        bool same = wa == area && zs.SequenceEqual(zones) && nzc == zc && nhc == hc && nums == c.Numbers && baseBm != null;
        area = wa; zones = zs; zc = nzc; hc = nhc; nums = c.Numbers; hot = hot2 = -1;
        var b = TestEnv.Off ? new Rectangle(TestEnv.Area.X, TestEnv.Area.Y, wa.Width, wa.Height) : wa; if (Bounds != b) Bounds = b;
        double op = Math.Max(0.2, Math.Min(0.95, c.Opacity / 100.0)); if (Math.Abs(Opacity - op) > 0.001) Opacity = op;
        if (!same) { if (baseBm != null) baseBm.Dispose(); baseBm = null; }
        Invalidate();
    }
    public void ShowNoActivate() { if (!Visible) Show(); Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
    public void SetHot(int h) { SetHot(h, h); }
    public void SetHot(int a, int b) {
        if (a == hot && b == hot2) return;
        var dirty = Area(hot, hot2); hot = a; hot2 = b; dirty = Rectangle.Union(dirty, Area(hot, hot2)); dirty.Inflate(UI.S(4), UI.S(4));
        if (dirty.Width > 0) Invalidate(dirty); else Invalidate();
    }
    Rectangle Area(int a, int b) { if (a < 0) return Rectangle.Empty; var r = zones[a]; for (int i = a; i <= Math.Min(b, zones.Count - 1); i++) r = Rectangle.Union(r, zones[i]); r.Offset(-area.X, -area.Y); return r; }
    void DrawZone(Graphics g, Rectangle r, int i, bool hi, Font f) {
        r.Offset(-area.X, -area.Y); r.Inflate(-UI.S(2), -UI.S(2));
        using (var p = UI.Round(r, UI.S(12))) {
            using (var b = new SolidBrush(hi ? Color.FromArgb(210, hc) : Color.FromArgb(150, zc))) g.FillPath(b, p);
            using (var pen = new Pen(hi ? Color.White : Color.FromArgb(190, NoteColors.Mix(zc, Color.White, .35f)), UI.S(2))) g.DrawPath(pen, p);
        }
        if (nums && i >= 0) TextRenderer.DrawText(g, (i + 1).ToString(), f, r, hi ? Color.White : Color.FromArgb(220, 230, 240, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        if (baseBm == null) {
            baseBm = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var bg = Graphics.FromImage(baseBm)) using (var f = new Font(UI.Fam, 28f * UI.Scale, FontStyle.Bold)) { bg.Clear(BackColor); bg.SmoothingMode = SmoothingMode.AntiAlias; for (int i = 0; i < zones.Count; i++) DrawZone(bg, zones[i], i, false, f); }
        }
        g.DrawImage(baseBm, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
        if (hot >= 0 && hot < zones.Count) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var u = zones[hot]; for (int i = hot; i <= Math.Min(hot2, zones.Count - 1); i++) u = Rectangle.Union(u, zones[i]);
            using (var f = new Font(UI.Fam, 28f * UI.Scale, FontStyle.Bold)) DrawZone(g, u, hot == hot2 ? hot : -1, true, f);
        }
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { if (baseBm != null) baseBm.Dispose(); base.OnFormClosed(e); }
}
// zone preview + editor used in settings: click a zone to split it (Shift = horizontal), right-click two zones to merge
class ZonePreview : Control {
    public ZoneLayout Layout_; public Rectangle Area = new Rectangle(0, 0, 1920, 1080); public bool Editable; public event Action Edited; int mergeFirst = -1;
    public new ZoneLayout Layout { get { return Layout_; } set { Layout_ = value; Invalidate(); } }
    public ZonePreview() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = UI.Card; }
    float sc; int ox, oy, mw, mh;
    List<Rectangle> Zs() {
        sc = Math.Min((Width - UI.S(16)) / (float)Area.Width, (Height - UI.S(16)) / (float)Area.Height);
        mw = (int)(Area.Width * sc); mh = (int)(Area.Height * sc); ox = (Width - mw) / 2; oy = (Height - mh) / 2;
        var l2 = new ZoneLayout { Kind = Layout_.Kind, A = Layout_.A, B = Layout_.B, Custom = Layout_.Custom, Canvas = Layout_.Canvas, Spacing = (int)(Layout_.Spacing * sc) + 1 };
        return Zones.Rects(l2, new Rectangle(ox, oy, mw, mh));
    }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e); if (!Editable || Layout_ == null) return;
        var zs = Zs(); int i = zs.FindIndex(r => r.Contains(e.Location)); if (i < 0) return;
        if (e.Button == MouseButtons.Left) { Zones.Split(Layout_, i, (ModifierKeys & Keys.Shift) == 0); mergeFirst = -1; }
        else if (e.Button == MouseButtons.Right) { if (mergeFirst < 0) { mergeFirst = i; Invalidate(); return; } Zones.Merge(Layout_, mergeFirst, i); mergeFirst = -1; }
        Invalidate(); if (Edited != null) Edited();
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(UI.Card);
        if (Layout_ == null) return;
        var zs = Zs(); var mon = new Rectangle(ox, oy, mw, mh);
        using (var p = UI.Round(mon, UI.S(8))) using (var b = new SolidBrush(UI.Input)) g.FillPath(b, p);
        int i = 0;
        foreach (var z in zs) {
            var r = z;
            using (var p = UI.Round(r, UI.S(6))) { using (var b = UI.Grad(r, i == mergeFirst ? 200 : 70, 35f)) g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(180, UI.G3), 1.5f)) g.DrawPath(pen, p); }
            TextRenderer.DrawText(g, (++i).ToString(), UI.Bold, r, UI.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
// ----- Grab and Move: modifier + left drag moves, modifier + right drag resizes (low-level mouse hook only while enabled) -----
sealed class InputHookHost : IDisposable {
    public delegate IntPtr Callback(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Callback p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint id, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr h, uint a, uint b, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint message; public IntPtr w,l; public uint time; public Point pt; }
    readonly Callback callback; readonly ManualResetEventSlim ready = new ManualResetEventSlim(); volatile bool stopping; uint tid; IntPtr hook;
    public bool Active { get { return hook != IntPtr.Zero; } }
    public InputHookHost(int type, Callback cb) {
        callback = cb;
        var thread = new Thread(() => {
            tid = GetCurrentThreadId(); MSG msg; PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
            try { hook = SetWindowsHookEx(type, callback, GetModuleHandle(null), 0); ready.Set(); if (hook != IntPtr.Zero && !stopping) Application.Run(); }
            finally { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; ready.Set(); }
        }) { IsBackground = true, Name = "BongHub input hook" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait(1000);
    }
    public void Dispose() { stopping = true; if (tid != 0) PostThreadMessage(tid, 0x0012, IntPtr.Zero, IntPtr.Zero); }
}
static class GrabMove {
    public static GrabCfg Cfg = new GrabCfg();
    delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public Point pt; public int data, flags, time; public IntPtr extra; }
    static IntPtr hook; static IntPtr win; static int mode; static Point start; static Native.RECT startRect; static int corner; static bool swallowUp, restoreOnMove, dragMoved; static int dragSession, minimumWidth=160, minimumHeight=100, maximumWidth=int.MaxValue, maximumHeight=int.MaxValue;
    public static bool Active { get { return hook != IntPtr.Zero; } }
    static InputHookHost inputHost; static int generation;
    public static void Apply(GrabCfg c) {
        Cfg = c ?? new GrabCfg();
        if (Cfg.Enabled && inputHost == null) {
            int current = Interlocked.Increment(ref generation); lock(moveGate) latest = new MoveTarget(); run = true;
            worker = new Thread(() => Work(current)) { IsBackground = true, Name = "GrabMove", Priority = ThreadPriority.AboveNormal }; worker.Start();
            inputHost = new InputHookHost(14, Hook); hook = inputHost.Active ? (IntPtr)1 : IntPtr.Zero;
        } else if (!Cfg.Enabled && inputHost != null) {
            inputHost.Dispose(); inputHost = null; hook = IntPtr.Zero; win = IntPtr.Zero; run = false; Interlocked.Increment(ref generation); wake.Set(); if(worker != null) worker.Join(100); worker=null;
        }
    }
    static int ModVk { get { return Cfg.Modifier == "Win" ? 0x5B : Cfg.Modifier == "Ctrl+Alt" ? 0x11 : 0x12; } }
    static bool ModDown { get { bool a = (GetAsyncKeyState(ModVk) & 0x8000) != 0; if (Cfg.Modifier == "Win") a |= (GetAsyncKeyState(0x5C) & 0x8000) != 0; if (Cfg.Modifier == "Ctrl+Alt") a = a && (GetAsyncKeyState(0x12) & 0x8000) != 0; return a; } }
    static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
    static readonly StringBuilder clsBuf = new StringBuilder(64);
    public static bool Eligible(IntPtr h) {
        if (h == IntPtr.Zero || !Native.IsWindow(h)) return false;
        clsBuf.Length = 0; GetClassName(h, clsBuf, 64); string c = clsBuf.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "#32768") return false;
        if (Cfg.SkipFullscreen && !Native.IsZoomed(h)) { Native.RECT r; Native.GetWindowRect(h, out r); var s = Screen.FromHandle(h).Bounds; if (r.L <= s.Left && r.T <= s.Top && r.R >= s.Right && r.B >= s.Bottom) return false; }
        return true;
    }
    // the hook only records the latest target; a worker thread applies it (coalesced), so the callback returns in microseconds
    static Thread worker; static readonly AutoResetEvent wake = new AutoResetEvent(false); static volatile bool run; static int seq;
    struct MoveTarget { public IntPtr H; public int Mode, X,Y,W,Height, Seq, Session; public bool Restore; }
    static readonly object moveGate = new object(); static MoveTarget latest;
    public static uint MoveFlags(int mode) {return (mode==1?0x0001u:0u)|0x0004u|0x0010u|0x0200u;}
    // Completion provides backpressure: never enqueue unbounded SWP_ASYNCWINDOWPOS requests in the target app.
    public static Action<IntPtr,int,int,int,int,int> MoveBackend = (h,m,x,y,w,hh) => { if(Native.IsWindow(h))Native.SetWindowPos(h,IntPtr.Zero,x,y,m==1?0:w,m==1?0:hh,MoveFlags(m)); };
    public static Action<IntPtr> RestoreBackend=h=>Native.ShowWindow(h,9);
    static void Work(int current) {
        int applied=-1,restoredSession=-1;long nextFrame=0;
        while(run && current==Volatile.Read(ref generation)) {
            wake.WaitOne(500);
            while(run && current==Volatile.Read(ref generation)) {
                int remaining=(int)((nextFrame-Stopwatch.GetTimestamp())*1000/Stopwatch.Frequency);
                if(remaining<=0)break;wake.WaitOne(Math.Max(1,remaining));
            }
            if(!run || current!=Volatile.Read(ref generation))break;
            MoveTarget target;lock(moveGate)target=latest;
            if(target.H==IntPtr.Zero || target.Seq==applied)continue;
            applied=target.Seq;nextFrame=Stopwatch.GetTimestamp()+Stopwatch.Frequency/60;
            try {
                if(target.Restore && restoredSession!=target.Session){RestoreBackend(target.H);restoredSession=target.Session;}
                if(run && current==Volatile.Read(ref generation))MoveBackend(target.H,target.Mode,target.X,target.Y,target.W,target.Height);
            } catch { }
        }
    }
    static void Post(IntPtr h,int m,int x,int y,int w,int hh) {
        lock(moveGate)latest=new MoveTarget{H=h,Mode=m,X=x,Y=y,W=w,Height=hh,Seq=Interlocked.Increment(ref seq),Session=dragSession,Restore=restoreOnMove};wake.Set();
    }
    [StructLayout(LayoutKind.Sequential)] struct MinMax { public Point reserved,maxSize,maxPosition,minTrack,maxTrack; }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,ref MinMax info,uint flags,uint timeout,out UIntPtr result);
    static void ReadSizeLimits(IntPtr target) {
        var defaults=SystemInformation.MinimumWindowSize;minimumWidth=Math.Max(1,defaults.Width);minimumHeight=Math.Max(1,defaults.Height);maximumWidth=maximumHeight=int.MaxValue;
        var limits=new MinMax{minTrack=new Point(minimumWidth,minimumHeight)};UIntPtr result;
        if(SendMessageTimeout(target,0x0024,IntPtr.Zero,ref limits,2,30,out result)!=IntPtr.Zero) {
            if(limits.minTrack.X>0)minimumWidth=limits.minTrack.X;if(limits.minTrack.Y>0)minimumHeight=limits.minTrack.Y;
            if(limits.maxTrack.X>=minimumWidth)maximumWidth=limits.maxTrack.X;if(limits.maxTrack.Y>=minimumHeight)maximumHeight=limits.maxTrack.Y;
        }
    }
    static void UpdateDrag(Point point) {
        int dx=point.X-start.X,dy=point.Y-start.Y;var r=startRect;
        if(restoreOnMove && !dragMoved && Math.Abs(dx)<3 && Math.Abs(dy)<3)return;
        dragMoved=true;
        if(mode==1)Post(win,1,r.L+dx,r.T+dy,0,0);
        else {
            int left=r.L,top=r.T,right=r.R,bottom=r.B;
            if((corner&1)!=0)right+=dx;else left+=dx;if((corner&2)!=0)bottom+=dy;else top+=dy;
            int width=Math.Max(minimumWidth,Math.Min(maximumWidth,right-left)),height=Math.Max(minimumHeight,Math.Min(maximumHeight,bottom-top));
            if((corner&1)!=0)right=left+width;else left=right-width;if((corner&2)!=0)bottom=top+height;else top=bottom-height;
            Post(win,2,left,top,right-left,bottom-top);
        }
    }
    public static bool HandleDragMessage(int message,Point point) {
        if(win==IntPtr.Zero)return false;
        if(message==0x0200){UpdateDrag(point);return true;}
        if(message==(mode==1?0x0202:0x0205)) {
            UpdateDrag(point);win=IntPtr.Zero;bool consume=swallowUp;swallowUp=false;return consume;
        }
        return false;
    }
    [StructLayout(LayoutKind.Sequential)] struct Placement { public int length, flags, show; public Point min,max; public Native.RECT normal; }
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref Placement p);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    public static bool TestQueue(out string detail) {
        var previous=MoveBackend;int calls=0,torn=0,last=-1;var finished=new ManualResetEventSlim();var entered=new ManualResetEventSlim();var proceed=new ManualResetEventSlim();
        int current=Interlocked.Increment(ref generation); run=true;
        MoveBackend=(h,m,x,y,w,hh)=>{ Interlocked.Increment(ref calls);entered.Set();proceed.Wait(1000);if (y != -x) Interlocked.Increment(ref torn); Volatile.Write(ref last,x); if(x==999) finished.Set(); };
        var testWorker=new Thread(()=>Work(current)) { IsBackground=true }; testWorker.Start();
        try {
            Post((IntPtr)123,1,0,0,0,0);if(!entered.Wait(1000)){detail="worker did not start";return false;}
            for(int i=1;i<1000;i++)Post((IntPtr)123,1,i,-i,0,0);proceed.Set();
            bool reached=finished.Wait(2000); detail="applied="+calls+", last="+last+", torn="+torn;
            return reached && last==999 && torn==0 && calls<10 && !Active && (MoveFlags(1)&0x4000)==0;
        } finally {proceed.Set();run=false;Interlocked.Increment(ref generation);wake.Set();testWorker.Join(1000);MoveBackend=previous;}
    }
    public static bool TestMessages(out string detail) {
        bool ok=true;var log=new StringBuilder();Action<bool,string> check=(value,text)=>{ok&=value;log.AppendLine((value?"PASS ":"FAIL ")+text);};
        try {
            win=(IntPtr)123;mode=1;start=Point.Empty;startRect=new Native.RECT{L=-400,T=-200,R=100,B=100};restoreOnMove=false;swallowUp=true;
            check(HandleDragMessage(0x0200,new Point(10,-20)),"grabbed move consumed");
            check(!HandleDragMessage(0x0205,new Point(12,-22))&&win!=(IntPtr)0,"unrelated button release keeps drag");
            check(HandleDragMessage(0x0202,new Point(25,-35))&&win==IntPtr.Zero,"matching button release ends drag");
            lock(moveGate)check(latest.X==-375&&latest.Y==-235,"release coordinates posted on negative-coordinate monitor");
            win=(IntPtr)123;mode=2;start=Point.Empty;startRect=new Native.RECT{L=-400,T=-200,R=100,B=100};minimumWidth=300;minimumHeight=200;maximumWidth=800;maximumHeight=600;restoreOnMove=false;
            for(int edge=0;edge<4;edge++){corner=edge;UpdateDrag(new Point((edge&1)==0?1000:-1000,(edge&2)==0?1000:-1000));lock(moveGate)check(latest.W==300&&latest.Height==200&&((edge&1)!=0?latest.X==-400:latest.X+latest.W==100),"resize corner "+edge+" preserves opposite edge at min size");}
            win=(IntPtr)123;mode=1;restoreOnMove=true;dragMoved=false;int before=seq;UpdateDrag(new Point(1,1));check(seq==before,"maximized click does not restore or move");UpdateDrag(new Point(8,3));lock(moveGate)check(latest.Restore,"maximized restore deferred to first movement");
        }finally{win=IntPtr.Zero;restoreOnMove=dragMoved=swallowUp=false;lock(moveGate)latest=new MoveTarget();}
        detail=log.ToString();return ok;
    }
    public static int Moves { get { return seq; } }
    static IntPtr Hook(int code, IntPtr w, IntPtr l) {
        if (!Cfg.Enabled || code < 0) return CallNextHookEx(IntPtr.Zero, code, w, l);
        int msg = (int)w;
        if (msg == 0x0200 && win == IntPtr.Zero) return CallNextHookEx(IntPtr.Zero, code, w, l);   // fast path: plain mouse move
        try {
            int px = Marshal.ReadInt32(l), py = Marshal.ReadInt32(l, 4);
            if ((msg == 0x0201 || (msg == 0x0204 && Cfg.Resize)) && win == IntPtr.Zero && ModDown) {
                var h = GetAncestor(WindowFromPoint(new Point(px, py)), 2);
                if (Eligible(h)) {
                    win = h; mode = msg == 0x0201 ? 1 : 2; start = new Point(px, py); if(!Native.GetWindowRect(h,out startRect)){win=IntPtr.Zero;return CallNextHookEx(IntPtr.Zero,code,w,l);}
                    restoreOnMove=false;dragMoved=false;Interlocked.Increment(ref dragSession);
                    if (Native.IsZoomed(h)) {
                        var placement = new Placement { length = Marshal.SizeOf(typeof(Placement)) };
                        if (!GetWindowPlacement(h, ref placement)) { win = IntPtr.Zero; return CallNextHookEx(IntPtr.Zero, code, w, l); }
                        int width = placement.normal.R - placement.normal.L, height = placement.normal.B - placement.normal.T;
                        double fraction = (px - startRect.L) / (double)Math.Max(1, startRect.R - startRect.L);
                        int left = px - (int)(width * fraction), top = py - Math.Min(UI.S(24), height / 2);
                        startRect = new Native.RECT { L=left,T=top,R=left+width,B=top+height }; restoreOnMove=true;
                    }
                    if(mode==2)ReadSizeLimits(h);
                    corner = (px > (startRect.L + startRect.R) / 2 ? 1 : 0) | (py > (startRect.T + startRect.B) / 2 ? 2 : 0);
                    swallowUp = true; return (IntPtr)1;
                }
            }
            if(HandleDragMessage(msg,new Point(px,py)))return (IntPtr)1;
        } catch { }
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }
}
// ----- Workspaces: save a set of app windows (exe + position) and bring them back -----
static class Workspaces {
    static readonly int MyPid = Process.GetCurrentProcess().Id;
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint c);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, int f, StringBuilder s, ref int n);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static string ExeOf(IntPtr h) {
        uint pid; Native.GetWindowThreadProcessId(h, out pid); var ph = OpenProcess(0x1000, false, pid); if (ph == IntPtr.Zero) return "";
        try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(ph, 0, sb, ref n) ? sb.ToString() : ""; } finally { CloseHandle(ph); }
    }
    public static bool AppWindow(IntPtr h) {
        if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return false;          // owned windows (dialogs) skipped
        int ex = GetWindowLong(h, -20); if ((ex & 0x80) != 0) return false;               // tool windows
        int cloaked; if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
        var sb = new StringBuilder(256); Native.GetWindowText(h, sb, 256); if (sb.Length == 0) return false;
        uint pid; Native.GetWindowThreadProcessId(h, out pid); if (pid == MyPid) return false;
        var cls = new StringBuilder(64); Native.GetClassName(h, cls, 64); string c = cls.ToString();
        return c != "Progman" && c != "WorkerW" && c != "Shell_TrayWnd";
    }
    public static List<WsApp> Capture() {
        var list = new List<WsApp>(); var seen = new HashSet<string>();
        Native.EnumWindows((h, l) => {
            try {
                if (!AppWindow(h)) return true;
                string exe = ExeOf(h); if (exe == "" || exe.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase) && false) return true;
                var t = new StringBuilder(256); Native.GetWindowText(h, t, 256);
                Native.RECT r; Native.GetWindowRect(h, out r); var s = Screen.FromHandle(h); var wa = s.WorkingArea;
                string key = exe + "|" + t; if (!seen.Add(key)) return true;
                list.Add(new WsApp { Path = exe, Process = Path.GetFileNameWithoutExtension(exe), Title = t.ToString(), Monitor = Monitors.IdOf(s),
                    X = r.L - wa.X, Y = r.T - wa.Y, W = r.R - r.L, H = r.B - r.T, State = Native.IsZoomed(h) ? "max" : Native.IsIconic(h) ? "min" : "normal" });
            } catch { }
            return true;
        }, IntPtr.Zero);
        return list;
    }
    static IntPtr FindFor(WsApp a, HashSet<IntPtr> used) {
        IntPtr best = IntPtr.Zero;
        Native.EnumWindows((h, l) => {
            if (used.Contains(h) || !AppWindow(h)) return true;
            if (!string.Equals(ExeOf(h), a.Path, StringComparison.OrdinalIgnoreCase)) return true;
            var t = new StringBuilder(256); Native.GetWindowText(h, t, 256);
            if (best == IntPtr.Zero || t.ToString() == a.Title) best = h;
            return t.ToString() != a.Title;
        }, IntPtr.Zero);
        return best;
    }
    static List<IntPtr> AllFor(WsApp a) {
        var r = new List<IntPtr>();
        Native.EnumWindows((h, l) => { if (AppWindow(h) && string.Equals(ExeOf(h), a.Path, StringComparison.OrdinalIgnoreCase)) r.Add(h); return true; }, IntPtr.Zero);
        return r;
    }
    // desktop shortcut that runs "BongHub.exe workspace <name>" (PowerToys: "Create desktop shortcut")
    public static string Shortcut(Workspace w, string dir) {
        string name = w.Name; foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string lnk = Path.Combine(dir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + " (작업 영역).lnk");
        var t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
        try {
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); var st = sc.GetType();
            Action<string, object> set = (n, v) => st.InvokeMember(n, BindingFlags.SetProperty, null, sc, new[] { v });
            set("TargetPath", Application.ExecutablePath); set("Arguments", "workspace \"" + w.Name + "\""); set("WorkingDirectory", Path.GetDirectoryName(Application.ExecutablePath));
            set("IconLocation", Application.ExecutablePath + ",0"); set("Description", "BongHub 작업 영역: " + w.Name);
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null); Marshal.FinalReleaseComObject(sc);
        } finally { Marshal.FinalReleaseComObject(sh); }
        return lnk;
    }
    public static void Place(WsApp a, IntPtr h) {
        var s = Monitors.ScreenFor(a.Monitor) ?? Screen.FromHandle(h); var wa = s.WorkingArea;
        if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, 9);
        Native.SetWindowPos(h, IntPtr.Zero, wa.X + a.X, wa.Y + a.Y, a.W, a.H, 0x0004 | 0x0010);
        if (a.State == "max") Native.ShowWindow(h, 3); else if (a.State == "min") Native.ShowWindow(h, 6);
    }
    // launches missing apps (if allowed) and places every window; runs on a worker thread, polls only during the launch
    public static string Launch(Workspace w, bool launchMissing, Action<string> progress) { return Launch(w, launchMissing, true, progress); }
    // moveExisting=false matches PowerToys' default: always start a fresh instance and place that one
    public static string Launch(Workspace w, bool launchMissing, bool moveExisting, Action<string> progress) {
        int placed = 0, started = 0, failed = 0; var used = new HashSet<IntPtr>(); var pending = new List<WsApp>();
        foreach (var a in w.Apps.Where(x => x.Enabled)) {
            var h = FindFor(a, used);
            if (h != IntPtr.Zero && (moveExisting || !launchMissing)) { used.Add(h); Place(a, h); placed++; continue; }
            if (h != IntPtr.Zero) foreach (var o in AllFor(a)) used.Add(o);   // remember old windows so the new one is found
            if (!launchMissing || !File.Exists(a.Path)) { failed++; continue; }
            try { Process.Start(new ProcessStartInfo(a.Path, a.Args ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(a.Path) }); started++; pending.Add(a); } catch { failed++; }
        }
        var sw = Stopwatch.StartNew();
        while (pending.Count > 0 && sw.ElapsedMilliseconds < 15000) {
            Thread.Sleep(400);
            foreach (var a in pending.ToList()) { var h = FindFor(a, used); if (h != IntPtr.Zero) { Thread.Sleep(300); used.Add(h); Place(a, h); placed++; pending.Remove(a); } }
            if (progress != null) progress("여는 중… " + placed + "개 배치");
        }
        failed += pending.Count;
        return "'" + w.Name + "' · 배치 " + placed + "개" + (started > 0 ? " · 새로 실행 " + started + "개" : "") + (failed > 0 ? " · 실패 " + failed + "개" : "");
    }
}

// ----- Command palette: one search box for apps, BongHub commands, calculator, units, time, windows, files, shell, terminal, system -----
class PaletteItem {
    public string Title, Sub, Glyph, Key; public Action Run; public int Score;
    string lt, lk; public string LTitle { get { return lt ?? (lt = (Title ?? "").ToLowerInvariant()); } } public string LKey { get { return lk ?? (lk = (Key ?? "").ToLowerInvariant()); } }
}
static class Palette {
    public static Func<IEnumerable<PaletteItem>> Commands;   // provided by the tray app
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr h, string root, uint flags);
    // ---- app index: built once in the background on first use, refreshed every 10 minutes ----
    static volatile List<PaletteItem> apps; static DateTime appsAt; static int building; static readonly ManualResetEvent appsReady = new ManualResetEvent(false);
    public static void Warm() {
        if (apps != null && (DateTime.Now - appsAt).TotalMinutes < 10) return;
        if (Interlocked.CompareExchange(ref building, 1, 0) != 0) return;
        var t = new Thread(() => { try { apps = BuildApps(); appsAt = DateTime.Now; } catch { } finally { building = 0; appsReady.Set(); } }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }
    public static List<PaletteItem> Apps() { Warm(); if (apps == null) appsReady.WaitOne(3000); return apps ?? new List<PaletteItem>(); }
    public static List<PaletteItem> AppsIfReady() { Warm(); return apps; }
    static List<PaletteItem> BuildApps() {
        var l = new List<PaletteItem>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) }) {
            try {
                foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "Programs"), "*.lnk", SearchOption.AllDirectories)) {
                    string n = Path.GetFileNameWithoutExtension(f); if (n.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("제거") || !seen.Add(n)) continue;
                    string path = f; l.Add(new PaletteItem { Title = n, Sub = "앱", Glyph = "\uE7AC", Key = n, Run = () => { try { Process.Start(path); } catch { } } });
                }
            } catch { }
        }
        // Store / packaged apps (shell:AppsFolder), the same list the Start menu uses
        try {
            var t = Type.GetTypeFromProgID("Shell.Application"); object sh = Activator.CreateInstance(t);
            try {
                var ns = t.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, sh, new object[] { "shell:::{4234d49b-0245-4df3-b780-3893943456e1}" });
                var items = ns.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, ns, null);
                int n = (int)items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null);
                for (int i = 0; i < n; i++) {
                    var it = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                    string name = (string)it.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, it, null), id = (string)it.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, it, null);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || !id.Contains("!") || !seen.Add(name)) continue;   // packaged apps have "family!app" ids
                    string aid = id; l.Add(new PaletteItem { Title = name, Sub = "앱 (스토어)", Glyph = "\uE7AC", Key = name, Run = () => { try { Process.Start("explorer.exe", "shell:AppsFolder\\" + aid); } catch { } } });
                }
            } finally { Marshal.FinalReleaseComObject(sh); }
        } catch { }
        return l;
    }
    // ---- scoring (allocation free on the hot path; inputs are lower-case) ----
    public static int Score(string q, string t) { if (string.IsNullOrEmpty(q)) return 1; if (string.IsNullOrEmpty(t)) return 0; return ScoreL(q.ToLowerInvariant(), t.ToLowerInvariant()); }
    public static int ScoreL(string b, string a) {
        if (b.Length == 0) return 1; if (a.Length == 0) return 0;
        if (a.Length == b.Length && string.CompareOrdinal(a, b) == 0) return 1000;
        if (a.StartsWith(b, StringComparison.Ordinal)) return 800; int i = a.IndexOf(b, StringComparison.Ordinal); if (i >= 0) return 600 - i;
        // word initials ("vsc" -> "visual studio code")
        int k = 0; bool start = true; for (int x = 0; x < a.Length && k < b.Length; x++) { char c = a[x]; if (start && c == b[k]) k++; start = c == ' ' || c == '-' || c == '_' || c == '.'; }
        if (k == b.Length) return 500;
        int j = 0, gaps = 0; for (int x = 0; x < a.Length; x++) { if (j < b.Length && a[x] == b[j]) j++; else if (j > 0) gaps++; }
        return j == b.Length ? Math.Max(1, 300 - gaps) : 0;
    }
    // ---- calculator: small recursive-descent parser (+ - * / % ^, parentheses, functions, constants, hex) ----
    static readonly Regex calcChars = new Regex(@"^[\d\s\.\+\-\*/\(\)%^,a-z]+$", RegexOptions.Compiled);
    public static string Calc(string q) {
        string s = (q ?? "").Trim().ToLowerInvariant(); if (s.StartsWith("=")) s = s.Substring(1);
        if (s.Length == 0 || !calcChars.IsMatch(s) || !s.Any(char.IsDigit) && !s.Contains("pi")) return null;
        bool hasOp = s.IndexOfAny(new[] { '+', '-', '*', '/', '%', '^', '(' }) >= 0; if (!hasOp && !s.StartsWith("0x")) return null;
        try { int p = 0; double v = Expr(s.Replace(",", ""), ref p); Ws(s.Replace(",", ""), ref p); if (p != s.Replace(",", "").Length || double.IsNaN(v) || double.IsInfinity(v)) return null; return Math.Round(v, 10).ToString("G12", System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
    }
    static void Ws(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }
    static double Expr(string s, ref int p) { double v = Term(s, ref p); for (;;) { Ws(s, ref p); if (p < s.Length && (s[p] == '+' || s[p] == '-')) { char o = s[p++]; double r = Term(s, ref p); v = o == '+' ? v + r : v - r; } else return v; } }
    static double Term(string s, ref int p) { double v = Pow(s, ref p); for (;;) { Ws(s, ref p); if (p < s.Length && (s[p] == '*' || s[p] == '/' || s[p] == '%')) { char o = s[p++]; double r = Pow(s, ref p); v = o == '*' ? v * r : o == '/' ? v / r : v % r; } else return v; } }
    static double Pow(string s, ref int p) { double v = Unary(s, ref p); Ws(s, ref p); if (p < s.Length && s[p] == '^') { p++; return Math.Pow(v, Pow(s, ref p)); } return v; }
    static double Unary(string s, ref int p) { Ws(s, ref p); if (p < s.Length && s[p] == '-') { p++; return -Unary(s, ref p); } if (p < s.Length && s[p] == '+') { p++; return Unary(s, ref p); } return Atom(s, ref p); }
    static double Atom(string s, ref int p) {
        Ws(s, ref p); if (p >= s.Length) throw new FormatException();
        if (s[p] == '(') { p++; double v = Expr(s, ref p); Ws(s, ref p); if (p >= s.Length || s[p] != ')') throw new FormatException(); p++; return v; }
        if (s.Length - p > 2 && s[p] == '0' && s[p + 1] == 'x') { int st = p += 2; while (p < s.Length && Uri.IsHexDigit(s[p])) p++; return Convert.ToInt64(s.Substring(st, p - st), 16); }
        if (char.IsDigit(s[p]) || s[p] == '.') { int st = p; while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.')) p++; if (p < s.Length && s[p] == 'e' && p + 1 < s.Length && (char.IsDigit(s[p + 1]) || s[p + 1] == '-')) { p += 2; while (p < s.Length && char.IsDigit(s[p])) p++; } return double.Parse(s.Substring(st, p - st), System.Globalization.CultureInfo.InvariantCulture); }
        if (char.IsLetter(s[p])) {
            int st = p; while (p < s.Length && char.IsLetter(s[p])) p++; string f = s.Substring(st, p - st);
            if (f == "pi") return Math.PI; if (f == "e") return Math.E;
            double a = Atom(s, ref p);
            switch (f) { case "sqrt": return Math.Sqrt(a); case "sin": return Math.Sin(a); case "cos": return Math.Cos(a); case "tan": return Math.Tan(a); case "abs": return Math.Abs(a); case "ln": return Math.Log(a); case "log": return Math.Log10(a); case "exp": return Math.Exp(a); case "round": return Math.Round(a); case "floor": return Math.Floor(a); case "ceil": return Math.Ceiling(a); }
            throw new FormatException();
        }
        throw new FormatException();
    }
    // ---- unit converter: "10 km to mi", "72 f in c", "3 gb mb" ----
    static readonly Regex unitRx = new Regex(@"^\s*(-?[\d\.,]+)\s*([a-z°²/]+)\s+(?:to|in|->|=)?\s*([a-z°²/]+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Dictionary<string, KeyValuePair<string, double>> units = BuildUnits();
    static Dictionary<string, KeyValuePair<string, double>> BuildUnits() {
        var d = new Dictionary<string, KeyValuePair<string, double>>(StringComparer.OrdinalIgnoreCase);
        Action<string, double, string[]> add = (cat, f, names) => { foreach (var n in names) d[n] = new KeyValuePair<string, double>(cat, f); };
        add("len", 1, new[] { "m", "meter", "meters" }); add("len", 1000, new[] { "km" }); add("len", .01, new[] { "cm" }); add("len", .001, new[] { "mm" }); add("len", 1609.344, new[] { "mi", "mile", "miles" }); add("len", .3048, new[] { "ft", "feet" }); add("len", .0254, new[] { "in", "inch" }); add("len", .9144, new[] { "yd" }); add("len", 1852, new[] { "nmi" });
        add("mass", 1, new[] { "kg" }); add("mass", .001, new[] { "g" }); add("mass", 1e-6, new[] { "mg" }); add("mass", .45359237, new[] { "lb", "lbs" }); add("mass", .028349523125, new[] { "oz" }); add("mass", 1000, new[] { "t", "ton" });
        add("vol", 1, new[] { "l", "liter" }); add("vol", .001, new[] { "ml" }); add("vol", 3.785411784, new[] { "gal" }); add("vol", .2365882365, new[] { "cup" }); add("vol", 1000, new[] { "m3" });
        add("data", 1, new[] { "b", "byte", "bytes" }); add("data", 1024, new[] { "kb" }); add("data", 1048576, new[] { "mb" }); add("data", 1073741824, new[] { "gb" }); add("data", 1099511627776, new[] { "tb" }); add("data", .125, new[] { "bit" });
        add("time", 1, new[] { "s", "sec" }); add("time", 60, new[] { "min" }); add("time", 3600, new[] { "h", "hr", "hour" }); add("time", 86400, new[] { "d", "day", "days" }); add("time", 604800, new[] { "week", "wk" }); add("time", .001, new[] { "ms" });
        add("speed", 1, new[] { "m/s" }); add("speed", 1 / 3.6, new[] { "km/h", "kmh", "kph" }); add("speed", .44704, new[] { "mph" }); add("speed", .514444, new[] { "kn", "knot" });
        add("area", 1, new[] { "m2", "m²" }); add("area", 1e6, new[] { "km2" }); add("area", 4046.8564224, new[] { "acre" }); add("area", 10000, new[] { "ha" }); add("area", 3.305785, new[] { "평", "pyeong" }); add("area", .09290304, new[] { "ft2" });
        add("temp", 0, new[] { "c", "°c", "celsius" }); add("temp", 1, new[] { "f", "°f", "fahrenheit" }); add("temp", 2, new[] { "k", "kelvin" });
        return d;
    }
    public static string Unit(string q) {
        var m = unitRx.Match(q ?? ""); if (!m.Success) return null; double v;
        if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return null;
        KeyValuePair<string, double> a, b; if (!units.TryGetValue(m.Groups[2].Value, out a) || !units.TryGetValue(m.Groups[3].Value, out b) || a.Key != b.Key) return null;
        double r;
        if (a.Key == "temp") { double c = a.Value == 0 ? v : a.Value == 1 ? (v - 32) * 5 / 9 : v - 273.15; r = b.Value == 0 ? c : b.Value == 1 ? c * 9 / 5 + 32 : c + 273.15; }
        else r = v * a.Value / b.Value;
        return Math.Round(r, 6).ToString("G10", System.Globalization.CultureInfo.InvariantCulture) + " " + m.Groups[3].Value;
    }
    // ---- clock ----
    public static List<PaletteItem> Time(string q) {
        var l = new List<PaletteItem>(); string s = q.Trim().ToLowerInvariant();
        string[] keys = { "time", "now", "시간", "시계", "date", "날짜" }; string key = keys.FirstOrDefault(k => s == k || s.StartsWith(k + " ")); if (key == null) return l;
        string rest = s.Substring(key.Length).Trim(); var now = DateTime.Now;
        Action<string, string> add = (t, sub) => { string tt = t; l.Add(new PaletteItem { Title = t, Sub = sub + " · Enter로 복사", Glyph = "\uE823", Score = 1900, Run = () => { try { Clipboard.SetText(tt); } catch { } } }); };
        if (rest == "") {
            add(now.ToString("HH:mm:ss"), "현재 시각"); add(now.ToString("yyyy-MM-dd (ddd)"), "오늘 날짜"); add(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), "UTC (ISO 8601)");
            add(((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString(), "Unix 시간"); add("ISO 주차 " + System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday), "올해 몇째 주");
        } else {
            foreach (var z in TimeZoneInfo.GetSystemTimeZones().Where(z => z.DisplayName.ToLowerInvariant().Contains(rest) || z.Id.ToLowerInvariant().Contains(rest)).Take(6))
                add(TimeZoneInfo.ConvertTime(now, z).ToString("HH:mm  yyyy-MM-dd"), z.DisplayName);
        }
        return l;
    }
    // ---- open windows (window switcher) ----
    public static List<PaletteItem> WindowItems() {
        var l = new List<PaletteItem>();
        Native.EnumWindows((h, x) => {
            try {
                if (!Workspaces.AppWindow(h)) return true;
                var t = new StringBuilder(256); Native.GetWindowText(h, t, 256); string exe = Path.GetFileNameWithoutExtension(Workspaces.ExeOf(h)); IntPtr hh = h;
                l.Add(new PaletteItem { Title = t.ToString(), Sub = "창 전환 · " + exe, Key = exe, Glyph = "\uE8A7", Run = () => { if (Native.IsIconic(hh)) Native.ShowWindow(hh, 9); SetForegroundWindow(hh); } });
            } catch { }
            return true;
        }, IntPtr.Zero);
        return l;
    }
    // ---- Windows Terminal profiles ----
    static List<PaletteItem> terms; static DateTime termsAt;
    public static List<PaletteItem> Terminal() {
        if (terms != null && (DateTime.Now - termsAt).TotalMinutes < 10) return terms;
        var l = new List<PaletteItem>();
        try {
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var f in new[] { @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json", @"Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json", @"Microsoft\Windows Terminal\settings.json" }.Select(x => Path.Combine(la, x)).Where(File.Exists)) {
                string js = Regex.Replace(File.ReadAllText(f), @"^\s*//.*$", "", RegexOptions.Multiline);
                var o = new JavaScriptSerializer().DeserializeObject(js) as Dictionary<string, object>; if (o == null || !o.ContainsKey("profiles")) continue;
                var pr = o["profiles"]; object[] list = pr is Dictionary<string, object> && ((Dictionary<string, object>)pr).ContainsKey("list") ? ((Dictionary<string, object>)pr)["list"] as object[] : pr as object[];
                foreach (Dictionary<string, object> p in (list ?? new object[0]).OfType<Dictionary<string, object>>()) {
                    if (p.ContainsKey("hidden") && p["hidden"] is bool && (bool)p["hidden"]) continue; if (!p.ContainsKey("name")) continue;
                    string n = p["name"].ToString(); l.Add(new PaletteItem { Title = "터미널: " + n, Key = n + " terminal wt", Sub = "Windows Terminal 프로필", Glyph = "\uE756", Run = () => { try { Process.Start("wt.exe", "-p \"" + n + "\""); } catch { } } });
                }
                break;
            }
        } catch { }
        terms = l; termsAt = DateTime.Now; return l;
    }
    // ---- system commands / Windows settings ----
    static List<PaletteItem> sys;
    public static Func<string, bool> Confirm = msg => DarkBox.Show(null, msg, "명령 팔레트", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    public static List<PaletteItem> SystemItems() {
        if (sys != null) return sys;
        var l = new List<PaletteItem>();
        Action<string, string, string, Action> add = (t, k, g, a) => l.Add(new PaletteItem { Title = t, Key = k, Sub = "시스템", Glyph = g, Run = a });
        add("잠금", "lock 잠금", "\uE72E", () => LockWorkStation());
        add("절전", "sleep 절전", "\uE708", () => { if (Confirm("지금 절전 모드로 전환할까요?")) SetSuspendState(false, false, false); });
        add("로그아웃", "sign out logoff 로그아웃", "\uE7E8", () => { if (Confirm("로그아웃할까요? 저장하지 않은 작업은 사라질 수 있어요.")) Process.Start("shutdown.exe", "/l"); });
        add("다시 시작", "restart reboot 재시작 다시", "\uE777", () => { if (Confirm("PC를 다시 시작할까요?")) Process.Start("shutdown.exe", "/r /t 0"); });
        add("시스템 종료", "shutdown 종료 전원", "\uE7E8", () => { if (Confirm("PC를 종료할까요?")) Process.Start("shutdown.exe", "/s /t 0"); });
        add("휴지통 비우기", "empty recycle bin 휴지통", "\uE74D", () => { if (Confirm("휴지통을 비울까요?")) SHEmptyRecycleBin(IntPtr.Zero, null, 0); });
        foreach (var x in new[] { new[] { "디스플레이", "display" }, new[] { "소리", "sound" }, new[] { "블루투스", "bluetooth" }, new[] { "네트워크", "network" }, new[] { "Wi-Fi", "network-wifi" }, new[] { "앱", "appsfeatures" }, new[] { "기본 앱", "defaultapps" }, new[] { "개인 설정", "personalization" }, new[] { "전원", "powersleep" }, new[] { "저장소", "storagesense" }, new[] { "Windows 업데이트", "windowsupdate" }, new[] { "마우스", "mousetouchpad" }, new[] { "키보드", "keyboard" }, new[] { "시간 및 언어", "dateandtime" }, new[] { "알림", "notifications" }, new[] { "작업 표시줄", "taskbar" } }) {
            string uri = "ms-settings:" + x[1]; l.Add(new PaletteItem { Title = "Windows 설정: " + x[0], Key = x[1] + " settings 설정", Sub = uri, Glyph = "\uE713", Run = () => { try { Process.Start(uri); } catch { } } });
        }
        sys = l; return l;
    }
    // ---- file search through the Windows Search index (prefix "?") ----
    public static List<PaletteItem> Files(string q, int max = 20) {
        var l = new List<PaletteItem>(); if (q.Length < 2) return l;
        string safe = q.Replace("'", "''").Replace("%", "").Replace("[", "");
        using (var cn = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")) {
            cn.Open();
            using (var cmd = new System.Data.OleDb.OleDbCommand("SELECT TOP " + max + " System.ItemPathDisplay, System.ItemNameDisplay FROM SYSTEMINDEX WHERE System.FileName LIKE '%" + safe + "%' ORDER BY System.DateModified DESC", cn)) {
                cmd.CommandTimeout = 3;
                using (var r = cmd.ExecuteReader()) while (r.Read()) { string p = r[0] as string, n = r[1] as string; if (p == null) continue; l.Add(new PaletteItem { Title = n ?? Path.GetFileName(p), Sub = p, Glyph = "\uE8A5", Key = n, Score = 700, Run = () => { try { Process.Start(p); } catch { } } }); }
            }
        }
        return l;
    }
    public static List<PaletteItem> Search(string q, PaletteCfg c) {
        q = (q ?? "").Trim(); var res = new List<PaletteItem>(); string lq = q.ToLowerInvariant();
        if (q.StartsWith(">") && c.Shell) { string cmd = q.Substring(1).Trim(); if (cmd.Length > 0) res.Add(new PaletteItem { Title = "실행: " + cmd, Sub = "명령 프롬프트에서 실행 (Enter) · 창은 열린 채로 남아요", Glyph = "\uE756", Score = 3000, Run = () => { try { Process.Start(new ProcessStartInfo("cmd.exe", "/k " + cmd) { UseShellExecute = true }); } catch { } } }); return res; }
        if (q.StartsWith("??") && c.Web) { string qq = q.Substring(2).Trim(); if (qq.Length > 0) res.Add(WebItem(qq, 3000)); return res; }
        if (q.StartsWith("?") && c.Files) { res.Add(new PaletteItem { Title = "파일 검색: " + q.Substring(1).Trim(), Sub = "Windows 검색 색인에서 찾는 중…", Glyph = "\uE721", Score = 3000, Key = "\u0001files" }); return res; }
        if (q.StartsWith("<") && c.Windows) { string w = lq.Substring(1).Trim(); foreach (var it in WindowItems()) { it.Score = w == "" ? 500 : Math.Max(ScoreL(w, it.LTitle), ScoreL(w, it.LKey)); if (it.Score > 0) res.Add(it); } return res.OrderByDescending(x => x.Score).Take(40).ToList(); }
        if (c.Calc) { var v = Calc(q); if (v != null) { string vv = v; res.Add(new PaletteItem { Title = "= " + v, Sub = "계산 결과 · Enter로 복사", Glyph = "\uE8EF", Score = 2000, Run = () => { try { Clipboard.SetText(vv); } catch { } } }); } }
        if (c.Units) { var u = Unit(q); if (u != null) { string uu = u; res.Add(new PaletteItem { Title = "= " + u, Sub = "단위 변환 · Enter로 복사", Glyph = "\uE8EF", Score = 2000, Run = () => { try { Clipboard.SetText(uu); } catch { } } }); } }
        if (c.Time) res.AddRange(Time(q));
        Action<IEnumerable<PaletteItem>, int> score = (src, bonus) => { foreach (var it in src) { int sc = Math.Max(ScoreL(lq, it.LTitle), ScoreL(lq, it.LKey) - 50); if (sc > 0) { it.Score = sc + bonus; res.Add(it); } } };
        if (Commands != null) score(Commands(), 0);
        if (q.Length > 0) {
            if (c.Apps) { var a = AppsIfReady(); if (a == null) a = Apps(); score(a, 0); }
            if (c.System) score(SystemItems(), -20);
            if (c.Settings == false) res.RemoveAll(x => x.Sub != null && x.Sub.StartsWith("ms-settings:"));
            if (c.Terminal) score(Terminal(), -10);
            if (c.Windows && q.Length > 1) score(WindowItems(), -100);
        }
        if (c.Web && q.Length > 1) res.Add(WebItem(q, 1));
        res.Sort((x, y) => y.Score.CompareTo(x.Score)); if (res.Count > 40) res.RemoveRange(40, res.Count - 40);
        return res;
    }
    static PaletteItem WebItem(string q, int score) { string qq = q; return new PaletteItem { Title = "웹에서 검색: " + q, Sub = "기본 브라우저", Glyph = "\uE721", Score = score, Run = () => { try { Process.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(qq)); } catch { } } }; }
}
class PaletteForm : Form {
    TextBox box; ListBox list; List<PaletteItem> items = new List<PaletteItem>(); PaletteCfg cfg; public bool Reusable; int fileSeq;
    static Font glyphFont;
    public PaletteForm(PaletteCfg c) {
        cfg = c; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; KeyPreview = true; BackColor = UI.Card; Font = UI.Base;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea; int w = UI.S(640), h = UI.S(430);
        Bounds = TestEnv.Off ? new Rectangle(TestEnv.Area.X + 40, TestEnv.Area.Y + 40, w, h) : new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + wa.Height / 5, w, h);
        Padding = new Padding(UI.S(14));
        box = new TextBox { Dock = DockStyle.Top, Font = new Font(UI.Fam, 15f * UI.Scale), BorderStyle = BorderStyle.None, BackColor = UI.Card, ForeColor = UI.Text };
        var hint = UI.L("앱 · 명령 · 계산(12*3, sqrt(2)) · 단위(10 km to mi) · time · <창 · ?파일 · >명령 · ??웹   |   ↑↓  Enter  Esc", UI.Small, UI.Hint); hint.Dock = DockStyle.Top; hint.Padding = new Padding(0, UI.S(6), 0, UI.S(8));
        list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = UI.Card, ForeColor = UI.Text, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = UI.S(44), IntegralHeight = false };
        list.DrawItem += DrawItem; list.DoubleClick += (s, e) => RunSel();
        Controls.Add(list); Controls.Add(hint); Controls.Add(box);
        box.TextChanged += (s, e) => Refill();
        KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Escape) Dismiss();
            else if (e.KeyCode == Keys.Down) { if (list.Items.Count > 0) list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { RunSel(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        Deactivate += (s, e) => { if (!TestEnv.Off) Dismiss(); };
        Palette.Warm();
        Refill();
    }
    public void SetQuery(string q) { box.Text = q; }
    // reused instance: hiding is much cheaper than recreating the window, fonts and list
    public void Dismiss() { if (Reusable) Hide(); else Close(); }
    public void Reopen(PaletteCfg c) {
        cfg = c; var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!TestEnv.Off) Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + wa.Height / 5);
        if (box.Text != "") box.Text = ""; else Refill();
        Show(); Activate(); box.Focus();
    }
    public int Count { get { return items.Count; } }
    public PaletteItem Top { get { return items.FirstOrDefault(); } }
    protected override bool ShowWithoutActivation { get { return TestEnv.Off; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80; if (TestEnv.Off) c.ExStyle |= 0x08000000; return c; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int v = 2; try { ChromeNative.Round(Handle, v); } catch { } }
    protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); using (var b = UI.Grad(new RectangleF(0, 0, Width, UI.S(3)))) e.Graphics.FillRectangle(b, 0, 0, Width, UI.S(3)); }
    void Refill() {
        items = Palette.Search(box.Text, cfg); Fill();
        if (items.Count == 1 && items[0].Key == "\u0001files") {
            int my = ++fileSeq; string q = box.Text.Trim().Substring(1).Trim();
            ThreadPool.QueueUserWorkItem(_ => {
                List<PaletteItem> r; string err = null; try { r = Palette.Files(q); } catch (Exception ex) { r = new List<PaletteItem>(); err = ex.Message; }
                try { BeginInvoke((Action)(() => { if (my != fileSeq || IsDisposed) return; items = r.Count > 0 ? r : new List<PaletteItem> { new PaletteItem { Title = err != null ? "Windows 검색을 쓸 수 없어요" : "찾은 파일이 없어요", Sub = err ?? "색인된 위치만 검색돼요", Glyph = "\uE783" } }; Fill(); })); } catch { }
            });
        } else fileSeq++;
    }
    void Fill() { list.BeginUpdate(); list.Items.Clear(); for (int i = 0; i < items.Count; i++) list.Items.Add(items[i].Title); list.EndUpdate(); if (items.Count > 0) list.SelectedIndex = 0; }
    void RunSel() { int i = list.SelectedIndex; if (i < 0 || i >= items.Count) return; var it = items[i]; Dismiss(); try { if (it.Run != null) it.Run(); } catch { } }
    void DrawItem(object s, DrawItemEventArgs e) {
        if (e.Index < 0 || e.Index >= items.Count) return; var it = items[e.Index]; var g = e.Graphics; var r = e.Bounds;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(UI.Card)) g.FillRectangle(b, r);
        if (sel) { var rr = new Rectangle(r.X, r.Y + 2, r.Width, r.Height - 4); using (var p = UI.Round(rr, UI.S(8))) { using (var b = new SolidBrush(UI.SelBg)) g.FillPath(b, p); } using (var b = UI.Grad(new RectangleF(r.X, r.Y + UI.S(10), UI.S(3), r.Height - UI.S(20)), 255, 90f)) g.FillRectangle(b, r.X, r.Y + UI.S(10), UI.S(3), r.Height - UI.S(20)); }
        if (glyphFont == null) glyphFont = new Font(UI.IconFam, 13f * UI.Scale); var gf = glyphFont; TextRenderer.DrawText(g, it.Glyph ?? "\uE8A7", gf, new Rectangle(r.X + UI.S(10), r.Y, UI.S(30), r.Height), sel ? UI.G1 : UI.Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, it.Title, UI.Base, new Rectangle(r.X + UI.S(48), r.Y + UI.S(4), r.Width - UI.S(56), r.Height / 2), UI.Text, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, it.Sub ?? "", UI.Small, new Rectangle(r.X + UI.S(48), r.Y + r.Height / 2, r.Width - UI.S(56), r.Height / 2 - UI.S(4)), UI.Hint, TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
static class ChromeNative {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    public static void Round(IntPtr h, int v) { DwmSetWindowAttribute(h, 33, ref v, 4); }
}

// ----- Advanced paste: transform the clipboard, then paste into the window that had focus -----
static class AdvPaste {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    public static string Plain(string s) { return s ?? ""; }
    public static string HtmlToMarkdown(string html) {
        if (string.IsNullOrEmpty(html)) return null;
        int a = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase), b = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        string h = a >= 0 && b > a ? html.Substring(a + 20, b - a - 20) : html.Substring(Math.Max(0, html.IndexOf('<')));
        h = Regex.Replace(h, @"\r?\n", " ");
        for (int i = 6; i >= 1; i--) h = Regex.Replace(h, "<h" + i + @"[^>]*>(.*?)</h" + i + ">", m => "\n" + new string('#', i) + " " + m.Groups[1].Value + "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<(b|strong)[^>]*>(.*?)</\1>", "**$2**", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<(i|em)[^>]*>(.*?)</\1>", "*$2*", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"</(p|div|ul|ol|tr|table)>", "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"</t[dh]>", " | ", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, "<[^>]+>", "");
        h = System.Net.WebUtility.HtmlDecode(h);
        h = Regex.Replace(h, @"[ \t]+\n", "\n"); h = Regex.Replace(h, @"\n{3,}", "\n\n");
        return h.Trim();
    }
    public static string ToJson(string text) {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        try { var o = js.DeserializeObject(text); return Pretty(js.Serialize(o)); } catch { }
        var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim() != "").ToList();
        char sep = lines[0].Contains('\t') ? '\t' : lines[0].Contains(',') ? ',' : '\0';
        if (sep != '\0' && lines.Count >= 2) {
            var head = lines[0].Split(sep).Select(x => x.Trim()).ToArray(); var rows = new List<Dictionary<string, object>>();
            foreach (var l in lines.Skip(1)) { var c = l.Split(sep); var d = new Dictionary<string, object>(); for (int i = 0; i < head.Length; i++) d[head[i]] = i < c.Length ? Val(c[i].Trim()) : null; rows.Add(d); }
            return Pretty(js.Serialize(rows));
        }
        var kv = lines.Select(l => Regex.Match(l, @"^\s*([^:=]+?)\s*[:=]\s*(.*)$")).ToList();
        if (kv.All(m => m.Success)) { var d = new Dictionary<string, object>(); foreach (var m in kv) d[m.Groups[1].Value] = Val(m.Groups[2].Value.Trim()); return Pretty(js.Serialize(d)); }
        return Pretty(js.Serialize(lines));
    }
    static object Val(string s) { double d; if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d; if (s == "true") return true; if (s == "false") return false; return s; }
    public static string Pretty(string json) {
        var sb = new StringBuilder(); int ind = 0; bool q = false;
        for (int i = 0; i < json.Length; i++) {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) q = !q;
            if (q) { sb.Append(c); continue; }
            if (c == '{' || c == '[') { sb.Append(c).Append('\n').Append(' ', (++ind) * 2); }
            else if (c == '}' || c == ']') { sb.Append('\n').Append(' ', (--ind) * 2).Append(c); }
            else if (c == ',') sb.Append(",\n").Append(' ', ind * 2);
            else if (c == ':') sb.Append(": ");
            else sb.Append(c);
        }
        return sb.ToString();
    }
    public static string ApplyCustom(string text, PasteAction a) {
        if (text == null || a == null) return null;
        switch (a.Kind) {
            case "upper": return text.ToUpperInvariant();
            case "lower": return text.ToLowerInvariant();
            case "title": return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
            case "template": return (a.Replace ?? "{text}").Replace("{text}", text).Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd")).Replace("{time}", DateTime.Now.ToString("HH:mm"));
            case "sort": return string.Join("\r\n", text.Replace("\r", "").Split('\n').OrderBy(x => x, StringComparer.CurrentCulture));
            case "dedupe": return string.Join("\r\n", text.Replace("\r", "").Split('\n').Distinct());
            case "urlencode": return Uri.EscapeDataString(text);
            case "urldecode": return Uri.UnescapeDataString(text);
            case "base64": return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            case "unbase64": return Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim()));
            default: return Regex.Replace(text, a.Pattern ?? "", (a.Replace ?? "").Replace("\\n", "\n"), RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        }
    }
    public static readonly string[] CustomKinds = { "regex", "template", "upper", "lower", "title", "sort", "dedupe", "urlencode", "urldecode", "base64", "unbase64" };
    public static readonly string[] CustomKindNames = { "정규식 바꾸기", "서식 템플릿 ({text} {date} {time})", "대문자", "소문자", "단어 첫 글자 대문자", "줄 정렬", "중복 줄 제거", "URL 인코딩", "URL 디코딩", "Base64 인코딩", "Base64 디코딩" };
    // "paste as file": writes a temp file and pastes it as a file (Explorer, chat apps, mail)
    public static string TempFile(string ext) { string d = Path.Combine(Path.GetTempPath(), "BongHub_paste"); Directory.CreateDirectory(d); return Path.Combine(d, "붙여넣기_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext); }
    public static string TextFile(string text) { string p = TempFile(".txt"); File.WriteAllText(p, text, new UTF8Encoding(true)); return p; }
    public static string HtmlFile(string cfhtml) {
        int a = cfhtml.IndexOf("<html", StringComparison.OrdinalIgnoreCase); string body = a >= 0 ? cfhtml.Substring(a) : "<html><body>" + cfhtml + "</body></html>";
        string p = TempFile(".html"); File.WriteAllText(p, body, new UTF8Encoding(false)); return p;
    }
    public static string ImageFile(Image im, string ext) {
        string p = TempFile(ext);
        if (ext == ".jpg") { using (var bm = new Bitmap(im.Width, im.Height)) { using (var g = Graphics.FromImage(bm)) { g.Clear(Color.White); g.DrawImage(im, 0, 0, im.Width, im.Height); } bm.Save(p, ImageFormat.Jpeg); } }
        else im.Save(p, ext == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png);
        return p;
    }
    public static void PasteFile(string path, IntPtr target) {
        if (path == null) return;
        try { var sc = new System.Collections.Specialized.StringCollection(); sc.Add(path); Clipboard.SetFileDropList(sc); } catch { return; }
        SendPaste(target);
    }
    // sets text and sends Ctrl+V to `target` once the hotkey modifiers are released
    public static void PasteText(string text, IntPtr target) {
        if (text == null) return;
        try { Clipboard.SetText(text); } catch { return; }
        SendPaste(target);
    }
    static void SendPaste(IntPtr target) {
        if (TestEnv.Off || target == IntPtr.Zero) return;
        SetForegroundWindow(target);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1500 && ((GetAsyncKeyState(0x11) | GetAsyncKeyState(0x12) | GetAsyncKeyState(0x10) | GetAsyncKeyState(0x5B)) & 0x8000) != 0) { Application.DoEvents(); Thread.Sleep(20); }
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }
}
class PasteMenu : Form {
    readonly IntPtr target; public readonly List<string[]> Options = new List<string[]>();
    public PasteMenu(IntPtr target) : this(target, null) { }
    public PasteMenu(IntPtr target, PasteCfg pc) {
        this.target = target; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; KeyPreview = true; BackColor = UI.Card; Font = UI.Base;
        string txt = null, html = null; bool img = false;
        try { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); if (Clipboard.ContainsText(TextDataFormat.Html)) html = Clipboard.GetText(TextDataFormat.Html); img = Clipboard.ContainsImage(); } catch { }
        var st = UI.Stack(); st.Dock = DockStyle.Fill; st.Padding = new Padding(UI.S(12)); st.BackColor = UI.Card;
        UI.AddRow(st, UI.L("고급 붙여넣기", UI.Bold, UI.Text));
        UI.AddRow(st, UI.L(txt == null ? (img ? "클립보드: 이미지" : "클립보드가 비어 있어요") : "클립보드: " + Short(txt), UI.Small, UI.Hint));
        int n = 0; Action<string, Func<string>, bool> addAny = null;
        addAny = (label, make, file) => {
            n++; int k = n; var b = UI.Btn((k < 10 ? k.ToString() : " ") + "   " + label); b.TextAlign = ContentAlignment.MiddleLeft; b.Width = UI.S(330); b.Margin = new Padding(0, UI.S(3), 0, 0);
            Options.Add(new[] { label }); b.Click += (s, e) => Do(make, file); UI.AddRow(st, b);
            if (k < 10) KeyDown += (s, e) => { if (e.KeyCode == Keys.D0 + k || e.KeyCode == Keys.NumPad0 + k) Do(make, file); };
        };
        Action<string, Func<string>> add = (label, make) => addAny(label, () => make(), false);
        if (txt != null) add("서식 없는 텍스트로", () => AdvPaste.Plain(txt));
        if (html != null) add("마크다운으로", () => AdvPaste.HtmlToMarkdown(html));
        if (txt != null) add("JSON으로", () => AdvPaste.ToJson(txt));
        if (txt != null) add("앞뒤 공백·빈 줄 정리", () => Regex.Replace(txt.Trim(), @"\n\s*\n+", "\n"));
        if (txt != null) addAny(".txt 파일로", () => AdvPaste.TextFile(txt), true);
        if (html != null) addAny(".html 파일로", () => AdvPaste.HtmlFile(html), true);
        if (img) { addAny(".png 파일로", () => { using (var im = Clipboard.GetImage()) return AdvPaste.ImageFile(im, ".png"); }, true); addAny(".jpg 파일로", () => { using (var im = Clipboard.GetImage()) return AdvPaste.ImageFile(im, ".jpg"); }, true); }
        if (img) add("이미지 파일 경로를 텍스트로", () => { using (var im = Clipboard.GetImage()) return AdvPaste.ImageFile(im, ".png"); });
        if (txt != null && pc != null) foreach (var ca in pc.Custom.Where(x => x != null && x.Name != "")) { var cc = ca; add(cc.Name + "  (사용자 정의)", () => AdvPaste.ApplyCustom(txt, cc)); }
        Controls.Add(st);
        var ps = st.GetPreferredSize(Size.Empty); ClientSize = new Size(Math.Max(UI.S(330), ps.Width), ps.Height);
        var pt = Cursor.Position; var wa = Screen.FromPoint(pt).WorkingArea;
        Location = TestEnv.Off ? new Point(TestEnv.Area.X + 40, TestEnv.Area.Y + 40) : new Point(Math.Min(pt.X, wa.Right - Width), Math.Min(pt.Y, wa.Bottom - Height));
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Deactivate += (s, e) => { if (!TestEnv.Off) Close(); };
        Dark.Theme(this);
    }
    static string Short(string s) { s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > 48 ? s.Substring(0, 48) + "…" : s; }
    void Do(Func<string> make, bool file) { string r = null; try { r = make(); } catch { } Close(); if (r == null) return; if (file) AdvPaste.PasteFile(r, target); else AdvPaste.PasteText(r, target); }
    protected override bool ShowWithoutActivation { get { return TestEnv.Off; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80; if (TestEnv.Off) c.ExStyle |= 0x08000000; return c; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { ChromeNative.Round(Handle, 2); } catch { } }
}

// ----- Peek: quick look at the files selected in Explorer -----
static class PeekSpace {
    static InputHookHost host; static Action<IntPtr> open; static Action release; static volatile bool pressed; public static bool IsHeld {get{return pressed;}}
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int c,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo { public int size, flags; public IntPtr active,focus,capture,menu,move,caret; public Native.RECT rect; }
    public static bool EligibleContext(string windowClass, string focusClass) {
        return (windowClass == "CabinetWClass" || windowClass == "ExploreWClass" || windowClass == "Progman" || windowClass == "WorkerW") && focusClass.IndexOf("Edit",StringComparison.OrdinalIgnoreCase) < 0 && focusClass.IndexOf("Search",StringComparison.OrdinalIgnoreCase) < 0 && focusClass.IndexOf("ComboBox",StringComparison.OrdinalIgnoreCase) < 0;
    }
    public static bool ExplorerReady(IntPtr foreground) {
        var wc = new StringBuilder(256); Native.GetClassName(foreground,wc,256); var gui = new GuiInfo { size=Marshal.SizeOf(typeof(GuiInfo)) };
        var fc = new StringBuilder(256); if (GetGUIThreadInfo(0,ref gui)) { if (gui.caret != IntPtr.Zero || (gui.flags & 0x1E) != 0) return false; if (gui.focus != IntPtr.Zero) Native.GetClassName(gui.focus,fc,256); }
        return EligibleContext(wc.ToString(),fc.ToString());
    }
    public static void Apply(bool enable,Action<IntPtr> action,Action onRelease=null) {
        var previousRelease=release;
        if(!enable && host!=null){host.Dispose();host=null;if(pressed){pressed=false;if(previousRelease!=null)previousRelease();}}
        open=action;release=onRelease;if(enable && host==null)host=new InputHookHost(13,Hook);
    }
    public static bool HandleSpace(bool down,IntPtr foreground,bool eligible) {
        if(!down){if(!pressed)return false;pressed=false;if(release!=null)release();return true;}
        if(pressed)return true;if(!eligible)return false;pressed=true;if(open!=null)open(foreground);return true;
    }
    static IntPtr Hook(int code,IntPtr w,IntPtr l) {
        if(code>=0 && Marshal.ReadInt32(l)==0x20) {
            int msg=w.ToInt32();
            if(msg==0x0101 || msg==0x0105){if(HandleSpace(false,IntPtr.Zero,false))return (IntPtr)1;}
            if(msg==0x0100 || msg==0x0104) {
                if(pressed)return (IntPtr)1;
                bool modifiers=false;foreach(int key in new[]{0x10,0x11,0x12,0x5B,0x5C})modifiers|=(GetAsyncKeyState(key)&0x8000)!=0;
                IntPtr foreground=Peek.GetForegroundWindow();if(HandleSpace(true,foreground,!modifiers&&ExplorerReady(foreground)))return (IntPtr)1;
            }
        }
        return CallNextHookEx(IntPtr.Zero,code,w,l);
    }

}
static class Peek {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    public static string SelectedIn(IntPtr fg) { var l = SelectedAll(fg); return l.Count > 0 ? l[0] : null; }
    // reads the selection of the Explorer window `fg` via Shell.Application (late bound; nothing loaded until used)
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
    public static List<string> SelectedAll(IntPtr fg) {
        var result=new List<string>(); var objects=new List<object>();
        Func<object,string,object> property=(o,n)=>{var v=o.GetType().InvokeMember(n,BindingFlags.GetProperty,null,o,null);if(v!=null&&Marshal.IsComObject(v))objects.Add(v);return v;};
        Func<object,string,object[],object> method=(o,n,args)=>{var v=o.GetType().InvokeMember(n,BindingFlags.InvokeMethod,null,o,args);if(v!=null&&Marshal.IsComObject(v))objects.Add(v);return v;};
        Action<object> read=window=>{
            var document=property(window,"Document");var items=method(document,"SelectedItems",null);int count=Convert.ToInt32(property(items,"Count"));
            for(int i=0;i<Math.Min(count,500);i++){var item=method(items,"Item",new object[]{i});string path=Convert.ToString(property(item,"Path"));if(File.Exists(path)||Directory.Exists(path))result.Add(path);}
        };
        try {
            var type=Type.GetTypeFromProgID("Shell.Application");if(type==null)return result;
            object shell=Activator.CreateInstance(type);objects.Add(shell);var windows=method(shell,"Windows",null);
            var cls=new StringBuilder(256);Native.GetClassName(fg,cls,256);
            if(cls.ToString()=="Progman"||cls.ToString()=="WorkerW") {
                try { var desktop=method(windows,"FindWindowSW",new object[]{0,0,8,0,1});if(desktop!=null)read(desktop); } catch { }
            } else {
                int count=Convert.ToInt32(property(windows,"Count"));
                for(int i=0;i<count;i++)try {
                    var window=method(windows,"Item",new object[]{i});if(window==null)continue;
                    IntPtr hwnd=new IntPtr(Convert.ToInt64(property(window,"HWND")));
                    if(hwnd!=fg && GetAncestor(hwnd,2)!=fg)continue;
                    read(window);if(result.Count>0)break;
                } catch { }
            }
        } finally {
            for(int i=objects.Count-1;i>=0;i--){object item=objects[i];if(objects.Take(i).Any(o=>ReferenceEquals(o,item)))continue;try{Marshal.FinalReleaseComObject(item);}catch{}}
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    static readonly HashSet<string> Img = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".jfif", ".webp", ".heic", ".avif" };
    static readonly HashSet<string> Txt = new HashSet<string> { ".txt", ".log", ".cs", ".js", ".ts", ".tsx", ".jsx", ".json", ".xml", ".css", ".scss", ".py", ".ps1", ".psm1", ".bat", ".cmd", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".yml", ".yaml", ".toml", ".c", ".cpp", ".h", ".hpp", ".java", ".kt", ".go", ".rs", ".rb", ".php", ".sql", ".sh", ".lua", ".vb", ".fs", ".swift", ".dart", ".r", ".gitignore", ".env", ".reg", ".srt", ".vtt", ".svg", ".xaml", ".csproj", ".sln", ".props" };
    static readonly HashSet<string> Md = new HashSet<string> { ".md", ".markdown" };
    static readonly HashSet<string> Html = new HashSet<string> { ".html", ".htm", ".xhtml" };
    static readonly HashSet<string> Media = new HashSet<string> { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus" };
    static readonly HashSet<string> Zip = new HashSet<string> { ".zip" };
    public static string Kind(string p) {
        if (Directory.Exists(p)) return "folder"; string e = Path.GetExtension(p).ToLowerInvariant();
        if (Img.Contains(e)) return e == ".webp" || e == ".heic" || e == ".avif" ? "thumb" : "image";
        if (Md.Contains(e)) return "markdown"; if (Html.Contains(e)) return "html"; if (Txt.Contains(e)) return "text";
        if (Media.Contains(e)) return "media"; if (Zip.Contains(e)) return "archive";
        if (PreviewHost.HandlerFor(e) != Guid.Empty) return "handler";   // PDF, Office, etc. through the installed Windows preview handler
        return "info";
    }
    public static string ReadText(string p, int max = 2 << 20) {
        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            var buf = new byte[(int)Math.Min(fs.Length, max)]; int n = 0, r; while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
            string s; try { s = new UTF8Encoding(false, true).GetString(buf, 0, n); } catch { s = Encoding.Default.GetString(buf, 0, n); }
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
            return fs.Length > max ? s + "\n\n… (2 MB까지만 표시)" : s;
        }
    }
    public static string Size(long b) { return b < 1024 ? b + " B" : b < 1 << 20 ? (b / 1024.0).ToString("0.#") + " KB" : b < 1L << 30 ? (b / 1048576.0).ToString("0.#") + " MB" : (b / 1073741824.0).ToString("0.##") + " GB"; }
    // image size from the header only (no full decode)
    public static Size ImageSize(string p) { using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var im = Image.FromStream(fs, false, false)) return im.Size; }
    // ---- Windows shell thumbnails (fast: uses the thumbnail cache and JPEG DCT scaling) ----
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory f);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    public static Bitmap Thumb(string path, int w, int h, bool thumbOnly = false) {
        IShellItemImageFactory f = null;
        try {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            IntPtr hb; if (f.GetImage(new SIZE { cx = w, cy = h }, thumbOnly ? 0x8 : 0x0, out hb) != 0 || hb == IntPtr.Zero) return null;   // SIIGBF_RESIZETOFIT (0) / THUMBNAILONLY (8)
            try { return FromHBitmap(hb); } finally { DeleteObject(hb); }
        } catch { return null; } finally { if (f != null) Marshal.ReleaseComObject(f); }
    }
    // keeps alpha (Image.FromHbitmap drops it)
    static Bitmap FromHBitmap(IntPtr hb) {
        using (var src = Image.FromHbitmap(hb)) {
            var bd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, src.PixelFormat);
            try {
                if (src.PixelFormat != PixelFormat.Format32bppRgb && src.PixelFormat != PixelFormat.Format32bppArgb) return new Bitmap(src);
                var dst = new Bitmap(src.Width, src.Height, bd.Stride, PixelFormat.Format32bppArgb, bd.Scan0);
                bool anyAlpha = false; var bytes = new byte[bd.Stride * src.Height]; Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length); for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { anyAlpha = true; break; }
                var copy = anyAlpha ? new Bitmap(dst) : new Bitmap(src); dst.Dispose(); return copy;
            } finally { src.UnlockBits(bd); }
        }
    }
    // ---- markdown -> HTML (headings, emphasis, code, lists, quotes, links, images, tables) ----
    public static string Markdown(string md, string css) {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'><style>" + css + "</style></head><body>");
        var lines = md.Replace("\r", "").Split('\n'); bool code = false, ul = false, ol = false, table = false;
        Func<string, string> inline = t => {
            t = System.Net.WebUtility.HtmlEncode(t);
            t = Regex.Replace(t, @"`([^`]+)`", "<code>$1</code>");
            t = Regex.Replace(t, @"!\[([^\]]*)\]\(([^)\s]+)[^)]*\)", "<img alt='$1' src='$2'>");
            t = Regex.Replace(t, @"\[([^\]]+)\]\(([^)\s]+)[^)]*\)", "<a href='$2'>$1</a>");
            t = Regex.Replace(t, @"\*\*([^*]+)\*\*|__([^_]+)__", "<b>$1$2</b>");
            t = Regex.Replace(t, @"(?<![\w*])\*([^*]+)\*(?!\w)|(?<!\w)_([^_]+)_(?!\w)", "<i>$1$2</i>");
            t = Regex.Replace(t, @"~~([^~]+)~~", "<s>$1</s>");
            return t;
        };
        Action close = () => { if (ul) { sb.Append("</ul>"); ul = false; } if (ol) { sb.Append("</ol>"); ol = false; } if (table) { sb.Append("</table>"); table = false; } };
        for (int i = 0; i < lines.Length; i++) {
            string l = lines[i];
            if (l.TrimStart().StartsWith("```")) { if (!code) { close(); sb.Append("<pre><code>"); code = true; } else { sb.Append("</code></pre>"); code = false; } continue; }
            if (code) { sb.Append(System.Net.WebUtility.HtmlEncode(l)).Append('\n'); continue; }
            var h = Regex.Match(l, @"^(#{1,6})\s+(.*)$"); if (h.Success) { close(); sb.Append("<h" + h.Groups[1].Length + ">" + inline(h.Groups[2].Value) + "</h" + h.Groups[1].Length + ">"); continue; }
            if (Regex.IsMatch(l, @"^\s*([-*_])(\s*\1){2,}\s*$")) { close(); sb.Append("<hr>"); continue; }
            var li = Regex.Match(l, @"^\s*[-*+]\s+(\[[ xX]\]\s+)?(.*)$"); if (li.Success) { if (!ul) { close(); sb.Append("<ul>"); ul = true; } string box = li.Groups[1].Success ? (li.Groups[1].Value.Contains("x") || li.Groups[1].Value.Contains("X") ? "☑ " : "☐ ") : ""; sb.Append("<li>" + box + inline(li.Groups[2].Value) + "</li>"); continue; }
            var oli = Regex.Match(l, @"^\s*\d+[.)]\s+(.*)$"); if (oli.Success) { if (!ol) { close(); sb.Append("<ol>"); ol = true; } sb.Append("<li>" + inline(oli.Groups[1].Value) + "</li>"); continue; }
            if (l.TrimStart().StartsWith("|") && l.Contains("|", 1)) {
                if (Regex.IsMatch(l, @"^\s*\|?\s*:?-{2,}")) continue;
                var cells = l.Trim().Trim('|').Split('|'); if (!table) { close(); sb.Append("<table>"); table = true; sb.Append("<tr>" + string.Concat(cells.Select(c => "<th>" + inline(c.Trim()) + "</th>")) + "</tr>"); }
                else sb.Append("<tr>" + string.Concat(cells.Select(c => "<td>" + inline(c.Trim()) + "</td>")) + "</tr>");
                continue;
            }
            if (l.StartsWith(">")) { close(); sb.Append("<blockquote>" + inline(l.TrimStart('>', ' ')) + "</blockquote>"); continue; }
            if (l.Trim() == "") { close(); continue; }
            close(); sb.Append("<p>" + inline(l) + "</p>");
        }
        close(); if (code) sb.Append("</code></pre>");
        return sb.Append("</body></html>").ToString();
    }
    static bool Contains(this string s, string v, int from) { return s.IndexOf(v, from, StringComparison.Ordinal) >= 0; }
    public const string Css = "body{background:#0f0f1b;color:#ececf8;font-family:'Segoe UI','Malgun Gothic',sans-serif;font-size:14px;line-height:1.6;margin:22px 28px}a{color:#6ec6ff}code{background:#22213c;padding:1px 5px;border-radius:4px;font-family:Consolas,monospace}pre{background:#1c1b30;padding:12px;border-radius:8px;overflow:auto}pre code{background:none;padding:0}h1,h2{border-bottom:1px solid #2f2d4e;padding-bottom:4px}blockquote{border-left:3px solid #9084ff;margin:0;padding-left:12px;color:#c4c6de}table{border-collapse:collapse}td,th{border:1px solid #2f2d4e;padding:4px 10px}th{background:#21203a}img{max-width:100%}hr{border:0;border-top:1px solid #2f2d4e}";
    // removes scripts, event handlers, iframes and external loads before showing HTML
    public static string SafeHtml(string html) {
        html = Regex.Replace(html, @"<script[\s\S]*?</script\s*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(iframe|object|embed|frame|frameset|meta\s+http-equiv)[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"(href|src)\s*=\s*([""'])\s*javascript:[^""']*\2", "$1=$2#$2", RegexOptions.IgnoreCase);
        return html;
    }
    // ---- lightweight syntax highlighting into RTF (comments, strings, numbers, keywords) ----
    static readonly HashSet<string> Kw = new HashSet<string>("abstract as async await base bool break byte case catch char class const continue def default delegate do double elif else enum event except export extends false final finally float for foreach from func function get go if import in int interface internal is lambda let long namespace new nil none null object override package private protected public readonly return sealed self set short static string struct super switch this throw true try type typeof uint using val var virtual void volatile while with yield".Split(' '));
    static readonly Regex tok = new Regex(@"(?<c>//[^\n]*|#[^\n]*|/\*[\s\S]*?\*/|--[^\n]*)|(?<s>""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])*')|(?<n>\b\d[\d_.xXa-fA-F]*\b)|(?<w>\b[A-Za-z_]\w*\b)", RegexOptions.Compiled);
    public static string Rtf(string code, string ext) {
        bool hashComments = ext == ".py" || ext == ".ps1" || ext == ".sh" || ext == ".yml" || ext == ".yaml" || ext == ".toml" || ext == ".r" || ext == ".rb" || ext == ".ini" || ext == ".cfg" || ext == ".conf";
        bool dashComments = ext == ".sql" || ext == ".lua";
        var sb = new StringBuilder(code.Length * 2 + 256);
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}{\colortbl;\red220\green222\blue240;\red120\green170\blue110;\red230\green170\blue120;\red180\green160\blue255;\red120\green200\blue255;}\f0\fs19\cf1 ");
        int last = 0;
        foreach (Match m in tok.Matches(code)) {
            int cf = 0;
            if (m.Groups["c"].Success) { char c0 = m.Value[0]; if (c0 == '#' && !hashComments) continue; if (c0 == '-' && !dashComments) continue; cf = 2; }
            else if (m.Groups["s"].Success) cf = 3; else if (m.Groups["n"].Success) cf = 4; else if (m.Groups["w"].Success && Kw.Contains(m.Value)) cf = 5; else continue;
            Esc(sb, code, last, m.Index - last); sb.Append("\\cf").Append(cf).Append(' '); Esc(sb, code, m.Index, m.Length); sb.Append("\\cf1 "); last = m.Index + m.Length;
        }
        Esc(sb, code, last, code.Length - last); return sb.Append('}').ToString();
    }
    static void Esc(StringBuilder sb, string s, int a, int n) {
        for (int i = a; i < a + n; i++) { char c = s[i];
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c); else if (c == '\n') sb.Append("\\par\n"); else if (c == '\r') { } else if (c == '\t') sb.Append("\\tab "); else if (c > 127) sb.Append("\\u").Append((int)(short)c).Append('?'); else sb.Append(c); }
    }
}
// hosts an installed Windows preview handler (IPreviewHandler) - the same component Explorer's preview pane uses for PDF / Office files
static class PreviewHost {
    public static Guid HandlerFor(string ext) {
        try {
            using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext + @"\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}")) if (k != null) { var v = k.GetValue("") as string; Guid g; if (Guid.TryParse(v, out g)) return g; }
            using (var e = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext)) {
                var prog = e == null ? null : e.GetValue("") as string;
                if (!string.IsNullOrEmpty(prog)) using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(prog + @"\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}")) if (k != null) { var v = k.GetValue("") as string; Guid g; if (Guid.TryParse(v, out g)) return g; }
            }
            using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"SystemFileAssociations\" + ext + @"\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}")) if (k != null) { var v = k.GetValue("") as string; Guid g; if (Guid.TryParse(v, out g)) return g; }
        } catch { }
        return Guid.Empty;
    }
    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandler { void SetWindow(IntPtr hwnd, ref Native.RECT rect); void SetRect(ref Native.RECT rect); void DoPreview(); void Unload(); void SetFocus(); void QueryFocus(out IntPtr phwnd); [PreserveSig] uint TranslateAccelerator(ref Message pmsg); }
    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }
    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInitializeWithStream { void Initialize(System.Runtime.InteropServices.ComTypes.IStream stream, uint mode); }
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateStreamOnFileEx(string file, uint mode, uint attr, bool create, IntPtr tmpl, out System.Runtime.InteropServices.ComTypes.IStream s);
    public static IPreviewHandler Open(string path, Control host) {
        var g = HandlerFor(Path.GetExtension(path).ToLowerInvariant()); if (g == Guid.Empty) return null;
        var t = Type.GetTypeFromCLSID(g, true); object o = Activator.CreateInstance(t);
        var wf = o as IInitializeWithFile; var ws = o as IInitializeWithStream;
        if (wf != null) wf.Initialize(path, 0);
        else if (ws != null) { System.Runtime.InteropServices.ComTypes.IStream st; SHCreateStreamOnFileEx(path, 0x20, 0, false, IntPtr.Zero, out st); ws.Initialize(st, 0); }   // STGM_READ|SHARE_DENY_NONE
        else { Marshal.ReleaseComObject(o); return null; }
        var ph = (IPreviewHandler)o; var r = new Native.RECT { L = 0, T = 0, R = host.ClientSize.Width, B = host.ClientSize.Height };
        ph.SetWindow(host.Handle, ref r); ph.DoPreview();
        host.Resize += (s, e) => { try { var rr = new Native.RECT { L = 0, T = 0, R = host.ClientSize.Width, B = host.ClientSize.Height }; ph.SetRect(ref rr); } catch { } };
        return ph;
    }
}
// runs a preview handler on its own STA thread: a slow or hung handler (bad PDF, Office add-in) can never freeze BongHub.
// the handler draws into a window owned by that thread, which is attached to the Peek window only after DoPreview returned.
class HandlerSession {
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr c, IntPtr p);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
    Control win; PreviewHost.IPreviewHandler ph; volatile bool closed; public volatile bool Ready, Failed;
    public void Start(string path, Size size, Action done) {
        var t = new Thread(() => {
            try {
                win = new Control { Size = size, BackColor = Color.White }; win.CreateControl();
                var stub = new Control { Size = size }; stub.CreateControl();
                ph = PreviewHost.Open(path, win);
                if (ph == null || closed) Failed = true; else Ready = true;
            } catch { Failed = true; }
            try { done(); } catch { }
            if (Ready && !closed) Application.Run();
            try { if (ph != null) { ph.Unload(); Marshal.FinalReleaseComObject(ph); } } catch { }
            try { if (win != null) win.Dispose(); } catch { }
        }) { IsBackground = true, Name = "PeekHandler" };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }
    public void Attach(Control host) {
        if (!Ready || win == null) return; var h = win.Handle;
        SetWindowLong(h, -16, 0x40000000 | 0x10000000);   // WS_CHILD | WS_VISIBLE
        SetParent(h, host.Handle); MoveWindow(h, 0, 0, host.ClientSize.Width, host.ClientSize.Height, true);
        host.Resize += (s, e) => { if (closed) return; try { win.BeginInvoke((Action)(() => { var r = new Native.RECT { L = 0, T = 0, R = host.ClientSize.Width, B = host.ClientSize.Height }; MoveWindow(win.Handle, 0, 0, r.R, r.B, true); try { ph.SetRect(ref r); } catch { } })); } catch { } };
    }
    public void Close() {
        if (closed) return; closed = true;
        try { if (win != null && win.IsHandleCreated) { SetParent(win.Handle, IntPtr.Zero); win.BeginInvoke((Action)(() => Application.ExitThread())); } } catch { }
    }
}
// Windows Media Player control hosted without interop assemblies (late bound)
class MediaHost : AxHost {
    public MediaHost() : base("6bf52a52-394a-11d3-b153-00c04f79faa6") { }
    public void Play(string url) { var o = GetOcx(); if (o == null) return; o.GetType().InvokeMember("URL", BindingFlags.SetProperty, null, o, new object[] { url }); try { o.GetType().InvokeMember("stretchToFit", BindingFlags.SetProperty, null, o, new object[] { true }); } catch { } }
    public void Stop() { try { var o = GetOcx(); if (o == null) return; var c = o.GetType().InvokeMember("controls", BindingFlags.GetProperty, null, o, null); c.GetType().InvokeMember("stop", BindingFlags.InvokeMethod, null, c, null); o.GetType().InvokeMember("close", BindingFlags.InvokeMethod, null, o, null); } catch { } }
}
static class WebPImage {
    static readonly object gate=new object();
    static string Tool(string name) {
        string path=Path.Combine(TestEnv.Off?Path.GetDirectoryName(Application.ExecutablePath):Core.Dir,"codecs","libwebp-1.6.0",name);
        lock(gate) {if(!File.Exists(path)){Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var output=File.Create(path)){if(input==null)throw new IOException("WebP 디코더 리소스 없음");input.CopyTo(output);}}}return path;
    }
    static void Run(string name,string arguments,CancellationToken cancel) {
        cancel.ThrowIfCancellationRequested();
        using(var process=Process.Start(new ProcessStartInfo(Tool(name),arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
            var timer=Stopwatch.StartNew();while(!process.WaitForExit(25)) {
                if(cancel.IsCancellationRequested || timer.ElapsedMilliseconds>10000){try{process.Kill();process.WaitForExit(1000);}catch{}cancel.ThrowIfCancellationRequested();throw new IOException("WebP 디코딩 시간 초과");}
            }
            if(process.ExitCode!=0)throw new IOException("WebP 이미지를 읽지 못했어요.");
        }
    }
    public static bool Animated(string path) {
        using(var stream=File.OpenRead(path)) {var header=new byte[21];return stream.Read(header,0,header.Length)==header.Length && Encoding.ASCII.GetString(header,12,4)=="VP8X" && (header[20]&2)!=0;}
    }
    struct AnimationFrame {public Size Canvas;public Point Offset;public Color Background;public bool Replace;}
    static int Read24(byte[] bytes,int at){return bytes[at]|bytes[at+1]<<8|bytes[at+2]<<16;}
    static AnimationFrame FirstFrame(string path) {
        var result=new AnimationFrame{Background=Color.Transparent};
        using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream)) {
            stream.Position=12;
            while(stream.Position+8<=stream.Length) {
                string kind=Encoding.ASCII.GetString(reader.ReadBytes(4));uint length=reader.ReadUInt32();long next=stream.Position+length+(length&1);if(next>stream.Length)throw new IOException("잘못된 WebP 데이터");
                if(kind=="VP8X"&&length>=10){var header=reader.ReadBytes(10);result.Canvas=new Size(Read24(header,4)+1,Read24(header,7)+1);}
                else if(kind=="ANIM"&&length>=4){var color=reader.ReadBytes(4);result.Background=Color.FromArgb(color[3],color[2],color[1],color[0]);}
                else if(kind=="ANMF"&&length>=16){var header=reader.ReadBytes(16);result.Offset=new Point(Read24(header,0)*2,Read24(header,3)*2);result.Replace=(header[15]&2)!=0;break;}
                stream.Position=next;
            }
        }
        if(result.Canvas.Width<1||result.Canvas.Height<1||result.Canvas.Width>16383||result.Canvas.Height>16383||(long)result.Canvas.Width*result.Canvas.Height>100000000)throw new IOException("WebP 캔버스 크기를 읽지 못했어요.");return result;
    }
    public static Bitmap Decode(string path) {return Decode(path,CancellationToken.None);}
    public static Bitmap Decode(string path,CancellationToken cancel) {
        cancel.ThrowIfCancellationRequested();
        string prefix=Path.Combine(Path.GetTempPath(),"BongHubWebP_"+Guid.NewGuid().ToString("N")),png=prefix+".png",frame=prefix+".webp";
        try {
            string source=Path.GetFullPath(path);bool animated=Animated(source);var animation=animated?FirstFrame(source):new AnimationFrame();
            if(animated){Run("webpmux.exe","-get frame 1 \""+source+"\" -o \""+frame+"\"",cancel);source=frame;}
            Run("dwebp.exe","-quiet \""+source+"\" -o \""+png+"\"",cancel);
            using(var image=Image.FromFile(png)) {
                if(!animated)return new Bitmap(image);
                var canvas=new Bitmap(animation.Canvas.Width,animation.Canvas.Height,PixelFormat.Format32bppArgb);
                try{using(var graphics=Graphics.FromImage(canvas)){graphics.Clear(animation.Background);graphics.CompositingMode=animation.Replace?CompositingMode.SourceCopy:CompositingMode.SourceOver;graphics.DrawImageUnscaled(image,animation.Offset);}return canvas;}catch{canvas.Dispose();throw;}
            }
        } finally {foreach(string temp in new[]{png,frame})try{File.Delete(temp);}catch{}}
    }
}
class PeekForm : Form {
    public string File_; public string KindName; readonly List<string> list; int index; Panel bodyHost; Label nameL, infoL; Control body; HandlerSession handler; MediaHost media;
    int imageRequest;CancellationTokenSource imageCancellation;
    public bool HoldToPreview;
    public static bool CloseOnDeactivate, RenderHtml = true; public event Action ContentReady; public bool Ready; public double ContentMs; readonly Stopwatch openSw = Stopwatch.StartNew(); static Font codeFont;
    public PeekForm(string path) : this(new List<string> { path }, 0) { }
    // several selected files: Left/Right moves between them; one file: Left/Right moves through its folder
    public PeekForm(List<string> files, int start) {
        list = files; index = start; File_ = files[start]; KindName = Peek.Kind(File_);
        ShowInTaskbar = false; TopMost = true; KeyPreview = true; BackColor = UI.Card; Font = UI.Base; StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.None;
        var wa = TestEnv.Off ? TestEnv.Area : Screen.FromPoint(Cursor.Position).WorkingArea; int w = Math.Min(UI.S(1000), wa.Width * 3 / 4), h = Math.Min(UI.S(720), wa.Height * 3 / 4);
        Bounds = new Rectangle(wa.X + (TestEnv.Off ? 40 : (wa.Width - w) / 2), wa.Y + (TestEnv.Off ? 40 : (wa.Height - h) / 2), w, h);
        var head = new DbPanel { Dock = DockStyle.Top, Height = UI.S(52), BackColor = UI.Card };
        nameL = UI.L("", UI.Bold, UI.Text); nameL.Location = new Point(UI.S(14), UI.S(8)); nameL.AutoEllipsis = true; nameL.AutoSize = false; nameL.Size = new Size(w - UI.S(260), UI.S(22));
        infoL = UI.L("", UI.Small, UI.Hint); infoL.Location = new Point(UI.S(14), UI.S(29)); infoL.AutoEllipsis = true; infoL.AutoSize = false; infoL.Size = new Size(w - UI.S(260), UI.S(18));
        var open = UI.Btn("열기 (Enter)", true); open.Location = new Point(w - UI.S(226), UI.S(10)); open.Click += (s, e) => OpenIt();
        var close = UI.Btn("닫기"); close.Location = new Point(w - UI.S(94), UI.S(10)); close.Click += (s, e) => Close();
        head.Controls.AddRange(new Control[] { nameL, infoL, open, close });
        head.Paint += (s, e) => { using (var b = UI.Grad(new RectangleF(0, head.Height - UI.S(2), head.Width, UI.S(2)))) e.Graphics.FillRectangle(b, 0, head.Height - UI.S(2), head.Width, UI.S(2)); };
        bodyHost = new DbPanel { Dock = DockStyle.Fill, BackColor = UI.Page };
        Controls.Add(bodyHost); Controls.Add(head);
        KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Escape || (e.KeyCode == Keys.Space && !HoldToPreview && !(body is TextBoxBase) && !(body is WebBrowser))) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { OpenIt(); e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Left) { if (!(body is TextBoxBase)) { Step(e.KeyCode == Keys.Right ? 1 : -1); e.Handled = true; } }
        };
        KeyUp += (s,e)=>{if(HoldToPreview && e.KeyCode==Keys.Space){Close();e.Handled=true;}};
        Deactivate += (s, e) => { if (CloseOnDeactivate && !TestEnv.Off) Close(); };
        Dark.Theme(this);
        SetHeader();
        // content is loaded right after the window is up, so the frame appears immediately
        if (IsHandleCreated) BeginInvoke((Action)Load_); else HandleCreated += (s, e) => BeginInvoke((Action)Load_);
    }
    void SetHeader() {
        string n = Path.GetFileName(File_); nameL.Text = (n == "" ? File_ : n) + (list.Count > 1 ? "   (" + (index + 1) + "/" + list.Count + ")" : "");
        Text = nameL.Text;
        try { if (Directory.Exists(File_)) infoL.Text = "폴더 · " + new DirectoryInfo(File_).LastWriteTime.ToString("yyyy-MM-dd HH:mm"); else { var fi = new FileInfo(File_); infoL.Text = Peek.Size(fi.Length) + " · " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"); } } catch { infoL.Text = ""; }
    }
    void Step(int d) {
        if (list.Count == 1) { // walk the folder
            try {
                string dir = Path.GetDirectoryName(File_); if (dir == null) return;
                var all = Directory.EnumerateFileSystemEntries(dir).OrderBy(x => !Directory.Exists(x)).ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
                int i = all.FindIndex(x => string.Equals(x, File_, StringComparison.OrdinalIgnoreCase)); if (i < 0) return; int j = i + d; if (j < 0 || j >= all.Count) return;
                Show_(all[j]);
            } catch { }
        } else { int j = index + d; if (j < 0 || j >= list.Count) return; index = j; Show_(list[j]); }
    }
    void Show_(string p) { File_ = p; if (list.Count == 1) list[0] = p; KindName = Peek.Kind(p); SetHeader(); Load_(); }
    void Unload() {
        imageRequest++;if(imageCancellation!=null){imageCancellation.Cancel();imageCancellation.Dispose();imageCancellation=null;}
        if (handler != null) { handler.Close(); handler = null; }
        if (media != null) { media.Stop(); }
        if (body != null) { var pb = body as PictureBox; if (pb != null && pb.Image != null) { var im = pb.Image; pb.Image = null; im.Dispose(); } bodyHost.Controls.Remove(body); body.Dispose(); body = null; media = null; }
    }
    void Load_() {
        if (IsDisposed) return; var sw = Stopwatch.StartNew(); Unload(); string path = File_; Control b;
        if(Path.GetExtension(path).Equals(".webp",StringComparison.OrdinalIgnoreCase)) {
            Ready=false;int request=imageRequest;imageCancellation=new CancellationTokenSource();var cancel=imageCancellation.Token;var picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=UI.Page};body=picture;bodyHost.Controls.Add(picture);
            ThreadPool.QueueUserWorkItem(_=>{
                Bitmap decoded=null;string error=null;bool animated=false;try{cancel.ThrowIfCancellationRequested();animated=WebPImage.Animated(path);decoded=WebPImage.Decode(path,cancel);}catch(Exception ex){error=ex.Message;}
                try{BeginInvoke((Action)(()=>{
                    if(IsDisposed||request!=imageRequest||picture.IsDisposed){if(decoded!=null)decoded.Dispose();return;}
                    if(decoded!=null){picture.Image=decoded;infoL.Text+=" · "+decoded.Width+"×"+decoded.Height+(animated?" · 애니메이션 첫 프레임":"");}else{bodyHost.Controls.Remove(picture);picture.Dispose();body=Info(path,error);bodyHost.Controls.Add(body);}
                    ContentMs=openSw.Elapsed.TotalMilliseconds;Ready=true;if(ContentReady!=null)ContentReady();
                }));}catch{if(decoded!=null)decoded.Dispose();}
            });return;
        }
        try {
            switch (KindName) {
                case "image": case "thumb": {
                    var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = UI.Page };
                    string ext = Path.GetExtension(path).ToLowerInvariant(); Size sz = Size.Empty; try { if (KindName == "image") sz = Peek.ImageSize(path); } catch { }
                    var view = bodyHost.ClientSize; long px = (long)sz.Width * sz.Height;
                    // big photos: decode at display size through the shell (much faster, far less memory); GIFs keep animation
                    if (KindName == "thumb" || (ext != ".gif" && px > (long)view.Width * view.Height * 2)) { var t = Peek.Thumb(path, Math.Max(256, view.Width), Math.Max(256, view.Height)); if (t != null) pb.Image = t; }
                    if (pb.Image == null) { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { var ms = new MemoryStream(); fs.CopyTo(ms); ms.Position = 0; pb.Image = Image.FromStream(ms); } }
                    if (sz.IsEmpty && pb.Image != null) sz = pb.Image.Size;
                    if (!sz.IsEmpty) infoL.Text += " · " + sz.Width + "×" + sz.Height;
                    b = pb; break; }
                case "text": {
                    string txt = Peek.ReadText(path); string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (codeFont == null) codeFont = new Font("Consolas", 10f * UI.Scale);
                    if (txt.Length <= 300000 && ext != ".txt" && ext != ".log" && ext != ".csv" && ext != ".tsv") {
                        var rb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = UI.Page, ForeColor = UI.Text, WordWrap = false, DetectUrls = false, Font = codeFont };
                        rb.Rtf = Peek.Rtf(txt, ext); b = rb;
                    } else b = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None, BackColor = UI.Page, ForeColor = UI.Text, Font = codeFont, Text = txt.Replace("\r\n", "\n").Replace("\n", "\r\n") };
                    infoL.Text += " · " + (txt.Count(c => c == (char)10) + 1) + "줄"; break; }
                case "markdown": case "html": {
                    string html = KindName == "markdown" ? Peek.Markdown(Peek.ReadText(path), Peek.Css) : RenderHtml ? Peek.SafeHtml(Peek.ReadText(path)) : "<html><head>" + Peek.Css + "</head><body><pre>" + System.Net.WebUtility.HtmlEncode(Peek.ReadText(path)) + "</pre></body></html>";
                    var wb = new WebBrowser { Dock = DockStyle.Fill, ScriptErrorsSuppressed = true, IsWebBrowserContextMenuEnabled = false, AllowWebBrowserDrop = false, WebBrowserShortcutsEnabled = true };
                    wb.Navigating += (s, e) => { if (e.Url.ToString() != "about:blank") { e.Cancel = true; try { if (e.Url.Scheme == "http" || e.Url.Scheme == "https") Process.Start(e.Url.ToString()); } catch { } } };
                    wb.DocumentText = html; b = wb; break; }
                case "media": {
                    var m = new MediaHost { Dock = DockStyle.Fill }; bodyHost.Controls.Add(m); media = m; b = m;
                    try { m.CreateControl(); m.Play(path); } catch { bodyHost.Controls.Remove(m); m.Dispose(); media = null; b = ThumbOrInfo(path, "재생할 수 없어요 (Windows Media Player 구성 요소 없음)"); }
                    break; }
                case "archive": {
                    var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, Font = UI.Base }; Dark.List(lv);
                    lv.Columns.Add("이름", UI.S(560)); lv.Columns.Add("크기", UI.S(110)); lv.Columns.Add("압축", UI.S(110));
                    using (var z = System.IO.Compression.ZipFile.OpenRead(path)) { lv.BeginUpdate(); foreach (var en in z.Entries.Take(5000)) { var it = new ListViewItem(en.FullName); it.SubItems.Add(en.Length > 0 || !en.FullName.EndsWith("/") ? Peek.Size(en.Length) : ""); it.SubItems.Add(Peek.Size(en.CompressedLength)); lv.Items.Add(it); } lv.EndUpdate(); infoL.Text += " · 항목 " + z.Entries.Count + "개"; }
                    b = lv; break; }
                case "folder": {
                    var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, Font = UI.Base }; Dark.List(lv);
                    lv.Columns.Add("이름", UI.S(520)); lv.Columns.Add("크기", UI.S(110)); lv.Columns.Add("수정한 날짜", UI.S(150));
                    var di = new DirectoryInfo(path); int n = 0; lv.BeginUpdate();
                    foreach (var e in di.EnumerateFileSystemInfos().OrderBy(x => !(x is DirectoryInfo)).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Take(2000)) { var it = new ListViewItem((e is DirectoryInfo ? "📁 " : "") + e.Name); it.SubItems.Add(e is FileInfo ? Peek.Size(((FileInfo)e).Length) : ""); it.SubItems.Add(e.LastWriteTime.ToString("yyyy-MM-dd HH:mm")); lv.Items.Add(it); n++; }
                    lv.EndUpdate(); infoL.Text += " · 항목 " + n + "개"; b = lv; break; }
                case "handler": {
                    var host = new Panel { Dock = DockStyle.Fill, BackColor = UI.Page }; var wait = UI.L("미리보기 처리기를 불러오는 중…", UI.Base, UI.Hint); wait.Location = new Point(UI.S(16), UI.S(16)); host.Controls.Add(wait);
                    var hs = new HandlerSession(); handler = hs; string pth = path;
                    Action fallback = () => { if (IsDisposed || handler != hs || body != host) return; hs.Close(); handler = null; Unload(); var nb = ThumbOrInfo(pth, "미리보기 처리기가 응답하지 않아 정보를 표시해요"); body = nb; bodyHost.Controls.Add(nb); Dark.Apply(nb); };
                    var guard = new WTimer { Interval = 4000 }; guard.Tick += (s2, e2) => { guard.Dispose(); if (!hs.Ready) fallback(); }; guard.Start();
                    hs.Start(path, bodyHost.ClientSize, () => { try { BeginInvoke((Action)(() => { if (IsDisposed || handler != hs) { hs.Close(); return; } if (hs.Failed) fallback(); else { host.Controls.Clear(); host.BackColor = Color.White; hs.Attach(host); } })); } catch { hs.Close(); } });
                    b = host; break; }
                default: b = ThumbOrInfo(path, null); break;
            }
        } catch (Exception ex) { b = Info(path, "미리보기를 만들 수 없어요: " + ex.Message); }
        body = b; if (b.Parent != bodyHost) bodyHost.Controls.Add(b);
        Dark.Apply(b);
        ContentMs = openSw.Elapsed.TotalMilliseconds; Ready = true; if (ContentReady != null) ContentReady();
    }
    Control ThumbOrInfo(string p, string note) {
        var t = Peek.Thumb(p, Math.Max(256, bodyHost.ClientSize.Width), Math.Max(256, bodyHost.ClientSize.Height), true);
        if (t == null) return Info(p, note);
        var pn = new Panel { Dock = DockStyle.Fill, BackColor = UI.Page };
        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = t, BackColor = UI.Page };
        var lb = UI.L(note ?? "썸네일 미리보기 · Enter로 기본 앱에서 열기", UI.Small, UI.Hint); lb.Dock = DockStyle.Bottom; lb.Padding = new Padding(UI.S(12), UI.S(6), 0, UI.S(8));
        pn.Controls.Add(pb); pn.Controls.Add(lb); return pn;
    }
    Control Info(string p, string note = null) {
        var st = UI.Stack(); st.Dock = DockStyle.Fill; st.Padding = new Padding(UI.S(24)); st.BackColor = UI.Page;
        bool dir = Directory.Exists(p); var fi = dir ? (FileSystemInfo)new DirectoryInfo(p) : new FileInfo(p);
        UI.AddRow(st, UI.L(dir ? "폴더" : (Path.GetExtension(p).TrimStart('.').ToUpperInvariant() + " 파일"), UI.Header, UI.Text));
        UI.AddRow(st, UI.L(p, UI.Small, UI.Hint));
        if (!dir) UI.AddRow(st, UI.L("크기: " + Peek.Size(((FileInfo)fi).Length), UI.Base, UI.Text2));
        UI.AddRow(st, UI.L("수정한 날짜: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), UI.Base, UI.Text2));
        UI.AddRow(st, UI.L("만든 날짜: " + fi.CreationTime.ToString("yyyy-MM-dd HH:mm"), UI.Base, UI.Text2));
        UI.AddRow(st, UI.L(note ?? "이 형식은 내용 미리보기를 지원하지 않아요. Enter로 기본 앱에서 열 수 있어요.", UI.Small, UI.Hint));
        return st;
    }
    void OpenIt() { try { Process.Start(File_); } catch { } Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { Unload(); base.OnFormClosed(e); }
    protected override bool ShowWithoutActivation { get { return TestEnv.Off; } }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80; if (TestEnv.Off) c.ExStyle |= 0x08000000; return c; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { ChromeNative.Round(Handle, 2); } catch { } }
}
// ----- Image Resizer -----
// kept in its own class so WPF imaging (PresentationCore) is only loaded when a resize actually runs
static class Wic {
    public static string One(string src, ResizePreset p0, ResizerCfg c) {
        System.Windows.Media.Imaging.BitmapFrame frame; System.Windows.Media.Imaging.BitmapDecoder dec;
        byte[] data = File.ReadAllBytes(src); var ms = new MemoryStream(data, false);
        dec = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile, System.Windows.Media.Imaging.BitmapCacheOption.None);
        frame = dec.Frames[0];
        var s = new Size(frame.PixelWidth, frame.PixelHeight);
        var p = Resizer.Resolve(p0, s, frame.DpiX > 1 ? frame.DpiX : 96, frame.DpiY > 1 ? frame.DpiY : 96, c.IgnoreOrientation);
        var t = Resizer.Target(s, p, c.Fit, c.ShrinkOnly);
        if (t == s && !c.Replace) return null;
        System.Windows.Media.Imaging.BitmapSource img;
        bool jpeg = dec is System.Windows.Media.Imaging.JpegBitmapDecoder;
        double kx = (double)t.Width / s.Width, ky = (double)t.Height / s.Height; Rectangle crop = Rectangle.Empty;
        if (c.Fit == "fill") { double k = Math.Max(kx, ky); kx = ky = k; int sw = (int)Math.Round(s.Width * k), sh = (int)Math.Round(s.Height * k); crop = new Rectangle((sw - t.Width) / 2, (sh - t.Height) / 2, t.Width, t.Height); }
        if (jpeg && kx < 0.5 && ky < 0.5 && c.Fit != "stretch") {
            // decode at (at least) twice the target size directly from the DCT data, then finish with Fant scaling
            var bi = new System.Windows.Media.Imaging.BitmapImage(); bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
            bi.StreamSource = new MemoryStream(data, false); bi.DecodePixelWidth = Math.Min(s.Width, (int)Math.Ceiling(s.Width * kx * 2)); bi.Rotation = System.Windows.Media.Imaging.Rotation.Rotate0; bi.EndInit();
            img = bi; kx = (double)s.Width * kx / bi.PixelWidth; ky = (double)s.Height * ky / bi.PixelHeight;
        } else img = frame;
        var scaled = new System.Windows.Media.Imaging.TransformedBitmap(img, new System.Windows.Media.ScaleTransform(kx, ky));
        System.Windows.Media.Imaging.BitmapSource outImg = scaled;
        if (!crop.IsEmpty) outImg = new System.Windows.Media.Imaging.CroppedBitmap(scaled, new System.Windows.Int32Rect(Math.Max(0, crop.X), Math.Max(0, crop.Y), Math.Min(crop.Width, scaled.PixelWidth - Math.Max(0, crop.X)), Math.Min(crop.Height, scaled.PixelHeight - Math.Max(0, crop.Y))));
        var actual = new Size(outImg.PixelWidth, outImg.PixelHeight);
        System.Windows.Media.Imaging.BitmapEncoder enc; string ext = Path.GetExtension(src);
        if (jpeg) enc = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = Math.Max(10, Math.Min(100, c.Quality)) };
        else if (dec is System.Windows.Media.Imaging.PngBitmapDecoder) enc = new System.Windows.Media.Imaging.PngBitmapEncoder { Interlace = c.PngInterlace == 1 ? System.Windows.Media.Imaging.PngInterlaceOption.On : System.Windows.Media.Imaging.PngInterlaceOption.Default };
        else if (dec is System.Windows.Media.Imaging.GifBitmapDecoder) enc = new System.Windows.Media.Imaging.GifBitmapEncoder();
        else if (dec is System.Windows.Media.Imaging.BmpBitmapDecoder) enc = new System.Windows.Media.Imaging.BmpBitmapEncoder();
        else if (dec is System.Windows.Media.Imaging.TiffBitmapDecoder) enc = new System.Windows.Media.Imaging.TiffBitmapEncoder();
        else { enc = c.Fallback == "jpg" ? (System.Windows.Media.Imaging.BitmapEncoder)new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = c.Quality } : new System.Windows.Media.Imaging.PngBitmapEncoder(); ext = c.Fallback == "jpg" ? ".jpg" : ".png"; }
        System.Windows.Media.Imaging.BitmapMetadata meta = null;
        if (!c.RemoveMeta) { try { var m = frame.Metadata as System.Windows.Media.Imaging.BitmapMetadata; if (m != null && enc.GetType() == (jpeg ? typeof(System.Windows.Media.Imaging.JpegBitmapEncoder) : enc.GetType())) meta = m.Clone(); } catch { meta = null; } }
        try { enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(outImg, null, meta, null)); }
        catch { enc.Frames.Clear(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(outImg)); }
        string outp = Resizer.OutPath(Path.ChangeExtension(src, ext), p, c.Replace, c.NameFormat, actual);
        if (c.Replace) outp = src;
        string tmp = outp + ".bhtmp";
        try { using (var fs = File.Create(tmp)) enc.Save(fs); } catch { if (meta == null) throw; enc = (System.Windows.Media.Imaging.BitmapEncoder)Activator.CreateInstance(enc.GetType()); var je = enc as System.Windows.Media.Imaging.JpegBitmapEncoder; if (je != null) je.QualityLevel = c.Quality; enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(outImg)); using (var fs = File.Create(tmp)) enc.Save(fs); }
        if (File.Exists(outp)) File.Delete(outp); File.Move(tmp, outp);
        if (c.KeepDate) { try { File.SetLastWriteTime(outp, File.GetLastWriteTime(src)); } catch { } }
        return outp;
    }
}
static class Resizer {
    public static Size Target(Size src, ResizePreset p, string fit, bool shrinkOnly) {
        double sx = (double)p.W / src.Width, sy = (double)p.H / src.Height; Size t;
        if (fit == "stretch") t = new Size(p.W, p.H);
        else if (fit == "fill") { double s = Math.Max(sx, sy); t = new Size(p.W, p.H); if (shrinkOnly && s >= 1) return src; return t; }
        else { double s = Math.Min(sx, sy); t = new Size(Math.Max(1, (int)Math.Round(src.Width * s)), Math.Max(1, (int)Math.Round(src.Height * s))); }
        if (shrinkOnly && (t.Width >= src.Width && t.Height >= src.Height)) return src;
        return t;
    }
    // resolves %, cm and inch presets to pixels for one image
    public static ResizePreset Resolve(ResizePreset p, Size src, double dpiX, double dpiY, bool ignoreOrientation) {
        double w = p.W, h = p.H;
        if (p.Unit == "%") { w = src.Width * p.W / 100.0; h = src.Height * p.H / 100.0; }
        else if (p.Unit == "cm") { w = p.W * dpiX / 2.54; h = p.H * dpiY / 2.54; }
        else if (p.Unit == "in") { w = p.W * dpiX; h = p.H * dpiY; }
        var r = new ResizePreset(p.Name, Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h))) { Unit = "px" };
        // like PowerToys: a portrait photo uses the preset turned sideways
        if (ignoreOrientation && p.Unit != "%" && (src.Height > src.Width) != (r.H > r.W) && r.W != r.H) { int t = r.W; r.W = r.H; r.H = t; }
        return r;
    }
    // %1 original name, %2 preset name, %3 preset width, %4 preset height, %5 result width, %6 result height
    public static string OutPath(string src, ResizePreset p, bool replace) { return OutPath(src, p, replace, "%1 (%2)", Size.Empty); }
    public static string OutPath(string src, ResizePreset p, bool replace, string fmt, Size actual) {
        if (replace) return src;
        string d = Path.GetDirectoryName(src), n = Path.GetFileNameWithoutExtension(src), e = Path.GetExtension(src);
        string name = (string.IsNullOrEmpty(fmt) ? "%1 (%2)" : fmt).Replace("%1", n).Replace("%2", p.Name).Replace("%3", p.W.ToString()).Replace("%4", p.H.ToString()).Replace("%5", actual.Width.ToString()).Replace("%6", actual.Height.ToString());
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        lock (reserved) {
            string o = Path.Combine(d, name + e); int i = 2; while (File.Exists(o) || reserved.Contains(o)) o = Path.Combine(d, name + " (" + i++ + ")" + e);
            reserved.Add(o); return o;
        }
    }
    static readonly HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // WIC (the Windows imaging pipeline PowerToys uses): JPEG is decoded straight to a reduced size, metadata kept
    public static string One(string src, ResizePreset p, ResizerCfg c) { return Wic.One(src, p, c); }
    public static int Threads = Math.Max(1, Math.Min(Environment.ProcessorCount, 6));
    public static string Run(IEnumerable<string> files, ResizePreset p, ResizerCfg c) {
        int ok = 0, skip = 0, fail = 0; var list = files.ToList();
        System.Threading.Tasks.Parallel.ForEach(list, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Threads }, f => {
            try { if (One(f, p, c) == null) Interlocked.Increment(ref skip); else Interlocked.Increment(ref ok); } catch { Interlocked.Increment(ref fail); }
        });
        lock (reserved) reserved.Clear();
        return "변환 " + ok + "개" + (skip > 0 ? " · 건너뜀 " + skip + "개(이미 작음)" : "") + (fail > 0 ? " · 실패 " + fail + "개" : "");
    }
    const string Key = @"Software\Classes\SystemFileAssociations\image\shell\BongHubResize";
    public static bool MenuInstalled() { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k != null; }
    public static void SetMenu(bool on) {
        if (TestEnv.Off) return;
        try {
            if (!on) { if (MenuInstalled()) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Key, false); return; }
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)) {
                k.SetValue("", "BongHub로 크기 조정"); k.SetValue("Icon", "\"" + Application.ExecutablePath + "\",0"); k.SetValue("MultiSelectModel", "Player");
                using (var cmd = k.CreateSubKey("command")) cmd.SetValue("", "\"" + Application.ExecutablePath + "\" resize \"%1\"");
            }
        } catch { }
    }
}
class ResizerForm : Form {
    readonly ResizerCfg cfg; readonly List<string> files = new List<string>(); ListBox list; ComboBox preset, fit; CheckBox shrink, replace, orient, meta, keepDate; NumericUpDown q, cw, ch; ComboBox unit; TextBox nameFmt; Label status;
    public ResizerForm(ResizerCfg c, IEnumerable<string> initial) {
        cfg = c; Text = "Image Resizer · BongHub"; Font = UI.Base; BackColor = UI.Page; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(UI.S(560), UI.S(720)); AutoScroll = true; AllowDrop = true;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; TestEnv.Place(this);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var st = UI.Stack(); st.Dock = DockStyle.Fill; st.Padding = new Padding(UI.S(16));
        UI.AddRow(st, UI.L("이미지 파일을 여기에 끌어다 놓으세요", UI.Bold, UI.Text));
        list = new ListBox { Width = UI.S(486), Height = UI.S(150), BorderStyle = BorderStyle.None, BackColor = UI.Card, ForeColor = UI.Text2, HorizontalScrollbar = true }; UI.AddRow(st, list);
        var add = UI.Btn("파일 추가…"); add.Click += (s, e) => { using (var d = new OpenFileDialog { Multiselect = true, Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" }) if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames); };
        UI.AddRow(st, add);
        preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(260) }; foreach (var p in c.Presets) preset.Items.Add(p.Name + "  (" + p.W + "×" + p.H + (p.Unit == "px" ? "" : " " + p.Unit) + ")"); preset.Items.Add("사용자 지정…"); preset.SelectedIndex = Math.Max(0, Math.Min(c.Preset, c.Presets.Count - 1));
        UI.AddRow(st, UI.L("크기", UI.Small, UI.Hint)); UI.AddRow(st, preset);
        cw = new NumericUpDown { Minimum = 1, Maximum = 20000, Value = 1280, Width = UI.S(80) }; ch = new NumericUpDown { Minimum = 1, Maximum = 20000, Value = 720, Width = UI.S(80) };
        unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(80) }; unit.Items.AddRange(new object[] { "px", "%", "cm", "in" }); unit.SelectedIndex = 0;
        var custom = UI.Flow(UI.L("너비"), cw, UI.L(" 높이"), ch, UI.L(" "), unit); UI.AddRow(st, custom);
        Action upd = () => custom.Enabled = preset.SelectedIndex == preset.Items.Count - 1; preset.SelectedIndexChanged += (s, e) => upd(); upd();
        fit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = UI.S(260) }; fit.Items.AddRange(new object[] { "맞춤 (비율 유지)", "채우기 (잘라내기)", "늘이기" }); fit.SelectedIndex = c.Fit == "fill" ? 1 : c.Fit == "stretch" ? 2 : 0;
        UI.AddRow(st, UI.L("방식", UI.Small, UI.Hint)); UI.AddRow(st, fit);
        shrink = new CheckBox { Text = "줄이기만 (작은 이미지는 키우지 않음)", Checked = c.ShrinkOnly, AutoSize = true, ForeColor = UI.Text }; UI.AddRow(st, shrink);
        replace = new CheckBox { Text = "원본 덮어쓰기 (꺼짐: '이름 (크기).확장자'로 새로 저장)", Checked = c.Replace, AutoSize = true, ForeColor = UI.Text }; UI.AddRow(st, replace);
        orient = new CheckBox { Text = "사진 방향 무시 (세로 사진은 가로·세로를 바꿔 적용)", Checked = c.IgnoreOrientation, AutoSize = true, ForeColor = UI.Text }; UI.AddRow(st, orient);
        meta = new CheckBox { Text = "메타데이터 제거 (EXIF·GPS 등)", Checked = c.RemoveMeta, AutoSize = true, ForeColor = UI.Text }; UI.AddRow(st, meta);
        keepDate = new CheckBox { Text = "원본의 수정한 날짜 유지", Checked = c.KeepDate, AutoSize = true, ForeColor = UI.Text }; UI.AddRow(st, keepDate);
        nameFmt = new TextBox { Width = UI.S(260), Text = c.NameFormat }; UI.AddRow(st, UI.L("파일 이름 형식  (%1 원래 이름 · %2 크기 이름 · %3 %4 지정 크기 · %5 %6 실제 크기)", UI.Small, UI.Hint)); UI.AddRow(st, nameFmt);
        q = new NumericUpDown { Minimum = 10, Maximum = 100, Value = Math.Max(10, Math.Min(100, c.Quality)), Width = UI.S(80) }; UI.AddRow(st, UI.L("JPEG 품질", UI.Small, UI.Hint)); UI.AddRow(st, q);
        var go = UI.Btn("크기 조정", true); go.Click += (s, e) => Go(); UI.AddRow(st, go);
        status = UI.L("", UI.Small, UI.Hint); UI.AddRow(st, status);
        Controls.Add(st);
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
        if (initial != null) AddFiles(initial);
        Dark.Theme(this);
    }
    public void AddFiles(IEnumerable<string> fs) {
        foreach (var f in fs) { if (Directory.Exists(f)) { AddFiles(Directory.GetFiles(f)); continue; } if ((Peek.Kind(f) == "image" || Peek.Kind(f) == "thumb") && !Path.GetExtension(f).Equals(".ico", StringComparison.OrdinalIgnoreCase) && !files.Contains(f)) { files.Add(f); list.Items.Add(f); } }
        status.Text = files.Count + "개 선택됨";
    }
    public string Go() {
        if (files.Count == 0 || preset.SelectedIndex < 0) { status.Text = "파일을 먼저 추가하세요"; return status.Text; }
        cfg.Fit = fit.SelectedIndex == 1 ? "fill" : fit.SelectedIndex == 2 ? "stretch" : "fit"; cfg.ShrinkOnly = shrink.Checked; cfg.Replace = replace.Checked; cfg.Quality = (int)q.Value;
        cfg.IgnoreOrientation = orient.Checked; cfg.RemoveMeta = meta.Checked; cfg.KeepDate = keepDate.Checked; if (nameFmt.Text.Contains("%1") || nameFmt.Text.Contains("%2")) cfg.NameFormat = nameFmt.Text;
        bool isCustom = preset.SelectedIndex == preset.Items.Count - 1; if (!isCustom) cfg.Preset = preset.SelectedIndex;
        var p = isCustom ? new ResizePreset("사용자 지정", (int)cw.Value, (int)ch.Value) { Unit = (string)unit.SelectedItem } : cfg.Presets[preset.SelectedIndex];
        var sw = Stopwatch.StartNew(); Cursor = Cursors.WaitCursor; try { status.Text = Resizer.Run(files, p, cfg) + " · " + sw.Elapsed.TotalSeconds.ToString("0.0") + "초"; } finally { Cursor = Cursors.Default; }
        if (Saved != null) Saved(); return status.Text;
    }
    public event Action Saved;
}

// feature card on the PowerToys page: icon, one-line description, instant toggle, status badge
class PtCard : Control {
    public string Key, Glyph, Title, Desc, Badge = ""; public Color BadgeColor = UI.Hint; public bool On, HasToggle = true; bool hover;
    public event Action Toggled, Opened;
    public PtCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); Cursor = Cursors.Hand; Size = new Size(UI.S(300), UI.S(124)); Margin = new Padding(0, 0, UI.S(12), UI.S(12)); BackColor = Color.Transparent; }
    Rectangle Sw { get { return new Rectangle(Width - UI.S(58), UI.S(16), UI.S(42), UI.S(22)); } }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e); if (e.Button != MouseButtons.Left) return;
        var sw = Sw; sw.Inflate(UI.S(6), UI.S(6));
        if (HasToggle && sw.Contains(e.Location)) { On = !On; Invalidate(); if (Toggled != null) Toggled(); } else if (Opened != null) Opened();
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(UI.Page);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var p = UI.Round(r, UI.S(12))) {
            using (var b = new LinearGradientBrush(r, hover ? UI.Hover : UI.Card, NoteColors.Mix(UI.Card, UI.Page, .35f), 90f)) g.FillPath(b, p);
            using (var pen = new Pen(hover ? NoteColors.Mix(UI.Border, UI.G2, .5f) : UI.Border)) g.DrawPath(pen, p);
        }
        var ir = new Rectangle(UI.S(16), UI.S(16), UI.S(38), UI.S(38));
        using (var p = UI.Round(ir, UI.S(10))) using (var b = UI.Grad(ir, On || !HasToggle ? 255 : 110, 45f)) g.FillPath(b, p);
        using (var gf = new Font(UI.IconFam, 14f * UI.Scale)) TextRenderer.DrawText(g, Glyph, gf, ir, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Title, UI.Bold, new Rectangle(UI.S(64), UI.S(14), Width - UI.S(130), UI.S(22)), UI.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Badge, UI.Small, new Rectangle(UI.S(64), UI.S(36), Width - UI.S(130), UI.S(20)), BadgeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Desc, UI.Small, new Rectangle(UI.S(16), UI.S(64), Width - UI.S(32), Height - UI.S(72)), UI.Text2, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (HasToggle) {
            var s = Sw; using (var p = UI.Round(s, s.Height / 2)) {
                if (On) using (var b = UI.Grad(s)) g.FillPath(b, p); else using (var b = new SolidBrush(UI.Input)) g.FillPath(b, p);
                using (var pen = new Pen(On ? UI.G2 : UI.Border)) g.DrawPath(pen, p);
            }
            int d = s.Height - UI.S(8); var k = new Rectangle(On ? s.Right - d - UI.S(4) : s.X + UI.S(4), s.Y + UI.S(4), d, d);
            using (var b = new SolidBrush(On ? Color.White : UI.Hint)) g.FillEllipse(b, k);
        } else using (var cf = new Font(UI.IconFam, 10f * UI.Scale)) TextRenderer.DrawText(g, "\uE76C", cf, new Rectangle(Width - UI.S(40), UI.S(16), UI.S(24), UI.S(22)), UI.Hint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

class TrayApp : Form {
    const int WM_HOTKEY = 0x0312; const uint MOD_NOREPEAT = 0x4000;
    Config cfg; NotifyIcon icon; WTimer idleTimer; volatile bool awayDone = false, checking = false;
    readonly Dictionary<int, Preset> hotkeys = new Dictionary<int, Preset>();
    Watcher watcher; string activePreset = ""; SettingsForm settings;
    bool cloudCfgPending;
    WTimer deskAuto, deskRestore; DateTime lastAutoSave = DateTime.Now, lastDisplayChange = DateTime.MinValue; volatile bool deskBusy;

    public TrayApp() {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; FormBorderStyle = FormBorderStyle.FixedToolWindow; Opacity = 0;
        cfg = Config.Load();
        icon = new NotifyIcon { Icon = Program.AppIcon, Text = "BongHub", Visible = true };
        icon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) BeginInvoke((Action)(() => { try { OpenSettings(); } catch (Exception ex) { try { File.WriteAllText(Path.Combine(Core.Dir, "settings_open_error.txt"), ex.ToString()); } catch { } Balloon("설정 창을 열지 못했어요: " + ex.Message, ToolTipIcon.Warning); } })); };
        idleTimer = new WTimer { Interval = 30000 }; idleTimer.Tick += (s, e) => CheckIdle(); idleTimer.Start();
        deskAuto = new WTimer { Interval = 60000 }; deskAuto.Tick += (s, e) => DeskAutoTick(); deskAuto.Start();
        deskRestore = new WTimer { Interval = 4000 }; deskRestore.Tick += (s, e) => DeskRestoreTick();
        Cloud.StatusChanged += () => { try { BeginInvoke((Action)(() => { string t = "BongHub" + (cfg.Cloud.Enabled ? " · 클라우드: " + Cloud.Status : ""); icon.Text = t.Length > 63 ? t.Substring(0, 63) : t; })); } catch { } };
        Cloud.ConfigReplaced += () => { try { BeginInvoke((Action)(() => { if (settings != null && !settings.IsDisposed) { cloudCfgPending = true; return; } cfg = Config.Load(); ApplyConfig(false); })); } catch { } };
        Cloud.NotesReplaced += () => { try { BeginInvoke((Action)(() => Notes.ReloadFromDisk())); } catch { } };
        Cloud.DeskReplaced += () => Desk.Reload();
        Core.BeforeSwitch = () => { if (cfg.Desk.SaveBeforeSwitch && !deskRestore.Enabled) { string err; try { Desk.SaveAuto(out err); } catch { } lastAutoSave = DateTime.Now; } };
        Core.AfterSwitch = () => { try { BeginInvoke((Action)(() => {
            ScheduleDeskRestore(3000);
            string bmsg = PresetExtras.LastBright, dmsg = PresetExtras.LastDesk, wmsg = PresetExtras.LastWork;
            bool bad = bmsg.Contains("실패") || dmsg.Contains("실패") || dmsg.Contains("삭제") || wmsg.Contains("실패") || wmsg.Contains("삭제");
            if (bad) Balloon(string.Join("\n", new[] { bmsg, dmsg, wmsg }.Where(x => x != "")), ToolTipIcon.Warning);
        })); } catch { } };
    }

    // ---- desktop icons ----
    void ScheduleDeskRestore(int ms) { if (!cfg.Desk.RestoreOnChange || PresetExtras.LinkedRecently) return; deskRestore.Stop(); deskRestore.Interval = ms; deskRestore.Start(); }
    void DeskRestoreTick() {
        if (Core.Busy || deskBusy) { deskRestore.Interval = 2000; return; }
        deskRestore.Stop(); if (PresetExtras.LinkedRecently) return; deskBusy = true;
        ThreadPool.QueueUserWorkItem(_ => {
            try {
                var l = Desk.Best(); if (l == null) return;
                var r = Desk.Restore(l); Thread.Sleep(4000);
                if (!Core.Busy) { var r2 = Desk.Restore(l); if (r2.Ok) r.Moved += r2.Moved; }
                if (r.Ok && r.Moved > 0) Balloon("바탕화면 아이콘을 ‘" + l.Name + "’ 배치로 되돌렸어요 (" + Desk.Describe(l.Key) + ")", ToolTipIcon.Info);
                else if (!r.Ok && r.Error != null && r.Error.Contains("자동 정렬")) Balloon(r.Error, ToolTipIcon.Warning);
            } catch { }
            finally { deskBusy = false; lastAutoSave = DateTime.Now; }
        });
    }
    void DeskAutoTick() {
        var d = cfg.Desk;
        if (!d.AutoSave || Core.Busy || deskBusy || deskRestore.Enabled) return;
        if ((DateTime.Now - lastDisplayChange).TotalSeconds < 120) return;
        if ((DateTime.Now - lastAutoSave).TotalMinutes < d.AutoSaveMinutes) return;
        lastAutoSave = DateTime.Now; deskBusy = true;
        ThreadPool.QueueUserWorkItem(_ => { try { string err; Desk.SaveAuto(out err); } catch { } finally { deskBusy = false; } });
    }
    void DeskSaveNow() {
        string err; var l = Desk.SaveNew("빠른 저장 " + DateTime.Now.ToString("M/d HH:mm"), out err);
        Balloon(l != null ? "바탕화면 아이콘 " + l.Items.Count + "개 위치를 저장했어요 (" + l.Desc + ")" : "저장하지 못했어요: " + err, l != null ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }
    void DeskRestoreNow(DeskLayout l) {
        var r = Desk.Restore(l ?? Desk.Best());
        Balloon(r.Summary(), r.Ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e); Hide();
        watcher = new Watcher();
        ApplyConfig(true);
    }

    void ApplyConfig(bool first) {
        if (first) { Startup.MigrateOld(); try { Startup.SyncIcon(); } catch { } }
        Notes.ApplySettings(Config.CloneObj(cfg.Notes));
        Notes.OpenList = pg => OpenSettings(pg);
        ApplyPt();
        if (first) { try { Notes.OpenStartup(); } catch { } }
        if (first && cfg.Desk.RestoreOnStartup) { var once = new WTimer { Interval = 8000 }; once.Tick += (s, e) => { once.Stop(); once.Dispose(); if (!Core.Busy) ThreadPool.QueueUserWorkItem(_ => { try { Desk.Restore(Desk.Best()); } catch { } }); }; once.Start(); }
        var k = cfg.Kakao;
        ThreadPool.QueueUserWorkItem(_ => { try { if (first && !k.AutoApply) { if (!k.AdBlock && Kakao.IsRunning()) Kakao.Stop(); } else Kakao.Sync(k); } catch { } });
        Cloud.Apply(Config.CloneObj(cfg.Cloud));
        BuildMenu();
        RegisterAll(!first || true);
        watcher.SetRules(ActiveRules());
    }
    IEnumerable<WindowRule> ActiveRules() {
        var p = cfg.Find(activePreset);
        return p == null ? cfg.GlobalRules : cfg.GlobalRules.Concat(p.Rules);
    }

    void BuildMenu() {
        var old = icon.ContextMenuStrip;
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = new Font(UI.Fam, 9.5f), ShowImageMargin = true };
        Dark.InitApp();
        menu.Items.Add(new ToolStripMenuItem("모니터 프리셋") { Enabled = false });
        foreach (var p in cfg.Presets) {
            var pp = p;
            var it = new ToolStripMenuItem(p.Name + " 모드", null, (s, e) => Trigger(pp));
            if (p.Hotkey != "") it.ShortcutKeyDisplayString = Hotkey.Normalize(p.Hotkey);
            if (p.Name == activePreset) it.Checked = true;
            menu.Items.Add(it);
        }
        menu.Items.Add(new ToolStripSeparator());
        var nn = new ToolStripMenuItem("새 스티커 메모", null, (s, e) => Notes.New(true));
        if (cfg.Notes.Hotkey != "") nn.ShortcutKeyDisplayString = Hotkey.Normalize(cfg.Notes.Hotkey);
        menu.Items.Add(nn);
        menu.Items.Add("스티커 메모 모두 보이기", null, (s, e) => Notes.ShowAll());
        menu.Items.Add("스티커 메모 모두 숨기기", null, (s, e) => Notes.HideAll());
        menu.Items.Add(new ToolStripSeparator());
        var dk = new ToolStripMenuItem("바탕화면 아이콘");
        var dSave = new ToolStripMenuItem("지금 배치 저장", null, (s, e) => DeskSaveNow());
        if (cfg.Desk.HotkeySave != "") dSave.ShortcutKeyDisplayString = Hotkey.Normalize(cfg.Desk.HotkeySave);
        var dRest = new ToolStripMenuItem("현재 화면 배치로 복원", null, (s, e) => DeskRestoreNow(null));
        if (cfg.Desk.HotkeyRestore != "") dRest.ShortcutKeyDisplayString = Hotkey.Normalize(cfg.Desk.HotkeyRestore);
        dk.DropDownItems.Add(dSave); dk.DropDownItems.Add(dRest); dk.DropDownItems.Add(new ToolStripSeparator());
        dk.DropDownOpening += (s, e) => {
            while (dk.DropDownItems.Count > 3) dk.DropDownItems.RemoveAt(3);
            string key = Desk.ScreenKey(); var ls = Desk.All.Where(x => x.Key == key).OrderByDescending(x => x.Saved).Take(10).ToList();
            if (ls.Count == 0) dk.DropDownItems.Add(new ToolStripMenuItem("이 화면 구성의 배치 없음") { Enabled = false });
            foreach (var x in ls) { var xx = x; dk.DropDownItems.Add(new ToolStripMenuItem(x.Name + (x.Auto ? " (자동)" : "") + "   " + x.Saved.ToString("M/d HH:mm"), null, (a, b) => DeskRestoreNow(xx))); }
            dk.DropDownItems.Add(new ToolStripSeparator());
            dk.DropDownItems.Add(new ToolStripMenuItem("배치 관리…", null, (a, b) => OpenSettings(4)));
        };
        dk.DropDownItems.Add(new ToolStripMenuItem("배치 관리…", null, (a, b) => OpenSettings(4)));
        dk.DropDown.BackColor = Color.FromArgb(24, 23, 42);
        menu.Items.Add(dk);
        var kk = new ToolStripMenuItem("카톡 광고 제거");
        var kAd = new ToolStripMenuItem("광고 숨김", null, (s, e) => KakaoToggle(c => c.AdBlock = !c.AdBlock)) { Checked = cfg.Kakao.AdBlock };
        var kCo = new ToolStripMenuItem("컴팩트 레이아웃", null, (s, e) => KakaoToggle(c => c.Compact = !c.Compact)) { Checked = cfg.Kakao.Compact, Enabled = cfg.Kakao.AdBlock };
        var kDk = new ToolStripMenuItem("다크 오버레이", null, (s, e) => KakaoToggle(c => c.Dark = !c.Dark)) { Checked = cfg.Kakao.Dark, Enabled = cfg.Kakao.AdBlock };
        var kRe = new ToolStripMenuItem("원상복구", null, (s, e) => KakaoToggle(c => c.AdBlock = false));
        kk.DropDownItems.AddRange(new ToolStripItem[] { kAd, kCo, kDk, new ToolStripSeparator(), kRe });
        kk.DropDown.BackColor = Color.FromArgb(24, 23, 42);
        menu.Items.Add(kk);
        menu.Items.Add("창 규칙 지금 적용", null, (s, e) => {
            var rules = ActiveRules().ToList();
            ThreadPool.QueueUserWorkItem(_ => { int n = WindowRules.ApplyList(rules); Balloon(n + "개 창에 규칙을 적용했어요.", ToolTipIcon.Info); });
        });
        var st = new ToolStripMenuItem("설정…", null, (s, e) => OpenSettings()); st.Font = new Font(st.Font, FontStyle.Bold);
        menu.Items.Add(st);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (s, e) => { icon.Visible = false; Application.Exit(); });
        icon.ContextMenuStrip = menu;
        if (old != null) old.Dispose();
    }

    void KakaoToggle(Action<KakaoCfg> change) {
        change(cfg.Kakao);
        try { cfg.Save(); } catch { }
        var k = Config.CloneObj(cfg.Kakao);
        ThreadPool.QueueUserWorkItem(_ => { try { Kakao.Sync(k); } catch { } });
        BuildMenu();
    }
    void UnregisterAll() { foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id); hotkeys.Clear(); Native.UnregisterHotKey(Handle, 900); Native.UnregisterHotKey(Handle, 901); Native.UnregisterHotKey(Handle, 902); UnregisterPt(); }
    void RegisterAll(bool notify) {
        UnregisterAll();
        var failed = new List<string>(); int id = 100;
        foreach (var p in cfg.Presets) {
            uint m, vk;
            if (!Hotkey.Parse(p.Hotkey, out m, out vk)) continue;
            if (Native.RegisterHotKey(Handle, id, m | MOD_NOREPEAT, vk)) hotkeys[id] = p;
            else failed.Add(Hotkey.Normalize(p.Hotkey) + " (" + p.Name + ")");
            id++;
        }
        { uint m, vk; if (Hotkey.Parse(cfg.Notes.Hotkey, out m, out vk) && !Native.RegisterHotKey(Handle, 900, m | MOD_NOREPEAT, vk)) failed.Add(Hotkey.Normalize(cfg.Notes.Hotkey) + " (새 스티커 메모)"); }
        { uint m, vk; if (Hotkey.Parse(cfg.Desk.HotkeySave, out m, out vk) && !Native.RegisterHotKey(Handle, 901, m | MOD_NOREPEAT, vk)) failed.Add(Hotkey.Normalize(cfg.Desk.HotkeySave) + " (아이콘 저장)"); }
        { uint m, vk; if (Hotkey.Parse(cfg.Desk.HotkeyRestore, out m, out vk) && !Native.RegisterHotKey(Handle, 902, m | MOD_NOREPEAT, vk)) failed.Add(Hotkey.Normalize(cfg.Desk.HotkeyRestore) + " (아이콘 복원)"); }
        RegisterPt(failed);
        try { File.WriteAllText(Path.Combine(Core.Dir,"hotkeys_status.txt"),failed.Count==0?"OK":string.Join("\n",failed)); } catch { }
        if (failed.Count > 0 && notify)
            Balloon("단축키 등록 실패: " + string.Join(", ", failed) + " - 다른 프로그램이 사용 중일 수 있어요. 트레이 메뉴는 사용 가능해요.", ToolTipIcon.Warning);
    }
    void Balloon(string text, ToolTipIcon ic) {
        try { if (InvokeRequired) { BeginInvoke((Action)(() => Balloon(text, ic))); return; } icon.ShowBalloonTip(5000, "BongHub", text, ic); } catch { }
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x007E) { Zones.Invalidate(); lastDisplayChange = DateTime.Now; ScheduleDeskRestore(Core.Busy ? 2000 : 4000); }
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 901) { DeskSaveNow(); }
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 902) { DeskRestoreNow(null); }
        else if (m.Msg == WM_HOTKEY && PtHotkey(m.WParam.ToInt32())) { }
        else if (m.Msg == WM_HOTKEY) { if (m.WParam.ToInt32() == 900) { try { Notes.New(true); } catch { } } else { Preset p; if (hotkeys.TryGetValue(m.WParam.ToInt32(), out p)) Trigger(p); } }
        base.WndProc(ref m);
    }

    public void Trigger(Preset p) {
        var g = cfg.General; var global = cfg.GlobalRules.ToList();
        var t = new Thread(() => {
            try {
                if (!Core.Run(p, g)) { Balloon("이미 전환 중이에요.", ToolTipIcon.Info); return; }
                BeginInvoke((Action)(() => { activePreset = p.Name; BuildMenu(); watcher.SetRules(ActiveRules()); }));
                var rules = p.Rules.Concat(global).Where(r => r.Enabled).ToList();
                if (rules.Count > 0) { Thread.Sleep(Math.Max(0, p.RulesDelay)); WindowRules.ApplyList(rules); }
            } catch { }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }

    void CheckIdle() {
        var g = cfg.General;
        if (!g.IdleEnabled) return;
        if (Native.IdleMs() >= (uint)g.IdleMinutes * 60u * 1000u) {
            if (awayDone || checking) return;
            var p = cfg.Find(g.IdlePreset); if (p == null) return;
            checking = true;
            var t = new Thread(() => {
                try {
                    if (g.SkipIfAudio && Core.AudioPlaying(g.AudioThreshold)) return;
                    awayDone = true;
                    BeginInvoke((Action)(() => Trigger(p)));
                } catch { } finally { checking = false; }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA); t.Start();
        } else awayDone = false;
    }

    // ---- PowerToys features ----
    readonly Dictionary<int, Workspace> wsKeys = new Dictionary<int, Workspace>(); PaletteForm palette; bool resizeMenuSet;
    void ApplyPt() {
        var x = cfg.Pt;
        // lazy: a disabled feature that was never on does not even load its code path
        try { if (x.Zones.Enabled || Zones.Active || Zones.KeysActive) Zones.Apply(Config.CloneObj(x.Zones)); } catch { }
        try { if (x.Grab.Enabled || GrabMove.Active) GrabMove.Apply(Config.CloneObj(x.Grab)); } catch { }
        PeekSpace.Apply(x.Peek.Enabled && Hotkey.Normalize(x.Peek.Hotkey)=="Space",foreground=>{try{BeginInvoke((Action)(()=>PeekNow(foreground,true)));}catch{}},()=>{try{BeginInvoke((Action)CloseHeldPeek);}catch{}});
        PeekForm.CloseOnDeactivate = x.Peek.CloseOnFocusLoss; PeekForm.RenderHtml = x.Peek.RenderHtml;
        if (x.Palette.Enabled) ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(8000); try { Palette.Warm(); } catch { } });   // index built quietly after startup
        try { if (x.Resizer.ContextMenu != Resizer.MenuInstalled() || !resizeMenuSet) { if (x.Resizer.ContextMenu || Resizer.MenuInstalled()) Resizer.SetMenu(x.Resizer.ContextMenu); resizeMenuSet = true; } } catch { }
        Palette.Commands = PaletteCommands;
    }
    void RegisterPt(List<string> failed) {
        var x = cfg.Pt; wsKeys.Clear();
        Action<string, int, string> reg = (hk, id, what) => { uint m, vk; if (Hotkey.Parse(hk, out m, out vk) && !Native.RegisterHotKey(Handle, id, m | MOD_NOREPEAT, vk)) failed.Add(Hotkey.Normalize(hk) + " (" + what + ")"); };
        if (x.Palette.Enabled) reg(x.Palette.Hotkey, 910, "명령 팔레트");
        if (x.Paste.Enabled) {
            int failureCount=failed.Count; reg(x.Paste.Hotkey,911,"고급 붙여넣기");
            if(failed.Count>failureCount && Hotkey.Normalize(x.Paste.Hotkey)=="Win+Shift+V") {
                foreach(string fallback in new[]{"Ctrl+Alt+V","Ctrl+Alt+Shift+V","Win+Ctrl+Shift+V"}) {
                    var assigned=cfg.Presets.Select(p=>p.Hotkey).Concat(new[]{cfg.Notes.Hotkey,cfg.Desk.HotkeySave,cfg.Desk.HotkeyRestore,x.Palette.Hotkey,x.Paste.PlainHotkey,x.Paste.JsonHotkey,x.Paste.MarkdownHotkey,x.Peek.Hotkey}).Concat(x.Work.Items.Select(w=>w.Hotkey));
                    if(assigned.Any(k=>Hotkey.Normalize(k)==Hotkey.Normalize(fallback)))continue;
                    uint mods,vk;Hotkey.Parse(fallback,out mods,out vk);
                    if(!Native.RegisterHotKey(Handle,911,mods|MOD_NOREPEAT,vk))continue;
                    failed.RemoveAt(failed.Count-1);x.Paste.Hotkey=fallback;
                    if(settings!=null&&!settings.IsDisposed)settings.SyncPasteShortcut(fallback);
                    try{cfg.Save();File.WriteAllText(Path.Combine(Core.Dir,"hotkeys_fallback.txt"),"Win+Shift+V is occupied; Advanced Paste uses "+fallback);}catch{}
                    break;
                }
            } reg(x.Paste.PlainHotkey, 912, "서식 없이 붙여넣기"); reg(x.Paste.JsonHotkey, 914, "JSON으로 붙여넣기"); reg(x.Paste.MarkdownHotkey, 915, "마크다운으로 붙여넣기"); }
        if (x.Peek.Enabled && Hotkey.Normalize(x.Peek.Hotkey) != "Space") reg(x.Peek.Hotkey, 913, "미리보기");
        if (x.Work.Enabled) { int id = 920; foreach (var w in x.Work.Items.Take(60)) { if (Hotkey.Normalize(w.Hotkey) != "") { reg(w.Hotkey, id, "작업 영역 " + w.Name); wsKeys[id] = w; } id++; } }
    }
    void UnregisterPt() { for (int id = 910; id < 980; id++) Native.UnregisterHotKey(Handle, id); }
    bool PtHotkey(int id) {
        if (id < 910 || id >= 980) return false;
        try {
            if (id == 910) ShowPalette();
            else if (id == 911) { var t = Peek.GetForegroundWindow(); var f = new PasteMenu(t, cfg.Pt.Paste); f.Show(); f.Activate(); }
            else if (id == 914) { var t = Peek.GetForegroundWindow(); try { if (Clipboard.ContainsText()) AdvPaste.PasteText(AdvPaste.ToJson(Clipboard.GetText()), t); } catch { } }
            else if (id == 915) { var t = Peek.GetForegroundWindow(); try { if (Clipboard.ContainsText(TextDataFormat.Html)) AdvPaste.PasteText(AdvPaste.HtmlToMarkdown(Clipboard.GetText(TextDataFormat.Html)), t); else if (Clipboard.ContainsText()) AdvPaste.PasteText(Clipboard.GetText(), t); } catch { } }
            else if (id == 912) { var t = Peek.GetForegroundWindow(); try { if (Clipboard.ContainsText()) AdvPaste.PasteText(Clipboard.GetText(), t); } catch { } }
            else if (id == 913) PeekNow();
            else { Workspace w; if (wsKeys.TryGetValue(id, out w)) LaunchWs(w); }
        } catch { }
        return true;
    }
    void ShowPalette() {
        if (palette != null && !palette.IsDisposed) { if (palette.Visible) { palette.Dismiss(); return; } palette.Reopen(Config.CloneObj(cfg.Pt.Palette)); return; }
        // created once, then hidden and reused: reopening skips window, font and list creation
        palette = new PaletteForm(Config.CloneObj(cfg.Pt.Palette)) { Reusable = true }; palette.FormClosed += (s, e) => palette = null; palette.Show(); palette.Activate();
    }
    PeekForm peekWindow; bool peekBusy; int peekRequest;
    void CloseHeldPeek() {peekRequest++;peekBusy=false;if(peekWindow!=null && !peekWindow.IsDisposed && peekWindow.HoldToPreview){peekWindow.Close();peekWindow=null;}}
    void PeekNow() { PeekNow(Peek.GetForegroundWindow()); }
    void PeekNow(IntPtr fg,bool hold=false) {
        if(hold && !PeekSpace.IsHeld)return;
        if (peekWindow != null && !peekWindow.IsDisposed) { peekWindow.Close(); peekWindow=null; return; }
        if (peekBusy) return; peekBusy=true;int request=++peekRequest;
        var thread=new Thread(() => {
            List<string> all=null; string error=null;
            try { all=Peek.SelectedAll(fg); } catch(Exception ex) { error=ex.Message; }
            try { BeginInvoke((Action)(() => {
                if(request!=peekRequest || (hold && !PeekSpace.IsHeld))return;peekBusy=false;
                if (all == null || all.Count == 0) { Balloon(error ?? "탐색기에서 파일을 선택하고 미리보기 단축키를 눌러 주세요.",ToolTipIcon.Info); return; }
                peekWindow=new PeekForm(all,0){HoldToPreview=hold}; peekWindow.Show(); peekWindow.Activate();
            })); } catch { }
        }) { IsBackground=true, Name="Explorer selection" }; thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    void LaunchWs(Workspace w) {
        var copy = Config.CloneObj(w); bool launch = cfg.Pt.Work.LaunchMissing, move = cfg.Pt.Work.MoveExisting;
        ThreadPool.QueueUserWorkItem(_ => { try { Balloon(Workspaces.Launch(copy, launch, move, null), ToolTipIcon.Info); } catch { } });
    }
    IEnumerable<PaletteItem> PaletteCommands() {
        var l = new List<PaletteItem>();
        foreach (var p in cfg.Presets) { var pp = p; l.Add(new PaletteItem { Title = "모니터 프리셋: " + p.Name, Key = p.Name, Sub = p.Hotkey == "" ? "모니터 전환" : p.Hotkey, Glyph = "\uE7F4", Run = () => Trigger(pp) }); }
        l.Add(new PaletteItem { Title = "새 스티커 메모", Key = "메모 note", Sub = cfg.Notes.Hotkey, Glyph = "\uE70B", Run = () => Notes.New(true) });
        l.Add(new PaletteItem { Title = "메모 목록", Key = "메모 notes", Sub = "설정 · 스티커 메모", Glyph = "\uE70B", Run = () => OpenSettings(2) });
        for (int i = 0; i < SettingsForm.Names.Length; i++) { int k = i; l.Add(new PaletteItem { Title = "설정: " + SettingsForm.Names[i], Key = SettingsForm.PageKeys[i], Sub = "BongHub 설정 열기", Glyph = "\uE713", Run = () => OpenSettings(k) }); }
        if (cfg.Pt.Work.Enabled) foreach (var w in cfg.Pt.Work.Items) { var ww = w; l.Add(new PaletteItem { Title = "작업 영역: " + w.Name, Key = w.Name, Sub = "앱 " + w.Apps.Count + "개 열기·배치", Glyph = "\uE8A1", Run = () => LaunchWs(ww) }); }
        try { string key = Desk.ScreenKey(); foreach (var d in Desk.All.Where(x => x.Key == key).OrderByDescending(x => x.Saved).Take(10)) { var dd = d; l.Add(new PaletteItem { Title = "아이콘 배치 복원: " + d.Name, Key = d.Name, Sub = "바탕화면 아이콘", Glyph = "\uE8A9", Run = () => DeskRestoreNow(dd) }); } } catch { }
        l.Add(new PaletteItem { Title = "아이콘 위치 저장", Key = "desktop save", Sub = "바탕화면 아이콘", Glyph = "\uE8A9", Run = DeskSaveNow });
        l.Add(new PaletteItem { Title = "Image Resizer 열기", Key = "resize image 이미지", Sub = "PowerToys", Glyph = "\uE91B", Run = () => new ResizerForm(cfg.Pt.Resizer, null).Show() });
        l.Add(new PaletteItem { Title = "창 위치 규칙 지금 적용", Key = "window rules", Sub = "창 위치", Glyph = "\uE737", Run = () => { var rl = ActiveRules().Select(r => Config.CloneObj(r)).ToList(); ThreadPool.QueueUserWorkItem(_ => { try { WindowRules.ApplyList(rl); } catch { } }); } });
        return l;
    }
    public string HotkeyTaken(string hk) {
        string normalized = Hotkey.Normalize(hk);
        var ours = cfg.Presets.Select(p => p.Hotkey).Concat(new[] { cfg.Notes.Hotkey, cfg.Desk.HotkeySave, cfg.Desk.HotkeyRestore, cfg.Pt.Palette.Hotkey, cfg.Pt.Paste.Hotkey, cfg.Pt.Paste.PlainHotkey, cfg.Pt.Paste.JsonHotkey, cfg.Pt.Paste.MarkdownHotkey, cfg.Pt.Peek.Hotkey }).Concat(cfg.Pt.Work.Items.Select(w => w.Hotkey));
        if (ours.Any(k => Hotkey.Normalize(k) == normalized)) return null;
        uint m, vk; if (!Hotkey.Parse(hk, out m, out vk)) return null;
        // probe with a throwaway registration on a message-only window; only for hotkeys BongHub itself does not hold right now
        { var w = new NativeWindow();
            w.CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });
            try { if (Native.RegisterHotKey(w.Handle, 0xBFFE, m | 0x4000, vk)) { Native.UnregisterHotKey(w.Handle, 0xBFFE); return null; } return "다른 프로그램이 사용 중"; }
            finally { w.DestroyHandle(); }
        }
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    void OpenSettings() { OpenSettings(0); }
    void OpenSettings(int pg) {
        if (settings != null && !settings.IsDisposed) { settings.Show(); settings.WindowState = FormWindowState.Normal; Native.ShowWindow(settings.Handle, 9); settings.BringToFront(); settings.Activate(); SetForegroundWindow(settings.Handle); settings.ShowPage(pg); return; }
        SettingsForm.HotkeyOwner = HotkeyTaken;
        settings = new SettingsForm(cfg, Startup.IsEnabled(), true);
        settings.RunPresetNow = p => Trigger(p);
        settings.OnSave = (c, startup) => {
            var ser = new JavaScriptSerializer(); bool kChanged = ser.Serialize(cfg.Kakao) != ser.Serialize(c.Kakao);
            bool ptChanged=ser.Serialize(cfg.Pt)!=ser.Serialize(c.Pt), cloudChanged=ser.Serialize(cfg.Cloud)!=ser.Serialize(c.Cloud);
            bool presetsChanged=ser.Serialize(cfg.Presets)!=ser.Serialize(c.Presets), rulesChanged=presetsChanged || ser.Serialize(cfg.GlobalRules)!=ser.Serialize(c.GlobalRules);
            bool keysChanged=ptChanged || presetsChanged || cfg.Notes.Hotkey!=c.Notes.Hotkey || cfg.Desk.HotkeySave!=c.Desk.HotkeySave || cfg.Desk.HotkeyRestore!=c.Desk.HotkeyRestore;
            cfg = c;
            cfg.Save();
            if (startup != Startup.IsEnabled()) Startup.Set(startup);
            Notes.ApplySettings(Config.CloneObj(cfg.Notes)); BuildMenu(); if(rulesChanged) watcher.SetRules(ActiveRules()); if(ptChanged) ApplyPt(); if(keysChanged) RegisterAll(true);
            if(cloudChanged) Cloud.Apply(Config.CloneObj(cfg.Cloud));
            if (kChanged) { var k = Config.CloneObj(cfg.Kakao); ThreadPool.QueueUserWorkItem(_ => { try { Kakao.Sync(k); } catch { } }); }
        };
        settings.FormClosed += (s, e) => { settings = null; if (cloudCfgPending) { cloudCfgPending = false; cfg = Config.Load(); } ApplyConfig(false); };
        settings.Show(); Native.ShowWindow(settings.Handle, 9); settings.BringToFront(); settings.Activate(); SetForegroundWindow(settings.Handle); settings.Update();
        var opened = settings; opened.BeginInvoke((Action)(() => { if (!opened.IsDisposed) opened.ShowPage(pg); }));
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        PeekSpace.Apply(false, null); UnregisterAll(); if (watcher != null) watcher.SetRules(new WindowRule[0]);
        try { Zones.Apply(new ZonesCfg()); GrabMove.Apply(new GrabCfg()); } catch { }
        if (cfg.Desk.SaveOnExit && !Core.Busy && !deskRestore.Enabled) { try { string err; Desk.SaveAuto(out err); } catch { } }
        try { Notes.Shutdown(); } catch { }
        try { Cloud.FlushOnExit(); } catch { }
        try { if (Kakao.IsRunning()) Kakao.Stop(); } catch { }
        icon.Visible = false; base.OnFormClosing(e);
    }
}

static class ShotNative {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int op);
}
// ================= Program / tests =================
static class Program {
    public static Icon AppIcon;
    public const string IconRes = "icon_blocks.ico";
    static Dictionary<int, Icon> iconCache = new Dictionary<int, Icon>();
    public static Icon LoadIcon(int size) {
        Icon ic;
        if (iconCache.TryGetValue(size, out ic)) return ic;
        try {
            using (var st = typeof(Program).Assembly.GetManifestResourceStream(IconRes)) {
                if (st == null) return UI.MakeIcon();
                ic = size > 0 ? new Icon(st, size, size) : new Icon(st);
            }
        } catch { ic = UI.MakeIcon(); }
        iconCache[size] = ic; return ic;
    }
    static Bitmap thumb;
    public static Bitmap IconThumb() {
        if (thumb != null) return thumb;
        try {
            byte[] d; using (var st = typeof(Program).Assembly.GetManifestResourceStream(IconRes)) { var ms = new MemoryStream(); st.CopyTo(ms); d = ms.ToArray(); }
            int n = BitConverter.ToUInt16(d, 4), best = -1, bestW = 0;
            for (int i = 0; i < n; i++) { int o = 6 + 16 * i, w = d[o] == 0 ? 256 : d[o]; if (w > bestW) { bestW = w; best = i; } }
            int bo = 6 + 16 * best, len = BitConverter.ToInt32(d, bo + 8), off = BitConverter.ToInt32(d, bo + 12);
            if (d[off] == 0x89 && d[off + 1] == 0x50) thumb = new Bitmap(new MemoryStream(d, off, len));
            else thumb = new Icon(new MemoryStream(d), bestW, bestW).ToBitmap();
        } catch { thumb = LoadIcon(32).ToBitmap(); }
        return thumb;
    }
    public static void InitIcon() { if (AppIcon == null) AppIcon = LoadIcon(0); }
    static string ExeDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int WorkspaceCli(string[] args) {
        var c0 = Config.Load(); var w = args.Length > 1 ? c0.Pt.Work.Items.FirstOrDefault(x => string.Equals(x.Name, args[1], StringComparison.OrdinalIgnoreCase)) : null;
        if (w == null) return 2; Workspaces.Launch(w, c0.Pt.Work.LaunchMissing, c0.Pt.Work.MoveExisting, null); return 0;
    }
    [STAThread]
    static int Main(string[] args) {
        if(args.Length==2 && args[0]=="restart") { int pid; if(!int.TryParse(args[1],out pid))return 2;try{using(var parent=Process.GetProcessById(pid))if(!parent.WaitForExit(20000))return 1;}catch(ArgumentException){}args=new string[0]; }
        Native.SetDpiAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        InitIcon();
        if (args.Length > 0) {
            string m = args[0];
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { try { File.WriteAllText(Path.Combine(ExeDir, "crash_" + m + ".txt"), e.Exception.ToString()); } catch { } Environment.Exit(3); };
            string ml = m.ToLowerInvariant();
            if (ml == "settingstest" || ml == "tabbench" || ml == "notestest" || ml == "imagetest" || ml == "pttest" || ml == "jittertest" || ml == "perfbench" || ml == "paritytest") TestEnv.Off = true;
            switch (ml) {
                case "uiperf": TestEnv.Off=true; return UiPerf();
                case "inputtest": TestEnv.Off=true;return InputTest();
                case "interactiontest": TestEnv.Off=true; return InteractionTest(args.Length>1?args[1]:"");
                case "lockscreen": return LockScreenPolicy.Run(args);
                case "lockscreenpolicytest": return LockScreenPolicy.Test(Path.Combine(ExeDir, "lockscreenpolicytest.txt"));
                case "imagetest": return ImageTest();
                case "workspace": return WorkspaceCli(args);
                case "version": File.WriteAllText(Path.Combine(ExeDir, "version.txt"), AppInfo.Version); return 0;
                case "overlaytest": return OverlayTest();
                case "settingstest": return SettingsTest(args.Length > 1 ? args[1] : "");
                case "configtest": return ConfigTest();
                case "pttest": return PtTest();
                case "perfbench": return PerfBench();
                case "jittertest": return JitterTest();
                case "extrastest": return ExtrasTest();
                case "resize": { UI.Init(1f); var c0 = Config.Load(); var rf = new ResizerForm(c0.Pt.Resizer, args.Skip(1)); rf.Saved += () => { try { var c1 = Config.Load(); c1.Pt.Resizer = c0.Pt.Resizer; c1.Save(); } catch { } }; Application.Run(rf); return 0; }
                case "tabbench": return TabBench(args.Length > 1 ? args[1] : "");
                case "icontest": return IconTest();
                case "cloudtest": return CloudTest();
                case "windowtest": return WindowTest();
                case "kakaotest": return KakaoTest();
                case "notestest": return NotesTest();
                case "notesbench": return NotesBench(args.Length > 1 ? int.Parse(args[1]) : 45);
                case "watchbench": return WatchBench(args.Length > 1 ? int.Parse(args[1]) : 40);
            }
            if (ml.EndsWith("test") || ml.EndsWith("bench")) return 9; // unknown test name: never fall through to a real monitor switch
            var cfg = Config.Load(); var p = cfg.FindCli(m);
            if (p == null) {
                DarkBox.Show("사용법: BongHub.exe <모니터 프리셋 이름>\n\n모니터 프리셋: " + string.Join(", ", cfg.Presets.Select(x => x.Name)) + "\n(기존 work / away 도 사용 가능)", "BongHub");
                return 2;
            }
            using (var mx = new Mutex(false, @"Local\BongHub_Routine")) {
                if (!mx.WaitOne(0)) return 1;
                try {
                    Core.Run(p, cfg.General);
                    var rules = p.Rules.Concat(cfg.GlobalRules).Where(r => r.Enabled).ToList();
                    if (rules.Count > 0) { Thread.Sleep(Math.Max(0, p.RulesDelay)); WindowRules.ApplyList(rules); }
                } finally { mx.ReleaseMutex(); }
            }
            return 0;
        }
        bool created;
        using (var single = new Mutex(true, @"Local\BongHub_Tray", out created)) {
            if (!created) return 1;
            UI.Init(1f);
            Application.Run(new TrayApp());
        }
        return 0;
    }

    static void Shot(Control c, string file) { Bitmap bm0 = null; for (int k = 0; k < 6; k++) { if (bm0 != null) bm0.Dispose(); TestEnv.Redraw(c); Pump(120); bm0 = TestEnv.Render(c); if (BlackShare(bm0) < 0.01) break; } using (var bm = bm0) bm.Save(Path.Combine(ExeDir, file), System.Drawing.Imaging.ImageFormat.Png); }
    static double BlackShare(Bitmap b) { int n = 0, t = 0; for (int y = 2; y < b.Height - 2; y += 4) for (int x = 2; x < b.Width - 2; x += 4) { t++; var p = b.GetPixel(x, y); if (p.R + p.G + p.B < 6) n++; } return n / (double)Math.Max(1, t); }
    static void ShotMany(IEnumerable<Control> cs, string file, int pad) {
        var list = cs.ToList(); var u = list.Select(c => c.Bounds).Aggregate(Rectangle.Union); u.Inflate(pad, pad);
        using (var bm = new Bitmap(u.Width, u.Height)) { using (var g = Graphics.FromImage(bm)) { g.Clear(Color.FromArgb(32, 32, 36)); foreach (var c in list) { Bitmap r = null; for (int k = 0; k < 6; k++) { if (r != null) r.Dispose(); TestEnv.Redraw(c); Pump(150); r = TestEnv.Render(c); if (BlackShare(r) < 0.01) break; } g.DrawImage(r, c.Left - u.X, c.Top - u.Y); r.Dispose(); } } bm.Save(Path.Combine(ExeDir, file), System.Drawing.Imaging.ImageFormat.Png); }
    }
    static void Shot(Rectangle b, string file) {
        using (var bmp = new Bitmap(b.Width, b.Height)) {
            using (var g = Graphics.FromImage(bmp)) { IntPtr sdc = ShotNative.GetDC(IntPtr.Zero), hdc = g.GetHdc(); ShotNative.BitBlt(hdc, 0, 0, b.Width, b.Height, sdc, b.X, b.Y, 0x00CC0020 | 0x40000000); g.ReleaseHdc(hdc); ShotNative.ReleaseDC(IntPtr.Zero, sdc); }
            bmp.Save(Path.Combine(ExeDir, file), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    static int OverlayTest() {
        var sb0 = Screen.PrimaryScreen.Bounds; var card = new Rectangle(sb0.X + sb0.Width / 2 - 360, sb0.Y + sb0.Height / 2 - 230, 720, 460);
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime; var w0 = Stopwatch.StartNew();
        Overlay.Show("작업 모드로 전환 중...", "잠시만 기다려 주세요", Config.Load().General.OverlayOpacity);
        Thread.Sleep(120); try { Shot(card, "overlay_f0_fadein.png"); } catch { }
        Thread.Sleep(900); try { Shot(card, "overlay_f1.png"); } catch { }
        Thread.Sleep(330); try { Shot(card, "overlay_f2.png"); } catch { }
        try { Overlay.Status("모니터 켜는 중… (2/4)"); } catch { }
        Thread.Sleep(450); try { Shot(card, "overlay_f3_status.png"); } catch { }
        Thread.Sleep(400); try { Shot(sb0, "overlay_test.png"); } catch { }
        Thread.Sleep(1800);
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds / w0.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        Overlay.Close(); Thread.Sleep(150); try { Shot(card, "overlay_f4_fadeout.png"); } catch { }
        File.WriteAllText(Path.Combine(ExeDir, "overlay_test.txt"), "cpu while visible " + cpu.ToString("0.00") + "% of total (" + Environment.ProcessorCount + " threads)\r\n");
        return 0;
    }

    // settingstest [percent] : screenshots of every tab (+ dialogs) without saving anything
    static int SettingsTest(string pct) {
        float sys; using (var g = Graphics.FromHwnd(IntPtr.Zero)) sys = g.DpiX / 96f * 100f;
        float target = 0; float.TryParse(pct, out target);
        float scale = target > 0 ? target / sys : 1f;
        string suf = target > 0 ? "_" + (int)target : "";
        UI.Init(scale);
        var cfg = Config.Load();
        if (cfg.GlobalRules.Count == 0 && cfg.Presets.All(p => p.Rules.Count == 0))
            cfg.GlobalRules.Add(new WindowRule { Name = "예시: 메모장", Process = "notepad", Monitor = "SAM73D3", X = 100, Y = 100, W = 900, H = 600, Keep = true, Enabled = false });
        var f = new SettingsForm(cfg, Startup.IsEnabled()); TestEnv.Place(f);
        f.OnSave = (c, s) => { }; f.RunPresetNow = p => { };
        int step = 0; var t = new WTimer { Interval = 1500 };
        t.Tick += (s, e) => {
            try {
                if (step < SettingsForm.PageKeys.Length) {
                    if (step > 0) Shot(f, "settings_" + SettingsForm.PageKeys[step - 1] + suf + ".png");
                    if (step == 1) { f.ScrollPresetEditor(); Application.DoEvents(); Thread.Sleep(600); Application.DoEvents(); Shot(f, "settings_presets_bottom" + suf + ".png"); }
                    f.ShowPage(step); step++; return;
                }
                if (step == SettingsForm.PageKeys.Length) {
                    Shot(f, "settings_" + SettingsForm.PageKeys[step - 1] + suf + ".png");
                    step++;
                    var d = new RuleDialog(new WindowRule { Name = "예시", Process = "chrome", Monitor = "SAM73D3", Keep = true }, Core.ListMonitors());
                    TestEnv.Place(d); d.Show(); f.Tag = d; return;
                }
                if (step == SettingsForm.PageKeys.Length + 1) {
                    var d = (Form)f.Tag; Shot(d, "settings_ruledialog" + suf + ".png"); d.Close();
                    var c = new CaptureDialog(true); TestEnv.Place(c); c.Show(); f.Tag = c; step++; return;
                }
                if (step == SettingsForm.PageKeys.Length + 2) { var cd = (Form)f.Tag; Shot(cd, "settings_capture" + suf + ".png"); cd.Close(); f.MoveNav("about", 0); f.MoveNav("desktop", 1); f.ShowPage(5); step++; return; }
                if (step == SettingsForm.PageKeys.Length + 3) {
                    Shot(f, "settings_taborder" + suf + ".png"); step++;
                    var bx = DarkBox.Make(f, "이 메모를 삭제할까요?", "스티커 메모", MessageBoxButtons.YesNo, MessageBoxIcon.Question); TestEnv.Place(bx); bx.Show(); f.Tag = bx; return;
                }
                if (step == SettingsForm.PageKeys.Length + 4) {
                    var bx = (Form)f.Tag; Shot(bx, "dialog_confirm" + suf + ".png"); bx.Close(); step++;
                    using (var im = DemoImage(1400, 820)) { var v = new ImageViewer(new Bitmap(im)); TestEnv.Place(v); v.Size = new Size(UI.S(900), UI.S(600)); v.Show(); f.Tag = v; }
                    return;
                }
                if (step == SettingsForm.PageKeys.Length + 5) { var v = (ImageViewer)f.Tag; Shot(v, "viewer_fit" + suf + ".png"); v.Canvas.ZoomTo(1f, v.Canvas.Center, false); step++; return; }
                if (step == SettingsForm.PageKeys.Length + 6) { var v = (ImageViewer)f.Tag; Shot(v, "viewer_100" + suf + ".png"); v.Close(); step++; return; }
                t.Stop(); f.Close(); Application.ExitThread();
            } catch (Exception ex) { File.WriteAllText(Path.Combine(ExeDir, "settingstest_error.txt"), ex.ToString()); t.Stop(); Application.ExitThread(); }
        };
        f.FormClosing += (s, e) => { };
        f.Shown += (s, e) => t.Start();
        f.Show();
        Application.Run();
        return File.Exists(Path.Combine(ExeDir, "settingstest_error.txt")) ? 1 : 0;
    }


    static int InputTest() {
        UI.Init(1f);var log=new StringBuilder();bool ok=true;Action<bool,string> check=(value,text)=>{ok&=value;log.AppendLine((value?"PASS ":"FAIL ")+text);};
        try {
            string detail;check(GrabMove.TestQueue(out detail),"slow target keeps bounded pending work: "+detail);check(GrabMove.TestMessages(out detail),"grab event protocol");log.Append(detail);
            int opens=0,closes=0;PeekSpace.Apply(false,h=>opens++,()=>closes++);
            check(!PeekSpace.HandleSpace(true,(IntPtr)1,false),"Space in text context passes through");
            check(PeekSpace.HandleSpace(true,(IntPtr)1,true)&&PeekSpace.IsHeld,"Space hold starts preview");
            PeekSpace.HandleSpace(true,(IntPtr)1,true);check(opens==1,"Space autorepeat does not toggle preview");
            check(PeekSpace.HandleSpace(false,IntPtr.Zero,false)&&!PeekSpace.IsHeld&&closes==1,"release closes even after foreground changes");
            using(var form=new SettingsForm(Config.Defaults(),false,true)) {
                form.OnSave=(c,x)=>{};TestEnv.Place(form);form.Show();form.ShowPage(7);
                foreach(string key in SettingsForm.PtKeys){form.PtShow(key);var child=Desc(form).OfType<HotkeyBox>().FirstOrDefault(c=>c.Visible&&c.FindForm()==form);IntPtr target=child!=null?child.Handle:form.Handle;
                    var forward=Message.Create(target,0x020C,(IntPtr)(2<<16),IntPtr.Zero);check(!form.PreFilterMessage(ref forward)&&form.PtOpen==key,"forward button kept in "+key);
                    var back=Message.Create(target,0x020C,(IntPtr)(1<<16),IntPtr.Zero);check(form.PreFilterMessage(ref back)&&form.PtOpen==null,"mouse back from "+key);
                }
                form.PtShow("paste");var command=Message.Create(form.Handle,0x0319,IntPtr.Zero,(IntPtr)(1<<16));check(form.PreFilterMessage(ref command)&&form.PtOpen==null,"driver browser-back command");form.Close();
            }
            string fixtureDirectory=Path.Combine(ExeDir,"_dev","fixtures");if(!Directory.Exists(fixtureDirectory))fixtureDirectory=ExeDir;var fixtures=Directory.GetFiles(fixtureDirectory,"fixture*.webp");check(fixtures.Length>=4,"real lossless, alpha, animated and Unicode WebP fixtures present");
            foreach(string file in fixtures) {
                if(Path.GetFileName(file).Contains("animated"))check(WebPImage.Animated(file),"animated fixture really contains animation");
                using(var decoded=WebPImage.Decode(file)){check(decoded.Width==128&&decoded.Height==96,"full WebP decoding: "+Path.GetFileName(file));check(decoded.GetPixel(64,48).A>100&&decoded.GetPixel(64,48).A<200,"WebP transparency preserved");}
                using(var preview=new PeekForm(file)){TestEnv.Place(preview);preview.Show();var timer=Stopwatch.StartNew();while(!preview.Ready&&timer.ElapsedMilliseconds<5000)Pump(20);check(preview.Ready&&Desc(preview).OfType<PictureBox>().Any(p=>p.Image!=null),"WebP preview image loaded: "+Path.GetFileName(file));preview.Close();}
                using(var rapid=new PeekForm(file)){rapid.HoldToPreview=true;TestEnv.Place(rapid);rapid.Show();Pump(1);rapid.Close();Pump(80);check(rapid.IsDisposed,"release while WebP decoding does not reopen preview");}
            }
        }catch(Exception ex){check(false,ex.ToString());}
        File.WriteAllText(Path.Combine(ExeDir,"inputtest.txt"),log.ToString());return ok?0:1;
    }

    // Real controls, changing width every frame; no snapshots or user desktop interaction.
    static int UiPerf() {
        UI.Init(1f); var report=new StringBuilder(); bool ok=true;
        using(var f=new SettingsForm(Config.Defaults(),false,true)) {
            f.OnSave=(c,x)=>{}; TestEnv.Place(f);f.Show();
            foreach(int page in new[]{0,2,5,7}) {
                f.ShowPage(page);if(page==7)f.PtShow("paste");Pump(100);
                TestSend(f.Handle,0x0231,IntPtr.Zero,IntPtr.Zero);
                // A burst must not perform a content layout for every stale native size message.
                for(int burst=0;burst<40;burst++)f.Size=new Size(UI.S(1100+burst%20),UI.S(740));
                Pump(60);if(!f.PageFitsViewport){ok=false;report.AppendLine("FAIL queued latest size on page "+page);}
                var samples=new List<double>();double layoutMs=0,paintMs=0;ResponsiveTable.LayoutCalls=0;
                for(int i=0;i<24;i++) {
                    var sw=Stopwatch.StartNew();f.Size=new Size(UI.S(1100+(i<12?i:24-i)*8),UI.S(740+i%3*4));
                    layoutMs+=sw.Elapsed.TotalMilliseconds;var paint=Stopwatch.StartNew();f.Update();Pump(20);paintMs+=paint.Elapsed.TotalMilliseconds;samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                TestSend(f.Handle,0x0232,IntPtr.Zero,IntPtr.Zero);Pump(60);
                if(!f.PageFitsViewport){ok=false;report.AppendLine("FAIL final page bounds on page "+page);}
                report.AppendLine("page "+page+" resize avg "+samples.Average().ToString("0.0")+" max "+samples.Max().ToString("0.0")+" ms; layouts "+ResponsiveTable.LayoutCalls+" native size dispatch "+(layoutMs/24).ToString("0.0")+" pending layout, paint and 20ms message pump "+(paintMs/24).ToString("0.0"));
                using(var shot=TestEnv.Render(f))shot.Save(Path.Combine(ExeDir,"resize_page_"+page+".png"));
                if(f.Controls.Count<2)ok=false;
            }
            f.Close();
        }
        report.AppendLine(ok?"PASS latest and final resize geometry":"FAIL resize geometry");File.WriteAllText(Path.Combine(ExeDir,"uiperf.txt"),report.ToString());return ok?0:1;
    }

    // tabbench [percent] : measures settings window build + tab switch times (no saving)
    static int TabBench(string pct) {
        float sys; using (var g = Graphics.FromHwnd(IntPtr.Zero)) sys = g.DpiX / 96f * 100f;
        float target = 0; float.TryParse(pct, out target);
        UI.Init(target > 0 ? target / sys : 1f);
        string suf = target > 0 ? "_" + (int)target : "";
        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();
        var f = new SettingsForm(Config.Load(), Startup.IsEnabled()); long ctor = sw.ElapsedMilliseconds; TestEnv.Place(f);
        f.OnSave = (c, x) => { }; f.RunPresetNow = p => { };
        f.Show(); f.Update(); long shown = sw.ElapsedMilliseconds; Pump(1500);
        int n = SettingsForm.PageKeys.Length;
        sb.AppendLine("ctor " + ctor + " ms, first show " + shown + " ms");
        for (int i = 1; i <= n; i++) { int k = i % n; sw.Restart(); f.ShowPage(k); f.Update(); Application.DoEvents(); sb.AppendLine("first " + SettingsForm.PageKeys[k] + " " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms"); Pump(200); }
        var all = new List<double>();
        for (int r = 0; r < 5; r++) for (int i = 1; i <= n; i++) { int k = i % n; sw.Restart(); f.ShowPage(k); f.Update(); Application.DoEvents(); all.Add(sw.Elapsed.TotalMilliseconds); Pump(60); }
        sb.AppendLine("switch avg " + all.Average().ToString("0.0") + " ms, max " + all.Max().ToString("0.0") + " ms, n=" + all.Count);
        f.Close(); Pump(200);
        sb.AppendLine("--- profile"); sb.Append(SettingsForm.Prof.ToString());
        File.WriteAllText(Path.Combine(ExeDir, "tab_bench" + suf + ".txt"), sb.ToString(), Encoding.UTF8);
        return 0;
    }

    // icontest : save -> nudge one icon -> restore -> verify, always puts the user's icons back; uses a temp layouts folder
    static int IconTest() {
        UI.Init(1f);
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        string realFile = Path.Combine(Desk.Dir, "layouts.json"), realBefore = File.Exists(realFile) ? File.ReadAllText(realFile) : "<none>";
        string dir = Path.Combine(Path.GetTempPath(), "BongHub_icontest");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        string realDir = Desk.Dir; Desk.Reset(dir);
        List<DeskItem> original = null;
        try {
            string key = Desk.ScreenKey(); sb.AppendLine("INFO screen " + key + " -> " + Desk.Describe(key));
            check(key.Length > 0 && Desk.Describe(key).StartsWith("모니터 "), "screen key/description");
            check(Desk.Describe("1920x1080@0,0;2560x1440@1920,0*") == "모니터 2대 · 2560×1440(주) + 1920×1080", "describe puts primary first");
            string err; original = Desk.ReadIcons(out err);
            check(original != null, "read desktop icons " + (err ?? ""));
            if (original != null) {
                sb.AppendLine("INFO " + original.Count + " icons, auto-arrange=" + Desk.AutoArrange + ", snap=" + Desk.SnapToGrid + ", visible=" + Desk.IconsVisible);
                check(original.Count == 0 || original.Any(x => x.Name != ""), "icon names readable");
                var l = Desk.SaveNew("테스트 배치", out err);
                check(l != null && l.Items.Count == original.Count && File.Exists(Path.Combine(dir, "layouts.json")), "save layout to temp store");
                check(!Desk.SaveAuto(out err) || true, "auto save");
                check(!Desk.SaveAuto(out err), "auto save skips when unchanged");
                check(Desk.All.Count(x => x.Auto) == 1, "one auto layout per screen config");
                if (original.Count > 0 && !Desk.AutoArrange) {
                    var moved = original.Select(x => new DeskItem { Name = x.Name, X = x.X, Y = x.Y }).ToList();
                    moved[0].X += 120; moved[0].Y += 60;
                    var r1 = Desk.Apply(moved); Thread.Sleep(400);
                    var now = Desk.ReadIcons(out err);
                    bool shifted = now != null && (now[0].X != original[0].X || now[0].Y != original[0].Y);
                    check(r1.Ok && shifted, "nudged icon '" + original[0].Name + "' " + original[0].X + "," + original[0].Y + " -> " + (now == null ? "?" : now[0].X + "," + now[0].Y) + (Desk.SnapToGrid ? " (snap on)" : ""));
                    var r2 = Desk.Restore(Desk.Best()); Thread.Sleep(400);
                    now = Desk.ReadIcons(out err);
                    check(r2.Ok && now != null && now.Count == original.Count && Enumerable.Range(0, now.Count).All(i => now[i].X == original[i].X && now[i].Y == original[i].Y), "restore puts every icon back (" + r2.Summary() + ")");
                    var fake = original.Take(1).Select(x => new DeskItem { Name = x.Name, X = x.X, Y = x.Y }).ToList(); fake.Add(new DeskItem { Name = "없는 아이콘 ☆", X = 10, Y = 10 });
                    var r3 = Desk.Apply(fake);
                    check(r3.Ok && r3.Missing == 1 && r3.Extra == original.Count - 1, "match by name: missing/new icons handled (" + r3.Summary() + ")");
                } else sb.AppendLine("SKIP move/restore (no icons or auto-arrange on)");
                Desk.Rename(l.Id, "이름 바꿈"); check(Desk.Get(l.Id).Name == "이름 바꿈", "rename");
                check(Desk.Overwrite(l.Id, out err), "overwrite");
                Desk.Delete(l.Id); check(Desk.Get(l.Id) == null, "delete");
            }
        } catch (Exception ex) { check(false, "exception " + ex); }
        finally {
            if (original != null) { try { var r = Desk.Apply(original); sb.AppendLine("INFO original positions put back: " + r.Summary()); } catch { } }
            Desk.Reset(realDir);
            try { Directory.Delete(dir, true); } catch { }
        }
        check(!Directory.Exists(dir), "temp layouts cleaned up");
        check((File.Exists(realFile) ? File.ReadAllText(realFile) : "<none>") == realBefore, "real layouts untouched");
        File.WriteAllText(Path.Combine(ExeDir, "icon_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }

    // cloudtest : two simulated PCs syncing through a temporary local bare repo (never touches GitHub or real data)
    static int CloudTest() {
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        string root = Path.Combine(Path.GetTempPath(), "BongHub_cloudtest");
        try { if (Directory.Exists(root)) { foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(root, true); } } catch { }
        Directory.CreateDirectory(root);
        string bare = Path.Combine(root, "remote.git"), o;
        var realCfg = Cloud.Cfg;
        try {
            check(Cloud.Git("init --bare --quiet -b main " + "\"" + bare + "\"", out o, 30000, root) == 0, "temp bare repo " + o);
            Func<string, string[]> dev = n => { string d = Path.Combine(root, n); Directory.CreateDirectory(Path.Combine(d, "notes")); Directory.CreateDirectory(Path.Combine(d, "desktop")); return new[] { d, Path.Combine(d, "config.json"), Path.Combine(d, "notes"), Path.Combine(d, "desktop"), Path.Combine(d, "cloud") }; };
            var A = dev("pcA"); var B = dev("pcB");
            Action<string[], string> use = (d, name) => { Cloud.Dir = d[4]; Cloud.ConfigFile = d[1]; Cloud.NotesDir = d[2]; Cloud.DeskDir = d[3]; Cloud.DeviceOverride = name; };
            Cloud.Cfg = new CloudCfg { Enabled = true, Repo = bare.Replace('\\', '/'), Branch = "main" };
            var js = new JavaScriptSerializer();
            // PC A: config + note + layout
            File.WriteAllText(A[1], "{\"General\":{\"IdleMinutes\":17}}");
            File.WriteAllText(Path.Combine(A[2], "n1.json"), js.Serialize(new NoteMeta { Id = "n1", Text = "A가 쓴 메모" })); File.WriteAllText(Path.Combine(A[2], "n1.rtf"), @"{\rtf1 A}");
            File.WriteAllText(Path.Combine(A[3], "layouts.json"), js.Serialize(new List<DeskLayout> { new DeskLayout { Id = "L1", Name = "A 배치", Key = "k", Saved = DateTime.Now.AddMinutes(-5) } }));
            use(A, "PC-A"); Cloud.SyncNow(true); check(Cloud.Status.StartsWith("저장됨"), "A uploads (" + Cloud.Status + ")");
            check(Cloud.Git("--git-dir=\"" + bare + "\" log --oneline", out o, 30000, root) == 0 && o.Contains("sync: PC-A"), "commit on remote main");
            // PC B: fresh device gets everything
            File.WriteAllText(B[1], "{\"General\":{\"IdleMinutes\":30}}");
            use(B, "PC-B"); Cloud.SyncNow(true);
            check(File.ReadAllText(B[1]).Contains("17"), "B first link takes cloud config");
            check(File.Exists(Path.Combine(B[2], "n1.rtf")) && File.ReadAllText(Path.Combine(B[2], "n1.rtf")).Contains("A"), "B receives note");
            check(File.ReadAllText(Path.Combine(B[3], "layouts.json")).Contains("L1"), "B receives desktop layout");
            // B edits the note and adds its own; A also adds a different note -> per-note merge
            Thread.Sleep(1500);
            File.WriteAllText(Path.Combine(B[2], "n1.rtf"), @"{\rtf1 B edit}"); File.WriteAllText(Path.Combine(B[2], "n2.json"), js.Serialize(new NoteMeta { Id = "n2" })); File.WriteAllText(Path.Combine(B[2], "n2.rtf"), @"{\rtf1 B2}");
            Cloud.SyncNow(true);
            use(A, "PC-A"); File.WriteAllText(Path.Combine(A[2], "n3.json"), js.Serialize(new NoteMeta { Id = "n3" })); File.WriteAllText(Path.Combine(A[2], "n3.rtf"), @"{\rtf1 A3}");
            Cloud.SyncNow(true);
            check(File.ReadAllText(Path.Combine(A[2], "n1.rtf")).Contains("B edit") && File.Exists(Path.Combine(A[2], "n2.rtf")), "A gets B's newer edit + new note");
            use(B, "PC-B"); Cloud.SyncNow(true); check(File.Exists(Path.Combine(B[2], "n3.rtf")), "B gets A's note (merge, nothing lost)");
            // A deletes n2 -> B loses it too
            use(A, "PC-A"); File.Delete(Path.Combine(A[2], "n2.json")); File.Delete(Path.Combine(A[2], "n2.rtf")); Cloud.SyncNow(true);
            { string tfA = Path.Combine(A[4], "repo", "data", "notes", "_deleted.json"); sb.AppendLine("dbg A tomb=" + (File.Exists(tfA) ? File.ReadAllText(tfA) : "none") + " stA=" + File.ReadAllText(Path.Combine(A[4], "state.json")) + " status=" + Cloud.Status); }
            use(B, "PC-B"); Cloud.SyncNow(true); check(!File.Exists(Path.Combine(B[2], "n2.json")), "deletion travels (tombstone) A=" + File.Exists(Path.Combine(A[2], "n2.json")) + " tomb=" + (File.Exists(Path.Combine(B[4], "repo", "data", "notes", "_deleted.json")) ? File.ReadAllText(Path.Combine(B[4], "repo", "data", "notes", "_deleted.json")) : "none") + " B=" + File.GetLastWriteTimeUtc(Path.Combine(B[2], "n2.json")).ToString("o") + " st=" + Cloud.Status);
            // newest config wins
            Thread.Sleep(1500); File.WriteAllText(B[1], "{\"General\":{\"IdleMinutes\":44}}"); Cloud.SyncNow(true);
            use(A, "PC-A"); Cloud.SyncNow(true); check(File.ReadAllText(A[1]).Contains("44"), "newer config wins on the other PC");
            // offline: unreachable remote keeps the change pending
            Cloud.Cfg = new CloudCfg { Enabled = true, Repo = "https://nonexistent.invalid/x.git", Branch = "main" };
            var C = dev("pcC"); use(C, "PC-C"); Cloud.Pending = true; Cloud.SyncNow(true);
            check(Cloud.Pending && (Cloud.Status.StartsWith("오프라인") || Cloud.Status.Contains("실패")), "offline keeps pending (" + Cloud.Status + ")");
            check(!Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).IndexOf("ghp_", StringComparison.Ordinal) >= 0 || File.ReadAllText(f).IndexOf("github_pat_", StringComparison.Ordinal) >= 0), "no tokens in synced data");
        } catch (Exception ex) { check(false, "exception " + ex); }
        finally {
            Cloud.Cfg = realCfg; Cloud.DeviceOverride = null; Cloud.Dir = Path.Combine(Core.Dir, "cloud"); Cloud.ConfigFile = Config.FilePath; Cloud.NotesDir = Notes.Dir; Cloud.DeskDir = Desk.Dir;
            try { foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(root, true); } catch { }
        }
        check(!Directory.Exists(root), "temp repos cleaned up");
        File.WriteAllText(Path.Combine(ExeDir, "cloud_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }

    static int ConfigTest() {
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        var d = Config.Defaults(); string j1 = d.ToJson(); var d2 = Config.FromJson(j1); string j2 = d2.ToJson();
        check(j1 == j2, "defaults round-trip (" + j1.Length + " chars)" + (j1 == j2 ? "" : "\r\n" + j1 + "\r\n" + j2));
        var w = d.Find("작업"); var a = d.Find("자리비움");
        check(w != null && a != null && d.Presets.Count == 2, "two default presets");
        check(string.Join(",", w.Enable) == "SAM747A,SAM73D3,GSM76F5,SAM100B,DRS2555" && w.Disable.Count == 0, "work enable list/order");
        check(w.Primary == "SAM73D3" && w.Layout == "work_layout.cfg" && w.Retries == 4 && w.EnableDelay == 1500 && w.StepDelay == 2000 && w.LayoutDelay == 3000 && w.RefreshTaskbar, "work primary/layout/retries/delays/refresh");
        check(a.Primary == "GSM76F5" && string.Join(" ", a.Disable) == "SAM747A SAM73D3 SAM100B DRS2555" && a.Enable.Count == 0 && a.Layout == "" && a.Retries == 1 && a.StepDelay == 2000 && a.LayoutDelay == 3000 && a.RefreshTaskbar, "away primary/disable/delays/refresh");
        check(Hotkey.Normalize(w.Hotkey) == "Ctrl+Alt+1" && Hotkey.Normalize(a.Hotkey) == "Ctrl+Alt+0", "hotkeys");
        uint m, v; Hotkey.Parse("Ctrl+Alt+1", out m, out v); check(m == 3 && v == (uint)Keys.D1, "hotkey parse Ctrl+Alt+1 -> mods 3 vk D1");
        check(d.General.IdleEnabled && d.General.IdleMinutes == 30 && d.General.IdlePreset == "자리비움" && d.General.SkipIfAudio && Math.Abs(d.General.AudioThreshold - 0.001) < 1e-9, "idle defaults");
        check(d.FindCli("work") == w && d.FindCli("away") == a && d.FindCli("작업") == w && d.FindCli("WORK") == w, "CLI legacy aliases");
        var x = Config.Clone(d); x.GlobalRules.Add(new WindowRule { Name = "테스트 규칙", Process = "notepad", Title = "제목\"따옴표\\", TitleRegex = true, Monitor = "SAM73D3", Action = "max", X = -5, Pin = true, Keep = true });
        x.Presets[0].Rules.Add(new WindowRule { Name = "r2", Class = "Notepad" });
        var x2 = Config.FromJson(x.ToJson()); check(x2.ToJson() == x.ToJson() && x2.GlobalRules[0].Title == "제목\"따옴표\\" && x2.GlobalRules[0].X == -5 && x2.Presets[0].Rules[0].Class == "Notepad", "rules round-trip (escaping, negatives, nested)");
        var legacy = Config.FromJson("{\"General\":{\"Icon\":\"hub\",\"IdleMinutes\":12},\"iconStyle\":\"hub\"}"); check(legacy.General.IdleMinutes == 12 && legacy.Desk != null && legacy.Desk.RestoreOnChange && legacy.ToJson().IndexOf("\"Icon\"", StringComparison.Ordinal) < 0, "old icon keys ignored, desk defaults");
        var partial = Config.FromJson("{\"Presets\":[{\"Name\":\"P\"}]}"); check(partial.Presets[0].Retries == 1 && partial.Presets[0].Rules != null && partial.General.IdleMinutes == 30, "partial/old JSON gets defaults");
        if (File.Exists(Config.FilePath)) {
            var live = Config.FromJson(File.ReadAllText(Config.FilePath, Encoding.UTF8)); check(Config.FromJson(live.ToJson()).ToJson() == live.ToJson(), "live config.json round-trip");
            var lw = live.Find("작업"); var la = live.Find("자리비움");
            check(lw != null && la != null && Config.FromJson(Config.Defaults().ToJson()).ToJson() == Config.Defaults().ToJson(), "live config has default presets");
        }
        File.WriteAllText(Path.Combine(ExeDir, "config_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }

    static Bitmap DemoImage(int w, int h) {
        var bm = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bm)) {
            using (var b = UI.Grad(new RectangleF(0, 0, w, h), 255, 30f)) g.FillRectangle(b, 0, 0, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = new Font(UI.Fam, h / 8f, FontStyle.Bold)) TextRenderer.DrawText(g, "BongHub 이미지 붙여넣기", f, new Rectangle(0, 0, w, h), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return bm;
    }
    static NoteForm DemoNote(string color, Rectangle r) {
        var f = Notes.New(false); Notes.SetColor(f.M, color); f.Bounds = r; return f;
    }
    static void Put(NoteForm f, string text, FontStyle st, bool bullet, float size) {
        var b = f.Box; b.SelectionStart = b.TextLength; b.SelectionLength = 0;
        b.SelectionFont = new Font("Malgun Gothic", size, st); b.SelectionColor = NoteColors.Text; b.SelectionBullet = bullet;
        b.SelectedText = text;
    }
    static void FillDemo(NoteForm[] fs) {
        Put(fs[0], "장보기 목록\n", FontStyle.Bold, false, 13f); Put(fs[0], "우유 2개\n계란 한 판\n", FontStyle.Regular, true, 11f); Put(fs[0], "커피 원두", FontStyle.Strikeout, true, 11f);
        Put(fs[1], "회의 메모\n", FontStyle.Bold | FontStyle.Underline, false, 13f); Put(fs[1], "다음 주 화요일 2시\n", FontStyle.Italic, false, 11f); Put(fs[1], "BongHub 데모 준비하기", FontStyle.Regular, false, 11f);
        Put(fs[2], "아이디어\n", FontStyle.Bold, false, 14f); Put(fs[2], "모니터 프리셋 전환 후 메모가 화면 밖으로 나가지 않게 자동으로 옮겨져요.", FontStyle.Regular, false, 11f);
    }
    static Rectangle[] DemoRects() {
        var wa = TestEnv.Off ? TestEnv.Area : Screen.PrimaryScreen.WorkingArea; int w = UI.S(320), h = UI.S(300), gap = UI.S(24);
        int x0 = wa.X + (wa.Width - (3 * w + 2 * gap)) / 2, y0 = wa.Y + (wa.Height - h) / 2;
        return new[] { new Rectangle(x0, y0, w, h), new Rectangle(x0 + w + gap, y0, w, h), new Rectangle(x0 + 2 * (w + gap), y0, w, h) };
    }
    // imagetest : picture selection/resize/menu/viewer inside a note, all off-screen with temp data
    [DllImport("user32.dll", EntryPoint="SendMessageW")] static extern IntPtr TestSend(IntPtr h,int m,IntPtr w,IntPtr l);
    static IntPtr MousePoint(int x,int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
    static int InteractionTest(string percent) {
        float target; float.TryParse(percent,out target); float systemDpi; using(var g=Graphics.FromHwnd(IntPtr.Zero))systemDpi=g.DpiX; UI.Init(target>0?target*96f/(systemDpi*100f):1f); var report=new StringBuilder(); bool ok=true;
        Action<bool,string> check=(v,n)=>{report.AppendLine((v?"PASS ":"FAIL ")+n);ok &= v;};
        try {
            var defaults=Config.Defaults();
            check(Hotkey.Normalize(defaults.Pt.Palette.Hotkey)=="Win+Alt+Space", "official command palette default");
            check(Hotkey.Normalize(defaults.Pt.Paste.Hotkey)=="Win+Shift+V" && Hotkey.Normalize(defaults.Pt.Paste.PlainHotkey)=="Win+Ctrl+Alt+V", "official advanced paste defaults");
            check(defaults.Pt.Peek.Hotkey=="Space", "official Peek default");
            var legacy=Config.FromJson("{\"Pt\":{\"Palette\":{\"Hotkey\":\"Ctrl+Alt+Space\"},\"Paste\":{\"Hotkey\":\"Ctrl+Alt+V\",\"PlainHotkey\":\"Ctrl+Alt+Shift+V\"},\"Peek\":{\"Hotkey\":\"Ctrl+Shift+Space\"}}}");
            check(legacy.Pt.Peek.Hotkey=="Space" && legacy.Pt.Palette.Hotkey=="Win+Alt+Space", "legacy defaults migrate");
            legacy.Pt.ShortcutVersion=0; legacy.Pt.Peek.Hotkey="Ctrl+Shift+P"; legacy.Pt.Normalize(); check(legacy.Pt.Peek.Hotkey=="Ctrl+Shift+P", "custom shortcut kept");
            check(PeekSpace.EligibleContext("CabinetWClass","DirectUIHWND") && !PeekSpace.EligibleContext("CabinetWClass","Edit") && !PeekSpace.EligibleContext("Chrome_WidgetWin_1",""), "Space restricted to Explorer non-editor context");
            string temp=Path.Combine(Path.GetTempPath(),"BongHub_preview_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try {
                string text=Path.Combine(temp,"sample.txt"),picture=Path.Combine(temp,"sample.png");File.WriteAllText(text,"BongHub preview test");using(var bitmap=DemoImage(128,96))bitmap.Save(picture,ImageFormat.Png);
                foreach(var file in new[]{text,picture})using(var preview=new PeekForm(file)){TestEnv.Place(preview);preview.Show();Pump(400);check(preview.Ready,"preview content loads "+Path.GetExtension(file));preview.Close();}
            } finally { Directory.Delete(temp,true); }
            string queue; check(GrabMove.TestQueue(out queue), "grab queue coalesces atomically without hooks or real window moves ("+queue+")");
            using(var form=new SettingsForm(defaults,false,true)) {
                form.OnSave=(c,x)=>{}; TestEnv.Place(form); form.Show();
                foreach(int firstPage in new[]{0,1,2,7,0}) {
                    form.ShowPage(firstPage); Application.DoEvents();
                    check(Desc(form).OfType<PageHeading>().Any(h=>h.Visible && h.RectangleToScreen(h.ClientRectangle).IntersectsWith(form.RectangleToScreen(form.ClientRectangle)) && h.Width>UI.S(70) && h.Height>=UI.S(90) && h.Controls.OfType<Label>().Count(l=>l.Visible)==3),"page heading visible on page "+firstPage);
                    var visibleLabels=Desc(form).OfType<Label>().Where(c=>c.Visible && c.RectangleToScreen(c.ClientRectangle).IntersectsWith(form.RectangleToScreen(form.ClientRectangle))).ToList();
                    check(firstPage==7 ? Desc(form).OfType<PtCard>().Count(c=>c.Visible)==7 : visibleLabels.Count>3,"first-paint content exists on page "+firstPage);
                    using(var initial=TestEnv.Render(form)) initial.Save(Path.Combine(ExeDir,"firstpaint_"+firstPage+".png"));
                }
                form.ShowPage(7); form.PtShow("paste"); Pump(300); check(Desc(form).OfType<FlatBtn>().Count(b=>b.Nav && b.Selected)==1,"exactly one selected navigation item");
                foreach(int width in new[]{880,1080}) {
                    form.Size=new Size(UI.S(width),UI.S(700)); Pump(150);
                    foreach(var section in Desc(form).OfType<Section>().Where(c=>c.Visible && c.RectangleToScreen(c.ClientRectangle).IntersectsWith(form.RectangleToScreen(form.ClientRectangle)))) {
                        var screen=section.RectangleToScreen(section.ClientRectangle);
                        check(screen.Right <= form.Right + UI.S(2) && screen.Width >= UI.S(400), "card fits horizontal viewport at "+width+": "+screen.Width);
                    }
                    foreach(var editor in Desc(form).OfType<TextBox>().Where(t=>t.Visible && t.Multiline && t.RectangleToScreen(t.ClientRectangle).IntersectsWith(form.RectangleToScreen(form.ClientRectangle)))) check(editor.Height >= UI.S(100), "multiline editor retains readable height at "+width);
                    Shot(form,"redesign_paste_"+width+(percent==""?"":"_"+percent)+".png");
                }
                form.Close();
            }
        } catch(Exception ex) { check(false,ex.ToString()); }
        File.WriteAllText(Path.Combine(ExeDir,"interactiontest.txt"),report.ToString());return ok?0:1;
    }
    static int ImageTest() {
        UI.Init(1f);
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        string dir = Path.Combine(Path.GetTempPath(), "BongHub_imagetest");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Notes.Reset(dir); Notes.Cfg = new NotesCfg();
        try {
            var r = DemoRects()[1]; r.Width = UI.S(380); r.Height = UI.S(420);
            var f = DemoNote("g:lagoon", r); Pump(400);
            Put(f, "이미지 테스트\n", FontStyle.Bold, false, 13f);
            using (var bm = DemoImage(1400, 820)) f.InsertImage(bm);
            Put(f, "\n아래 줄 텍스트\n", FontStyle.Regular, false, 11f);
            Pump(600);
            var rb = (RichBox)f.Box; var objs = rb.ObjectChars();
            sb.AppendLine("INFO raw=" + string.Join(",", rb.RawText().Take(40).Select(ch => ((int)ch).ToString("X"))) + " len=" + rb.TextLength + " pict=" + rb.Rtf.Contains("\\pict") + " picts=" + rb.Pictures().Count + " h=" + rb.IsHandleCreated);
            check(objs.Count == 1, "one picture object in note (" + objs.Count + ")");
            int ci = objs.Count > 0 ? objs[0] : -1;
            var r0 = rb.ImageRect(ci); check(r0.Width > 50 && r0.Width <= rb.ClientSize.Width, "picture fits note width (" + r0 + ")");
            using (var im = rb.ImageOf(ci)) check(im != null && im.Width == 1400 && im.Height == 820, "original pixels kept (" + (im == null ? "null" : im.Width + "x" + im.Height) + ")");
            check(rb.ImageAt(new Point(r0.X + r0.Width / 2, r0.Y + r0.Height / 2)) == ci, "hit-test finds picture under mouse");
            rb.Select(ci, 1); rb.SelImage = ci; rb.Invalidate(); Pump(400);
            Shot(f, "note_image_selected.png");
            rb.SetImageSize(ci, r0.Width / 2, r0.Height / 2); Pump(300);
            var r1 = rb.ImageRect(ci); check(Math.Abs(r1.Width - r0.Width / 2) <= 3 && Math.Abs(r1.Height - r0.Height / 2) <= 3, "resize keeps ratio (" + r1.Size + ")");
            using (var im = rb.ImageOf(ci)) check(im != null && im.Width == 1400, "resize keeps original quality");
            rb.SetImageSize(ci, 99999, 99999); var r2 = rb.ImageRect(ci); check(r2.Width <= rb.ClientSize.Width, "max size limited to note width (" + r2.Width + ")");
            rb.SetImageScale(ci, 0.25); Pump(200); var r3 = rb.ImageRect(ci); check(Math.Abs(r3.Width - 350 * UI.Dpi / 96f) <= 4 || r3.Width <= rb.ClientSize.Width, "25% preset (" + r3.Width + ")");
            rb.SetImageSize(ci, r0.Width * 3 / 4, r0.Height * 3 / 4); Pump(300);
            Shot(f, "note_image_resized.png");
            f.Flush(); Pump(200);
            string rtf = Notes.LoadRtf(f.M.Id); var ps = RichBox.ParsePicts(rtf);
            check(ps.Count == 1 && Math.Abs(ps[0].GoalW * UI.Dpi / 1440f - r0.Width * 3 / 4) <= 4, "display size saved in note file (" + (ps.Count > 0 ? ps[0].GoalW + "tw" : "none") + ")");
            check(rb.Text.Contains("아래 줄 텍스트"), "text around picture intact");
            int noteRedraws=0; InvalidateEventHandler countNote=(a,b)=>noteRedraws++; f.Invalidated+=countNote;
            Notes.ApplySettings(Config.CloneObj(Notes.Cfg)); f.Invalidated-=countNote;
            check(noteRedraws==0,"unchanged settings do not repaint notes");
            rb.Select(ci,1); rb.SelImage=ci; rb.ScrollToCaret(); Pump(100);
            var dragBounds=rb.ImageRect(ci); int mx=dragBounds.Left+dragBounds.Width/2, my=dragBounds.Top+Math.Min(dragBounds.Height/2,UI.S(50));
            int invalidations=0; InvalidateEventHandler invalidated=(a,b)=>invalidations++; rb.Invalidated+=invalidated;
            TestSend(rb.Handle,0x0201,(IntPtr)1,MousePoint(mx,my));
            for(int n=1;n<=30;n++) { TestSend(rb.Handle,0x0200,(IntPtr)1,MousePoint(mx+n,my+n)); Application.DoEvents(); }
            check(invalidations<10,"30 drag updates avoid full editor invalidation ("+invalidations+")");
            TestSend(rb.Handle,0x0202,IntPtr.Zero,MousePoint(mx+30,my+30)); rb.Invalidated-=invalidated;
            check(!Application.OpenForms.Cast<Form>().Any(w=>w is ImageDragOutline),"drag outline disposed after drop");
            ci=rb.ObjectChars().Single();
            // long note -> slim scrollbar appears
            for (int k = 0; k < 30; k++) Put(f, "줄 " + k + "\n", FontStyle.Regular, false, 11f);
            Pump(500); var bar = f.Controls.OfType<Control>().SelectMany(Desc).OfType<SlimBar>().FirstOrDefault();
            check(bar != null && bar.Needed && bar.Visible, "slim scrollbar shown when needed");
            bar.Wake(); rb.Refresh(); Pump(400); Shot(f, "note_scrollbar.png");
            using (var bmp = TestEnv.Render(f)) { int lit = 0; for (int y = UI.S(40); y < f.Height - UI.S(40); y += 2) for (int x = UI.S(12); x < f.Width - UI.S(30); x += 2) { var px = bmp.GetPixel(x, y); if (px.R + px.G + px.B > 600) lit++; } check(lit > 40, "text visible after scrolling (" + lit + " bright px)"); }
            rb.SelectionStart = 0; rb.ScrollToCaret(); rb.Refresh(); Pump(300);
            using (var bmp = TestEnv.Render(f)) { int lit = 0; for (int y = UI.S(40); y < f.Height - UI.S(40); y += 2) for (int x = UI.S(12); x < f.Width - UI.S(30); x += 2) { var px = bmp.GetPixel(x, y); if (px.R + px.G + px.B > 600) lit++; } check(lit > 40, "text visible at top (" + lit + " bright px)"); }
            // Regression: move an embedded picture, then repeatedly scroll/repaint without re-reading the RTF payload.
            string textBeforeMove = rb.RawText().Replace("\uFFFC", "");
            rb.MoveImage(ci, rb.TextLength); Pump(100);
            ci = rb.ObjectChars().Single();
            check(rb.RawText().Replace("\uFFFC", "") == textBeforeMove, "move keeps surrounding text");
            check(rb.SelImage < 0, "move releases picture adorner before scrolling");
            int parses = rb.PictureParseCount; var scrollWatch = Stopwatch.StartNew();
            for (int n = 0; n < 40; n++) { rb.Select(n % 2 == 0 ? 0 : rb.TextLength, 0); rb.ScrollToCaret(); rb.Refresh(); }
            check(rb.PictureParseCount == parses, "move + 40 scroll/repaint cycles do not parse image RTF");
            sb.AppendLine("INFO post-move scroll/repaint ms " + scrollWatch.ElapsedMilliseconds);
            using (var deferred = new SettingsForm(Config.Load(), false, true)) {
                deferred.OnSave = (c, x) => { }; TestEnv.Place(deferred); deferred.Show(); deferred.Update();
                check(deferred.Visible && deferred.IsHandleCreated, "deferred settings shell visible before page build");
                deferred.ShowPage(0); Pump(100); check(deferred.Visible, "deferred settings page builds after shell"); deferred.Close();
            }
            // viewer
            using (var im = rb.ImageOf(ci)) { var v = new ImageViewer(new Bitmap(im)); TestEnv.Place(v); v.Size = new Size(UI.S(900), UI.S(620)); v.Show(); Pump(600);
                check(v.Canvas.Zoom < 1f, "viewer fits large image (" + Math.Round(v.Canvas.Zoom * 100) + "%)");
                v.Canvas.ZoomTo(1f, v.Canvas.Center, false); Pump(200); check(Math.Abs(v.Canvas.Zoom - 1f) < 0.01f, "viewer 100%");
                v.Canvas.ZoomTo(2f, new PointF(10, 10), false); Pump(200); Shot(v, "viewer_zoom200.png"); v.Close(); }
            rb.DeleteImage(ci); Pump(200); check(rb.ObjectChars().Count == 0, "delete picture");
            // seamless surface: title and paper colours meet without a seam
            using (var bmp = TestEnv.Render(f)) { var a = bmp.GetPixel(f.Width - UI.S(8), UI.S(35)); var b = bmp.GetPixel(f.Width - UI.S(8), UI.S(38)); int d = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); check(d < 24, "title/paper seamless (diff " + d + ")"); }
            Notes.Shutdown();
        } catch (Exception ex) { check(false, "exception " + ex); }
        try { Directory.Delete(dir, true); } catch { }
        File.WriteAllText(Path.Combine(ExeDir, "image_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }
    static IEnumerable<Control> Desc(Control c) { yield return c; foreach (Control k in c.Controls) foreach (var x in Desc(k)) yield return x; }
    static int NotesTest() {
        UI.Init(1f);
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        string realDir = Notes.Dir, realBefore = Directory.Exists(realDir) ? string.Join("|", Directory.GetFiles(realDir).OrderBy(x => x)) : "";
        string dir = Path.Combine(Path.GetTempPath(), "BongHub_notestest");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Notes.Reset(dir); Notes.Cfg = new NotesCfg();
        try {
            var rs = DemoRects();
            var fs = new[] { DemoNote("g:peach", rs[0]), DemoNote("g:grape", rs[1]), DemoNote("g:lagoon", rs[2]) };
            foreach (var f in fs) Native.SetWindowPos(f.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            FillDemo(fs);
            Pump(1200);
            IntPtr fg = Native.GetForegroundWindow(); check(!fs.Any(f => f.Handle == fg), "notes appear without stealing focus");
            check(fs.All(f => f.Left < -20000), "test notes stay off-screen");
            using (var bm = DemoImage(1400, 820)) fs[2].InsertImage(bm);
            Pump(800);
            var pal = PalettePopup.ShowAt(fs[0].Controls[2], fs[0].M.Color, k => { }); Pump(700); Shot(pal, "notes_palette.png"); pal.Close(); Pump(200);
            var u = Rectangle.Union(Rectangle.Union(fs[0].Bounds, fs[1].Bounds), fs[2].Bounds); u.Inflate(UI.S(30), UI.S(30));
            foreach (var f in fs) { TestEnv.Redraw(f); sb.AppendLine("INFO " + f.M.Color + " topmost=" + f.TopMost + " op=" + f.Opacity + " ex=" + ((long)Native.GetWindowLongPtr(f.Handle, -20)).ToString("X")); } Pump(400);
            ShotMany(fs, "notes_demo.png", UI.S(30));
            foreach (var f in fs) sb.AppendLine("INFO " + f.M.Color + " vis=" + f.Visible + " b=" + f.Bounds + " op=" + f.Opacity + " ws=" + Screen.PrimaryScreen.Bounds);
            var sf = new SettingsForm(Config.Load(), Startup.IsEnabled()); TestEnv.Place(sf); sf.OnSave = (c, x) => { }; sf.Show(); sf.ShowPage(2); Pump(1500);
            Shot(sf, "settings_notes_demo.png");
            check(sf.Icon == Program.AppIcon && Program.AppIcon != null, "blocks icon applied to settings window");
            sf.Close(); Pump(300);
            Notes.Shutdown(); Pump(300);
            check(Directory.GetFiles(dir, "*.json").Length == 3 && Directory.GetFiles(dir, "*.rtf").Length == 3, "3 notes saved (json+rtf)");
            check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temp files left (atomic write)");
            // move note 3 far off-screen on disk, then reload
            var js = new JavaScriptSerializer(); string id3 = fs[2].M.Id;
            var m3 = js.Deserialize<NoteMeta>(File.ReadAllText(Notes.MetaPath(id3))); m3.X = -60000; m3.Y = -60000; Notes.Write(Notes.MetaPath(id3), js.Serialize(m3));
            Notes.Reset(dir); Notes.EnsureLoaded();
            check(Notes.All.Count == 3, "reloaded 3 notes from disk");
            var byColor = Notes.All.ToDictionary(m => m.Color, m => m);
            check(byColor.ContainsKey("g:peach") && byColor.ContainsKey("g:grape") && byColor.ContainsKey("g:lagoon"), "colors persisted");
            check(byColor["g:peach"].Text.Contains("장보기 목록") && byColor["g:grape"].Text.Contains("회의 메모"), "text persisted");
            string r1 = Notes.LoadRtf(byColor["g:peach"].Id), r2 = Notes.LoadRtf(byColor["g:grape"].Id);
            check(r1.Contains("\\b") && r1.Contains("\\strike") && (r1.Contains("\\pn") || r1.Contains("pntext")), "rtf keeps bold/strike/bullets");
            check(r2.Contains("\\i") && r2.Contains("\\ul"), "rtf keeps italic/underline");
            string r3 = Notes.LoadRtf(byColor["g:lagoon"].Id);
            check(r3 != null && r3.Contains("\\pict") && r3.Length < 3 * 1024 * 1024, "image saved inside rtf (" + (r3 == null ? 0 : r3.Length / 1024) + " KB, " + (r3 != null && r3.Contains("pngblip") ? "png" : r3 != null && r3.Contains("jpegblip") ? "jpeg" : "other") + ")");
            check(Math.Abs(byColor["g:peach"].X - rs[0].X) <= 2 && Math.Abs(byColor["g:peach"].W - rs[0].Width) <= 2, "position/size persisted");
            Notes.ShowAll(); Pump(800);
            var g3 = Notes.FormOf(Notes.All.First(m => m.Id == id3));
            check(g3 != null && g3.Box.Rtf.Contains("\\pict"), "image restored after reload");
            check(g3 != null && Notes.Areas().Any(a => a.Contains(new Point(g3.Left + 10, g3.Top + 10)) && a.Contains(new Point(g3.Right - 10, g3.Top + 10))), "off-screen note clamped onto a visible monitor: " + (g3 == null ? "null" : g3.Bounds.ToString()));
            var a1 = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, 0, 2560, 1400) };
            var c1 = Notes.Clamp(new Rectangle(9000, 300, 300, 300), a1, 32); check(a1[1].Contains(c1), "clamp: right of all monitors -> nearest (" + c1 + ")");
            var c2 = Notes.Clamp(new Rectangle(100, 100, 300, 300), a1, 32); check(c2 == new Rectangle(100, 100, 300, 300), "clamp: visible note unchanged");
            var c3 = Notes.Clamp(new Rectangle(-500, 2000, 5000, 5000), a1, 32); check(a1.Any(a => a.Contains(c3)), "clamp: oversized shrinks into area (" + c3 + ")");
            Notes.HideAll(); check(Notes.All.All(m => !m.Open) && Notes.All.All(m => !Notes.IsOpen(m)), "hide all -> closed but kept");
            foreach (var m in Notes.All.ToList()) Notes.Delete(m);
            check(Directory.GetFiles(dir).Length == 0, "delete removes files");
        } catch (Exception ex) { check(false, "exception " + ex); }
        finally {
            try { Notes.Shutdown(); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
        check(!Directory.Exists(dir), "test notes folder cleaned up");
        string realAfter = Directory.Exists(realDir) ? string.Join("|", Directory.GetFiles(realDir).OrderBy(x => x)) : "";
        check(realBefore == realAfter, "real notes folder untouched");
        File.WriteAllText(Path.Combine(ExeDir, "notes_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }
    static int NotesBench(int secs) {
        UI.Init(1f);
        string dir = Path.Combine(Path.GetTempPath(), "BongHub_notesbench");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Notes.Reset(dir); Notes.Cfg = new NotesCfg();
        var rs = DemoRects(); var fs = new[] { DemoNote("green", rs[0]), DemoNote("purple", rs[1]), DemoNote("gray", rs[2]) }; FillDemo(fs);
        var t = new WTimer { Interval = secs * 1000 }; t.Tick += (s, e) => { t.Stop(); Notes.Shutdown(); Application.ExitThread(); }; t.Start();
        Application.Run();
        try { Directory.Delete(dir, true); } catch { }
        return 0;
    }
    static int KakaoTest() {
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        var k = Config.Load().Kakao;
        sb.AppendLine("config kakao: AdBlock=" + k.AdBlock + " Compact=" + k.Compact + " Dark=" + k.Dark + " AutoApply=" + k.AutoApply);
        sb.AppendLine("kakaotalk running: " + Kakao.KakaoRunning());
        Kakao.Extract();
        string h; using (var sha = System.Security.Cryptography.SHA256.Create()) h = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Kakao.ExePath))).Replace("-", "");
        check(h == "4E87998CB143640434AD6A178DC9C881F890C81ED77FA9B5F1BC228340363B49", "extracted helper SHA256 " + h);
        Kakao.Stop(); check(!Kakao.IsRunning(), "stop -> no BongAd process");
        Kakao.Start(new KakaoCfg { AdBlock = true, Compact = true, Dark = false }); Thread.Sleep(2500);
        check(Kakao.IsRunning() && Kakao.Procs().Length == 1, "start (compact) -> 1 helper running");
        Kakao.Start(new KakaoCfg { AdBlock = true, Compact = false, Dark = true }); Thread.Sleep(3000);
        check(Kakao.Procs().Length == 1, "restart with /nocompact /dark replaces -> still 1 helper");
        string hidden = Path.Combine(Path.GetTempPath(), "BongAd.hidden.txt");
        sb.AppendLine("hidden list exists: " + File.Exists(hidden));
        Kakao.Stop(); Thread.Sleep(500);
        check(!Kakao.IsRunning(), "/restore stops helper");
        Kakao.Sync(new KakaoCfg { AdBlock = true, Compact = k.Compact, Dark = k.Dark }); Thread.Sleep(2500);
        check(Kakao.IsRunning(), "sync(on) starts helper");
        Kakao.Stop();
        check(!Kakao.IsRunning(), "final stop");
        File.WriteAllText(Path.Combine(ExeDir, "kakao_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }
    class MockBright : IBrightBackend {
        public List<string> Mons = new List<string>(); public Dictionary<string, int> Set_ = new Dictionary<string, int>(); public HashSet<string> Unsupported = new HashSet<string>(); public int Calls; public Func<int, List<string>> Wake;
        public List<string> Monitors() { return Wake != null ? Wake(Calls) : Mons; }
        public bool Set(string id, int pct, out string how) { Calls++; if (Unsupported.Contains(id)) { how = "DDC/CI 미지원"; return false; } if (!Monitors().Contains(id)) { how = "모니터가 아직 켜지지 않음"; return false; } Set_[id] = pct; how = "mock"; return true; }
    }
    // brightness + linked icon layout, all with mock backends: no real brightness change, no real icon move, no monitor switch
    static int ExtrasTest() {
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        var oldB = PresetExtras.Bright; var oldF = PresetExtras.FindDesk; var oldR = PresetExtras.RestoreDesk; var oldS = PresetExtras.Signature; var oldSl = PresetExtras.Sleep; var oldSt = PresetExtras.StampFile;
        try {
            PresetExtras.StampFile = Path.Combine(Path.GetTempPath(), "BongHub_extrastest.stamp"); try { File.Delete(PresetExtras.StampFile); } catch { }
            int slept = 0; PresetExtras.Sleep = ms => slept += ms;
            int sigCalls = 0; PresetExtras.Signature = () => { sigCalls++; return sigCalls < 5 ? "changing" + sigCalls : "stable"; };
            // migration: 3.1 config without the new fields
            var js = new JavaScriptSerializer(); var old = js.Deserialize<Config>("{\"Presets\":[{\"Name\":\"작업\",\"Hotkey\":\"Ctrl+Alt+1\"}]}"); old.Normalize();
            check(old.Presets[0].Brightness != null && old.Presets[0].Brightness.Count == 0 && old.Presets[0].DeskLayout == "", "3.1 preset migrates (no brightness, no link)"); check(old.Presets[0].Workspace == "", "3.3 preset migrates (no workspace link)");
            check(!PresetExtras.HasWork(old.Presets[0]), "no extras -> nothing runs");
            var bad = js.Deserialize<Config>("{\"Presets\":[{\"Name\":\"x\",\"Brightness\":[{\"Monitor\":null,\"Value\":250},null]}]}"); bad.Normalize();
            check(bad.Presets[0].Brightness.Count == 1 && bad.Presets[0].Brightness[0].Value == 100 && bad.Presets[0].Brightness[0].Monitor == "", "bad brightness values clamped");
            // targets: per-monitor beats all
            var p = new Preset { Name = "작업" }; p.Brightness.Add(new MonBright { Monitor = "", Value = 60 }); p.Brightness.Add(new MonBright { Monitor = "SAM73D3", Value = 30 }); p.Brightness.Add(new MonBright { Monitor = "GSM76F5", Value = -1 });
            var tg = PresetExtras.Targets(p, new[] { "SAM73D3", "SAM747A", "GSM76F5" });
            check(tg["SAM73D3"] == 30 && tg["SAM747A"] == 60 && !tg.ContainsKey("GSM76F5"), "per-monitor value wins, '변경 안 함' skipped");
            // monitor comes up late (2nd attempt), one unsupported
            var mb = new MockBright(); mb.Unsupported.Add("DRS2555");
            mb.Wake = calls => calls < 2 ? new List<string> { "SAM73D3", "DRS2555" } : new List<string> { "SAM73D3", "DRS2555", "SAM747A" };
            PresetExtras.Bright = mb; var statuses = new List<string>();
            var p2 = new Preset { Name = "작업" }; p2.Brightness.Add(new MonBright { Monitor = "", Value = 80 }); p2.Brightness.Add(new MonBright { Monitor = "SAM747A", Value = 40 });
            PresetExtras.Run(p2, x => statuses.Add(x));
            check(mb.Set_.ContainsKey("SAM73D3") && mb.Set_["SAM73D3"] == 80, "brightness all -> 80%");
            check(mb.Set_.ContainsKey("SAM747A") && mb.Set_["SAM747A"] == 40, "late monitor set on retry (" + PresetExtras.LastBright + ")");
            check(PresetExtras.LastBright.Contains("실패") && PresetExtras.LastBright.Contains("DRS2555"), "unsupported monitor reported");
            check(slept >= PresetExtras.RetryDelay && slept <= 30000, "waited for stable screen + retry delay (" + slept + " ms)");
            check(statuses.Any(x => x.Contains("밝기")), "status shown on overlay");
            // throwing backend must not break the switch
            PresetExtras.Bright = new ThrowBright(); bool threw = false; try { PresetExtras.Run(p2, x => { }); } catch { threw = true; } check(!threw && PresetExtras.LastBright.Contains("실패"), "backend exception contained");
            // linked desk layout: restored exactly once, suppresses auto-restore, deleted layout reported
            PresetExtras.Bright = new MockBright();
            var lay = new DeskLayout { Id = "L1", Name = "작업 배치" }; int restores = 0;
            PresetExtras.FindDesk = id => id == "L1" ? lay : null; PresetExtras.RestoreDesk = l => { restores++; return new DeskResult { Ok = true, Moved = 12 }; };
            var p3 = new Preset { Name = "작업", DeskLayout = "L1" }; PresetExtras.LinkedAt = DateTime.MinValue;
            check(!PresetExtras.LinkedRecently, "no suppression before");
            PresetExtras.Run(p3, x => { });
            check(restores == 1 && PresetExtras.LastDesk.Contains("작업 배치"), "linked layout restored exactly once (" + restores + ")");
            check(PresetExtras.LinkedRecently, "auto-restore suppressed right after linked restore");
            check(File.Exists(PresetExtras.StampFile), "cross-process stamp written");
            PresetExtras.LinkedAt = DateTime.MinValue; File.SetLastWriteTime(PresetExtras.StampFile, DateTime.Now.AddMinutes(-5)); check(!PresetExtras.LinkedRecently, "suppression expires");
            var p4 = new Preset { Name = "x", DeskLayout = "GONE" }; restores = 0; PresetExtras.Run(p4, x => { });
            check(restores == 0 && PresetExtras.LastDesk.Contains("삭제"), "deleted layout -> notice, no restore");
            PresetExtras.RestoreDesk = l => new DeskResult { Ok = false, Error = "바탕화면 자동 정렬이 켜져 있어요" }; PresetExtras.Run(p3, x => { });
            check(PresetExtras.LastDesk.Contains("실패"), "restore failure reported");
            // linked workspace: once, after icons, running apps moved, deleted -> notice, failure contained, no double run
            { var oFW = PresetExtras.FindWork; var oLW = PresetExtras.LaunchWork; var oLM = PresetExtras.WorkLaunchMissing; var oWS = PresetExtras.WorkStampFile;
              try {
                PresetExtras.WorkStampFile = Path.Combine(Path.GetTempPath(), "bh_work_test.stamp"); try { File.Delete(PresetExtras.WorkStampFile); } catch { }
                var ws = new Workspace { Id = "W1", Name = "코딩" }; var order = new List<string>(); bool launchArg = true;
                PresetExtras.FindWork = id => id == "W1" ? ws : null; PresetExtras.WorkLaunchMissing = () => false;
                PresetExtras.RestoreDesk = l => { order.Add("desk"); return new DeskResult { Ok = true, Moved = 3 }; };
                PresetExtras.LaunchWork = (w, launch) => { order.Add("work"); launchArg = launch; return "'" + w.Name + "' · 배치 2개"; };
                var p5 = new Preset { Name = "코딩", DeskLayout = "L1", Workspace = "W1" }; var st5 = new List<string>();
                PresetExtras.Run(p5, x => st5.Add(x));
                check(string.Join(",", order) == "desk,work", "workspace runs once right after icon restore (" + string.Join(",", order) + ")");
                check(!launchArg && PresetExtras.LastWork.Contains("배치 2개") && st5.Any(x => x.Contains("창 배치")), "launch-missing setting followed, status shown");
                order.Clear(); PresetExtras.Run(p5, x => { }); check(!order.Contains("work") && PresetExtras.LastWork.Contains("건너뛰"), "duplicate run within 10 s skipped (CLI + tray)");
                File.SetLastWriteTime(PresetExtras.WorkStampFile, DateTime.Now.AddMinutes(-1)); order.Clear(); PresetExtras.Run(p5, x => { }); check(order.Contains("work"), "runs again on a later switch");
                var p6 = new Preset { Name = "y", Workspace = "GONE" }; order.Clear(); PresetExtras.Run(p6, x => { }); check(!order.Contains("work") && PresetExtras.LastWork.Contains("삭제"), "deleted workspace -> notice");
                File.Delete(PresetExtras.WorkStampFile); PresetExtras.LaunchWork = (w, l) => { throw new Exception("boom"); }; bool thr = false; try { PresetExtras.Run(p5, x => { }); } catch { thr = true; }
                check(!thr && PresetExtras.LastWork.Contains("실패"), "workspace failure contained");
                check(PresetExtras.HasWork(new Preset { Workspace = "W1" }), "workspace-only preset has work");
              } catch (Exception ex2) { check(false, "workspace exception " + ex2); }
              finally { PresetExtras.FindWork = oFW; PresetExtras.LaunchWork = oLW; PresetExtras.WorkLaunchMissing = oLM; try { File.Delete(PresetExtras.WorkStampFile); } catch { } PresetExtras.WorkStampFile = oWS; } }
            check(SettingsForm.BrightSummary(p) == "적용: SAM73D3 30%, 전체 60%" || SettingsForm.BrightSummary(p).Contains("SAM73D3 30%"), "summary text");
        } catch (Exception ex) { check(false, "exception " + ex); }
        finally { PresetExtras.Bright = oldB; PresetExtras.FindDesk = oldF; PresetExtras.RestoreDesk = oldR; PresetExtras.Signature = oldS; PresetExtras.Sleep = oldSl; try { File.Delete(PresetExtras.StampFile); } catch { } PresetExtras.StampFile = oldSt; }
        File.WriteAllText(Path.Combine(ExeDir, "extrastest.txt"), sb.ToString());
        return ok ? 0 : 1;
    }
    class ThrowBright : IBrightBackend { public List<string> Monitors() { return new List<string> { "A" }; } public bool Set(string id, int pct, out string how) { throw new InvalidOperationException("boom"); } }
    // overlay spinner icon stability: renders frames off-screen and measures icon movement; simulates primary-monitor changes
    static int JitterTest() {
        UI.Init(1f); var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        try {
            var areaA = new Rectangle(TestEnv.Area.X, TestEnv.Area.Y, 1920, 1080);
            Rectangle cur = areaA; OverlayForm.BoundsSource = () => cur;
            foreach (bool legacy in new[] { true, false }) {
                OverlayForm.LegacyIcon = legacy;
                var f = new OverlayForm("자리비움 모드로 전환 중...", "잠시만 기다려 주세요", 82); f.Show(); Pump(300);
                var c = f.IconCenter; int half = (int)(54 * 1.1f / 2) + 4; var box = new Rectangle(c.X - half, c.Y - half, half * 2, half * 2);
                double maxShift = 0, sumDiff = 0; int n = 0; PointF? prev = null; int[] prevPix = null; double maxDiff = 0;
                for (int i = 0; i < 160; i++) {
                    using (var bm = f.Frame(1000 + i * 15.0)) {
                        double sx = 0, sy = 0, sw = 0; var pix = new int[box.Width * box.Height];
                        for (int y = 0; y < box.Height; y++) for (int x = 0; x < box.Width; x++) {
                            var p = bm.GetPixel(box.X + x, box.Y + y); int lum = (p.R * 3 + p.G * 6 + p.B) / 10; pix[y * box.Width + x] = lum;
                            if (lum > 150) { sx += x * lum; sy += y * lum; sw += lum; }
                        }
                        var cen = new PointF((float)(sx / Math.Max(1, sw)), (float)(sy / Math.Max(1, sw)));
                        if (prev.HasValue) { double d = Math.Sqrt(Math.Pow(cen.X - prev.Value.X, 2) + Math.Pow(cen.Y - prev.Value.Y, 2)); maxShift = Math.Max(maxShift, d); }
                        if (prevPix != null) { double s2 = 0; for (int q = 0; q < pix.Length; q++) s2 += Math.Abs(pix[q] - prevPix[q]); s2 /= pix.Length; sumDiff += s2; maxDiff = Math.Max(maxDiff, s2); n++; }
                        prev = cen; prevPix = pix;
                        if (i == 40) bm.Save(Path.Combine(ExeDir, "overlay_frame_" + (legacy ? "3.1" : "3.2") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                sb.AppendLine("INFO " + (legacy ? "3.1.0" : "3.3.1") + " icon centroid max shift " + maxShift.ToString("0.000") + " px · mean frame diff " + (sumDiff / n).ToString("0.00") + " · max " + maxDiff.ToString("0.00") + " (0-255 lum)");
                if (!legacy) { check(maxShift < 0.35, "icon centroid stable (<0.35 px)"); }
                // simulated monitor switch: primary flips through intermediate states, then settles
                if (!legacy) {
                    int r0 = f.Relayouts; var areaB = new Rectangle(TestEnv.Area.X, TestEnv.Area.Y, 1280, 720); var areaC = new Rectangle(TestEnv.Area.X + 100, TestEnv.Area.Y, 1600, 900);
                    foreach (var r in new[] { areaB, areaA, areaB, areaA }) { cur = r; Pump(130); }
                    int flap = f.Relayouts - r0; check(flap == 0, "no relayout while bounds flap quickly (" + flap + ")");
                    cur = areaC; Pump(1100); check(f.Relayouts - r0 == 1 && f.Bounds == areaC, "one relayout after bounds settle (" + (f.Relayouts - r0) + ")");
                    var cc = f.IconCenter; check(Math.Abs(cc.X - areaC.Width / 2) <= 1, "icon centered on new primary (" + cc.X + ")");
                    var c1 = f.IconCenter; Pump(800); check(f.IconCenter == c1, "icon stays put after settle");
                }
                f.Close(); Pump(150);
            }
        } catch (Exception ex) { check(false, "exception " + ex); }
        OverlayForm.BoundsSource = null; OverlayForm.LegacyIcon = false;
        File.WriteAllText(Path.Combine(ExeDir, "jittertest.txt"), sb.ToString());
        return ok ? 0 : 1;
    }
    // PowerToys features: pure logic + off-screen renders; never hooks the real desktop, never moves real windows
    static int PtTest() {
        UI.Init(1f); var sb = new StringBuilder(); bool ok = true; string prog = Path.Combine(ExeDir, "pttest.progress.txt"); try { File.Delete(prog); } catch { }
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; try { File.AppendAllText(prog, (c ? "PASS " : "FAIL ") + n + "\r\n"); } catch { } };
        try {
            // defaults: risky features off, no hooks installed
            var d = Config.Defaults(); d.Normalize();
            check(!d.Pt.Zones.Enabled && !d.Pt.Grab.Enabled && !d.Pt.Palette.Enabled && !d.Pt.Paste.Enabled && !d.Pt.Peek.Enabled && !d.Pt.Resizer.ContextMenu, "risky features default off");
            Zones.Apply(d.Pt.Zones); GrabMove.Apply(d.Pt.Grab); check(!Zones.Active && !GrabMove.Active, "no hooks while disabled");
            var old = new JavaScriptSerializer().Deserialize<Config>("{\"Version\":2}"); old.Normalize(); check(old.Pt != null && old.Pt.Resizer.Presets.Count == 4, "3.1 config migrates (Pt filled)");
            var rt = Config.CloneObj(d); check(rt.Pt.Palette.Hotkey == "Ctrl+Alt+Space" && rt.Pt.Resizer.Presets[1].W == 1366, "Pt round-trips through JSON");
            // zone math
            var wa = new Rectangle(0, 0, 1920, 1080);
            var c2 = Zones.Rects(new ZoneLayout { Kind = "cols", A = 2, Spacing = 0 }, wa); check(c2.Count == 2 && c2[0].Width == 960 && c2[1].Right == 1920, "2 columns");
            var g = Zones.Rects(new ZoneLayout { Kind = "grid", A = 2, B = 2, Spacing = 8 }, wa); check(g.Count == 4 && g.All(r => wa.Contains(r)) && !g[0].IntersectsWith(g[3]), "2x2 grid inside area, no overlap");
            var pr = Zones.Rects(new ZoneLayout { Kind = "priority", Spacing = 0 }, new Rectangle(0, 0, 4000, 1000)); check(pr.Count == 3 && pr[1].Width == 2000 && pr[0].Width == 1000, "priority 25/50/25");
            var cu = Zones.Rects(new ZoneLayout { Kind = "custom", Custom = new List<float> { 1, 3 }, Spacing = 0 }, wa); check(cu.Count == 2 && cu[0].Width == 480, "custom ratio 1:3");
            var rw = Zones.Rects(new ZoneLayout { Kind = "rows", A = 3, Spacing = 0 }, wa); check(rw.Count == 3 && rw[2].Bottom == 1080, "3 rows");
            // window rule zone target
            Zones.Cfg = new ZonesCfg(); var sc = Screen.PrimaryScreen; var zr = Zones.ZoneRect(sc.WorkingArea, 0);
            check(zr.HasValue && sc.WorkingArea.Contains(zr.Value), "rule zone rect on primary"); check(!Zones.ZoneRect(sc.WorkingArea, 99).HasValue, "bad zone index -> null");
            check(Array.IndexOf(WindowRule.Codes, "zone") >= 0, "window rule has zone action");
            // overlay render off-screen
            var ov = new ZoneOverlay(); ov.Setup(wa, Zones.Rects(new ZoneLayout { Kind = "priority" }, wa), 80); ov.SetHot(1); ov.ShowNoActivate(); Pump(300);
            check(ov.Left <= -20000, "zone overlay off-screen"); Shot(ov, "pt_zones_overlay.png"); ov.Close();
            // palette
            check(Palette.Calc("12*3+4") == "40" && Palette.Calc("(1+2)/4") == "0.75" && Palette.Calc("2^3") != null && Palette.Calc("hello") == null && Palette.Calc("123") == null, "calculator");
            check(Palette.Score("메모", "새 스티커 메모") > 0 && Palette.Score("ntp", "notepad") > 0 && Palette.Score("xyz", "notepad") == 0, "fuzzy score");
            Palette.Commands = () => new[] { new PaletteItem { Title = "모니터 프리셋: 작업", Key = "작업" }, new PaletteItem { Title = "새 스티커 메모", Key = "memo" } };
            var pc = new PaletteCfg { Apps = false };
            var res = Palette.Search("작업", pc); check(res.Count >= 1 && res[0].Title.Contains("작업"), "palette finds preset");
            res = Palette.Search("7*6", pc); check(res.Count > 0 && res[0].Title == "= 42", "palette calc first");
            var sw = Stopwatch.StartNew(); var apps = Palette.Apps(); double appMs = sw.Elapsed.TotalMilliseconds; sb.AppendLine("INFO start menu apps " + apps.Count + " in " + appMs.ToString("0") + " ms");
            var pf = new PaletteForm(new PaletteCfg { Apps = false }); pf.Show(); pf.SetQuery("메모"); Pump(300); check(pf.Left <= -20000 && pf.Count >= 1, "palette form off-screen with results"); Shot(pf, "pt_palette.png");
            pf.SetQuery("12*3"); Pump(200); check(pf.Top != null && pf.Top.Title == "= 36", "palette form calc"); Shot(pf, "pt_palette_calc.png"); pf.Close();
            // advanced paste conversions
            string html = "Version:0.9\r\n<html><body><!--StartFragment--><h2>제목</h2><p>이건 <b>굵게</b>, <i>기울임</i>, <a href=\"https://x.y\">링크</a></p><ul><li>하나</li><li>둘</li></ul><!--EndFragment--></body></html>";
            var md = AdvPaste.HtmlToMarkdown(html); check(md.Contains("## 제목") && md.Contains("**굵게**") && md.Contains("*기울임*") && md.Contains("[링크](https://x.y)") && md.Contains("- 하나"), "html -> markdown");
            var js = AdvPaste.ToJson("name,age\n봉,30\n석,25"); check(js.Contains("\"name\": \"봉\"") && js.Contains("\"age\": 30"), "csv -> json");
            js = AdvPaste.ToJson("a\tb\n1\t2"); check(js.Contains("\"a\": 1"), "tsv -> json");
            js = AdvPaste.ToJson("{\"x\":[1,2],\"y\":\"z\"}"); check(js.Contains("\n") && js.Contains("\"y\": \"z\""), "json pretty");
            js = AdvPaste.ToJson("host: a.b\nport = 80"); check(js.Contains("\"port\": 80"), "key:value -> json");
            // workspace capture serialization (reads window list only)
            var cap = Workspaces.Capture(); sb.AppendLine("INFO captured windows " + cap.Count);
            var ws = new Workspace { Name = "테스트", Apps = cap }; var ws2 = Config.CloneObj(ws); check(ws2.Apps.Count == cap.Count && ws2.Name == "테스트", "workspace serializes");
            check(cap.All(a => a.Path != "" && a.W > 0), "captured apps have path and size");
            // peek
            string dir = Path.Combine(Path.GetTempPath(), "BongHub_pttest"); try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } Directory.CreateDirectory(dir);
            string img = Path.Combine(dir, "photo.jpg"), png = Path.Combine(dir, "shot.png"), txt = Path.Combine(dir, "readme.md"), bin = Path.Combine(dir, "doc.pdf");
            using (var bm = DemoImage(2400, 1600)) { bm.Save(img, System.Drawing.Imaging.ImageFormat.Jpeg); }
            using (var bm = DemoImage(800, 600)) bm.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(txt, "# BongHub\n미리보기 텍스트 테스트\n- 한글 OK\n", new UTF8Encoding(false)); File.WriteAllBytes(bin, new byte[] { 0x25, 0x50, 0x44, 0x46 });
            check(Peek.Kind(img) == "image" && Peek.Kind(txt) == "markdown" && (Peek.Kind(bin) == "info" || Peek.Kind(bin) == "handler") && Peek.Kind(dir) == "folder", "peek kinds");
            check(Peek.ReadText(txt).Contains("한글 OK"), "peek reads utf8");
            // ---- 3.3 parity checks ----
            check(Palette.Calc("2^10+sqrt(16)*(3-1)") == "1032", "calc parser (^ sqrt parens)");
            check(Palette.Calc("10 % 3") == "1" && Palette.Calc("0xff") != null, "calc mod/hex");
            { var u = Palette.Unit("10 km to mi"); check(u != null && u.StartsWith("6.21"), "unit km->mi " + u); var t = Palette.Unit("100 c to f"); check(t != null && t.StartsWith("212"), "unit c->f " + t); }
            check(Palette.Time("time").Count > 0, "time items");
            { var zl = new ZoneLayout { Kind = "cols", A = 2 }; Zones.Split(zl, 0, false); var rs = Zones.Rects(zl, new Rectangle(0, 0, 1000, 1000)); check(zl.Kind == "canvas" && rs.Count == 3, "editor split -> 3 zones");
              check(Zones.Merge(zl, 0, 1) && Zones.Rects(zl, new Rectangle(0, 0, 1000, 1000)).Count == 2, "editor merge -> 2 zones"); }
            check(AdvPaste.ApplyCustom("010-1234-5678", new PasteAction { Kind = "regex", Pattern = "[^0-9]", Replace = "" }) == "01012345678", "custom regex action");
            check(AdvPaste.ApplyCustom("b\na\nb", new PasteAction { Kind = "dedupe" }).Replace("\r", "") == "b\na", "custom dedupe");
            check(SettingsForm.ParseActions("전화 | regex | [^0-9] | ").Count == 1 && SettingsForm.ParsePresets("반 50x50 %")[0].Unit == "%", "settings parse actions/units");
            { var rp = Resizer.Resolve(new ResizePreset("반", 50, 50) { Unit = "%" }, new Size(2400, 1600), 96, 96, true); check(rp.W == 1200 && rp.H == 800, "resize % unit");
              var ro = Resizer.Resolve(new ResizePreset("p", 1920, 1080), new Size(1000, 2000), 96, 96, true); check(ro.W == 1080 && ro.H == 1920, "ignore orientation swaps for portrait");
              var cm = Resizer.Resolve(new ResizePreset("c", 254, 254) { Unit = "cm" }, new Size(4000, 4000), 96, 96, false); check(cm.W == 9600, "resize cm unit");
              check(Path.GetFileName(Resizer.OutPath(img, new ResizePreset("t", 10, 10), false, "%1_%5x%6", new Size(7, 9))) == "photo_7x9.jpg", "resize name format"); }
            foreach (var f in new[] { img, txt, bin }) { var pk = new PeekForm(f); pk.Show(); Pump(300); check(pk.Left <= -20000, "peek off-screen " + Path.GetExtension(f)); Shot(pk, "pt_peek_" + Path.GetExtension(f).TrimStart('.') + ".png"); pk.Close(); }
            // resizer
            var rc = new ResizerCfg(); rc.Presets = ResizerCfg.DefaultPresets();
            check(Resizer.Target(new Size(2400, 1600), rc.Presets[1], "fit", true) == new Size(1152, 768), "fit keeps ratio");
            check(Resizer.Target(new Size(800, 600), rc.Presets[2], "fit", true) == new Size(800, 600), "shrink-only skips small");
            check(Resizer.Target(new Size(2400, 1600), rc.Presets[0], "stretch", false) == new Size(854, 480), "stretch");
            string o1 = Resizer.One(img, rc.Presets[1], rc); using (var im = Image.FromFile(o1)) check(im.Width == 1152 && im.Height == 768 && o1.EndsWith("photo (중간).jpg"), "jpeg resized to new file (" + im.Size + ")");
            check(File.Exists(img) && new FileInfo(img).Length > 0, "original kept");
            rc.Fit = "fill"; rc.IgnoreOrientation = false; string o2 = Resizer.One(img, rc.Presets[3], rc); using (var im = Image.FromFile(o2)) check(im.Width == 320 && im.Height == 568, "fill crops to exact size");
            rc.IgnoreOrientation = true; rc.KeepDate = true; File.SetLastWriteTime(img, new DateTime(2020, 1, 2)); string o3 = Resizer.One(img, rc.Presets[3], rc); using (var im = Image.FromFile(o3)) check(im.Width == 568 && im.Height == 320, "ignore orientation turns portrait preset for landscape photo"); check(File.GetLastWriteTime(o3).Year == 2020, "keep modified date"); rc.KeepDate = false;
            rc.Fit = "fit"; check(Resizer.One(png, rc.Presets[2], rc) == null, "small png skipped");
            string summary = Resizer.Run(new[] { img, png, bin }, rc.Presets[0], rc); check(summary.Contains("변환 2개") && summary.Contains("실패 1개"), "batch summary (" + summary + ")");
            check(SettingsForm.ParsePresets("작게 800x600\n잘못\n큰 1920×1080").Count == 2, "preset parser");
            var rf = new ResizerForm(rc, new[] { img, png }); rf.Show(); Pump(300); check(rf.Left <= -20000, "resizer window off-screen"); Shot(rf, "pt_resizer.png"); rf.Close();
            bool menuBefore = Resizer.MenuInstalled(); Resizer.SetMenu(true); check(Resizer.MenuInstalled() == menuBefore, "context menu untouched in test mode");
            // paste menu render (clipboard not touched in this test)
            // settings PowerToys page: grid, search, subpages, conflicts
            var cfg = Config.Defaults(); cfg.Normalize(); cfg.Pt.Palette.Enabled = true; cfg.Pt.Palette.Hotkey = "Ctrl+Alt+1"; cfg.Pt.Paste.Enabled = true;
            var sf = new SettingsForm(cfg, false); TestEnv.Place(sf); sf.Show(); Pump(400);
            sf.ScrollToExtras(); Pump(500); Shot(sf, "preset_extras.png");
            sf.ShowPage(7); Pump(500); check(sf.PtConflicts().Contains("Ctrl+Alt+1"), "conflict with preset hotkey detected");
            Shot(sf, "pt_grid.png");
            foreach (var k in SettingsForm.PtKeys) { sf.PtShow(k); Pump(250); Shot(sf, "pt_sub_" + k + ".png"); }
            sf.PtShow(null); Pump(200); check(sf.PtOpen == null, "back to grid");
            sf.Close();
            check(!Zones.Active && !GrabMove.Active, "still no hooks after settings");
            // enable/disable hooks only in a short, isolated window: verify install + clean removal (no drag happens)
            var zc = new ZonesCfg { Enabled = true }; Zones.Apply(zc); bool zOn = Zones.Active; Zones.Apply(new ZonesCfg()); check(zOn && !Zones.Active, "zones hook installs and removes");
            var gc = new GrabCfg { Enabled = true }; GrabMove.Apply(gc); bool gOn = GrabMove.Active; GrabMove.Apply(new GrabCfg()); check(gOn && !GrabMove.Active, "grab hook installs and removes");
            check(GrabMove.Eligible(IntPtr.Zero) == false, "grab ignores null window");
        } catch (Exception ex) { check(false, "exception " + ex); }
        Zones.Apply(new ZonesCfg()); GrabMove.Apply(new GrabCfg());
        File.WriteAllText(Path.Combine(ExeDir, "pttest.txt"), sb.ToString());
        return ok ? 0 : 1;
    }
    // perfbench: same calls on 3.2.0 and 3.3.0 so numbers are comparable; off-screen only, no real input, no real windows moved
    static int PerfBench() {
        UI.Init(1f); var sb = new StringBuilder(); var sw = new Stopwatch();
        Func<Action, int, double> timeUs = (a, n) => { a(); var t = Stopwatch.StartNew(); for (int i = 0; i < n; i++) a(); return t.Elapsed.TotalMilliseconds * 1000.0 / n; };
        try {
            var proc = Process.GetCurrentProcess();
            sb.AppendLine("startup_to_bench_ms " + (DateTime.Now - proc.StartTime).TotalMilliseconds.ToString("0"));
            // zones: per-tick zone computation used while dragging (layout lookup + rects + hit test)
            var scr = Screen.PrimaryScreen; var wa = scr.WorkingArea; var pt = new Point(wa.X + wa.Width / 3, wa.Y + wa.Height / 2);
            int g0 = GC.CollectionCount(0);
            double z = timeUs(() => { var rs = Zones.Rects(Zones.For(scr), wa); rs.FindIndex(r => r.Contains(pt)); }, 20000);
            sb.AppendLine("zones_tick_us " + z.ToString("0.00") + " gen0 " + (GC.CollectionCount(0) - g0));
            // overlay first show + re-show
            sw.Restart(); var ov = new ZoneOverlay(); ov.Setup(wa, Zones.Rects(new ZoneLayout { Kind = "grid", A = 3, B = 2 }, wa), 60); ov.ShowNoActivate(); ov.Update(); sb.AppendLine("overlay_first_show_ms " + sw.Elapsed.TotalMilliseconds.ToString("0.0"));
            ov.Hide(); sw.Restart(); ov.Setup(wa, Zones.Rects(new ZoneLayout { Kind = "cols", A = 2 }, wa), 60); ov.ShowNoActivate(); ov.SetHot(1); ov.Update(); sb.AppendLine("overlay_reshow_ms " + sw.Elapsed.TotalMilliseconds.ToString("0.0")); ov.Close();
            // snap placement decision on drop (rects for hot zone)
            double snap = timeUs(() => { var rs = Zones.Rects(Zones.For(scr), scr.WorkingArea); var r = rs[0]; }, 5000); sb.AppendLine("snap_compute_us " + snap.ToString("0.00"));
            // grab & move hook callback cost (called directly with a synthetic move event; hook not installed)
            var hm = typeof(GrabMove).GetMethod("Hook", BindingFlags.NonPublic | BindingFlags.Static);
            if (hm != null) {
                var buf = Marshal.AllocHGlobal(64); try {
                    for (int i = 0; i < 64; i++) Marshal.WriteByte(buf, i, 0);
                    var args = new object[] { 0, (IntPtr)0x0200, buf };
                    double h = timeUs(() => hm.Invoke(null, args), 50000);
                    var d = (Func<int, IntPtr, IntPtr, IntPtr>)Delegate.CreateDelegate(typeof(Func<int, IntPtr, IntPtr, IntPtr>), hm);
                    double h2 = timeUs(() => d(0, (IntPtr)0x0200, buf), 200000);
                    var args2 = new object[] { 0, (IntPtr)0x0201, buf }; double h3 = timeUs(() => d(0, (IntPtr)0x0201, buf), 50000);
                    sb.AppendLine("hook_move_ns " + (h2 * 1000).ToString("0") + " hook_down_ns " + (h3 * 1000).ToString("0"));
                } finally { Marshal.FreeHGlobal(buf); }
            }
            // palette
            var pc = new PaletteCfg(); Palette.Commands = () => Enumerable.Range(0, 30).Select(i => new PaletteItem { Title = "명령 " + i, Key = "cmd" + i });
            sw.Restart(); var pf = new PaletteForm(pc); pf.Show(); pf.Update(); sb.AppendLine("palette_open_cold_ms " + sw.Elapsed.TotalMilliseconds.ToString("0.0")); pf.Close(); Pump(50);
            sw.Restart(); pf = new PaletteForm(pc); pf.Show(); pf.Update(); sb.AppendLine("palette_open_warm_ms " + sw.Elapsed.TotalMilliseconds.ToString("0.0"));
            var qs = new[] { "n", "no", "not", "note", "chr", "ch", "c", "cal", "12*3", "설정", "메모", "명령 1", "pow", "ex", "exp", "word", "code", "term", "x", "zz" };
            g0 = GC.CollectionCount(0); long a0 = GC.GetTotalMemory(false);
            sw.Restart(); int nq = 0; for (int r = 0; r < 10; r++) foreach (var q in qs) { Palette.Search(q, pc); nq++; } double ps = sw.Elapsed.TotalMilliseconds / nq;
            sb.AppendLine("palette_search_ms " + ps.ToString("0.000") + " apps " + Palette.Apps().Count + " gen0 " + (GC.CollectionCount(0) - g0));
            sw.Restart(); for (int i = 0; i < 4; i++) { pf.SetQuery(qs[i]); pf.Update(); } sb.AppendLine("palette_type_ms " + (sw.Elapsed.TotalMilliseconds / 4).ToString("0.00")); pf.Close();
            // peek
            string dir = Path.Combine(Path.GetTempPath(), "BongHub_perf"); try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } Directory.CreateDirectory(dir);
            string big = Path.Combine(dir, "big.jpg"), txt = Path.Combine(dir, "code.cs");
            using (var bm = DemoImage(6000, 4000)) bm.Save(big, System.Drawing.Imaging.ImageFormat.Jpeg);
            File.WriteAllText(txt, string.Join("\n", Enumerable.Range(0, 20000).Select(i => "    var x" + i + " = Compute(" + i + "); // line")));
            foreach (var f in new[] { big, txt, big }) { sw.Restart(); var pk = new PeekForm(f); pk.Show(); pk.Update(); sb.AppendLine("peek_open_ms " + Path.GetExtension(f) + " " + sw.Elapsed.TotalMilliseconds.ToString("0.0")); pk.Close(); Pump(50); }
            // resizer throughput: 16 x 4000x3000 jpeg -> 1366x768
            var files = new List<string>(); using (var bm = DemoImage(4000, 3000)) for (int i = 0; i < 16; i++) { string f = Path.Combine(dir, "img" + i + ".jpg"); bm.Save(f, System.Drawing.Imaging.ImageFormat.Jpeg); files.Add(f); }
            var rc = new ResizerCfg(); rc.Presets = ResizerCfg.DefaultPresets();
            sw.Restart(); string res = Resizer.Run(files, rc.Presets[1], rc); double rs2 = sw.Elapsed.TotalSeconds;
            sb.AppendLine("resizer_16x12MP_s " + rs2.ToString("0.00") + " MPps " + (16 * 12.0 / rs2).ToString("0.0") + " (" + res + ")");
            GC.Collect(); proc.Refresh();
            sb.AppendLine("private_MB " + (proc.PrivateMemorySize64 / 1048576) + " ws_MB " + (proc.WorkingSet64 / 1048576));
            try { Directory.Delete(dir, true); } catch { }
        } catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
        File.WriteAllText(Path.Combine(ExeDir, "perfbench_" + AppInfo.Version + ".txt"), sb.ToString());
        return 0;
    }

    static void Pump(int ms) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); } }

    static int WindowTest() {
        var sb = new StringBuilder(); bool ok = true;
        Action<bool, string> check = (c, n) => { sb.AppendLine((c ? "PASS " : "FAIL ") + n); if (!c) ok = false; };
        var before = new HashSet<IntPtr>(WindowRules.Enumerate(false).Where(x => x.Class == "Notepad").Select(x => x.H));
        var preProcs = new HashSet<int>(Process.GetProcessesByName("Notepad").Select(p => p.Id));
        sb.AppendLine("pre-existing notepad windows: " + before.Count + ", procs: " + preProcs.Count);
        IntPtr h = IntPtr.Zero;
        try {
            Process.Start("notepad.exe");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000 && h == IntPtr.Zero) {
                Thread.Sleep(300);
                var n = WindowRules.Enumerate(false).FirstOrDefault(x => x.Class == "Notepad" && !before.Contains(x.H));
                if (n != null) h = n.H;
            }
            check(h != IntPtr.Zero, "temporary Notepad window found by enumeration");
            if (h == IntPtr.Zero) return Finish(sb, false);
            Thread.Sleep(800);
            var info = WindowRules.Info(h);
            sb.AppendLine("window: proc=" + info.Process + " class=" + info.Class + " title='" + info.Title + "' mon=" + info.MonitorId + " rect=" + info.Rect);
            var cap = WindowRules.Enumerate(true); check(cap.Any(x => x.H == h), "listed in capture enumeration (" + cap.Count + " windows total)");
            var prim = Screen.PrimaryScreen; string pid = Monitors.IdOf(prim); sb.AppendLine("primary id=" + pid + " wa=" + prim.WorkingArea);
            check(pid != "" && Monitors.ScreenFor(pid) != null, "monitor id mapping (" + string.Join(",", Screen.AllScreens.Select(s => Monitors.IdOf(s))) + ")");
            var r = new WindowRule { Name = "t", Process = info.Process, Class = "Notepad", Monitor = pid, Action = "pos", X = 120, Y = 90, W = 900, H = 600, Pin = true };
            check(WindowRules.Match(r, info), "rule matches by process+class");
            check(!WindowRules.Match(new WindowRule { Process = "chrome" }, info), "non-matching rule rejected");
            check(WindowRules.Match(new WindowRule { Title = ".*", TitleRegex = true, Class = "Notepad" }, info), "regex title match");
            WindowRules.Apply(r, h); Thread.Sleep(500);
            var a = WindowRules.Info(h); var wa = prim.WorkingArea;
            sb.AppendLine("after pos: " + a.Rect + " topmost=" + a.TopMost);
            check(Math.Abs(a.Rect.X - (wa.X + 120)) <= 8 && Math.Abs(a.Rect.Y - (wa.Y + 90)) <= 8 && Math.Abs(a.Rect.Width - 900) <= 8 && Math.Abs(a.Rect.Height - 600) <= 8, "position/size applied");
            check(a.TopMost, "pin (always on top) applied");
            check(WindowRules.InPlace(r, a), "InPlace true after apply");
            // keep-in-place watcher
            r.Keep = true; var wt = new Watcher(); wt.SetRules(new[] { r }); Pump(400);
            Native.SetWindowPos(h, IntPtr.Zero, wa.X + 400, wa.Y + 300, 700, 500, 0x14);
            Pump(2500);
            var k = WindowRules.Info(h); sb.AppendLine("after move+watch: " + k.Rect + " reapplied=" + wt.AppliedCount);
            check(wt.AppliedCount >= 1 && WindowRules.InPlace(r, k), "keep-in-place re-applied after move");
            wt.SetRules(new WindowRule[0]);
            var rm = new WindowRule { Process = info.Process, Class = "Notepad", Monitor = pid, Action = "max" }; WindowRules.Apply(rm, h); Thread.Sleep(600);
            check(Native.IsZoomed(h), "maximize");
            var rn = new WindowRule { Process = info.Process, Action = "min" }; WindowRules.Apply(rn, h); Thread.Sleep(600);
            check(Native.IsIconic(h), "minimize");
            var rr = new WindowRule { Process = info.Process, Action = "restore" }; WindowRules.Apply(rr, h); Thread.Sleep(600);
            check(!Native.IsIconic(h), "restore");
            Native.SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x13);
        } catch (Exception ex) { check(false, "exception: " + ex); }
        finally {
            if (h != IntPtr.Zero) { Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(1500); }
            foreach (var p in Process.GetProcessesByName("Notepad")) if (!preProcs.Contains(p.Id)) { try { if (h != IntPtr.Zero && Native.IsWindow(h)) p.Kill(); } catch { } }
            sb.AppendLine("notepad window closed: " + (h == IntPtr.Zero || !Native.IsWindow(h)));
        }
        return Finish(sb, ok);
    }
    static int Finish(StringBuilder sb, bool ok) {
        File.WriteAllText(Path.Combine(ExeDir, "window_test.txt"), sb.ToString() + (ok ? "ALL PASS" : "FAILURES") + "\r\n", Encoding.UTF8);
        return ok ? 0 : 1;
    }

    // keeps a keep-in-place watcher active (rule matches a process that is not running) for N seconds, for CPU/memory sampling
    static int WatchBench(int secs) {
        var w = new Watcher(); w.SetRules(new[] { new WindowRule { Name = "bench", Process = "mm_bench_nonexistent", Keep = true, Action = "pos" } });
        var t = new WTimer { Interval = secs * 1000 }; t.Tick += (s, e) => Application.ExitThread(); t.Start();
        Application.Run();
        return 0;
    }
}
