using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BongHub;

/// <summary>One 3.4.x window rule. Matching and placement are pure; the surface does the move.</summary>
public sealed class WindowRule
{
    public bool Enabled = true;
    public string Name = "";
    public string Process = "";
    public string Title = "";
    public bool TitleRegex;
    public string Class = "";
    public string Monitor = "";
    public string Action = "pos";
    public int X, Y, W = 1280, H = 800;
    public bool Pin, Keep;
    public int Zone;
}

public sealed class WindowPick
{
    public string Process = "";
    public string Path = "";
    public string Title = "";
    public string Class = "";
    public int X, Y, W, H;
}

/// <summary>Copies what a crosshair drop captured. It does not enumerate real windows.</summary>
public static class WindowPicker
{
    public static WindowPick From(TrackedWindow window) => new()
    {
        Process = window.Process ?? "",
        Path = window.Path ?? "",
        Title = window.Title ?? "",
        Class = window.Class ?? "",
        X = window.X,
        Y = window.Y,
        W = window.W,
        H = window.H
    };

    /// <summary>The last window that contains the point wins. No enumeration of real windows.</summary>
    public static TrackedWindow? Hit(IEnumerable<TrackedWindow> windows, int x, int y)
    {
        TrackedWindow? best = null;
        foreach (var w in windows)
        {
            if (w.W <= 0 || w.H <= 0) continue;
            if (x >= w.X && y >= w.Y && x < w.X + w.W && y < w.Y + w.H) best = w;
        }
        return best;
    }

    /// <summary>DIP cursor to physical pixels. Tests pass the DPI; the live picker uses GetCursorPos and never this conversion.</summary>
    public static (int X, int Y) Physical(double dipX, double dipY, double dpi)
    {
        if (dpi < 0.5 || double.IsNaN(dpi) || double.IsInfinity(dpi)) dpi = 1;
        return ((int)Math.Round(dipX * dpi), (int)Math.Round(dipY * dpi));
    }

    public static bool Accept(PickProbe probe)
    {
        if (probe == null) return false;
        var w = probe.Window;
        return probe.Visible && !probe.Cloaked && !probe.Own && !probe.Shadow && w != null && w.W > 0 && w.H > 0;
    }

    /// <summary>
    /// Highest Z window whose rect contains the point, after dropping invisible, cloaked, own, and shadow windows.
    /// The result is the top-level owner when that owner is also an acceptable probe.
    /// </summary>
    public static TrackedWindow? Choose(IReadOnlyList<PickProbe>? probes, int x, int y)
    {
        if (probes == null || probes.Count == 0) return null;
        PickProbe? best = null;
        foreach (var p in probes)
        {
            if (!Accept(p)) continue;
            var w = p.Window;
            if (x < w.X || y < w.Y || x >= w.X + w.W || y >= w.Y + w.H) continue;
            if (best == null || p.Z >= best.Z) best = p;
        }
        if (best == null) return null;
        return ResolveOwner(best, probes).Window;
    }

    static PickProbe ResolveOwner(PickProbe hit, IReadOnlyList<PickProbe> probes)
    {
        var current = hit;
        for (int guard = 0; guard < 8 && current.OwnerId != 0; guard++)
        {
            PickProbe? owner = null;
            foreach (var p in probes)
                if (p.Window != null && p.Window.Id == current.OwnerId) { owner = p; break; }
            if (owner == null || !Accept(owner)) break;
            current = owner;
        }
        return current;
    }
}

/// <summary>One window the crosshair might land on. Rects are physical pixels.</summary>
public sealed class PickProbe
{
    public TrackedWindow Window = new();
    public bool Visible = true;
    public bool Cloaked;
    public bool Own;
    public bool Shadow;
    /// <summary>WS_EX_TOOLWINDOW and tooltip classes. Session capture skips these.</summary>
    public bool Tool;
    public long OwnerId;
    public int Z;
}

/// <summary>Which file names to ask the shell for, and how to stop a blank HICON from looking like a missing icon.</summary>
public static class IconBits
{
    public static List<string> FilesToTry(string? process, string? path)
    {
        var list = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            string t = s.Trim();
            if (list.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(t);
        }
        Add(path);
        if (!string.IsNullOrWhiteSpace(path) && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) Add(path + ".exe");
        Add(process);
        if (!string.IsNullOrWhiteSpace(process) && !process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) Add(process + ".exe");
        return list;
    }

    /// <summary>
    /// BGRA pixels. A fully transparent buffer with color becomes opaque (the HICON color plane without a mask).
    /// A buffer with no color returns null so the tile can draw a glyph instead of a blank image.
    /// </summary>
    public static byte[]? RepairAlpha(byte[]? pixels)
    {
        if (pixels == null || pixels.Length < 4 || pixels.Length % 4 != 0) return null;
        bool anyAlpha = false, anyColor = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] != 0) anyAlpha = true;
            if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) anyColor = true;
        }
        if (!anyColor && !anyAlpha) return null;
        if (anyAlpha) return pixels;
        var copy = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            copy[i] = pixels[i];
            copy[i + 1] = pixels[i + 1];
            copy[i + 2] = pixels[i + 2];
            copy[i + 3] = (byte)((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0 ? 255 : 0);
        }
        return copy;
    }
}

public sealed class TrackedWindow
{
    public long Id;
    public string Process = "";
    public string Title = "";
    public string Class = "";
    public string Path = "";
    public int X, Y, W, H;
    public bool Max, Min, TopMost;
    /// <summary>Restored rect from GetWindowPlacement. Used when <see cref="Max"/> or <see cref="Min"/> is set.</summary>
    public int NormalX, NormalY, NormalW, NormalH;
}

public readonly record struct WorkRect(int X, int Y, int W, int H);

/// <summary>Unset <see cref="Allow"/> means blocked, so tests and <c>--off</c> cannot enumerate real top-level windows.</summary>
public static class WindowGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm()
    {
        if (Allow?.Invoke() != true) return;
        Armed = true;
    }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 실제 창을 찾거나 옮기지 않아요.");
    }
}

public interface IWindowSurface : IDisposable
{
    IReadOnlyList<TrackedWindow> List();
    bool TryWorkArea(string monitorId, long windowId, out WorkRect area);
    void Place(long id, int x, int y, int w, int h);
    void Minimize(long id);
    void Maximize(long id, WorkRect area);
    void Restore(long id);
    void Pin(long id);
}

public sealed class FakeWindowSurface : IWindowSurface
{
    public List<TrackedWindow> Windows { get; } = new();
    public Dictionary<string, WorkRect> Areas { get; } = new Dictionary<string, WorkRect>(StringComparer.OrdinalIgnoreCase) { [""] = new WorkRect(0, 0, 1920, 1080) };
    public List<string> Ops { get; } = new();
    public IReadOnlyList<TrackedWindow> List() => Windows.Select(Clone).ToList();
    public bool TryWorkArea(string monitorId, long windowId, out WorkRect area) => Areas.TryGetValue(monitorId ?? "", out area);
    public int WorkOriginX, WorkOriginY;
    public void Place(long id, int x, int y, int w, int h)
    {
        var win = Find(id);
        win.X = x; win.Y = y; win.W = w; win.H = h; win.Min = false; win.Max = false;
        var ws = PlacementSpace.ToWorkspace(new PlacementSpace.Box(x, y, w, h), WorkOriginX, WorkOriginY);
        win.NormalX = ws.X; win.NormalY = ws.Y; win.NormalW = ws.W; win.NormalH = ws.H;
        Ops.Add("place " + id);
    }
    public void Minimize(long id) { var win = Find(id); win.Min = true; win.Max = false; Ops.Add("min " + id); }
    public void Maximize(long id, WorkRect area)
    {
        var win = Find(id);
        if (win.NormalW <= 0)
        {
            var ws = PlacementSpace.ToWorkspace(new PlacementSpace.Box(win.X, win.Y, win.W, win.H), WorkOriginX, WorkOriginY);
            win.NormalX = ws.X; win.NormalY = ws.Y; win.NormalW = ws.W; win.NormalH = ws.H;
        }
        win.Min = false; win.Max = true; win.X = area.X; win.Y = area.Y; win.W = area.W; win.H = area.H;
        Ops.Add("max " + id);
    }
    public void Restore(long id) { var win = Find(id); win.Min = false; win.Max = false; Ops.Add("restore " + id); }
    /// <summary>What the taskbar does on restore: show the stored normal rect, which is workspace coordinates.</summary>
    public void ShowRestored(long id)
    {
        var win = Find(id);
        var screen = PlacementSpace.ToScreen(new PlacementSpace.Box(win.NormalX, win.NormalY, win.NormalW, win.NormalH), WorkOriginX, WorkOriginY);
        win.Min = false; win.Max = false;
        win.X = screen.X; win.Y = screen.Y; win.W = screen.W; win.H = screen.H;
        Ops.Add("show " + id);
    }
    /// <summary>Rewrite the restore rect of windows the session did not place. Their normal rect is workspace coordinates.</summary>
    public void Anchor(IReadOnlyCollection<long>? skip, IReadOnlyList<PlacementSpace.Monitor>? before, IReadOnlyList<PlacementSpace.Monitor>? after)
    {
        foreach (var win in Windows)
        {
            if (skip != null && skip.Contains(win.Id)) continue;
            var now = new PlacementSpace.Box(win.X, win.Y, win.W, win.H);
            var old = new PlacementSpace.Box(win.NormalX, win.NormalY, win.NormalW, win.NormalH);
            var screen = PlacementSpace.RestoreTarget(win.Min, now, old, WorkOriginX, WorkOriginY, before, after);
            var ws = PlacementSpace.ToWorkspace(screen, WorkOriginX, WorkOriginY);
            win.NormalX = ws.X; win.NormalY = ws.Y; win.NormalW = ws.W; win.NormalH = ws.H;
            Ops.Add("anchor " + win.Id);
        }
    }
    public void Pin(long id) { Find(id).TopMost = true; Ops.Add("pin " + id); }
    public void Dispose() { }
    TrackedWindow Find(long id) => Windows.First(w => w.Id == id);
    static TrackedWindow Clone(TrackedWindow w) => new() { Id = w.Id, Process = w.Process, Title = w.Title, Class = w.Class, Path = w.Path, X = w.X, Y = w.Y, W = w.W, H = w.H, Max = w.Max, Min = w.Min, TopMost = w.TopMost, NormalX = w.NormalX, NormalY = w.NormalY, NormalW = w.NormalW, NormalH = w.NormalH };
}

public static class RuleMatch
{
    static readonly Dictionary<string, Regex?> Regexes = new();

    public static bool HasCriteria(WindowRule r) => r.Process.Length > 0 || r.Title.Length > 0 || r.Class.Length > 0;

    public static bool Match(WindowRule r, TrackedWindow w)
    {
        if (!HasCriteria(r)) return false;
        if (r.Process.Length > 0)
        {
            string p = r.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? r.Process[..^4] : r.Process;
            if (!string.Equals(p, w.Process, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (r.Class.Length > 0 && !string.Equals(r.Class, w.Class, StringComparison.OrdinalIgnoreCase)) return false;
        if (r.Title.Length > 0)
        {
            if (r.TitleRegex)
            {
                if (!Regexes.TryGetValue(r.Title, out var rx))
                {
                    try { rx = new Regex(r.Title, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
                    catch { rx = null; }
                    Regexes[r.Title] = rx;
                }
                if (rx == null) return false;
                try { if (!rx.IsMatch(w.Title)) return false; } catch { return false; }
            }
            else if (w.Title.IndexOf(r.Title, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }

    public static bool InPlace(WindowRule r, TrackedWindow w, IWindowSurface surface)
    {
        bool missing = r.Monitor.Length > 0 && !surface.TryWorkArea(r.Monitor, w.Id, out _);
        bool onTarget = r.Monitor.Length == 0 || (surface.TryWorkArea(r.Monitor, w.Id, out var want) && surface.TryWorkArea("", w.Id, out var cur) && want.X == cur.X && want.Y == cur.Y);
        bool ok = true;
        if (!missing)
        {
            switch (r.Action)
            {
                case "pos":
                case "zone":
                    surface.TryWorkArea(r.Monitor, w.Id, out var wa);
                    int x = wa.X + r.X, y = wa.Y + r.Y;
                    ok = !w.Min && !w.Max && Math.Abs(x - w.X) <= 8 && Math.Abs(y - w.Y) <= 8 && Math.Abs(r.W - w.W) <= 8 && Math.Abs(r.H - w.H) <= 8;
                    break;
                case "monitor": ok = onTarget; break;
                case "max": ok = w.Max && onTarget; break;
                case "min": ok = w.Min; break;
                case "restore": ok = !w.Min && !w.Max; break;
                default: ok = onTarget; break;
            }
        }
        if (r.Pin && !w.TopMost) ok = false;
        return ok;
    }
}

public static class RuleEngine
{
    public static int Apply(IWindowSurface surface, IEnumerable<WindowRule> rules)
    {
        var list = rules.Where(r => r.Enabled && RuleMatch.HasCriteria(r)).ToList();
        if (list.Count == 0) return 0;
        int n = 0;
        foreach (var w in surface.List())
        {
            foreach (var r in list)
            {
                if (!RuleMatch.Match(r, w)) continue;
                ApplyOne(surface, r, w);
                n++;
                break;
            }
        }
        return n;
    }

    public static void ApplyOne(IWindowSurface surface, WindowRule r, TrackedWindow w)
    {
        bool missing = r.Monitor.Length > 0 && !surface.TryWorkArea(r.Monitor, w.Id, out _);
        surface.TryWorkArea(missing ? "" : r.Monitor, w.Id, out var wa);
        if (!missing)
        {
            switch (r.Action)
            {
                case "pos":
                case "zone":
                    surface.Restore(w.Id);
                    surface.Place(w.Id, wa.X + r.X, wa.Y + r.Y, r.W, r.H);
                    break;
                case "min":
                    surface.Minimize(w.Id);
                    break;
                case "max":
                    surface.Maximize(w.Id, wa);
                    break;
                case "restore":
                    surface.Restore(w.Id);
                    break;
                case "monitor":
                case "none":
                    if (r.Action == "none" && r.Monitor.Length == 0) break;
                    if (surface.TryWorkArea("", w.Id, out var cur) && (cur.X != wa.X || cur.Y != wa.Y))
                    {
                        int width = Math.Min(w.W, wa.W), height = Math.Min(w.H, wa.H);
                        int nx = Math.Max(wa.X, Math.Min(wa.X + (w.X - cur.X), wa.X + wa.W - width));
                        int ny = Math.Max(wa.Y, Math.Min(wa.Y + (w.Y - cur.Y), wa.Y + wa.H - height));
                        surface.Place(w.Id, nx, ny, width, height);
                    }
                    break;
            }
        }
        if (r.Pin) surface.Pin(w.Id);
    }
}

/// <summary>Keep-in-place check with no timer and no event hook. Callers decide when to ask.</summary>
public sealed class KeepWatch
{
    readonly List<WindowRule> rules = new();
    public int Applied { get; private set; }
    public bool Active => rules.Count > 0;
    public void SetRules(IEnumerable<WindowRule> all) => rules.ClearAndAdd(all.Where(r => r.Enabled && r.Keep && RuleMatch.HasCriteria(r)));
    public void Check(IWindowSurface surface, long id)
    {
        if (!Active) return;
        var w = surface.List().FirstOrDefault(x => x.Id == id);
        if (w == null) return;
        foreach (var r in rules)
        {
            if (!RuleMatch.Match(r, w)) continue;
            if (!RuleMatch.InPlace(r, w, surface)) { RuleEngine.ApplyOne(surface, r, w); Applied++; }
            return;
        }
    }
}

static class RuleListExt
{
    public static void ClearAndAdd<T>(this List<T> list, IEnumerable<T> items) { list.Clear(); list.AddRange(items); }
}

public sealed class WorkspaceItem
{
    public bool Enabled = true;
    public string Process = "";
    public string Title = "";
    public string Path = "";
    public int X, Y, W, H;
    public string State = "normal";
}

public sealed class WorkspaceMove
{
    public long WindowId;
    public int X, Y, W, H;
    public string State = "normal";
}

/// <summary>Places windows that are already open. It never starts a process.</summary>
public static class WorkspacePlan
{
    public static List<WorkspaceMove> Plan(IReadOnlyList<TrackedWindow> open, IEnumerable<WorkspaceItem> apps, out int missing)
    {
        var used = new HashSet<long>();
        var moves = new List<WorkspaceMove>();
        missing = 0;
        foreach (var app in apps)
        {
            if (!app.Enabled) continue;
            var win = Find(open, used, app);
            if (win == null) { missing++; continue; }
            used.Add(win.Id);
            moves.Add(new WorkspaceMove { WindowId = win.Id, X = app.X, Y = app.Y, W = app.W, H = app.H, State = app.State });
        }
        return moves;
    }

    static TrackedWindow? Find(IReadOnlyList<TrackedWindow> open, HashSet<long> used, WorkspaceItem app)
    {
        TrackedWindow? first = null;
        foreach (var w in open)
        {
            if (used.Contains(w.Id) || !SameProcess(w, app)) continue;
            if (app.Title.Length > 0 && string.Equals(w.Title, app.Title, StringComparison.OrdinalIgnoreCase)) return w;
            first ??= w;
        }
        return first;
    }

    static bool SameProcess(TrackedWindow w, WorkspaceItem app)
    {
        if (app.Path.Length > 0 && string.Equals(w.Path, app.Path, StringComparison.OrdinalIgnoreCase)) return true;
        string want = Strip(app.Process);
        return want.Length > 0 && string.Equals(want, Strip(w.Process), StringComparison.OrdinalIgnoreCase);
    }

    static string Strip(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}

/// <summary>
/// <c>WINDOWPLACEMENT.rcNormalPosition</c> is workspace coordinates. The origin is the primary monitor's work area.
/// Screen pixels are what <see cref="TrackedWindow"/> stores. Tests use <see cref="FakeWindowSurface"/> and never a real monitor.
/// </summary>
public static class PlacementSpace
{
    public readonly record struct Box(int X, int Y, int W, int H);
    public readonly record struct Monitor(string Id, int X, int Y, int W, int H);

    public static Box ToWorkspace(Box screen, int originX, int originY)
        => new(screen.X - originX, screen.Y - originY, screen.W, screen.H);

    public static Box ToScreen(Box workspace, int originX, int originY)
        => new(workspace.X + originX, workspace.Y + originY, workspace.W, workspace.H);

    public static bool Iconic(Box screen) => screen.X <= -30000 || screen.W <= 0 || screen.H <= 0;

    /// <summary>Same local offset on the monitor with the same id, after that monitor moves.</summary>
    public static Box Carry(Box screen, IReadOnlyList<Monitor>? before, IReadOnlyList<Monitor>? after)
    {
        if (before == null || after == null || before.Count == 0) return screen;
        int cx = screen.X + Math.Max(1, screen.W) / 2;
        int cy = screen.Y + Math.Max(1, screen.H) / 2;
        Monitor host = default;
        bool found = false;
        foreach (var m in before)
        {
            if (cx < m.X || cy < m.Y || cx >= m.X + m.W || cy >= m.Y + m.H) continue;
            host = m;
            found = true;
            break;
        }
        if (!found || string.IsNullOrEmpty(host.Id)) return screen;
        foreach (var m in after)
        {
            if (!string.Equals(m.Id, host.Id, StringComparison.OrdinalIgnoreCase) || m.W <= 0 || m.H <= 0) continue;
            return new Box(m.X + (screen.X - host.X), m.Y + (screen.Y - host.Y), screen.W, screen.H);
        }
        return screen;
    }

    /// <summary>
    /// Screen rect a later restore should show. A visible window keeps the monitor it is on now.
    /// A minimized window only has its old normal rect, so that rect rides the monitor it sat on.
    /// </summary>
    public static Box RestoreTarget(bool minimized, Box screenNow, Box oldNormalWorkspace, int originX, int originY, IReadOnlyList<Monitor>? before, IReadOnlyList<Monitor>? after)
    {
        if (!minimized && !Iconic(screenNow)) return screenNow;
        var oldScreen = ToScreen(oldNormalWorkspace, originX, originY);
        if (oldScreen.W <= 0 || oldScreen.H <= 0) return screenNow;
        return Carry(oldScreen, before, after);
    }

    /// <summary>Fake windows only. Empty means the restore rect survives a monitor move and a taskbar offset.</summary>
    public static string Story()
    {
        var screen = new Box(1920, 40, 640, 480);
        var ws = ToWorkspace(screen, 0, 40);
        if (ws.X != 1920 || ws.Y != 0 || ws.W != 640) return "workspace " + ws.X + "," + ws.Y;
        var back = ToScreen(ws, 0, 40);
        if (back.X != 1920 || back.Y != 40) return "screen " + back.X + "," + back.Y;
        var placed = new FakeWindowSurface { WorkOriginY = 40 };
        placed.Windows.Add(new TrackedWindow { Id = 3, X = 100, Y = 100, W = 200, H = 200 });
        placed.Place(3, 1920, 40, 640, 480);
        if (placed.Windows[0].NormalY != 0 || placed.Windows[0].NormalX != 1920 || placed.Windows[0].Y != 40) return "place wrote screen coordinates into the normal rect";
        placed.Windows[0].Min = true;
        placed.Windows[0].X = -32000;
        placed.Windows[0].Y = -32000;
        placed.Windows[0].W = 160;
        placed.Windows[0].H = 28;
        placed.ShowRestored(3);
        if (placed.Windows[0].X != 1920 || placed.Windows[0].Y != 40 || placed.Windows[0].Min) return "restore left the right monitor";
        var before = new[] { new Monitor("A", 0, 0, 1920, 1080), new Monitor("B", 1920, 0, 1920, 1080) };
        var after = new[] { new Monitor("A", 0, 0, 1920, 1080), new Monitor("B", -1920, 0, 1920, 1080) };
        var idle = new FakeWindowSurface();
        idle.Windows.Add(new TrackedWindow { Id = 8, Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = 2000, NormalY = 100, NormalW = 800, NormalH = 600 });
        idle.Windows.Add(new TrackedWindow { Id = 9, X = -1700, Y = 80, W = 500, H = 400, NormalX = 2100, NormalY = 80, NormalW = 500, NormalH = 400 });
        idle.Anchor(null, before, after);
        idle.ShowRestored(8);
        var hidden = idle.Windows.First(w => w.Id == 8);
        if (hidden.X != -1840 || hidden.Y != 100) return "minimized window restored at " + hidden.X + "," + hidden.Y;
        idle.ShowRestored(9);
        var shown = idle.Windows.First(w => w.Id == 9);
        if (shown.X != -1700 || shown.Y != 80) return "visible window jumped to " + shown.X + "," + shown.Y;
        var session = new FakeWindowSurface { WorkOriginY = 40 };
        session.Windows.Add(new TrackedWindow { Id = 1 });
        session.Place(1, 1920, 40, 400, 300);
        session.Windows[0].X = -1800;
        session.Windows[0].Y = 40;
        session.Anchor(new long[] { 1 }, before, after);
        session.ShowRestored(1);
        if (session.Windows[0].X != 1920 || session.Windows[0].Y != 40) return "session window was reanchored";
        return "";
    }
}

/// <summary>
/// Real top-level windows. The constructor enumerates only after <see cref="WindowGate.Demand"/>.
/// There is no event hook and no timer here.
/// </summary>
public sealed class LiveWindowSurface : IWindowSurface
{
    public static int Attempts;
    public static int NativeCalls;
    readonly List<TrackedWindow> windows = new();
    readonly Dictionary<long, IntPtr> hwnds = new();

    public LiveWindowSurface()
    {
        Attempts++;
        WindowGate.Demand();
        NativeCalls++;
        Snapshot();
    }

    public IReadOnlyList<TrackedWindow> List() => windows.Select(w => new TrackedWindow { Id = w.Id, Process = w.Process, Title = w.Title, Class = w.Class, Path = w.Path, X = w.X, Y = w.Y, W = w.W, H = w.H, Max = w.Max, Min = w.Min, TopMost = w.TopMost }).ToList();

    public bool TryWorkArea(string monitorId, long windowId, out WorkRect area)
    {
        area = default;
        if (!hwnds.TryGetValue(windowId, out var hwnd)) return false;
        if (string.IsNullOrEmpty(monitorId)) return WorkOf(MonitorFromWindow(hwnd, 2), out area);
        return WorkOfId(monitorId, out area);
    }

    public void Place(long id, int x, int y, int w, int h)
    {
        if (!hwnds.TryGetValue(id, out var hwnd)) return;
        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, 4);
        var origin = PrimaryWorkOrigin();
        var ws = PlacementSpace.ToWorkspace(new PlacementSpace.Box(x, y, w, h), origin.X, origin.Y);
        var placed = new WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = 1,
            normal = new RECT { Left = ws.X, Top = ws.Y, Right = ws.X + ws.W, Bottom = ws.Y + ws.H }
        };
        SetWindowPlacement(hwnd, ref placed);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x4 | 0x10 | 0x200);
        Update(id, hwnd);
    }

    public void Minimize(long id)
    {
        if (hwnds.TryGetValue(id, out var hwnd) && !IsIconic(hwnd)) ShowWindow(hwnd, 7);
        if (windows.FirstOrDefault(w => w.Id == id) is { } win) { win.Min = true; win.Max = false; }
    }

    public void Maximize(long id, WorkRect area)
    {
        if (!hwnds.TryGetValue(id, out var hwnd)) return;
        var placed = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placed) || placed.normal.Right <= placed.normal.Left)
        {
            var origin = PrimaryWorkOrigin();
            var ws = PlacementSpace.ToWorkspace(new PlacementSpace.Box(area.X + 40, area.Y + 40, Math.Max(200, area.W - 80), Math.Max(150, area.H - 80)), origin.X, origin.Y);
            placed.normal = new RECT { Left = ws.X, Top = ws.Y, Right = ws.X + ws.W, Bottom = ws.Y + ws.H };
        }
        placed.length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>();
        placed.showCmd = 3;
        SetWindowPlacement(hwnd, ref placed);
        if (windows.FirstOrDefault(w => w.Id == id) is { } win) { win.Min = false; win.Max = true; }
    }

    /// <summary>
    /// After a monitor arrangement change, windows the session did not place still restore from the old normal rect.
    /// Rewrite that rect from where the window is now, in workspace coordinates. Minimized windows ride their monitor.
    /// </summary>
    public int AnchorRestores(IReadOnlyCollection<long>? skip, IReadOnlyList<PlacementSpace.Monitor>? before, IReadOnlyList<PlacementSpace.Monitor>? after)
    {
        var origin = PrimaryWorkOrigin();
        int n = 0;
        foreach (var pair in hwnds)
        {
            if (skip != null && skip.Contains(pair.Key)) continue;
            var hwnd = pair.Value;
            if (!GetWindowRect(hwnd, out var rc)) continue;
            var placed = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placed)) continue;
            var now = new PlacementSpace.Box(rc.Left, rc.Top, Math.Max(0, rc.Right - rc.Left), Math.Max(0, rc.Bottom - rc.Top));
            var old = new PlacementSpace.Box(placed.normal.Left, placed.normal.Top, Math.Max(0, placed.normal.Right - placed.normal.Left), Math.Max(0, placed.normal.Bottom - placed.normal.Top));
            var screen = PlacementSpace.RestoreTarget(IsIconic(hwnd), now, old, origin.X, origin.Y, before, after);
            if (screen.W < 1 || screen.H < 1) continue;
            var ws = PlacementSpace.ToWorkspace(screen, origin.X, origin.Y);
            placed.length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>();
            placed.normal = new RECT { Left = ws.X, Top = ws.Y, Right = ws.X + ws.W, Bottom = ws.Y + ws.H };
            SetWindowPlacement(hwnd, ref placed);
            n++;
        }
        return n;
    }

    static (int X, int Y) PrimaryWorkOrigin()
    {
        int x = 0, y = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, ref rc, _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref mi) && (mi.flags & 1) != 0)
            {
                x = mi.rcWork.Left;
                y = mi.rcWork.Top;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return (x, y);
    }

    public void Restore(long id)
    {
        if (hwnds.TryGetValue(id, out var hwnd) && (IsIconic(hwnd) || IsZoomed(hwnd))) ShowWindow(hwnd, 4);
        if (windows.FirstOrDefault(w => w.Id == id) is { } win) { win.Min = false; win.Max = false; }
    }

    public void Pin(long id)
    {
        if (hwnds.TryGetValue(id, out var hwnd)) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        if (windows.FirstOrDefault(w => w.Id == id) is { } win) win.TopMost = true;
    }

    public void Dispose() { }

    void Snapshot()
    {
        uint self = (uint)Environment.ProcessId;
        var found = new List<IntPtr>();
        EnumWindows((h, _) => { found.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in found)
        {
            try
            {
                if (!Eligible(h)) continue;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == self) continue;
                string cls = ClassOf(h);
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") continue;
                FrameOf(h, out var rc);
                ImageOf(pid, out string process, out string path);
                var win = new TrackedWindow
                {
                    Id = h.ToInt64(),
                    Process = process,
                    Path = path,
                    Title = TextOf(h),
                    Class = cls,
                    X = rc.Left, Y = rc.Top, W = rc.Right - rc.Left, H = rc.Bottom - rc.Top,
                    Max = IsZoomed(h), Min = IsIconic(h),
                    TopMost = (GetWindowLongPtr(h, -20).ToInt64() & 8) != 0
                };
                windows.Add(win);
                hwnds[win.Id] = h;
            }
            catch { }
        }
    }

    void Update(long id, IntPtr hwnd)
    {
        if (windows.FirstOrDefault(w => w.Id == id) is not { } win) return;
        GetWindowRect(hwnd, out var rc);
        win.X = rc.Left; win.Y = rc.Top; win.W = rc.Right - rc.Left; win.H = rc.Bottom - rc.Top;
        win.Max = IsZoomed(hwnd); win.Min = IsIconic(hwnd);
    }

    static bool Eligible(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        if (GetAncestor(h, 2) != h) return false;
        if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return false;
        long ex = GetWindowLongPtr(h, -20).ToInt64();
        long st = GetWindowLongPtr(h, -16).ToInt64();
        bool app = (ex & 0x40000) != 0;
        if ((ex & 0x80) != 0 && !app) return false;
        if (GetWindow(h, 4) != IntPtr.Zero && !app) return false;
        if ((st & 0xC00000) == 0 && GetWindowTextLength(h) == 0) return false;
        return true;
    }

    static void ImageOf(uint pid, out string process, out string path)
    {
        process = "";
        path = "";
        IntPtr p = OpenProcess(0x1000, false, pid);
        if (p == IntPtr.Zero) return;
        try
        {
            var sb = new StringBuilder(1024);
            int n = sb.Capacity;
            if (!QueryFullProcessImageName(p, 0, sb, ref n)) return;
            path = sb.ToString();
            process = Path.GetFileNameWithoutExtension(path);
        }
        finally { CloseHandle(p); }
    }

    /// <summary>Visible frame without the DWM shadow. Falls back to the window rect. Physical pixels.</summary>
    static void FrameOf(IntPtr h, out RECT rc)
    {
        if (DwmGetWindowAttribute(h, 9, out rc, Marshal.SizeOf<RECT>()) == 0 && rc.Right > rc.Left && rc.Bottom > rc.Top) return;
        GetWindowRect(h, out rc);
    }

    /// <summary>
    /// Windows under a crosshair. Includes owned popups so the owner can be resolved, and marks cloaked, shadow, and our own process.
    /// Demands the window gate. Tests never call this.
    /// </summary>
    public static IReadOnlyList<PickProbe> ReadProbes()
    {
        Attempts++;
        WindowGate.Demand();
        NativeCalls++;
        uint self = (uint)Environment.ProcessId;
        var found = new List<IntPtr>();
        EnumWindows((h, _) => { found.Add(h); return true; }, IntPtr.Zero);
        var list = new List<PickProbe>(found.Count);
        int z = found.Count;
        var origin = PrimaryWorkOrigin();
        foreach (var h in found)
        {
            try
            {
                GetWindowThreadProcessId(h, out uint pid);
                string cls = ClassOf(h);
                bool shadow = cls.Contains("Shadow", StringComparison.OrdinalIgnoreCase);
                bool cloaked = DwmGetWindowAttribute(h, 14, out int cloak, 4) == 0 && cloak != 0;
                FrameOf(h, out var rc);
                ImageOf(pid, out string process, out string path);
                IntPtr owner = GetAncestor(h, 3);
                long ownerId = owner != IntPtr.Zero && owner != h ? owner.ToInt64() : 0;
                list.Add(new PickProbe
                {
                    Visible = IsWindowVisible(h),
                    Cloaked = cloaked,
                    Own = pid == self,
                    Shadow = shadow,
                    Tool = (GetWindowLongPtr(h, -20).ToInt64() & 0x80) != 0 || cls.Contains("tooltips", StringComparison.OrdinalIgnoreCase),
                    OwnerId = ownerId,
                    Z = z,
                    Window = new TrackedWindow
                    {
                        Id = h.ToInt64(),
                        Process = process,
                        Path = path,
                        Title = TextOf(h),
                        Class = cls,
                        X = rc.Left, Y = rc.Top, W = Math.Max(0, rc.Right - rc.Left), H = Math.Max(0, rc.Bottom - rc.Top),
                        Max = IsZoomed(h),
                        Min = IsIconic(h),
                        NormalX = rc.Left, NormalY = rc.Top, NormalW = Math.Max(0, rc.Right - rc.Left), NormalH = Math.Max(0, rc.Bottom - rc.Top)
                    }
                });
                if (list[^1].Window.Max || list[^1].Window.Min)
                {
                    var place = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
                    if (GetWindowPlacement(h, ref place) && place.normal.Right > place.normal.Left && place.normal.Bottom > place.normal.Top)
                    {
                        var normal = PlacementSpace.ToScreen(new PlacementSpace.Box(place.normal.Left, place.normal.Top, place.normal.Right - place.normal.Left, place.normal.Bottom - place.normal.Top), origin.X, origin.Y);
                        list[^1].Window.NormalX = normal.X;
                        list[^1].Window.NormalY = normal.Y;
                        list[^1].Window.NormalW = normal.W;
                        list[^1].Window.NormalH = normal.H;
                    }
                }
            }
            catch { }
            z--;
        }
        return list;
    }

    static string TextOf(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        var sb = new StringBuilder(Math.Max(n + 1, 2));
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static bool WorkOf(IntPtr monitor, out WorkRect area)
    {
        area = default;
        if (monitor == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi)) return false;
        area = new WorkRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
        return area.W > 0 && area.H > 0;
    }

    static bool WorkOfId(string id, out WorkRect area)
    {
        area = default;
        bool found = false;
        WorkRect hit = default;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, ref rc, _) =>
        {
            var mx = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoEx(m, ref mx)) return true;
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(mx.device, 0, ref dd, 0)) return true;
            var parts = (dd.DeviceID ?? "").Split('\\');
            string hardware = parts.Length > 1 ? parts[1] : "";
            if (!string.Equals(hardware, id, StringComparison.OrdinalIgnoreCase)) return true;
            hit = new WorkRect(mx.rcWork.Left, mx.rcWork.Top, mx.rcWork.Right - mx.rcWork.Left, mx.rcWork.Bottom - mx.rcWork.Top);
            found = true;
            return false;
        }, IntPtr.Zero);
        area = hit;
        return found;
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    delegate bool MonProc(IntPtr m, IntPtr dc, ref RECT rc, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT rc, int size);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT place);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT place);
    [StructLayout(LayoutKind.Sequential)] struct POINTL { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPLACEMENT { public uint length, flags, showCmd; public POINTL min, max; public RECT normal; }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoEx(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, uint num, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, int flags, StringBuilder s, ref int n);
}
