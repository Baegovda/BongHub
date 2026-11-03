using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BongHub;

/// <summary>Program icons for the profile canvas. The shell is touched only after the window gate opens.</summary>
public static class AppIcon
{
    public static int Attempts, NativeCalls;
    public static Func<string, ImageSource?>? Fake;
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Lookup(string? process, string? path)
    {
        var files = IconBits.FilesToTry(process, path);
        if (files.Count == 0) return null;
        string key = files[0];
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (Fake != null)
        {
            var made = Fake(key);
            Cache[key] = made;
            return made;
        }
        if (!WindowGate.RealAllowed) return null;
        try
        {
            foreach (var file in files)
            {
                var made = ReadReal(file);
                if (made == null) continue;
                Cache[key] = made;
                return made;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>A frozen bitmap from repaired BGRA pixels, or null when the buffer has no visible pixel. 96 DPI so tests do not depend on the monitor.</summary>
    public static ImageSource? FromPixels(int width, int height, byte[]? bgra)
    {
        var pixels = IconBits.RepairAlpha(bgra);
        if (pixels == null || width < 1 || height < 1 || pixels.Length < width * height * 4) return null;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    public static ImageSource? ReadReal(string file)
    {
        Attempts++;
        WindowGate.Demand();
        NativeCalls++;
        if (string.IsNullOrWhiteSpace(file)) return null;
        bool exists = false;
        try { exists = File.Exists(file); } catch { exists = false; }
        // A real exe path is asked without USEFILEATTRIBUTES so the shell returns that file's icon.
        // A bare name falls back to the extension icon.
        uint[] flags = exists
            ? new uint[] { 0x100, 0x100 | 0x1 }
            : new uint[] { 0x100 | 0x10, 0x100 | 0x1 | 0x10 };
        foreach (uint flag in flags)
        {
            var info = new SHFILEINFO();
            IntPtr h = SHGetFileInfo(file, 0x80, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flag);
            if (h == IntPtr.Zero || info.hIcon == IntPtr.Zero) continue;
            try
            {
                var image = FromIcon(info.hIcon);
                if (image != null) return image;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        return null;
    }

    static ImageSource? FromIcon(IntPtr icon)
    {
        const int size = 32;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr dc = CreateCompatibleDC(screen);
        var info = new BITMAPINFO();
        info.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        info.biWidth = size;
        info.biHeight = -size;
        info.biPlanes = 1;
        info.biBitCount = 32;
        IntPtr bits;
        IntPtr dib = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            return null;
        }
        IntPtr old = SelectObject(dc, dib);
        try
        {
            DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, 3);
            var raw = new byte[size * size * 4];
            Marshal.Copy(bits, raw, 0, raw.Length);
            return FromPixels(size, size, raw);
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint cb, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
