using System.Windows.Interop;
using System.Windows.Threading;

namespace BongHub;

/// <summary>
/// One <c>RegisterHotKey</c> chord (id 940). Not a hook.
/// The key-up is read with <c>GetAsyncKeyState</c> so releasing over an item can launch it.
/// Smoke, note tests, and <c>--off</c> never call the API.
/// </summary>
public static class AirDockHotkey
{
    public const int Id = 940;
    const uint ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;
    public static int NativeRegistrations { get; private set; }
    public static int HookInstalls => 0;
    public static bool NativeAllowed => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;

    static HwndSource? source;
    static bool armed;
    static DispatcherTimer? poll;

    public static void Bind()
    {
        if (!NativeAllowed) return;
        Ensure();
        Unregister();
        if (source == null || !ConfigStore.AirEnabled) return;
        if (!HotkeyChord.TryParse(ConfigStore.AirHotkey, out uint mods, out uint vk)) return;
        bool ok = false;
        try { ok = RegisterHotKey(source.Handle, Id, mods | ModNoRepeat, vk); }
        catch (Exception ex) { AppEnv.Log("airdock hotkey: " + ex.Message); }
        NativeRegistrations++;
        armed = ok;
        if (!ok) AppEnv.Log("airdock hotkey: register failed");
    }

    public static void Stop()
    {
        poll?.Stop();
        poll = null;
        if (source != null && NativeAllowed) Unregister();
        try { source?.Dispose(); } catch { }
        source = null;
    }

    public static void Dispatch()
    {
        if (!armed || !ConfigStore.AirEnabled || ConfigStore.AirItems.Count == 0) return;
        AirDockWindow.OpenAtCursor();
        poll?.Stop();
        poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        poll.Tick += (_, _) =>
        {
            if (ChordDown()) return;
            poll?.Stop();
            var path = AirDockWindow.ReleasePath();
            AirDockWindow.CloseCurrent();
            if (path != null) AirDockLaunch.Run(path);
        };
        poll.Start();
    }

    static bool ChordDown()
    {
        if (!HotkeyChord.TryParse(ConfigStore.AirHotkey, out uint mods, out uint vk)) return false;
        if (!Down(vk)) return false;
        if ((mods & HotkeyChord.Ctrl) != 0 && !Down(0x11)) return false;
        if ((mods & HotkeyChord.Alt) != 0 && !Down(0x12) && !Down(0xA4) && !Down(0xA5)) return false;
        if ((mods & HotkeyChord.Shift) != 0 && !Down(0x10)) return false;
        if ((mods & HotkeyChord.Win) != 0 && !Down(0x5B) && !Down(0x5C)) return false;
        return true;
    }

    static bool Down(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    static void Ensure()
    {
        if (source != null || !NativeAllowed) return;
        var p = new HwndSourceParameters("BongHubAirDock")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            PositionX = 0, PositionY = 0, Width = 0, Height = 0,
            ParentWindow = new IntPtr(-3)
        };
        source = new HwndSource(p);
        source.AddHook((hwnd, msg, wParam, lParam, ref handled) =>
        {
            if (msg == WmHotkey) { handled = true; Dispatch(); }
            return IntPtr.Zero;
        });
    }

    static void Unregister()
    {
        armed = false;
        if (source == null) return;
        try { UnregisterHotKey(source.Handle, Id); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);
}
