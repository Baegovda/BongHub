using System.Windows.Interop;

namespace BongHub;

/// <summary>
/// Profile chords via <c>RegisterHotKey</c> (ids 910+). Not a hook.
/// <c>--off</c>, smoke, and note tests never call <c>RegisterHotKey</c>.
/// </summary>
public static class ProfileHotkey
{
    public const int FirstId = 910;
    public const int Capacity = 24;
    const uint ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;

    public static int NativeRegistrations { get; private set; }
    public static int NativeUnregistrations { get; private set; }
    public static int HookInstalls => 0;
    public static string LastError { get; private set; } = "";
    public static bool NativeAllowed => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;
    public static Action<string>? Fired;

    static HwndSource? source;
    static readonly Dictionary<int, string> Ids = new();

    public static string RegisterFailure(string name) => (name ?? "프로필") + " 단축키를 등록하지 못했어요. 다른 프로그램이 쓰고 있을 수 있어요.";

    public static void Bind(IReadOnlyList<MonitorProfile>? profiles, string? noteHotkey)
    {
        LastError = "";
        if (!NativeAllowed) return;
        Ensure();
        UnregisterAll();
        if (source == null || profiles == null) return;
        var taken = new List<string>();
        int slot = 0;
        foreach (var profile in profiles)
        {
            if (slot >= Capacity) break;
            if (string.IsNullOrWhiteSpace(profile.Hotkey)) continue;
            var (value, error) = HotkeyEdit.Apply("", profile.Hotkey, taken, noteHotkey);
            if (error.Length > 0 || value.Length == 0)
            {
                if (error.Length > 0 && LastError.Length == 0) LastError = (profile.Name.Length > 0 ? profile.Name + ": " : "") + error;
                continue;
            }
            if (!HotkeyChord.TryParse(value, out uint mods, out uint vk)) continue;
            int id = FirstId + slot;
            bool ok = false;
            try { ok = RegisterHotKey(source.Handle, id, mods | ModNoRepeat, vk); }
            catch (Exception e) { AppEnv.Log("profile hotkey: " + e.Message); }
            NativeRegistrations++;
            if (!ok)
            {
                LastError = RegisterFailure(profile.Name);
                continue;
            }
            Ids[id] = profile.Id ?? "";
            taken.Add(value);
            slot++;
        }
    }

    public static void Stop()
    {
        if (source != null && NativeAllowed) UnregisterAll();
        try { source?.Dispose(); } catch { }
        source = null;
    }

    /// <summary>The WM_HOTKEY body. Tests can call this only after a bind that did not touch the API.</summary>
    public static void Dispatch(int id)
    {
        if (!Ids.TryGetValue(id, out var profile) || profile.Length == 0) return;
        try { Fired?.Invoke(profile); } catch (Exception e) { AppEnv.Log("profile hotkey: " + e.Message); }
    }

    static void Ensure()
    {
        if (source != null || !NativeAllowed) return;
        var p = new HwndSourceParameters("BongHubProfileHotkey")
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
        if (msg == WmHotkey)
        {
            handled = true;
            Dispatch(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    static void UnregisterAll()
    {
        if (source == null) { Ids.Clear(); return; }
        foreach (var id in Ids.Keys.ToList())
        {
            try { if (UnregisterHotKey(source.Handle, id)) NativeUnregistrations++; } catch { }
        }
        Ids.Clear();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
