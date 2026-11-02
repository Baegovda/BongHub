using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BongHub.Next;

/// <summary>
/// Starts the embedded helper only after <see cref="KakaoGate.Demand"/>.
/// This type does not call FindWindow, EnumWindows, ShowWindow, or SetWindowPos.
/// The helper process does that work, and only after a normal-mode start.
/// </summary>
public static class KakaoNative
{
    public static bool Launched { get; private set; }
    public static string LastArgs { get; private set; } = "";

    public static void Install()
    {
        if (!KakaoGate.RealAllowed) return;
    }

    public static void Sync(KakaoSettings cfg)
    {
        KakaoSystem.Touch();
        if (cfg.AdBlock)
        {
            string args = KakaoArgs.Format(cfg);
            if (!HelperRunning() || LastArgs != args) Start(args);
        }
        else if (Launched) Restore();
    }

    public static void Start(string args)
    {
        KakaoSystem.Touch();
        string path = Extract();
        if (path.Length == 0) return;
        KakaoSystem.NoteProcess();
        var psi = new ProcessStartInfo(path, args ?? "")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        };
        var started = Process.Start(psi);
        started?.Dispose();
        LastArgs = args ?? "";
        Launched = true;
    }

    public static void Restore()
    {
        KakaoSystem.Touch();
        string path = HelperPath();
        if (!File.Exists(path)) { Launched = false; LastArgs = ""; return; }
        KakaoSystem.NoteProcess();
        var psi = new ProcessStartInfo(path, "/restore")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        };
        var started = Process.Start(psi);
        try { if (started != null && !started.WaitForExit(4000)) started.Kill(entireProcessTree: false); }
        catch { }
        finally { started?.Dispose(); }
        Launched = false;
        LastArgs = "";
    }

    static bool HelperRunning()
    {
        KakaoSystem.NoteQuery();
        bool any = false;
        try
        {
            var list = Process.GetProcessesByName("BongAd");
            any = list.Length > 0;
            foreach (var p in list) p.Dispose();
        }
        catch { any = false; }
        return any;
    }

    static string HelperPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "helpers");
        return Path.Combine(dir, "BongAd.exe");
    }

    static string Extract()
    {
        byte[] data = Payload();
        if (data.Length == 0) return "";
        string path = HelperPath();
        KakaoFiles.WriteIfChanged(path, data);
        return File.Exists(path) ? path : "";
    }

    static byte[] Payload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BongAd.exe");
        if (stream == null) return Array.Empty<byte>();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
