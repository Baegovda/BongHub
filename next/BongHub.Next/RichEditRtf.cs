using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace BongHub.Next;

/// <summary>
/// Loads and saves RichEdit RTF. On x64 the native <c>EDITSTREAM</c> is 24 bytes: cookie at 0, <c>DWORD</c> error at 8, callback at 16.
/// msftedit's indirect call is CFG-checked. A managed entry (<c>GetFunctionPointerForDelegate</c> or <c>UnmanagedCallersOnly</c>)
/// is not a valid target and fast-fails (<c>FAST_FAIL_GUARD_ICALL_CHECK_FAILURE</c>, code 10, exit <c>0xC0000409</c>).
/// The callback is a <c>VirtualAlloc</c> stub registered with <c>SetProcessValidCallTargets</c>. It only touches a native cookie.
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
    const int OutFirst = 64 * 1024, OutMax = 256 * 1024 * 1024;
    static readonly object StubGate = new();
    static IntPtr Stub;

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
        if (!EnsureStub(log)) { detail = "cfg stub"; return false; }
        byte[] src = input ?? [];
        int cap = inbound ? src.Length : OutFirst;
        for (int attempt = 0; ; attempt++)
        {
            bool ok = StreamOnce(hwnd, inbound, src, cap, log, out output, out detail, out bool grow);
            if (ok || !grow || cap >= OutMax || attempt >= 24) return ok;
            int next = cap > OutMax / 2 ? OutMax : Math.Max(cap * 2, OutFirst);
            log?.Invoke($"richedit stream out grow {cap} -> {next}");
            cap = next;
        }
    }

    static bool StreamOnce(IntPtr hwnd, bool inbound, byte[] src, int cap, Action<string>? log, out byte[] output, out string detail, out bool grow)
    {
        output = [];
        detail = "";
        grow = false;
        int bytes = Math.Max(cap, 1);
        IntPtr data = Marshal.AllocHGlobal(bytes);
        IntPtr cookie = Marshal.AllocHGlobal(24);
        IntPtr mem = Marshal.AllocHGlobal(EditStreamBytes);
        string which = inbound ? "in" : "out";
        try
        {
            if (inbound && src.Length > 0) Marshal.Copy(src, 0, data, src.Length);
            Marshal.WriteInt32(cookie, 0, inbound ? 0 : 1);
            Marshal.WriteInt32(cookie, 4, 0);
            Marshal.WriteInt32(cookie, 8, cap);
            Marshal.WriteInt32(cookie, 12, 0);
            Marshal.WriteIntPtr(cookie, 16, data);
            Marshal.WriteInt64(mem, 0, 0);
            Marshal.WriteInt64(mem, 8, 0);
            Marshal.WriteInt64(mem, 16, 0);
            Marshal.WriteIntPtr(mem, 0, cookie);
            Marshal.WriteInt32(mem, 8, 0);
            Marshal.WriteIntPtr(mem, 16, Stub);
            var placed = Marshal.ReadIntPtr(mem, CallbackOffset);
            if (placed != Stub) { detail = "callback slot"; log?.Invoke("richedit callback not at offset " + CallbackOffset); return false; }
            long addr = Stub.ToInt64();
            string size = inbound ? "bytes=" + src.Length : "cap=" + cap;
            log?.Invoke($"richedit stream {which} begin {size} ptr=0x{addr:X} align={(int)(addr & 15)} module={ModuleOf(Stub)}");
            if (inbound) SendMessage(hwnd, EmSetSel, IntPtr.Zero, new IntPtr(-1));
            SendMessage(hwnd, inbound ? EmStreamIn : EmStreamOut, new IntPtr(SfRtf), mem);
            uint err = (uint)Marshal.ReadInt32(mem, 8);
            int calls = Marshal.ReadInt32(cookie, 12);
            int pos = Marshal.ReadInt32(cookie, 4);
            log?.Invoke($"richedit stream {which} end err={err} calls={calls} pos={pos}");
            detail = "err=" + err + " calls=" + calls;
            if (!inbound && err != 0 && calls > 0 && (pos <= 0 || pos >= cap - 8192)) grow = cap < OutMax;
            if (err != 0) return false;
            if (!inbound)
            {
                if (pos < 0) pos = 0;
                if (pos > cap) pos = cap;
                output = new byte[pos];
                if (pos > 0) Marshal.Copy(data, output, 0, pos);
            }
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
            Marshal.FreeHGlobal(cookie);
            Marshal.FreeHGlobal(data);
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

    static bool EnsureStub(Action<string>? log)
    {
        if (Stub != IntPtr.Zero) return true;
        lock (StubGate)
        {
            if (Stub != IntPtr.Zero) return true;
            try
            {
                IntPtr page = VirtualAlloc(IntPtr.Zero, (UIntPtr)4096, 0x1000 | 0x2000, 0x40);
                if (page == IntPtr.Zero) { log?.Invoke("richedit stub alloc " + Marshal.GetLastWin32Error()); return false; }
                Marshal.Copy(StubCode, 0, page, StubCode.Length);
                FlushInstructionCache((IntPtr)(-1), page, (UIntPtr)StubCode.Length);
                string cfg = MarkCfg(page);
                VirtualProtect(page, (UIntPtr)4096, 0x20, out _);
                IntPtr probe = ProbeAddress();
                long stubAddr = page.ToInt64();
                long probeAddr = probe.ToInt64();
                log?.Invoke($"richedit stub ptr=0x{stubAddr:X} align={(int)(stubAddr & 15)} cfg={cfg} module={ModuleOf(page)} probe=0x{probeAddr:X} probeAlign={(int)(probeAddr & 15)} probeModule={ModuleOf(probe)}");
                if (!cfg.StartsWith("ok", StringComparison.Ordinal)) return false;
                Stub = page;
                return true;
            }
            catch (Exception e)
            {
                log?.Invoke("richedit stub " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }
    }

    static string MarkCfg(IntPtr page)
    {
        var mbi = new MemInfo();
        if (VirtualQuery(page, out mbi, (UIntPtr)Marshal.SizeOf<MemInfo>()) == UIntPtr.Zero)
            return "virtualquery " + Marshal.GetLastWin32Error();
        var tries = new (IntPtr Base, ulong Size)[]
        {
            (page, 4096),
            (mbi.AllocationBase, mbi.RegionSize),
            (mbi.AllocationBase, 65536),
        };
        string last = "";
        foreach (var (baseAddr, size) in tries)
        {
            if (baseAddr == IntPtr.Zero || size == 0) continue;
            var info = new CfgTarget { Offset = UIntPtr.Zero, Flags = (UIntPtr)1 };
            if (SetProcessValidCallTargets((IntPtr)(-1), baseAddr, (UIntPtr)size, 1, ref info))
                return "ok base=0x" + baseAddr.ToInt64().ToString("X") + " size=" + size;
            last = "err=" + Marshal.GetLastWin32Error() + " base=0x" + baseAddr.ToInt64().ToString("X") + " size=" + size;
        }
        return last.Length == 0 ? "no region" : last;
    }

    static string ModuleOf(IntPtr addr)
    {
        if (addr == IntPtr.Zero) return "(null)";
        if (!GetModuleHandleExW(0x4 | 0x2, addr, out var mod) || mod == IntPtr.Zero) return "(no module)";
        var sb = new StringBuilder(520);
        if (GetModuleFileNameW(mod, sb, sb.Capacity) == 0) return "(module)";
        return sb.ToString();
    }

    static IntPtr ProbeAddress()
    {
        unsafe
        {
            delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, uint> fp = &Probe;
            return (IntPtr)fp;
        }
    }

    // Not passed to msftedit. Logged so a failing run shows where a managed entry lives.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint Probe(IntPtr cookie, IntPtr buf, int cb, IntPtr pcb) => 0;

    // x64 stub. Cookie: mode@0 (0 in, 1 out), pos@4, cap@8, calls@12, data@16.
    // Disassembled and checked: in copies min(remaining, cb); out fails when pos+cb would pass cap.
    static readonly byte[] StubCode =
    [
        0x56, 0x57, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x89, 0xCB, 0xFF, 0x43, 0x0C, 0x4D, 0x85, 0xC9,
        0x0F, 0x84, 0x8A, 0x00, 0x00, 0x00, 0x41, 0xC7, 0x01, 0x00, 0x00, 0x00, 0x00, 0x45, 0x85, 0xC0,
        0x0F, 0x8E, 0x87, 0x00, 0x00, 0x00, 0x48, 0x85, 0xD2, 0x0F, 0x84, 0x71, 0x00, 0x00, 0x00, 0x83,
        0x3B, 0x00, 0x75, 0x35, 0x8B, 0x43, 0x08, 0x2B, 0x43, 0x04, 0x0F, 0x8E, 0x6D, 0x00, 0x00, 0x00,
        0x44, 0x39, 0xC0, 0x76, 0x03, 0x41, 0x8B, 0xC0, 0x48, 0x8B, 0x73, 0x10, 0x48, 0x63, 0x4B, 0x04,
        0x48, 0x01, 0xCE, 0x48, 0x89, 0xD7, 0x89, 0xC1, 0x41, 0x89, 0xC2, 0xF3, 0xA4, 0x44, 0x01, 0x53,
        0x04, 0x45, 0x89, 0x11, 0xE9, 0x44, 0x00, 0x00, 0x00, 0x8B, 0x43, 0x04, 0x44, 0x01, 0xC0, 0x0F,
        0x82, 0x2B, 0x00, 0x00, 0x00, 0x3B, 0x43, 0x08, 0x0F, 0x87, 0x22, 0x00, 0x00, 0x00, 0x48, 0x89,
        0xD6, 0x48, 0x8B, 0x7B, 0x10, 0x48, 0x63, 0x4B, 0x04, 0x48, 0x01, 0xCF, 0x44, 0x89, 0xC1, 0x45,
        0x89, 0xC3, 0xF3, 0xA4, 0x44, 0x01, 0x5B, 0x04, 0x45, 0x89, 0x19, 0xE9, 0x0D, 0x00, 0x00, 0x00,
        0xB8, 0x01, 0x00, 0x00, 0x00, 0x48, 0x83, 0xC4, 0x20, 0x5B, 0x5F, 0x5E, 0xC3, 0x33, 0xC0, 0x48,
        0x83, 0xC4, 0x20, 0x5B, 0x5F, 0x5E, 0xC3,
    ];

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
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32.dll")]
    static extern bool FlushInstructionCache(IntPtr process, IntPtr addr, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern UIntPtr VirtualQuery(IntPtr addr, out MemInfo info, UIntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessValidCallTargets(IntPtr process, IntPtr virt, UIntPtr region, uint count, ref CfgTarget info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetModuleHandleExW(uint flags, IntPtr addr, out IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetModuleFileNameW(IntPtr module, StringBuilder name, int size);

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    struct MemInfo
    {
        [FieldOffset(0)] public IntPtr BaseAddress;
        [FieldOffset(8)] public IntPtr AllocationBase;
        [FieldOffset(16)] public uint AllocationProtect;
        [FieldOffset(20)] public ushort PartitionId;
        [FieldOffset(24)] public ulong RegionSize;
        [FieldOffset(32)] public uint State;
        [FieldOffset(36)] public uint Protect;
        [FieldOffset(40)] public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CfgTarget
    {
        public UIntPtr Offset;
        public UIntPtr Flags;
    }
}
