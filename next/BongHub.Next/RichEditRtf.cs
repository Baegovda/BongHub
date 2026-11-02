using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace BongHub.Next;

/// <summary>
/// Loads and saves RichEdit RTF without a managed <c>ref</c> delegate.
/// On x64 the native <c>EDITSTREAM</c> is 24 bytes: cookie at 0, <c>DWORD</c> error at 8, callback at 16.
/// A packed 20-byte struct makes msftedit call the wrong address and CFG fast-fails
/// (<c>FAST_FAIL_GUARD_ICALL_CHECK_FAILURE</c>, code 10, exit <c>0xC0000409</c>).
/// The callback is <see cref="UnmanagedCallersOnlyAttribute"/> plus <c>delegate* unmanaged[Stdcall]</c>.
/// </summary>
static class RichEditRtf
{
    public const int EmStreamIn = 0x0400 + 73;
    public const int EmStreamOut = 0x0400 + 74;
    const int EmSetSel = 0x00B1;
    const int EmSetTextEx = 0x0400 + 97;
    const int EmGetTextEx = 0x0400 + 94;
    const int EmGetTextLengthEx = 0x0400 + 95;
    const int SfRtf = 2;
    const uint WsPopup = 0x80000000, WsChild = 0x40000000, EsMultiline = 0x0004, EsAutoVScroll = 0x0040;
    const uint ExNoActivate = 0x08000000, ExToolWindow = 0x00000080;
    static readonly IntPtr CallbackPtr = MakeCallback();

    public static int EditStreamBytes => Marshal.SizeOf<EditStream64>();
    public static int CallbackOffset => Marshal.OffsetOf<EditStream64>(nameof(EditStream64.pfnCallback)).ToInt32();

    public static bool LayoutOk => IntPtr.Size == 8 && EditStreamBytes == 24 && CallbackOffset == 16;

    /// <summary>Hidden popup, not shown, no message loop. For the headless CI repro and for experiments.</summary>
    public static IntPtr CreateHidden(out string error)
    {
        error = "";
        if (LoadLibrary("msftedit.dll") == IntPtr.Zero) { error = "msftedit.dll " + Marshal.GetLastWin32Error(); return IntPtr.Zero; }
        uint style = WsPopup | EsMultiline | EsAutoVScroll;
        var hwnd = CreateWindowExW(ExNoActivate | ExToolWindow, "RICHEDIT50W", "", style, -32000, -32000, 8, 8, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd != IntPtr.Zero) return hwnd;
        int popupErr = Marshal.GetLastWin32Error();
        hwnd = CreateWindowExW(0, "RICHEDIT50W", "", WsChild | EsMultiline | EsAutoVScroll, 0, 0, 8, 8, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) error = "CreateWindowEx popup=" + popupErr + " message=" + Marshal.GetLastWin32Error();
        return hwnd;
    }

    public static void Destroy(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
    }

    public static bool IsVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

    /// <summary>Prefer EM_SETTEXTEX (no callback). If RichEdit stores the RTF source as plain text, fall back to EM_STREAMIN.</summary>
    public static bool TryLoad(IntPtr hwnd, byte[] rtf, Action<string>? log, out string how)
    {
        how = "";
        if (!LayoutOk) { how = "need x64 EDITSTREAM 24/16"; log?.Invoke(how); return false; }
        log?.Invoke($"richedit layout bytes={EditStreamBytes} callbackOff={CallbackOffset}");
        if (TrySetTextEx(hwnd, rtf, 0, log) && Accepted(hwnd, rtf, log)) { how = "settextex-acp"; log?.Invoke("richedit load " + how); return true; }
        if (TrySetTextEx(hwnd, rtf, 65001, log) && Accepted(hwnd, rtf, log)) { how = "settextex-utf8"; log?.Invoke("richedit load " + how); return true; }
        if (!Stream(hwnd, inbound: true, rtf, out _, out var detail, log)) { how = "streamin failed " + detail; return false; }
        if (!Accepted(hwnd, rtf, log)) { how = "streamin left raw rtf"; log?.Invoke("richedit " + how); return false; }
        how = "streamin";
        log?.Invoke("richedit load streamin");
        return true;
    }

    public static bool TrySave(IntPtr hwnd, out byte[] rtf, Action<string>? log)
        => Stream(hwnd, inbound: false, null, out rtf, out _, log);

    public static string Plain(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        try
        {
            int n = TextChars(hwnd);
            if (n <= 0) return "";
            if (n > 8 * 1024 * 1024) n = 8 * 1024 * 1024;
            int bytes = (n + 1) * 2;
            IntPtr mem = Marshal.AllocHGlobal(bytes);
            IntPtr spec = Marshal.AllocHGlobal(Marshal.SizeOf<GetTextEx>());
            try
            {
                var gt = new GetTextEx { cb = bytes, flags = 1, codepage = 1200 };
                Marshal.StructureToPtr(gt, spec, false);
                SendMessage(hwnd, EmGetTextEx, spec, mem);
                return (Marshal.PtrToStringUni(mem) ?? "").TrimEnd('\0', '\r', '\n');
            }
            finally { Marshal.FreeHGlobal(mem); Marshal.FreeHGlobal(spec); }
        }
        catch { return ""; }
    }

    public static bool Stream(IntPtr hwnd, bool inbound, byte[]? input, out byte[] output, out string detail, Action<string>? log)
    {
        output = [];
        detail = "";
        if (!LayoutOk) { detail = "layout"; return false; }
        if (hwnd == IntPtr.Zero) { detail = "hwnd"; return false; }
        var state = new StreamBox { Input = input ?? [], Out = inbound ? null : new MemoryStream(), Log = log };
        var pin = GCHandle.Alloc(state);
        IntPtr mem = Marshal.AllocHGlobal(EditStreamBytes);
        string which = inbound ? "in" : "out";
        try
        {
            Marshal.WriteInt64(mem, 0, 0);
            Marshal.WriteInt64(mem, 8, 0);
            Marshal.WriteInt64(mem, 16, 0);
            if (inbound) SendMessage(hwnd, EmSetSel, IntPtr.Zero, new IntPtr(-1));
            var es = new EditStream64 { dwCookie = GCHandle.ToIntPtr(pin), dwError = 0, pfnCallback = CallbackPtr };
            Marshal.StructureToPtr(es, mem, false);
            var placed = Marshal.ReadIntPtr(mem, CallbackOffset);
            if (placed != es.pfnCallback) { detail = "callback slot"; log?.Invoke("richedit callback not at offset " + CallbackOffset); return false; }
            log?.Invoke($"richedit stream {which} begin bytes={state.Input.Length} ptr=0x{es.pfnCallback.ToInt64():X} size={EditStreamBytes}");
            SendMessage(hwnd, inbound ? EmStreamIn : EmStreamOut, new IntPtr(SfRtf), mem);
            uint err = (uint)Marshal.ReadInt32(mem, 8);
            log?.Invoke($"richedit stream {which} end err={err}");
            detail = "err=" + err;
            if (err != 0) return false;
            if (state.Out != null) output = state.Out.ToArray();
            return true;
        }
        catch (Exception e)
        {
            detail = e.GetType().Name;
            log?.Invoke("richedit stream " + which + ": " + e.Message);
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
            if (pin.IsAllocated) pin.Free();
        }
    }

    static bool TrySetTextEx(IntPtr hwnd, byte[] rtf, uint codePage, Action<string>? log)
    {
        var payload = new byte[rtf.Length + 1];
        Buffer.BlockCopy(rtf, 0, payload, 0, rtf.Length);
        var pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        IntPtr spec = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(new SetTextEx { flags = 0, codepage = codePage }, spec, false);
            var n = SendMessage(hwnd, EmSetTextEx, spec, pin.AddrOfPinnedObject());
            log?.Invoke($"richedit settextex cp={codePage} result={n.ToInt64()}");
            return n != IntPtr.Zero;
        }
        catch (Exception e)
        {
            log?.Invoke("richedit settextex: " + e.Message);
            return false;
        }
        finally
        {
            if (pin.IsAllocated) pin.Free();
            Marshal.FreeHGlobal(spec);
        }
    }

    public static int CharCount(IntPtr hwnd) => TextChars(hwnd);

    static bool Accepted(IntPtr hwnd, byte[] rtf, Action<string>? log)
    {
        if (Parsed(hwnd, rtf)) return true;
        log?.Invoke("richedit text still looks like rtf source");
        return false;
    }

    static bool Parsed(IntPtr hwnd, byte[] source)
    {
        var plain = Plain(hwnd);
        if (plain.Contains("\\rtf", StringComparison.Ordinal)) return false;
        if (plain.Contains("\\pict", StringComparison.Ordinal)) return false;
        if (plain.Contains("\\pngblip", StringComparison.Ordinal)) return false;
        if (plain.Contains("\\jpegblip", StringComparison.Ordinal)) return false;
        if (Encoding.ASCII.GetString(source).Contains("\\u", StringComparison.Ordinal) && plain.Length == 0) return false;
        return true;
    }

    /// <summary>Saved RTF still carries the source blip kind and, for a PNG, the file signature.</summary>
    public static bool KeepsBlips(byte[] source, byte[] saved)
    {
        if (saved.Length == 0) return false;
        var src = Encoding.Latin1.GetString(source).ToLowerInvariant();
        var dst = Encoding.Latin1.GetString(saved).ToLowerInvariant();
        if (src.Contains("\\pngblip", StringComparison.Ordinal) && !dst.Contains("\\pngblip", StringComparison.Ordinal)) return false;
        if (src.Contains("\\jpegblip", StringComparison.Ordinal) && !dst.Contains("\\jpegblip", StringComparison.Ordinal)) return false;
        const string sig = "89504e470d0a1a0a";
        if (CompactHex(src).Contains(sig, StringComparison.Ordinal) && !CompactHex(dst).Contains(sig, StringComparison.Ordinal) && !ContainsRawPng(saved)) return false;
        return dst.Contains("{\\rtf", StringComparison.Ordinal);
    }

    static string CompactHex(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (c is >= '0' and <= '9' or >= 'a' and <= 'f') sb.Append(c);
        return sb.ToString();
    }

    static readonly byte[] PngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static bool ContainsRawPng(byte[] data) => data.AsSpan().IndexOf(PngSig) >= 0;

    static int TextChars(IntPtr hwnd)
    {
        IntPtr spec = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(new GetTextLengthEx { flags = 1 | 2 | 8, codepage = 1200 }, spec, false);
            int n = (int)SendMessage(hwnd, EmGetTextLengthEx, spec, IntPtr.Zero);
            return n < 0 ? 0 : n;
        }
        finally { Marshal.FreeHGlobal(spec); }
    }

    static IntPtr MakeCallback()
    {
        unsafe
        {
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, uint> fp = &StreamCallback;
            return (IntPtr)fp;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint StreamCallback(IntPtr cookie, IntPtr buf, int cb, IntPtr pcb)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not StreamBox st) { WriteCount(pcb, 0); return 1; }
            if (!st.Saw)
            {
                st.Saw = true;
                try { st.Log?.Invoke("richedit stream callback"); } catch { }
            }
            if (st.Out == null)
            {
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
            if (cb > 0 && buf != IntPtr.Zero)
            {
                var tmp = new byte[cb];
                Marshal.Copy(buf, tmp, 0, cb);
                st.Out.Write(tmp, 0, cb);
            }
            WriteCount(pcb, cb < 0 ? 0 : cb);
            return 0;
        }
        catch
        {
            WriteCount(pcb, 0);
            return 1;
        }
    }

    static void WriteCount(IntPtr pcb, int n)
    {
        if (pcb != IntPtr.Zero) Marshal.WriteInt32(pcb, n);
    }

    sealed class StreamBox { public byte[] Input = []; public int Pos; public MemoryStream? Out; public Action<string>? Log; public bool Saw; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct EditStream64
    {
        [FieldOffset(0)] public IntPtr dwCookie;
        [FieldOffset(8)] public uint dwError;
        [FieldOffset(16)] public IntPtr pfnCallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SetTextEx { public uint flags; public uint codepage; }

    [StructLayout(LayoutKind.Sequential)]
    struct GetTextLengthEx { public uint flags; public uint codepage; }

    [StructLayout(LayoutKind.Sequential)]
    struct GetTextEx
    {
        public int cb;
        public uint flags;
        public uint codepage;
        public IntPtr lpDefaultChar;
        public IntPtr lpUsedDefChar;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string file);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
