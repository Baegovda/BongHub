using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BongHub;

public sealed class KakaoSettings
{
    public bool AdBlock;
    public bool Compact = true;
    public bool AutoApply = true;
}

/// <summary>One KakaoTalk window in a test. Coordinates are screen pixels. This type never calls user32.</summary>
public sealed class KakaoWindow
{
    public int Id;
    public int ParentId;
    public string Class = "";
    public string Title = "";
    public string Process = "";
    public int X, Y, W, H;
    public bool Visible = true;
}

/// <summary>
/// Unset <see cref="Allow"/> means blocked, so a test host that only compiles this file cannot start the helper.
/// </summary>
public static class KakaoGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm() { if (Allow?.Invoke() == true) Armed = true; }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 카톡 창을 찾거나 숨기거나 도우미를 실행하지 않아요.");
    }
}

/// <summary>Counters for the real helper. A demand failure leaves every native counter at zero.</summary>
public static class KakaoSystem
{
    public static int Attempts, NativeCalls, ProcessStarts, WindowCalls, ProcessQueries;
    public static void Touch()
    {
        Attempts++;
        KakaoGate.Demand();
        NativeCalls++;
    }
    public static void NoteProcess()
    {
        KakaoGate.Demand();
        ProcessStarts++;
    }
    public static void NoteQuery()
    {
        KakaoGate.Demand();
        ProcessQueries++;
    }
    public static void NoteWindow()
    {
        KakaoGate.Demand();
        WindowCalls++;
    }
}

public static class KakaoArgs
{
    /// <summary><c>/nocompact</c> when compact is off. The stored overlay flag is not an argument.</summary>
    public static string Format(KakaoSettings cfg)
    {
        return cfg.Compact ? "" : "/nocompact";
    }

    public static bool IsAdTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        return title.Contains("광고", StringComparison.Ordinal)
            || title.Contains("AdView", StringComparison.Ordinal)
            || title.Contains("BannerAd", StringComparison.Ordinal)
            || title.Contains("AdFit", StringComparison.Ordinal)
            || title.Contains("MOMENT", StringComparison.Ordinal);
    }

    public static bool IsKakaoProcess(string process)
        => process.EndsWith("KakaoTalk.exe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>In-memory helper. Start replaces the one running copy. Restore does not look at a real process.</summary>
public sealed class FakeKakaoHost
{
    public int Running;
    public string? LastArgs;
    public List<string> Log { get; } = new();

    public void Start(string args)
    {
        Log.Add("start " + args);
        Running = 1;
        LastArgs = args ?? "";
    }

    public void Restore()
    {
        Log.Add("restore");
        Running = 0;
        LastArgs = null;
    }
}

public static class KakaoSync
{
    public static void Apply(FakeKakaoHost host, KakaoSettings cfg)
    {
        if (cfg.AdBlock)
        {
            string args = KakaoArgs.Format(cfg);
            if (host.Running == 0 || host.LastArgs != args) host.Start(args);
        }
        else if (host.Running > 0 && host.LastArgs != null) host.Restore();
    }
}

/// <summary>The hide and compact moves BongAd would make. Nothing here calls a window API.</summary>
public static class KakaoPlan
{
    public static List<string> Commands(IReadOnlyList<KakaoWindow> windows, bool compact)
    {
        var cmds = new List<string>();
        if (windows == null) return cmds;
        foreach (var top in windows)
        {
            if (top.ParentId != 0 || !top.Visible || !KakaoArgs.IsKakaoProcess(top.Process)) continue;
            if (top.Class == "EVA_Window_Dblclk" && top.Title == "카카오톡")
                Layout(windows, top, compact, cmds);
            var ad = FindAd(windows, top.Id);
            if (ad == null) continue;
            if (top.Title.Length == 0) cmds.Add("hide " + top.Id);
            else cmds.Add("hide " + ChildOf(windows, ad, top.Id));
        }
        return cmds;
    }

    static void Layout(IReadOnlyList<KakaoWindow> windows, KakaoWindow top, bool compact, List<string> cmds)
    {
        int target = top.Y + top.H - 1;
        foreach (var kid in Children(windows, top.Id))
        {
            if (!kid.Visible) continue;
            if (kid.Class.StartsWith("EVA_ChildWindow", StringComparison.Ordinal) && kid.Title.Length == 0)
            {
                if (target - (kid.Y + kid.H) <= 20 && kid.H > 20 && kid.H < 250) cmds.Add("hide " + kid.Id);
            }
            else if (kid.Title.StartsWith("OnlineMainView", StringComparison.Ordinal) || kid.Title.StartsWith("LockModeView", StringComparison.Ordinal))
            {
                int up = compact && kid.Y >= 34 ? 8 : 0;
                int db = target - (kid.Y + kid.H);
                if (up != 0 || db > 2 || db < -1)
                    cmds.Add("place " + kid.Id + " " + kid.X + " " + (kid.Y - up) + " " + kid.W + " " + (target - (kid.Y - up)));
                if (compact)
                    foreach (var view in Children(windows, kid.Id))
                        if (view.Title.StartsWith("ChatRoomListView", StringComparison.Ordinal))
                            CompactList(windows, view.Id, cmds);
            }
        }
    }

    static void CompactList(IReadOnlyList<KakaoWindow> windows, int viewId, List<string> cmds)
    {
        foreach (var kid in Children(windows, viewId))
        {
            if (!kid.Visible) continue;
            if (kid.Class == "EVA_Window_Dblclk" && kid.Title.Length == 0 && kid.Y >= 52 && kid.Y <= 60)
                cmds.Add("place " + kid.Id + " " + kid.X + " " + (kid.Y - 8) + " " + kid.W + " " + kid.H);
            else if (kid.Title.StartsWith("ChatRoomListCtrl", StringComparison.Ordinal) && kid.Y >= 100)
                cmds.Add("place " + kid.Id + " " + kid.X + " " + (kid.Y - 14) + " " + kid.W + " " + (kid.H + 14));
        }
    }

    static KakaoWindow? FindAd(IReadOnlyList<KakaoWindow> windows, int topId)
    {
        var pending = new Queue<int>();
        pending.Enqueue(topId);
        while (pending.Count > 0)
        {
            int id = pending.Dequeue();
            foreach (var kid in Children(windows, id))
            {
                if (KakaoArgs.IsAdTitle(kid.Title)) return kid;
                pending.Enqueue(kid.Id);
            }
        }
        return null;
    }

    static int ChildOf(IReadOnlyList<KakaoWindow> windows, KakaoWindow ad, int topId)
    {
        int id = ad.Id;
        while (true)
        {
            var node = windows.First(w => w.Id == id);
            if (node.ParentId == topId || node.ParentId == 0) return node.ParentId == topId ? node.Id : topId;
            id = node.ParentId;
        }
    }

    static IEnumerable<KakaoWindow> Children(IReadOnlyList<KakaoWindow> windows, int parent)
    {
        foreach (var w in windows)
            if (w.ParentId == parent) yield return w;
    }
}

public static class KakaoFiles
{
    public const string Sha256 = "4E87998CB143640434AD6A178DC9C881F890C81ED77FA9B5F1BC228340363B49";

    public static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Writes the helper bytes only when they differ. It does not start a process.</summary>
    public static bool WriteIfChanged(string path, byte[] data)
    {
        if (string.IsNullOrEmpty(path) || data == null || data.Length == 0) return false;
        try
        {
            if (File.Exists(path) && Same(File.ReadAllBytes(path), data)) return false;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, data);
            return true;
        }
        catch { return false; }
    }
}
