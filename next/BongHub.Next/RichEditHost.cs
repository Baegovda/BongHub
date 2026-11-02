using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace BongHub.Next;

/// <summary>
/// RichEdit 5 (<c>RICHEDIT50W</c> / msftedit) hosted in WPF. Created only when the WPF editor would drop a <c>\pict</c>.
/// WinForms is not referenced. Picture bytes stay inside the control's RTF.
/// </summary>
public sealed class RichEditHost : HwndHost
{
    const int WsChild = 0x40000000, WsVisible = 0x10000000, WsVScroll = 0x00200000;
    const int EsMultiline = 0x0004, EsAutoVScroll = 0x0040, EsWantReturn = 0x1000;
    const int EmSetSel = 0x00B1, EmReplaceSel = 0x00C2, EmSetReadOnly = 0x00CF;
    const int EmStreamIn = 0x0400 + 73, EmStreamOut = 0x0400 + 74, EmSetBkgndColor = 0x0400 + 67;
    const int EmGetTextEx = 0x0400 + 94, EmGetTextLengthEx = 0x0400 + 95;
    const int SfRtf = 2;

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
        if (initial.Length > 0) LoadedRtf = Stream(hwnd, EmStreamIn, initial, out _);
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
        int n = CharCount();
        SendMessage(hwnd, EmSetSel, new IntPtr(n), new IntPtr(n));
        SendMessageW(hwnd, EmReplaceSel, new IntPtr(1), text);
    }

    public byte[]? StreamOut()
    {
        if (hwnd == IntPtr.Zero) return null;
        return Stream(hwnd, EmStreamOut, null, out var bytes) ? bytes : null;
    }

    public string Plain()
    {
        if (hwnd == IntPtr.Zero) return "";
        try
        {
            int n = CharCount();
            if (n <= 0) return "";
            if (n > 8 * 1024 * 1024) n = 8 * 1024 * 1024;
            var sb = new System.Text.StringBuilder(n + 1);
            var gt = new GETTEXTEX { cb = (n + 1) * 2, flags = 1, codepage = 1200 };
            SendGetText(hwnd, EmGetTextEx, ref gt, sb);
            return sb.ToString().TrimEnd('\0', '\r', '\n');
        }
        catch { return ""; }
    }

    int CharCount()
    {
        var gl = new GETTEXTLENGTHEX { flags = 1 | 2 | 8, codepage = 1200 };
        int n = (int)SendLen(hwnd, EmGetTextLengthEx, ref gl, IntPtr.Zero);
        return n < 0 ? 0 : n;
    }

    // Static, blittable, stdcall. A `ref` parameter makes the CLR emit a marshal stub that CFG
    // (FAST_FAIL_GUARD_ICALL_CHECK_FAILURE, fast-fail code 10, exit 0xC0000409) rejects when
    // msftedit indirect-calls it. GetFunctionPointerForDelegate on a blittable delegate is a valid target.
    static readonly EditStreamProc ReadKeep = Read;
    static readonly EditStreamProc WriteKeep = Write;
    static readonly IntPtr ReadPtr = Marshal.GetFunctionPointerForDelegate(ReadKeep);
    static readonly IntPtr WritePtr = Marshal.GetFunctionPointerForDelegate(WriteKeep);

    static bool Stream(IntPtr hwnd, int msg, byte[]? input, out byte[] output)
    {
        output = [];
        bool inbound = msg == EmStreamIn;
        var state = new StreamBox { Input = input ?? [], Out = inbound ? null : new MemoryStream() };
        var pin = GCHandle.Alloc(state);
        string which = inbound ? "in" : "out";
        AppEnv.Log($"richedit stream {which} begin bytes={state.Input.Length}");
        try
        {
            var es = new EDITSTREAM
            {
                dwCookie = GCHandle.ToIntPtr(pin),
                pfnCallback = inbound ? ReadPtr : WritePtr,
            };
            SendStream(hwnd, msg, new IntPtr(SfRtf), ref es);
            AppEnv.Log($"richedit stream {which} end err={es.dwError}");
            if (es.dwError != 0) return false;
            if (state.Out != null) output = state.Out.ToArray();
            return true;
        }
        catch (Exception e)
        {
            AppEnv.Log("richedit stream " + which + ": " + e);
            return false;
        }
        finally
        {
            if (pin.IsAllocated) pin.Free();
            GC.KeepAlive(ReadKeep);
            GC.KeepAlive(WriteKeep);
        }
    }

    static uint Read(IntPtr cookie, IntPtr buf, int cb, IntPtr pcb)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not StreamBox st) { WriteCount(pcb, 0); return 1; }
            int n = 0;
            if (cb > 0 && buf != IntPtr.Zero && st.Pos < st.Input.Length)
            {
                n = Math.Min(cb, st.Input.Length - st.Pos);
                Marshal.Copy(st.Input, st.Pos, buf, n);
                st.Pos += n;
            }
            WriteCount(pcb, n);
            return 0;
        }
        catch { WriteCount(pcb, 0); return 1; }
    }

    static uint Write(IntPtr cookie, IntPtr buf, int cb, IntPtr pcb)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not StreamBox st || st.Out == null) { WriteCount(pcb, 0); return 1; }
            if (cb > 0 && buf != IntPtr.Zero)
            {
                var tmp = new byte[cb];
                Marshal.Copy(buf, tmp, 0, cb);
                st.Out.Write(tmp, 0, cb);
            }
            WriteCount(pcb, Math.Max(0, cb));
            return 0;
        }
        catch { WriteCount(pcb, 0); return 1; }
    }

    static void WriteCount(IntPtr pcb, int n)
    {
        if (pcb != IntPtr.Zero) Marshal.WriteInt32(pcb, n);
    }

    sealed class StreamBox { public byte[] Input = []; public int Pos; public MemoryStream? Out; }

    // Natural alignment matches the native EDITSTREAM: pointer, DWORD, pad, pointer. 24 bytes on x64, 12 on x86.
    [StructLayout(LayoutKind.Sequential)]
    struct EDITSTREAM
    {
        public IntPtr dwCookie;
        public uint dwError;
        public IntPtr pfnCallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GETTEXTLENGTHEX { public uint flags, codepage; }

    [StructLayout(LayoutKind.Sequential)]
    struct GETTEXTEX
    {
        public int cb;
        public uint flags;
        public uint codepage;
        public IntPtr lpDefaultChar;
        public IntPtr lpUsedDefChar;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint EditStreamProc(IntPtr cookie, IntPtr buf, int cb, IntPtr pcb);

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
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendStream(IntPtr hWnd, int msg, IntPtr wParam, ref EDITSTREAM lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendLen(IntPtr hWnd, int msg, ref GETTEXTLENGTHEX wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    static extern IntPtr SendGetText(IntPtr hWnd, int msg, ref GETTEXTEX wParam, System.Text.StringBuilder lParam);
}
