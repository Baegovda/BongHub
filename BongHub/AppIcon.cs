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
        string key = !string.IsNullOrEmpty(path) ? path : (process ?? "");
        if (key.Length == 0) return null;
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
            var made = ReadReal(key) ?? (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? null : ReadReal(key + ".exe"));
            if (made != null) Cache[key] = made;
            return made;
        }
        catch { return null; }
    }

    public static ImageSource? ReadReal(string file)
    {
        Attempts++;
        WindowGate.Demand();
        NativeCalls++;
        foreach (uint flags in new uint[] { 0x100 | 0x10, 0x100 | 0x1 | 0x10 })
        {
            var info = new SHFILEINFO();
            IntPtr h = SHGetFileInfo(file, 0x80, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (h == IntPtr.Zero || info.hIcon == IntPtr.Zero) continue;
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        return null;
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
}
