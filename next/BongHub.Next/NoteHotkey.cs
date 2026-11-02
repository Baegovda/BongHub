using System.Windows.Interop;

namespace BongHub.Next;

/// <summary>
/// Global new-note hotkey (3.4.9 id 900, <c>RegisterHotKey</c> + <c>MOD_NOREPEAT</c>).
/// This is not a Windows hook. <c>--off</c>, smoke, and note tests never call <c>RegisterHotKey</c>;
/// they deliver <c>WM_HOTKEY</c> through <see cref="DispatchNewNote"/>.
/// </summary>
public static class NoteHotkey
{
    public const int Id = 900;
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
    const uint ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;

    public static int NativeRegistrations { get; private set; }
    public static int NativeUnregistrations { get; private set; }
    /// <summary>Phase 1 does not call SetWindowsHookEx. Stays 0.</summary>
    public static int HookInstalls => 0;
    public static bool Registered { get; private set; }
    public static string Desired { get; private set; } = "";
    public static bool NativeAllowed => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;

    static HwndSource? source;
    static TrayService? tray;

    public static void Start(TrayService? owner)
    {
        tray = owner;
        if (!NativeAllowed) return;
        EnsureWindow();
        Apply(ConfigStore.Notes.Hotkey, owner);
    }

    public static void Stop()
    {
        if (source != null && NativeAllowed) UnregisterNative();
        Registered = false;
        try { source?.Dispose(); } catch { }
        source = null;
        tray = null;
    }

    /// <summary>Save path: re-register immediately. Tests update <see cref="Desired"/> and do not touch the real key.</summary>
    public static void Apply(string? hotkey, TrayService? owner = null)
    {
        if (owner != null) tray = owner;
        Desired = hotkey ?? "";
        if (!NativeAllowed) { Registered = false; return; }
        EnsureWindow();
        UnregisterNative();
        if (source == null || !TryParse(Desired, out uint mods, out uint vk)) { Registered = false; return; }
        bool ok = false;
        try { ok = RegisterHotKey(source.Handle, Id, mods | ModNoRepeat, vk); }
        catch (Exception e) { AppEnv.Log("hotkey: " + e.Message); }
        NativeRegistrations++;
        Registered = ok;
        if (!ok) ReportFailure(tray, Normalize(Desired));
    }

    public static void ReportFailure(TrayService? owner, string hotkey)
    {
        owner?.ShowBalloon("단축키 등록 실패: " + hotkey + " (새 스티커 메모) - 다른 프로그램이 사용 중일 수 있어요. 트레이 메뉴는 사용 가능해요.");
    }

    /// <summary>The WM_HOTKEY body. Tests call this instead of installing a hotkey.</summary>
    public static void DispatchNewNote() => NoteManager.NewNote();

    public static bool TryParse(string? s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (var raw in s.Split('+'))
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            string u = t.ToLowerInvariant();
            if (u is "ctrl" or "control") mods |= Ctrl;
            else if (u == "alt") mods |= Alt;
            else if (u == "shift") mods |= Shift;
            else if (u is "win" or "windows") mods |= Win;
            else if (!TryVk(t, out vk)) return false;
        }
        return vk != 0;
    }

    public static string Normalize(string? s)
    {
        if (!TryParse(s, out uint mods, out uint vk)) return "";
        var parts = new List<string>();
        if ((mods & Win) != 0) parts.Add("Win");
        if ((mods & Ctrl) != 0) parts.Add("Ctrl");
        if ((mods & Alt) != 0) parts.Add("Alt");
        if ((mods & Shift) != 0) parts.Add("Shift");
        parts.Add(VkName(vk));
        return string.Join("+", parts);
    }

    static void EnsureWindow()
    {
        if (source != null || !NativeAllowed) return;
        var p = new HwndSourceParameters("BongHubNextHotkey")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
        };
        source = new HwndSource(p);
        source.AddHook(Hook);
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            try { DispatchNewNote(); } catch (Exception e) { AppEnv.Log("hotkey note: " + e.Message); }
        }
        return IntPtr.Zero;
    }

    static void UnregisterNative()
    {
        if (source == null) return;
        try
        {
            if (UnregisterHotKey(source.Handle, Id)) NativeUnregistrations++;
        }
        catch { }
        Registered = false;
    }

    static bool TryVk(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = token[0];
            if (c is >= '0' and <= '9') { vk = (uint)('0' + (c - '0')); return true; }
            if (c is >= 'a' and <= 'z') { vk = (uint)('A' + (c - 'a')); return true; }
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
        }
        string u = token.ToLowerInvariant();
        if (u.Length == 4 && u.StartsWith("num", StringComparison.Ordinal) && u[3] is >= '0' and <= '9')
        { vk = (uint)(0x60 + (u[3] - '0')); return true; }
        if (u.Length is >= 2 and <= 3 && u[0] == 'f' && int.TryParse(u[1..], out int fn) && fn is >= 1 and <= 24)
        { vk = (uint)(0x70 + fn - 1); return true; }
        if (Names.TryGetValue(u, out vk)) return true;
        return false;
    }

    static string VkName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        foreach (var kv in Names) if (kv.Value == vk) return Pretty(kv.Key);
        return "Key" + vk.ToString("X");
    }

    static string Pretty(string lower)
    {
        if (lower.StartsWith("oem", StringComparison.Ordinal)) return "Oem" + lower[3..];
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20, ["tab"] = 0x09, ["escape"] = 0x1B, ["esc"] = 0x1B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["back"] = 0x08,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["prior"] = 0x21,
        ["pagedown"] = 0x22, ["next"] = 0x22, ["insert"] = 0x2D, ["delete"] = 0x2E,
        ["snapshot"] = 0x2C, ["pause"] = 0x13,
        ["oemplus"] = 0xBB, ["oemminus"] = 0xBD, ["oemcomma"] = 0xBC, ["oemperiod"] = 0xBE,
        ["oem1"] = 0xBA, ["oem2"] = 0xBF, ["oem3"] = 0xC0, ["oem4"] = 0xDB,
        ["oem5"] = 0xDC, ["oem6"] = 0xDD, ["oem7"] = 0xDE,
    };

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
