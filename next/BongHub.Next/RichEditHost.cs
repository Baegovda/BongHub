using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace BongHub.Next;

/// <summary>
/// RichEdit 5 (<c>RICHEDIT50W</c> / msftedit) hosted in WPF. Created only when the WPF editor would drop a <c>\pict</c>.
/// WinForms is not referenced. Load and save go through <see cref="RichEditRtf"/> so the EDITSTREAM callback is an
/// <c>UnmanagedCallersOnly</c> function, not a <c>GetFunctionPointerForDelegate</c> thunk.
/// </summary>
public sealed class RichEditHost : HwndHost
{
    const int WsChild = 0x40000000, WsVisible = 0x10000000, WsVScroll = 0x00200000;
    const int EsMultiline = 0x0004, EsAutoVScroll = 0x0040, EsWantReturn = 0x1000;
    const int EmSetSel = 0x00B1, EmReplaceSel = 0x00C2, EmSetReadOnly = 0x00CF;
    const int EmSetBkgndColor = 0x0400 + 67;

    byte[] initial = [];
    IntPtr hwnd;
    public bool LoadedRtf { get; private set; }

    public RichEditHost(byte[] rtf) { initial = rtf ?? []; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (LoadLibrary("msftedit.dll") == IntPtr.Zero)
            throw new InvalidOperationException("msftedit.dll");
        int style = WsChild | WsVisible | WsVScroll | EsMultiline | EsAutoVScroll | EsWantReturn;
        hwnd = CreateWindowExW(0, "RICHEDIT50W", "", style, 0, 0, 80, 80, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("RICHEDIT50W");
        AppEnv.Log("richedit hwnd created");
        if (initial.Length > 0) LoadedRtf = RichEditRtf.TryLoad(hwnd, initial, AppEnv.Log, out _);
        initial = [];
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != IntPtr.Zero) DestroyWindow(hwnd.Handle);
        this.hwnd = IntPtr.Zero;
    }

    public void SetPaper(Color c)
    {
        if (hwnd == IntPtr.Zero) return;
        int cref = c.R | (c.G << 8) | (c.B << 16);
        SendMessage(hwnd, EmSetBkgndColor, IntPtr.Zero, new IntPtr(cref));
    }

    public void SetReadOnly(bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        SendMessage(hwnd, EmSetReadOnly, on ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
    }

    public void InsertText(string text)
    {
        if (hwnd == IntPtr.Zero || text.Length == 0) return;
        int n = RichEditRtf.CharCount(hwnd);
        SendMessage(hwnd, EmSetSel, new IntPtr(n), new IntPtr(n));
        SendMessageW(hwnd, EmReplaceSel, new IntPtr(1), text);
    }

    public byte[]? StreamOut()
    {
        if (hwnd == IntPtr.Zero) return null;
        return RichEditRtf.TrySave(hwnd, out var bytes, AppEnv.Log) ? bytes : null;
    }

    public string Plain() => hwnd == IntPtr.Zero ? "" : RichEditRtf.Plain(hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}
