using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace BongHub;

/// <summary>
/// Monitor preset planning. This file records commands and brightness targets.
/// The live switch and brightness calls live in <c>DisplayNative</c> and run only after
/// <see cref="DisplayGate.Demand"/> from a normal-mode click.
/// </summary>
public sealed class MonitorPreset
{
    public string Name = "";
    public string Hotkey = "";
    public List<string> Enable = new();
    public List<string> Disable = new();
    public string Primary = "";
    public string Layout = "";
    public List<BrightTarget> Brightness = new();
    public string DeskLayout = "";
    public string Workspace = "";
}

public sealed class BrightTarget
{
    public string Monitor = "";
    public int Value = -1;
}

public sealed class LinkedDesk
{
    public string Id = "";
    public string Name = "";
}

public sealed class DeskLinkResult
{
    public bool Ok;
    public string Error = "";
    public int Moved;
    public string Summary() => Ok ? "아이콘 " + Moved + "개 위치 복원" : Error;
}

public sealed class LinkedWork
{
    public string Id = "";
    public string Name = "";
}

/// <summary>One display command string. The fake records it. The real backend is not in this file.</summary>
public interface IDisplayBackend
{
    void Apply(IReadOnlyList<string> commands);
}

public interface IBrightBackend
{
    List<string> Monitors();
    bool Set(string id, int pct, out string how);
}

/// <summary>Unset <see cref="Allow"/> means blocked, so a test host that only compiles this file cannot change a display.</summary>
public static class DisplayGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm() { if (Allow?.Invoke() == true) Armed = true; }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 모니터와 밝기를 바꾸지 않아요.");
    }
}

/// <summary>
/// Counters for the real display path. A call that throws in <see cref="DisplayGate.Demand"/>
/// leaves the native counters at zero.
/// </summary>
public static class DisplaySystem
{
    public static int Attempts, NativeCalls, MmtStarts;
    public static int ChangeDisplaySettingsExCalls, SetDisplayConfigCalls;
    public static int BrightnessAttempts, BrightnessCalls, DdcCalls;

    public static void Touch()
    {
        Attempts++;
        DisplayGate.Demand();
        NativeCalls++;
    }

    public static void NoteMmt()
    {
        DisplayGate.Demand();
        MmtStarts++;
    }

    public static void NoteDdc()
    {
        DisplayGate.Demand();
        DdcCalls++;
    }
}

public static class DisplaySink
{
    public static Action<IReadOnlyList<string>>? Commit;
}

public delegate bool BrightSet(string id, int pct, out string how);

public static class BrightSink
{
    public static Func<List<string>>? Monitors;
    public static BrightSet? Set;
}

public readonly record struct ProfileBox(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

public sealed class ProfileWindow
{
    public string Process = "";
    public string Title = "";
    public string Class = "";
    public int X, Y, W = 96, H = 64;
}

public sealed class ProfileRecord
{
    public string Name = "";
    public string Icons = "";
    public List<ProfileWindow> Windows = new();
}

/// <summary>Monitor-profile geometry. Drag positions snap to a grid and stay inside the monitor. No window API.</summary>
public static class ProfileLayout
{
    public static ProfileBox Snap(ProfileBox window, ProfileBox monitor, int grid)
    {
        int g = grid < 1 ? 1 : grid;
        int x = monitor.X + (int)Math.Round((window.X - monitor.X) / (double)g) * g;
        int y = monitor.Y + (int)Math.Round((window.Y - monitor.Y) / (double)g) * g;
        int w = Math.Max(g, window.W);
        int h = Math.Max(g, window.H);
        if (x < monitor.X) x = monitor.X;
        if (y < monitor.Y) y = monitor.Y;
        if (x + w > monitor.Right) x = Math.Max(monitor.X, monitor.Right - w);
        if (y + h > monitor.Bottom) y = Math.Max(monitor.Y, monitor.Bottom - h);
        return new ProfileBox(x, y, Math.Min(w, monitor.W), Math.Min(h, monitor.H));
    }

    /// <summary>Maps a window from monitor pixels into the on-screen graphic, then snaps it inside that graphic.</summary>
    public static ProfileBox Fit(ProfileBox window, ProfileBox source, ProfileBox canvas)
    {
        if (source.W < 1 || source.H < 1) return Snap(window, canvas, 8);
        double sx = canvas.W / (double)source.W;
        double sy = canvas.H / (double)source.H;
        int x = canvas.X + (int)Math.Round((window.X - source.X) * sx);
        int y = canvas.Y + (int)Math.Round((window.Y - source.Y) * sy);
        int w = Math.Max(8, (int)Math.Round(window.W * sx));
        int h = Math.Max(8, (int)Math.Round(window.H * sy));
        return Snap(new ProfileBox(x, y, w, h), canvas, 8);
    }
}

/// <summary>Additive profile file. It never rewrites <c>config.json</c>.</summary>
public static class ProfileCodec
{
    public static string ToJson(IEnumerable<ProfileRecord> profiles)
    {
        var arr = new JsonArray();
        foreach (var p in profiles)
        {
            var wins = new JsonArray();
            foreach (var w in p.Windows)
                wins.Add(new JsonObject
                {
                    ["process"] = w.Process ?? "",
                    ["title"] = w.Title ?? "",
                    ["class"] = w.Class ?? "",
                    ["x"] = w.X,
                    ["y"] = w.Y,
                    ["w"] = w.W,
                    ["h"] = w.H
                });
            arr.Add(new JsonObject { ["name"] = p.Name ?? "", ["icons"] = p.Icons ?? "", ["windows"] = wins });
        }
        return new JsonObject { ["profiles"] = arr }.ToJsonString();
    }

    public static List<ProfileRecord> Parse(string? json)
    {
        var list = new List<ProfileRecord>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["profiles"] is not JsonArray arr) return list;
            foreach (var node in arr.OfType<JsonObject>())
            {
                var rec = new ProfileRecord { Name = node["name"]?.GetValue<string>() ?? "", Icons = node["icons"]?.GetValue<string>() ?? "" };
                if (node["windows"] is JsonArray wins)
                    foreach (var item in wins.OfType<JsonObject>())
                        rec.Windows.Add(new ProfileWindow
                        {
                            Process = item["process"]?.GetValue<string>() ?? "",
                            Title = item["title"]?.GetValue<string>() ?? "",
                            Class = item["class"]?.GetValue<string>() ?? "",
                            X = item["x"]?.GetValue<int>() ?? 0,
                            Y = item["y"]?.GetValue<int>() ?? 0,
                            W = item["w"]?.GetValue<int>() ?? 96,
                            H = item["h"]?.GetValue<int>() ?? 64
                        });
                list.Add(rec);
            }
        }
        catch { }
        return list;
    }
}

public static class ProfileCloud
{
    public static void Attach(CloudWorkspace ws, string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            string text = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(text)) ws.ProfilesJson = text;
        }
        catch { }
    }
}

public sealed class ProfileMonitor
{
    public string Id = "";
    /// <summary>Windows Display Settings number, the same one Identify shows.</summary>
    public int Number = 1;
    public int X, Y, W = 1920, H = 1080;
    /// <summary>Work area in the same pixels as <see cref="X"/>. Zero size means the full monitor, for profiles saved before the taskbar was recorded.</summary>
    public int WorkX, WorkY, WorkW, WorkH;
    public bool Primary = true;
    public bool Enabled = true;
    public string Device = "";
    public string Path = "";
    public string Edid = "";
    /// <summary>Windows scaling percent. 100 is 96 DPI. 125 is the owner's mixed desk.</summary>
    public int Scale = 100;
    /// <summary>0, 90, 180, or 270. Matches the display's current orientation.</summary>
    public int Orientation;
    /// <summary>Runtime. False when this saved screen is not in the current display list.</summary>
    public bool Connected = true;
    public ProfileBox Box => new(X, Y, W, H);
    /// <summary>Snap target. Half, maximize, quarter, and dock-flush use this, not the full monitor.</summary>
    public ProfileBox Work => WorkW > 8 && WorkH > 8 ? new ProfileBox(WorkX, WorkY, WorkW, WorkH) : Box;
}

public sealed class ProfileApp
{
    public string Id = "";
    public string Process = "";
    public string Path = "";
    public string Title = "";
    public string Class = "";
    public string Name = "";
    public int Monitor = 1;
    public int X, Y, W = 800, H = 600;
    public string State = "normal";
    /// <summary>Pin above other windows when the profile is applied.</summary>
    public bool TopMost;
    /// <summary>Start <see cref="Path"/> when the window is not already open. Off unless the user asks.</summary>
    public bool Launch;
}

public sealed class ProfileIcon
{
    public string Name = "";
    public int X, Y;
    /// <summary>False hides the icon in the preview and skips it on restore. Missing JSON means shown.</summary>
    public bool Show = true;
}

/// <summary>One monitor profile. Extra fields sit beside the 3.4.x preset values and never replace config.json.</summary>
public sealed class MonitorProfile
{
    public string Id = "";
    public string Name = "";
    public string Hotkey = "";
    public string Primary = "";
    public string Layout = "";
    public string DeskLayout = "";
    public string Workspace = "";
    public string IconsSaved = "";
    public bool RestoreIcons = true;
    public List<string> Enable = new();
    public List<string> Disable = new();
    public List<BrightTarget> Brightness = new();
    public List<ProfileMonitor> Monitors = new();
    public List<ProfileApp> Apps = new();
    public List<ProfileIcon> Icons = new();
    /// <summary>1.1.0 files stored window rects in the 320×180 editor, not in monitor pixels.</summary>
    public bool LiftCanvas;
    /// <summary>Snapshot every visible window when leaving this profile. Missing JSON means on.</summary>
    public bool RememberWindows = true;
    public string SessionSaved = "";
    public List<SessionWindow> Session = new();
    /// <summary><c>LVM_GETORIGIN</c> captured with the icons. Missing JSON means zero.</summary>
    public int IconOriginX, IconOriginY;
}

public readonly record struct SnapPreview(string Kind, ProfileBox Box);

public readonly record struct ProfileScene(double Scale, int OriginX, int OriginY, ProfileBox Bounds);

/// <summary>The shared edge the pointer can cross after a monitor snap. Empty when nothing snapped.</summary>
public readonly record struct SnapGuide(int X1, int Y1, int X2, int Y2, int X3 = 0, int Y3 = 0, int X4 = 0, int Y4 = 0)
{
    public bool Active => X1 != X2 || Y1 != Y2;
    public bool Corner => X3 != X4 || Y3 != Y4;
}

public readonly record struct MonitorSnap(ProfileBox Box, SnapGuide Guide);

/// <summary>Monitor-profile geometry. Nothing here talks to a display, a window, or the desktop.</summary>
public static class ProfileSnap
{
    public const int Edge = 28;
    public const int Grid = 8;
    /// <summary>Window-to-window attraction. A few physical pixels, so tiles do not jump across a gap.</summary>
    public const int WindowSnap = 4;
    /// <summary>How deep a half / maximize zone reaches into the monitor, as a percent of that side.</summary>
    /// <summary>Half, maximize, and quarter only inside this thin edge band. The rest of an edge keeps the window's size and docks flush.</summary>
    public const int ZonePercent = 6;
    /// <summary>Window-to-monitor-edge magnet, in virtual pixels. A couple of canvas pixels at most.</summary>
    public const int MonitorEdgeSnap = 2;

    /// <summary>Edge magnet in virtual pixels for a canvas scale. Never more than <see cref="MonitorEdgeSnap"/>.</summary>
    public static int EdgeSlop(double canvasScale)
    {
        if (canvasScale < 0.01 || double.IsNaN(canvasScale) || double.IsInfinity(canvasScale)) canvasScale = 0.01;
        int seen = (int)Math.Round(2.5 * canvasScale);
        return Math.Min(MonitorEdgeSnap, Math.Max(0, seen));
    }

    /// <summary>Half, max, and quarter apply only after the pointer enters that zone during a drag. A click that starts inside one does not stick.</summary>
    public static bool ArmZone(string? startedIn, string? now, bool moved)
    {
        if (!moved || string.IsNullOrEmpty(now)) return false;
        if (!string.IsNullOrEmpty(startedIn) && startedIn == now) return false;
        return true;
    }

    public static ProfileBox SnapMonitor(ProfileBox moving, IReadOnlyList<ProfileBox> others, int threshold)
        => SnapMonitorWithGuide(moving, others, threshold).Box;

    /// <summary>
    /// Pulls a monitor onto a neighbor so the touching edges are the same line and overlap by more than a point.
    /// A gap or an overlap is not a snap. The guide is that shared segment.
    /// </summary>
    public static MonitorSnap SnapMonitorWithGuide(ProfileBox moving, IReadOnlyList<ProfileBox> others, int threshold)
    {
        int x = moving.X, y = moving.Y;
        SnapGuide guide = default;
        int best = threshold < 0 ? 0 : threshold + 1;
        int bestShare = -1;
        if (others != null && threshold >= 0)
        {
            for (int i = 0; i < others.Count; i++)
            {
                var o = others[i];
                ConsiderX(o.Right, o, i, true);
                ConsiderX(o.X - moving.W, o, i, false);
                ConsiderY(o.Bottom, o, i, true);
                ConsiderY(o.Y - moving.H, o, i, false);
            }
        }
        bool didX = x != moving.X, didY = y != moving.Y;
        if (others != null && threshold >= 0 && didX != didY)
            guide = SnapMissingAxis(moving, others, threshold, ref x, ref y, didX, guide);
        didX = x != moving.X;
        didY = y != moving.Y;
        if (didX && didY && guide.Active && !guide.Corner)
        {
            if (guide.Y1 == guide.Y2)
                guide = new SnapGuide(guide.X1, guide.Y1, guide.X2, guide.Y2, x, Math.Min(y, moving.Y), x, Math.Max(y, moving.Y) + 1);
            else
                guide = new SnapGuide(guide.X1, guide.Y1, guide.X2, guide.Y2, Math.Min(x, moving.X), y, Math.Max(x, moving.X) + 1, y);
        }
        return new MonitorSnap(new ProfileBox(x, y, moving.W, moving.H), guide);

        void ConsiderX(int targetX, ProfileBox o, int index, bool movingOnRight)
        {
            int dist = Math.Abs(targetX - moving.X);
            if (dist > threshold || dist > best) return;
            int ny = AlignEdge(moving.Y, moving.H, o.Y, o.H, threshold);
            int share = OverlapLen(ny, moving.H, o.Y, o.H);
            if (share <= 0) return;
            if (dist == best && share <= bestShare) return;
            var candidate = new ProfileBox(targetX, ny, moving.W, moving.H);
            if (InteriorsOverlapAny(candidate, others, index)) return;
            best = dist;
            bestShare = share;
            x = targetX;
            y = ny;
            int top = Math.Max(ny, o.Y);
            int bot = Math.Min(ny + moving.H, o.Bottom);
            int gx = movingOnRight ? o.Right : o.X;
            guide = new SnapGuide(gx, top, gx, bot);
        }

        void ConsiderY(int targetY, ProfileBox o, int index, bool movingBelow)
        {
            int dist = Math.Abs(targetY - moving.Y);
            if (dist > threshold || dist > best) return;
            int nx = AlignEdge(moving.X, moving.W, o.X, o.W, threshold);
            int share = OverlapLen(nx, moving.W, o.X, o.W);
            if (share <= 0) return;
            if (dist == best && share <= bestShare) return;
            var candidate = new ProfileBox(nx, targetY, moving.W, moving.H);
            if (InteriorsOverlapAny(candidate, others, index)) return;
            best = dist;
            bestShare = share;
            x = nx;
            y = targetY;
            int left = Math.Max(nx, o.X);
            int right = Math.Min(nx + moving.W, o.Right);
            int gy = movingBelow ? o.Bottom : o.Y;
            guide = new SnapGuide(left, gy, right, gy);
        }
    }

    static SnapGuide SnapMissingAxis(ProfileBox moving, IReadOnlyList<ProfileBox> others, int threshold, ref int x, ref int y, bool haveX, SnapGuide guide)
    {
        int ax = x, ay = y;
        int best = threshold + 1;
        int pick = haveX ? ay : ax;
        int gx1 = 0, gy1 = 0, gx2 = 0, gy2 = 0;
        bool found = false;
        for (int i = 0; i < others.Count; i++)
        {
            var o = others[i];
            if (haveX)
            {
                TryY(o.Bottom, o.X, o.Right);
                TryY(o.Y - moving.H, o.X, o.Right);
                TryY(o.Y, o.X, o.Right);
                TryY(o.Bottom - moving.H, o.X, o.Right);
            }
            else
            {
                TryX(o.Right, o.Y, o.Bottom);
                TryX(o.X - moving.W, o.Y, o.Bottom);
                TryX(o.X, o.Y, o.Bottom);
                TryX(o.Right - moving.W, o.Y, o.Bottom);
            }
        }
        if (!found) return guide;
        if (haveX) y = pick;
        else x = pick;
        return new SnapGuide(guide.X1, guide.Y1, guide.X2, guide.Y2, gx1, gy1, gx2, gy2);

        void TryY(int targetY, int left, int right)
        {
            int dist = Math.Abs(targetY - moving.Y);
            if (dist > threshold || dist >= best) return;
            var box = new ProfileBox(ax, targetY, moving.W, moving.H);
            if (InteriorsOverlapAny(box, others, -1)) return;
            best = dist;
            pick = targetY;
            found = true;
            int a = Math.Max(ax, left), b = Math.Min(ax + moving.W, right);
            if (a >= b) b = a + 1;
            gx1 = a; gx2 = b; gy1 = gy2 = targetY;
        }

        void TryX(int targetX, int top, int bot)
        {
            int dist = Math.Abs(targetX - moving.X);
            if (dist > threshold || dist >= best) return;
            var box = new ProfileBox(targetX, ay, moving.W, moving.H);
            if (InteriorsOverlapAny(box, others, -1)) return;
            best = dist;
            pick = targetX;
            found = true;
            int a = Math.Max(ay, top), b = Math.Min(ay + moving.H, bot);
            if (a >= b) b = a + 1;
            gy1 = a; gy2 = b; gx1 = gx2 = targetX;
        }
    }

    static int AlignEdge(int pos, int size, int otherPos, int otherSize, int threshold)
    {
        int best = threshold + 1;
        int y = pos;
        void Take(int candidate)
        {
            int d = Math.Abs(candidate - pos);
            if (d <= threshold && d < best) { best = d; y = candidate; }
        }
        Take(otherPos);
        Take(otherPos + otherSize - size);
        return y;
    }

    static int OverlapLen(int a, int aSize, int b, int bSize)
    {
        int start = Math.Max(a, b);
        int end = Math.Min(a + aSize, b + bSize);
        return end - start;
    }

    static bool InteriorsOverlap(ProfileBox a, ProfileBox b)
    {
        int w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        int h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0;
    }

    static bool InteriorsOverlapAny(ProfileBox box, IReadOnlyList<ProfileBox> others, int ignore)
    {
        for (int i = 0; i < others.Count; i++)
            if (i != ignore && InteriorsOverlap(box, others[i])) return true;
        return false;
    }

    /// <summary>Depth of a snap zone on one side. At least <see cref="Edge"/>, otherwise a slice of the monitor.</summary>
    public static int ZoneDepth(int span)
    {
        if (span < 1) span = 1;
        int slice = span * ZonePercent / 100;
        return Math.Max(Edge, slice);
    }

    public static ProfileBox ZoneBox(string kind, ProfileBox monitor)
    {
        int hw = Math.Max(1, monitor.W / 2);
        int hh = Math.Max(1, monitor.H / 2);
        return kind switch
        {
            "left" => new ProfileBox(monitor.X, monitor.Y, hw, monitor.H),
            "right" => new ProfileBox(monitor.X + hw, monitor.Y, monitor.W - hw, monitor.H),
            "top" => new ProfileBox(monitor.X, monitor.Y, monitor.W, monitor.H),
            "tl" => new ProfileBox(monitor.X, monitor.Y, hw, hh),
            "tr" => new ProfileBox(monitor.X + hw, monitor.Y, monitor.W - hw, hh),
            "bl" => new ProfileBox(monitor.X, monitor.Y + hh, hw, monitor.H - hh),
            "br" => new ProfileBox(monitor.X + hw, monitor.Y + hh, monitor.W - hw, monitor.H - hh),
            _ => default
        };
    }

    public static SnapPreview PreviewAt(int x, int y, ProfileBox monitor)
    {
        int zx = ZoneDepth(monitor.W);
        int zy = ZoneDepth(monitor.H);
        int dxL = x - monitor.X, dxR = monitor.Right - x, dyT = y - monitor.Y, dyB = monitor.Bottom - y;
        bool left = dxL >= 0 && dxL <= zx;
        bool right = dxR >= 0 && dxR <= zx;
        bool top = dyT >= 0 && dyT <= zy;
        bool bottom = dyB >= 0 && dyB <= zy;
        string kind = "";
        if (top && left) kind = "tl";
        else if (top && right) kind = "tr";
        else if (bottom && left) kind = "bl";
        else if (bottom && right) kind = "br";
        else if (top) kind = "top";
        else if (left) kind = "left";
        else if (right) kind = "right";
        if (kind.Length == 0) return new SnapPreview("", default);
        return new SnapPreview(kind, ZoneBox(kind, monitor));
    }

    public static ProfileBox Resize(ProfileBox window, ProfileBox monitor, string edge, int dx, int dy, int min)
    {
        int x = window.X, y = window.Y, w = window.W, h = window.H;
        string e = edge ?? "";
        if (e.Contains('w')) { x += dx; w -= dx; }
        if (e.Contains('e')) w += dx;
        if (e.Contains('n')) { y += dy; h -= dy; }
        if (e.Contains('s')) h += dy;
        if (w < min) { if (e.Contains('w')) x -= min - w; w = min; }
        if (h < min) { if (e.Contains('n')) y -= min - h; h = min; }
        if (x < monitor.X) { w -= monitor.X - x; x = monitor.X; }
        if (y < monitor.Y) { h -= monitor.Y - y; y = monitor.Y; }
        if (x + w > monitor.Right) w = monitor.Right - x;
        if (y + h > monitor.Bottom) h = monitor.Bottom - y;
        return new ProfileBox(x, y, Math.Max(min, w), Math.Max(min, h));
    }

    /// <summary>Bands of the monitor that are not in the work area. Empty when no taskbar was stored.</summary>
    public static List<ProfileBox> TaskbarStrips(ProfileMonitor m)
    {
        var list = new List<ProfileBox>();
        if (m == null || m.WorkW <= 8 || m.WorkH <= 8) return list;
        var full = m.Box;
        var work = m.Work;
        if (work.Y > full.Y) list.Add(new ProfileBox(full.X, full.Y, full.W, work.Y - full.Y));
        if (work.Bottom < full.Bottom) list.Add(new ProfileBox(full.X, work.Bottom, full.W, full.Bottom - work.Bottom));
        if (work.X > full.X) list.Add(new ProfileBox(full.X, full.Y, work.X - full.X, full.H));
        if (work.Right < full.Right) list.Add(new ProfileBox(work.Right, full.Y, full.Right - work.Right, full.H));
        return list;
    }

    public static ProfileBox SnapWindow(ProfileBox window, ProfileBox monitor, IReadOnlyList<ProfileBox> others, int grid, int? edgeSlop = null)
    {
        int g = grid < 1 ? 1 : grid;
        int x = monitor.X + (int)Math.Round((window.X - monitor.X) / (double)g) * g;
        int y = monitor.Y + (int)Math.Round((window.Y - monitor.Y) / (double)g) * g;
        int w = Math.Max(g, window.W);
        int h = Math.Max(g, window.H);
        int slop = edgeSlop ?? MonitorEdgeSnap;
        x = Pull(x, monitor.X, slop);
        x = Pull(x, monitor.Right - w, slop);
        y = Pull(y, monitor.Y, slop);
        y = Pull(y, monitor.Bottom - h, slop);
        if (others != null)
            foreach (var o in others)
            {
                x = Pull(x, o.Right, WindowSnap);
                x = Pull(x, o.X - w, WindowSnap);
                y = Pull(y, o.Bottom, WindowSnap);
                y = Pull(y, o.Y - h, WindowSnap);
            }
        if (x < monitor.X) x = monitor.X;
        if (y < monitor.Y) y = monitor.Y;
        if (x + w > monitor.Right) x = Math.Max(monitor.X, monitor.Right - w);
        if (y + h > monitor.Bottom) y = Math.Max(monitor.Y, monitor.Bottom - h);
        return new ProfileBox(x, y, Math.Min(w, monitor.W), Math.Min(h, monitor.H));
    }

    static int Pull(int value, int target, int slop) => Math.Abs(value - target) <= slop ? target : value;

    public static (int Index, ProfileBox Local) Transfer(ProfileBox absolute, IReadOnlyList<ProfileBox> monitors)
    {
        if (monitors == null || monitors.Count == 0) return (0, absolute);
        int cx = absolute.X + absolute.W / 2;
        int cy = absolute.Y + absolute.H / 2;
        int index = 0;
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (cx >= m.X && cy >= m.Y && cx < m.Right && cy < m.Bottom) { index = i; break; }
        }
        var host = monitors[index];
        return (index, new ProfileBox(absolute.X - host.X, absolute.Y - host.Y, absolute.W, absolute.H));
    }

    public static ProfileScene Fit(IReadOnlyList<ProfileBox> monitors, int canvasW, int canvasH, int pad)
    {
        if (monitors == null || monitors.Count == 0) return new ProfileScene(1, pad, pad, new ProfileBox(0, 0, 1, 1));
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int maxX = monitors.Max(m => m.Right);
        int maxY = monitors.Max(m => m.Bottom);
        int bw = Math.Max(1, maxX - minX);
        int bh = Math.Max(1, maxY - minY);
        double s = Math.Min((canvasW - pad * 2) / (double)bw, (canvasH - pad * 2) / (double)bh);
        if (s <= 0 || double.IsInfinity(s)) s = 0.1;
        return new ProfileScene(s, pad, pad, new ProfileBox(minX, minY, bw, bh));
    }

    public static ProfileBox ToCanvas(ProfileBox box, ProfileScene scene, int minW = 28, int minH = 22)
    {
        var p = ToPoint(box.X, box.Y, scene);
        int w = Math.Max(minW, (int)Math.Round(box.W * scene.Scale));
        int h = Math.Max(minH, (int)Math.Round(box.H * scene.Scale));
        return new ProfileBox(p.X, p.Y, w, h);
    }

    public static (int X, int Y) ToPoint(int x, int y, ProfileScene scene)
        => (scene.OriginX + (int)Math.Round((x - scene.Bounds.X) * scene.Scale),
            scene.OriginY + (int)Math.Round((y - scene.Bounds.Y) * scene.Scale));

    /// <summary>Height of the scaled arrangement. A wide strip stays short instead of leaving an empty band.</summary>
    public static int ContentHeight(IReadOnlyList<ProfileBox> monitors, int width, int pad)
    {
        var scene = Fit(monitors, Math.Max(pad * 2 + 1, width), 10000, pad);
        int h = pad * 2 + (int)Math.Ceiling(scene.Bounds.H * scene.Scale);
        return Math.Max(72, h);
    }
}

/// <summary>1.1.0 editor: a 320×180 graphic at (20,10) stood for a 1920×1080 monitor. Window rects in that file are canvas units.</summary>
public static class ProfileLegacy
{
    public static readonly ProfileBox Canvas = new(20, 10, 320, 180);

    public static bool InCanvas(ProfileApp app)
        => app.W > 0 && app.H > 0 && app.W <= Canvas.W && app.H <= Canvas.H && app.X >= 0 && app.X < Canvas.X + Canvas.W + 48 && app.Y >= 0 && app.Y < Canvas.Y + Canvas.H + 48;

    public static ProfileBox ToMonitor(ProfileBox canvas, ProfileBox monitor)
    {
        double sx = monitor.W / (double)Canvas.W;
        double sy = monitor.H / (double)Canvas.H;
        int x = (int)Math.Round((canvas.X - Canvas.X) * sx);
        int y = (int)Math.Round((canvas.Y - Canvas.Y) * sy);
        int w = Math.Max(1, (int)Math.Round(canvas.W * sx));
        int h = Math.Max(1, (int)Math.Round(canvas.H * sy));
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x + w > monitor.W) x = Math.Max(0, monitor.W - w);
        if (y + h > monitor.H) y = Math.Max(0, monitor.H - h);
        return new ProfileBox(x, y, Math.Min(w, monitor.W), Math.Min(h, monitor.H));
    }
}

/// <summary>One live display. <see cref="Number"/> is the Windows Identify number from <c>\\.\DISPLAYn</c>.</summary>
public sealed class ScreenInfo
{
    public int Number;
    public int W, H, X, Y;
    public bool Primary;
    public string Device = "";
    public string Path = "";
    public string Edid = "";
    /// <summary>Scaling percent. 100 is 96 DPI. 0 means the caller did not say, and readers treat it as 100.</summary>
    public int Scale = 100;
    /// <summary>0, 90, 180, or 270.</summary>
    public int Orientation;
    /// <summary>Work area in screen pixels. Zero size means the caller did not measure a taskbar.</summary>
    public int WorkX, WorkY, WorkW, WorkH;
}

/// <summary>Binds saved monitors to physical screens. No display API.</summary>
public static class ScreenBind
{
    public static int WindowsNumber(string? device)
    {
        if (string.IsNullOrEmpty(device)) return 0;
        int i = device.Length;
        while (i > 0 && char.IsDigit(device[i - 1])) i--;
        if (i >= device.Length) return 0;
        return int.TryParse(device[i..], out int n) ? n : 0;
    }

    public static int Score(ProfileMonitor saved, ScreenInfo live)
    {
        int score = 0;
        if (saved.Path.Length > 0 && string.Equals(saved.Path, live.Path, StringComparison.OrdinalIgnoreCase)) score += 1_000_000;
        if (saved.Edid.Length > 0 && string.Equals(saved.Edid, live.Edid, StringComparison.OrdinalIgnoreCase)) score += 100_000;
        if (saved.W == live.W && saved.H == live.H) score += 10_000;
        int dist = Math.Abs(saved.X - live.X) + Math.Abs(saved.Y - live.Y);
        score += Math.Max(0, 5_000 - dist / 4);
        return score;
    }

    public static ScreenInfo? Best(ProfileMonitor saved, IReadOnlyList<ScreenInfo> live, ISet<int> used, int minimum = 0)
    {
        ScreenInfo? pick = null;
        int best = int.MinValue;
        foreach (var s in live)
        {
            if (used.Contains(s.Number)) continue;
            int score = Score(saved, s);
            if (score < minimum) continue;
            if (pick == null || score > best) { pick = s; best = score; }
        }
        return pick;
    }

    static void Retarget(MonitorProfile profile, Dictionary<ProfileMonitor, int> oldNumbers)
    {
        foreach (var app in profile.Apps)
        {
            var owner = profile.Monitors.FirstOrDefault(m => oldNumbers.TryGetValue(m, out int n) && n == app.Monitor);
            if (owner != null) app.Monitor = owner.Number;
        }
    }

    static void Remember(Dictionary<ProfileMonitor, int> old, ProfileMonitor m) { if (!old.ContainsKey(m)) old[m] = m.Number; }

    public static void Adopt(MonitorProfile profile, IReadOnlyList<ScreenInfo>? live)
    {
        if (live == null || live.Count == 0)
        {
            foreach (var m in profile.Monitors) m.Connected = true;
            return;
        }
        var old = new Dictionary<ProfileMonitor, int>();
        foreach (var m in profile.Monitors) Remember(old, m);
        var used = new HashSet<int>();
        foreach (var m in profile.Monitors)
        {
            var hit = Best(m, live, used, 10_000);
            if (hit == null) { m.Connected = false; continue; }
            used.Add(hit.Number);
            m.Connected = true;
            m.Number = hit.Number;
            m.Device = hit.Device ?? "";
            m.Path = hit.Path ?? "";
            m.Edid = hit.Edid ?? "";
            m.W = hit.W;
            m.H = hit.H;
            ApplyWork(m, hit);
            string hitPath = hit.Path ?? "";
            string hitDevice = hit.Device ?? "";
            if ((m.Id ?? "").Length == 0) m.Id = hitPath.Length > 0 ? hitPath : hitDevice;
        }
        foreach (var s in live)
        {
            if (profile.Monitors.Any(m => m.Connected && m.Number == s.Number)) continue;
            var added = new ProfileMonitor
            {
                Id = (s.Path ?? "").Length > 0 ? s.Path ?? "" : s.Device ?? "",
                Number = s.Number,
                X = s.X, Y = s.Y, W = s.W, H = s.H,
                WorkX = s.WorkW > 8 ? s.WorkX : s.X, WorkY = s.WorkH > 8 ? s.WorkY : s.Y,
                WorkW = s.WorkW > 8 ? s.WorkW : s.W, WorkH = s.WorkH > 8 ? s.WorkH : s.H,
                Primary = s.Primary, Enabled = true, Connected = true,
                Device = s.Device ?? "", Path = s.Path ?? "", Edid = s.Edid ?? ""
            };
            Remember(old, added);
            profile.Monitors.Add(added);
        }
        Retarget(profile, old);
        if (profile.Monitors.Count > 0 && !profile.Monitors.Any(m => m.Primary && m.Connected))
        {
            var pick = profile.Monitors.FirstOrDefault(m => m.Connected) ?? profile.Monitors[0];
            foreach (var m in profile.Monitors) m.Primary = false;
            pick.Primary = true;
        }
    }

    public static bool Mismatch(MonitorProfile profile, IReadOnlyList<ScreenInfo>? live)
    {
        if (live == null || live.Count == 0 || profile.Monitors.Count == 0) return false;
        if (profile.Monitors.Any(m => !m.Connected)) return true;
        foreach (var s in live)
            if (profile.Monitors.All(m => m.Number != s.Number)) return true;
        foreach (var m in profile.Monitors)
        {
            var hit = live.FirstOrDefault(s => s.Number == m.Number);
            if (hit == null) return true;
            if (m.W != hit.W || m.H != hit.H) return true;
            if (m.Path.Length > 0 && hit.Path.Length > 0 && !string.Equals(m.Path, hit.Path, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static void MatchCurrent(MonitorProfile profile, IReadOnlyList<ScreenInfo> live)
    {
        var old = new Dictionary<ProfileMonitor, int>();
        foreach (var m in profile.Monitors) Remember(old, m);
        var used = new HashSet<int>();
        foreach (var m in profile.Monitors)
        {
            var hit = Best(m, live, used);
            if (hit == null) { m.Connected = false; continue; }
            used.Add(hit.Number);
            m.Connected = true;
            m.Number = hit.Number;
            m.X = hit.X; m.Y = hit.Y; m.W = hit.W; m.H = hit.H;
            ApplyWork(m, hit);
            m.Primary = hit.Primary;
            m.Device = hit.Device ?? "";
            m.Path = hit.Path ?? "";
            m.Edid = hit.Edid ?? "";
            m.Enabled = m.Enabled || hit.Primary;
        }
        foreach (var s in live)
        {
            if (used.Contains(s.Number)) continue;
            var added = new ProfileMonitor
            {
                Id = (s.Path ?? "").Length > 0 ? s.Path ?? "" : s.Number.ToString(),
                Number = s.Number, X = s.X, Y = s.Y, W = s.W, H = s.H,
                WorkX = s.WorkW > 8 ? s.WorkX : s.X, WorkY = s.WorkH > 8 ? s.WorkY : s.Y,
                WorkW = s.WorkW > 8 ? s.WorkW : s.W, WorkH = s.WorkH > 8 ? s.WorkH : s.H,
                Primary = s.Primary, Enabled = true, Connected = true,
                Device = s.Device ?? "", Path = s.Path ?? "", Edid = s.Edid ?? ""
            };
            Remember(old, added);
            profile.Monitors.Add(added);
        }
        Retarget(profile, old);
        var drop = profile.Monitors.Where(m => !m.Connected).ToList();
        foreach (var gone in drop)
        {
            int previous = old.TryGetValue(gone, out int n) ? n : gone.Number;
            var keep = profile.Monitors.FirstOrDefault(m => m.Connected && !ReferenceEquals(m, gone));
            if (keep != null)
            {
                foreach (var app in profile.Apps)
                    if (app.Monitor == previous || app.Monitor == gone.Number) app.Monitor = keep.Number;
            }
            profile.Monitors.Remove(gone);
        }
    }

    /// <summary>Identity of the live desk. Empty when nothing is connected. Used to refresh only when Windows actually changes.</summary>
    public static string Signature(IReadOnlyList<ScreenInfo>? live)
    {
        if (live == null || live.Count == 0) return "";
        return string.Join("|", live.OrderBy(s => s.Number).ThenBy(s => s.X).ThenBy(s => s.Y).Select(s =>
            s.Number + "@" + s.X + "," + s.Y + " " + s.W + "x" + s.H
            + " w" + s.WorkX + "," + s.WorkY + " " + s.WorkW + "x" + s.WorkH
            + " " + (s.Scale <= 0 ? 100 : s.Scale) + "% r" + s.Orientation
            + (s.Primary ? "*" : "") + " " + (s.Device ?? "")));
    }

    /// <summary>
    /// Copies the live Windows arrangement onto the profile: same screens, positions, sizes, scaling, orientation, and Identify numbers.
    /// A saved screen that is not connected stays in the list and is marked disconnected. Nothing is deleted.
    /// Placeholder rows (the 1920 strip from an old preset) are replaced, because they are not a real desk.
    /// </summary>
    public static void FollowLive(MonitorProfile profile, IReadOnlyList<ScreenInfo>? live)
    {
        if (profile == null || live == null || live.Count == 0 || profile.Monitors.Count == 0) return;
        bool blank = profile.Monitors.All(m => (m.Path ?? "").Length == 0 && (m.Device ?? "").Length == 0 && (m.Edid ?? "").Length == 0);
        if (blank || ProfileDoc.IsPlaceholderRow(profile.Monitors))
        {
            profile.Monitors.Clear();
            foreach (var s in live) profile.Monitors.Add(FromScreen(s));
            EnsurePrimary(profile);
            foreach (var app in profile.Apps)
                if (profile.Monitors.All(m => m.Number != app.Monitor))
                    app.Monitor = (profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors[0]).Number;
            ProfileDoc.SyncLists(profile);
            return;
        }
        var old = new Dictionary<ProfileMonitor, int>();
        foreach (var m in profile.Monitors) Remember(old, m);
        var used = new HashSet<int>();
        foreach (var m in profile.Monitors)
        {
            var hit = Best(m, live, used, 10_000);
            if (hit == null) { m.Connected = false; continue; }
            used.Add(hit.Number);
            CopyLive(m, hit);
        }
        foreach (var s in live)
        {
            if (used.Contains(s.Number)) continue;
            var added = FromScreen(s);
            Remember(old, added);
            profile.Monitors.Add(added);
            used.Add(s.Number);
        }
        Retarget(profile, old);
        EnsurePrimary(profile);
        ProfileDoc.SyncLists(profile);
    }

    static void ApplyWork(ProfileMonitor m, ScreenInfo s)
    {
        if (s.WorkW > 8 && s.WorkH > 8)
        {
            m.WorkX = s.WorkX;
            m.WorkY = s.WorkY;
            m.WorkW = s.WorkW;
            m.WorkH = s.WorkH;
            return;
        }
        m.WorkX = s.X;
        m.WorkY = s.Y;
        m.WorkW = s.W;
        m.WorkH = s.H;
    }

    static void CopyLive(ProfileMonitor m, ScreenInfo hit)
    {
        m.Connected = true;
        m.Number = hit.Number;
        m.X = hit.X;
        m.Y = hit.Y;
        m.W = hit.W;
        m.H = hit.H;
        ApplyWork(m, hit);
        m.Scale = hit.Scale <= 0 ? 100 : hit.Scale;
        m.Orientation = hit.Orientation;
        m.Primary = hit.Primary;
        m.Device = hit.Device ?? "";
        m.Path = hit.Path ?? "";
        m.Edid = hit.Edid ?? "";
        if ((m.Id ?? "").Length == 0) m.Id = m.Path.Length > 0 ? m.Path : (m.Device.Length > 0 ? m.Device : hit.Number.ToString());
    }

    static ProfileMonitor FromScreen(ScreenInfo s)
    {
        string path = s.Path ?? "";
        string device = s.Device ?? "";
        return new ProfileMonitor
        {
            Id = path.Length > 0 ? path : (device.Length > 0 ? device : s.Number.ToString()),
            Number = s.Number,
            X = s.X, Y = s.Y, W = s.W, H = s.H,
            WorkX = s.WorkW > 8 ? s.WorkX : s.X, WorkY = s.WorkH > 8 ? s.WorkY : s.Y,
            WorkW = s.WorkW > 8 ? s.WorkW : s.W, WorkH = s.WorkH > 8 ? s.WorkH : s.H,
            Scale = s.Scale <= 0 ? 100 : s.Scale,
            Orientation = s.Orientation,
            Primary = s.Primary,
            Enabled = true,
            Connected = true,
            Device = device,
            Path = path,
            Edid = s.Edid ?? ""
        };
    }

    static void EnsurePrimary(MonitorProfile profile)
    {
        if (profile.Monitors.Count == 0) return;
        if (profile.Monitors.Any(m => m.Primary && m.Connected))
        {
            var keep = profile.Monitors.First(m => m.Primary && m.Connected);
            foreach (var m in profile.Monitors) m.Primary = ReferenceEquals(m, keep);
            return;
        }
        var pick = profile.Monitors.FirstOrDefault(m => m.Connected) ?? profile.Monitors[0];
        foreach (var m in profile.Monitors) m.Primary = false;
        pick.Primary = true;
    }
}

/// <summary>Live display list. Tests set <see cref="Source"/> or leave the gate shut, so the native read never runs.</summary>
public static class ScreenCatalog
{
    public static int Attempts, NativeCalls;
    public static Func<IReadOnlyList<ScreenInfo>>? Source;
    public static Func<IReadOnlyList<ScreenInfo>>? Live;

    public static IReadOnlyList<ScreenInfo> Current()
    {
        if (Source != null) return Source();
        if (!DisplayGate.RealAllowed || Live == null) return Array.Empty<ScreenInfo>();
        return ReadReal();
    }

    public static IReadOnlyList<ScreenInfo> ReadReal()
    {
        Attempts++;
        DisplayGate.Demand();
        NativeCalls++;
        return Live?.Invoke() ?? Array.Empty<ScreenInfo>();
    }
}

public static class ProfileDoc
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public static bool IsPlaceholderRow(IReadOnlyList<ProfileMonitor> monitors)
    {
        if (monitors == null || monitors.Count == 0) return true;
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (m.W != 1920 || m.H != 1080 || m.Y != 0 || m.X != i * 1920) return false;
        }
        return true;
    }

    public static void SeedScreens(MonitorProfile profile, IReadOnlyList<MonitorSpec>? screens)
    {
        if (screens == null || screens.Count == 0 || !IsPlaceholderRow(profile.Monitors)) return;
        string keepPrimary = profile.Primary ?? "";
        var enable = profile.Enable.ToList();
        var ordered = screens.OrderBy(s => s.Y).ThenBy(s => s.X).ToList();
        profile.Monitors.Clear();
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            string id;
            if (s.Primary && keepPrimary.Length > 0) id = keepPrimary;
            else
            {
                id = i < enable.Count ? enable[i] : (i + 1).ToString();
                if (keepPrimary.Length > 0 && string.Equals(id, keepPrimary, StringComparison.OrdinalIgnoreCase)) id = (i + 1).ToString();
            }
            profile.Monitors.Add(new ProfileMonitor { Id = id, Number = i + 1, X = s.X, Y = s.Y, W = s.W, H = s.H, Primary = s.Primary, Enabled = true });
        }
        if (!profile.Monitors.Any(m => m.Primary)) profile.Monitors[0].Primary = true;
        var prim = profile.Monitors.First(m => m.Primary);
        profile.Primary = keepPrimary.Length > 0 ? keepPrimary : prim.Id;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in profile.Monitors)
        {
            if (m.Id.Length == 0 || !ids.Add(m.Id)) m.Id = m.Number.ToString();
            ids.Add(m.Id);
        }
    }

    public static void LiftCanvasWindows(MonitorProfile profile)
    {
        if (!profile.LiftCanvas || profile.Apps.Count == 0 || profile.Monitors.Count == 0) return;
        if (!profile.Apps.All(ProfileLegacy.InCanvas)) { profile.LiftCanvas = false; return; }
        var mon = profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors[0];
        foreach (var app in profile.Apps)
        {
            var box = ProfileLegacy.ToMonitor(new ProfileBox(app.X, app.Y, app.W, app.H), new ProfileBox(0, 0, mon.W, mon.H));
            app.X = box.X;
            app.Y = box.Y;
            app.W = box.W;
            app.H = box.H;
            app.Monitor = mon.Number;
        }
        profile.LiftCanvas = false;
    }

    public static MonitorProfile FromPreset(MonitorPreset preset)
    {
        var profile = new MonitorProfile
        {
            Id = NewId(),
            Name = preset?.Name ?? "",
            Hotkey = preset?.Hotkey ?? "",
            Primary = preset?.Primary ?? "",
            Layout = preset?.Layout ?? "",
            DeskLayout = preset?.DeskLayout ?? "",
            Workspace = preset?.Workspace ?? ""
        };
        if (preset?.Enable != null) profile.Enable.AddRange(preset.Enable.Where(s => !string.IsNullOrEmpty(s)));
        if (preset?.Disable != null) profile.Disable.AddRange(preset.Disable.Where(s => !string.IsNullOrEmpty(s)));
        if (preset?.Brightness != null)
            foreach (var b in preset.Brightness)
                profile.Brightness.Add(new BrightTarget { Monitor = b.Monitor, Value = b.Value });
        int x = 0;
        int number = 1;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddMon(string id, bool enabled)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
            bool primary = string.Equals(id, profile.Primary, StringComparison.OrdinalIgnoreCase) || (profile.Primary.Length == 0 && number == 1 && enabled);
            profile.Monitors.Add(new ProfileMonitor { Id = id, Number = number, X = x, Y = 0, W = 1920, H = 1080, Primary = primary, Enabled = enabled });
            x += 1920;
            number++;
        }
        foreach (var id in profile.Enable) AddMon(id, true);
        foreach (var id in profile.Disable) AddMon(id, false);
        if (profile.Monitors.Count == 0)
            profile.Monitors.Add(new ProfileMonitor { Id = profile.Primary.Length > 0 ? profile.Primary : "1", Number = 1, W = 1920, H = 1080, Primary = true, Enabled = true });
        if (!profile.Monitors.Any(m => m.Primary)) profile.Monitors[0].Primary = true;
        var prim = profile.Monitors.First(m => m.Primary);
        profile.Primary = prim.Id;
        SyncLists(profile);
        return profile;
    }

    public static void SyncLists(MonitorProfile profile)
    {
        var prim = profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors.FirstOrDefault();
        if (prim != null)
        {
            foreach (var m in profile.Monitors) m.Primary = false;
            prim.Primary = true;
            prim.Enabled = true;
            profile.Primary = prim.Id;
        }
        profile.Enable = profile.Monitors.Where(m => m.Enabled && m.Id.Length > 0).Select(m => m.Id).ToList();
        profile.Disable = profile.Monitors.Where(m => !m.Enabled && m.Id.Length > 0).Select(m => m.Id).ToList();
    }

    public static bool SetPrimary(MonitorProfile profile, int number)
    {
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == number);
        if (mon == null) return false;
        foreach (var m in profile.Monitors) m.Primary = false;
        mon.Primary = true;
        mon.Enabled = true;
        SyncLists(profile);
        return profile.Primary == mon.Id && profile.Enable.Contains(mon.Id);
    }

    public static bool SetEnabled(MonitorProfile profile, int number, bool on)
    {
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == number);
        if (mon == null) return false;
        if (!on && (mon.Primary || profile.Monitors.Count(m => m.Enabled) <= 1)) return false;
        mon.Enabled = on;
        SyncLists(profile);
        return mon.Enabled == on;
    }

    public static ProfileApp FromRule(WindowRule rule)
    {
        string state = rule.Action == "max" ? "top" : rule.Action == "min" ? "min" : "normal";
        string name = rule.Name ?? "";
        string title = rule.Title ?? "";
        string process = rule.Process ?? "";
        return new ProfileApp
        {
            Id = NewId(),
            Process = process,
            Title = title,
            Class = rule.Class ?? "",
            Name = name.Length > 0 ? name : (title.Length > 0 ? title : process),
            Monitor = 1,
            X = rule.X,
            Y = rule.Y,
            W = rule.W > 0 ? rule.W : 800,
            H = rule.H > 0 ? rule.H : 600,
            State = state
        };
    }

    static bool SameApp(ProfileApp app, WindowRule rule)
    {
        if (rule.Process.Length > 0 && !string.Equals(app.Process, rule.Process, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.Class.Length > 0 && !string.Equals(app.Class, rule.Class, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.Title.Length > 0 && !string.Equals(app.Title, rule.Title, StringComparison.OrdinalIgnoreCase)) return false;
        return rule.Process.Length > 0 || rule.Class.Length > 0 || rule.Title.Length > 0;
    }

    static void Absorb(MonitorProfile profile, MonitorPreset preset)
    {
        if (profile.Hotkey.Length == 0) profile.Hotkey = preset.Hotkey ?? "";
        if (profile.Layout.Length == 0) profile.Layout = preset.Layout ?? "";
        if (profile.DeskLayout.Length == 0) profile.DeskLayout = preset.DeskLayout ?? "";
        if (profile.Workspace.Length == 0) profile.Workspace = preset.Workspace ?? "";
        if (profile.Primary.Length == 0) profile.Primary = preset.Primary ?? "";
        if (profile.Enable.Count == 0 && preset.Enable != null) profile.Enable.AddRange(preset.Enable.Where(s => !string.IsNullOrEmpty(s)));
        if (profile.Disable.Count == 0 && preset.Disable != null) profile.Disable.AddRange(preset.Disable.Where(s => !string.IsNullOrEmpty(s)));
        if (profile.Brightness.Count == 0 && preset.Brightness != null)
            foreach (var b in preset.Brightness) profile.Brightness.Add(new BrightTarget { Monitor = b.Monitor, Value = b.Value });
        if (profile.Monitors.Count == 0)
        {
            var built = FromPreset(preset);
            profile.Monitors = built.Monitors;
            profile.Primary = built.Primary;
            profile.Enable = built.Enable;
            profile.Disable = built.Disable;
        }
    }

    public static List<MonitorProfile> Migrate(IEnumerable<MonitorPreset>? presets, IEnumerable<WindowRule>? rules, string? json, IReadOnlyList<MonitorSpec>? screens = null)
    {
        var list = Parse(json);
        var presetList = presets?.Where(p => p != null).ToList() ?? new List<MonitorPreset>();
        if (list.Count == 0)
        {
            foreach (var p in presetList) list.Add(FromPreset(p));
        }
        else
        {
            foreach (var p in presetList)
            {
                var hit = list.FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.Ordinal));
                if (hit == null) list.Add(FromPreset(p));
                else Absorb(hit, p);
            }
        }
        if (list.Count == 0) list.Add(FromPreset(new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1" }));
        foreach (var p in list)
        {
            if (p.Monitors.Count == 0) p.Monitors.Add(new ProfileMonitor { Id = "1", Number = 1, W = 1920, H = 1080, Primary = true, Enabled = true });
            SeedScreens(p, screens);
            LiftCanvasWindows(p);
        }
        var home = list[0];
        foreach (var rule in rules ?? Array.Empty<WindowRule>())
        {
            if (rule == null || !RuleMatch.HasCriteria(rule)) continue;
            if (list.Any(p => p.Apps.Any(a => SameApp(a, rule)))) continue;
            home.Apps.Add(FromRule(rule));
        }
        return list;
    }

    public static MonitorProfile Add(List<MonitorProfile> list)
    {
        int n = list.Count + 1;
        var profile = FromPreset(new MonitorPreset { Name = "프로필 " + n });
        list.Add(profile);
        return profile;
    }

    public static void Rename(MonitorProfile profile, string name)
    {
        string next = (name ?? "").Trim();
        if (next.Length > 0) profile.Name = next;
    }

    public static MonitorProfile? Duplicate(List<MonitorProfile> list, string id)
    {
        var src = list.FirstOrDefault(p => p.Id == id);
        if (src == null) return null;
        var copy = Parse(ToJson(new[] { src })).FirstOrDefault();
        if (copy == null) return null;
        copy.Id = NewId();
        copy.Name = src.Name + " 복사";
        foreach (var app in copy.Apps) app.Id = NewId();
        list.Add(copy);
        return copy;
    }

    public static bool Delete(List<MonitorProfile> list, string id)
    {
        var src = list.FirstOrDefault(p => p.Id == id);
        return src != null && list.Remove(src);
    }

    public static MonitorPreset ToPreset(MonitorProfile profile)
    {
        var preset = new MonitorPreset
        {
            Name = profile.Name ?? "",
            Hotkey = profile.Hotkey ?? "",
            Primary = profile.Primary ?? "",
            Layout = profile.Layout ?? "",
            DeskLayout = profile.Icons.Count > 0 ? "" : (profile.DeskLayout ?? ""),
            Workspace = profile.Workspace ?? ""
        };
        preset.Enable.AddRange(profile.Enable ?? new List<string>());
        preset.Disable.AddRange(profile.Disable ?? new List<string>());
        if (profile.Brightness != null)
            foreach (var b in profile.Brightness) preset.Brightness.Add(new BrightTarget { Monitor = b.Monitor, Value = b.Value });
        return preset;
    }

    public static List<ProfileIcon> CaptureIcons(IReadOnlyList<DeskIcon> icons)
        => (icons ?? Array.Empty<DeskIcon>()).Select(i => new ProfileIcon { Name = i.Name ?? "", X = i.X, Y = i.Y }).ToList();

    public static DeskResult RestoreIcons(IReadOnlyList<ProfileIcon> icons, IIconSurface surface, IReadOnlyCollection<string>? only = null)
    {
        IEnumerable<ProfileIcon> src = icons ?? Array.Empty<ProfileIcon>();
        src = src.Where(i => i.Show);
        if (only != null)
        {
            var set = new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
            src = src.Where(i => set.Contains(i.Name ?? ""));
        }
        return IconMatch.Apply(surface, src.Select(i => new DeskIcon { Name = i.Name ?? "", X = i.X, Y = i.Y }).ToList());
    }

    public static string ExportOne(MonitorProfile profile) => ToJson(new[] { profile });

    public static MonitorProfile? ImportOne(string? json)
    {
        var list = Parse(json);
        var profile = list.FirstOrDefault();
        if (profile == null) return null;
        if ((profile.Id ?? "").Length == 0) profile.Id = NewId();
        return profile;
    }

    public static int Place(MonitorProfile profile, IWindowSurface surface, List<long>? placed = null)
    {
        var live = surface.List();
        int n = 0;
        var used = new HashSet<long>();
        foreach (var app in profile.Apps)
        {
            var hit = WindowIdentity.Best(app, live.Where(w => !used.Contains(w.Id) && !SessionLayout.OwnProcess(w)));
            if (hit == null) continue;
            used.Add(hit.Id);
            var mon = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors.FirstOrDefault();
            if (mon == null) continue;
            if (app.State is "top" or "max") surface.Maximize(hit.Id, new WorkRect(mon.Work.X, mon.Work.Y, mon.Work.W, mon.Work.H));
            else if (app.State == "min") surface.Minimize(hit.Id);
            else
            {
                var box = app.State is "left" or "right" or "tl" or "tr" or "bl" or "br"
                    ? ZoneBoxRelative(app.State, mon)
                    : new ProfileBox(mon.X + app.X, mon.Y + app.Y, app.W, app.H);
                surface.Place(hit.Id, box.X, box.Y, box.W, box.H);
            }
            if (app.TopMost && app.State != "min") surface.Pin(hit.Id);
            placed?.Add(hit.Id);
            n++;
        }
        return n;
    }

    /// <summary>Paths to start because the window is missing and the user turned on launch. Does not start anything.</summary>
    public static List<string> MissingLaunches(MonitorProfile profile, IReadOnlyList<TrackedWindow> open)
    {
        var list = new List<string>();
        if (profile?.Apps == null) return list;
        open ??= Array.Empty<TrackedWindow>();
        foreach (var app in profile.Apps)
        {
            string path = app.Path ?? "";
            if (!app.Launch || path.Length == 0) continue;
            if (open.Any(w => AppMatch(app, w))) continue;
            list.Add(path);
        }
        return list;
    }

    static ProfileBox ZoneBoxRelative(string state, ProfileMonitor mon)
    {
        var abs = ProfileSnap.ZoneBox(state, mon.Work);
        return abs;
    }

    static bool AppMatch(ProfileApp app, TrackedWindow window) => WindowIdentity.Matches(app, window);

    public static string ToJson(IEnumerable<MonitorProfile> profiles)
    {
        var arr = new JsonArray();
        foreach (var p in profiles) arr.Add(WriteProfile(p));
        return new JsonObject { ["v"] = 2, ["profiles"] = arr }.ToJsonString();
    }

    static JsonObject WriteProfile(MonitorProfile p)
    {
        var monitors = new JsonArray();
        foreach (var m in p.Monitors)
            monitors.Add(new JsonObject
            {
                ["id"] = m.Id, ["number"] = m.Number, ["x"] = m.X, ["y"] = m.Y, ["w"] = m.W, ["h"] = m.H,
                ["workX"] = m.Work.X, ["workY"] = m.Work.Y, ["workW"] = m.Work.W, ["workH"] = m.Work.H,
                ["scale"] = m.Scale <= 0 ? 100 : m.Scale, ["orientation"] = m.Orientation,
                ["primary"] = m.Primary, ["enabled"] = m.Enabled, ["device"] = m.Device, ["path"] = m.Path, ["edid"] = m.Edid
            });
        var windows = new JsonArray();
        foreach (var a in p.Apps)
            windows.Add(new JsonObject
            {
                ["id"] = a.Id, ["process"] = a.Process, ["path"] = a.Path, ["title"] = a.Title, ["class"] = a.Class, ["name"] = a.Name,
                ["monitor"] = a.Monitor, ["x"] = a.X, ["y"] = a.Y, ["w"] = a.W, ["h"] = a.H, ["state"] = a.State,
                ["topmost"] = a.TopMost, ["launch"] = a.Launch
            });
        var icons = new JsonArray();
        foreach (var i in p.Icons) icons.Add(new JsonObject { ["name"] = i.Name, ["x"] = i.X, ["y"] = i.Y, ["show"] = i.Show });
        var bright = new JsonArray();
        foreach (var b in p.Brightness) bright.Add(new JsonObject { ["monitor"] = b.Monitor, ["value"] = b.Value });
        return new JsonObject
        {
            ["id"] = p.Id, ["name"] = p.Name, ["hotkey"] = p.Hotkey, ["primary"] = p.Primary, ["layout"] = p.Layout, ["windowSpace"] = "pixels",             ["iconsSaved"] = p.IconsSaved, ["restoreIcons"] = p.RestoreIcons,
            ["rememberWindows"] = p.RememberWindows, ["sessionSaved"] = p.SessionSaved ?? "",
            ["iconOriginX"] = p.IconOriginX, ["iconOriginY"] = p.IconOriginY,
            ["deskLayout"] = p.DeskLayout, ["icons"] = p.DeskLayout, ["workspace"] = p.Workspace,
            ["enable"] = new JsonArray(p.Enable.Select(s => JsonValue.Create(s)).ToArray()),
            ["disable"] = new JsonArray(p.Disable.Select(s => JsonValue.Create(s)).ToArray()),
            ["brightness"] = bright, ["monitors"] = monitors, ["windows"] = windows, ["iconItems"] = icons,
            ["session"] = WriteSession(p.Session)
        };
    }

    static JsonArray WriteSession(List<SessionWindow>? session)
    {
        var arr = new JsonArray();
        foreach (var s in session ?? new List<SessionWindow>())
            arr.Add(new JsonObject
            {
                ["hwnd"] = JsonValue.Create(s.Hwnd), ["process"] = s.Process, ["path"] = s.Path, ["title"] = s.Title, ["class"] = s.Class,
                ["monitor"] = s.MonitorId, ["x"] = s.X, ["y"] = s.Y, ["w"] = s.W, ["h"] = s.H,
                ["state"] = s.State, ["z"] = s.Z, ["dpi"] = s.Dpi
            });
        return arr;
    }

    public static List<MonitorProfile> Parse(string? json)
    {
        var list = new List<MonitorProfile>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["profiles"] is not JsonArray arr) return list;
            foreach (var node in arr.OfType<JsonObject>()) list.Add(ReadProfile(node));
        }
        catch { }
        return list;
    }

    static MonitorProfile ReadProfile(JsonObject node)
    {
        var profile = new MonitorProfile
        {
            Id = Str(node["id"]),
            Name = Str(node["name"]),
            Hotkey = Str(node["hotkey"]),
            Primary = Str(node["primary"]),
            Layout = Str(node["layout"]),
            DeskLayout = Str(node["deskLayout"]).Length > 0 ? Str(node["deskLayout"]) : Str(node["icons"]),
            Workspace = Str(node["workspace"]),
            IconsSaved = Str(node["iconsSaved"]),
            RestoreIcons = node["restoreIcons"] == null || Bool(node["restoreIcons"]),
            RememberWindows = node["rememberWindows"] == null || Bool(node["rememberWindows"]),
            SessionSaved = Str(node["sessionSaved"]),
            IconOriginX = Num(node["iconOriginX"]),
            IconOriginY = Num(node["iconOriginY"])
        };
        if (profile.Id.Length == 0) profile.Id = NewId();
        Strings(node["enable"], profile.Enable);
        Strings(node["disable"], profile.Disable);
        if (node["brightness"] is JsonArray bright)
            foreach (var item in bright.OfType<JsonObject>())
                profile.Brightness.Add(new BrightTarget { Monitor = Str(item["monitor"]), Value = Num(item["value"]) });
        if (node["monitors"] is JsonArray monitors)
            foreach (var item in monitors.OfType<JsonObject>())
                profile.Monitors.Add(new ProfileMonitor
                {
                    Id = Str(item["id"]),
                    Number = Num(item["number"], 1),
                    X = Num(item["x"]),
                    Y = Num(item["y"]),
                    W = Num(item["w"], 1920),
                    H = Num(item["h"], 1080),
                    WorkX = Num(item["workX"]),
                    WorkY = Num(item["workY"]),
                    WorkW = Num(item["workW"]),
                    WorkH = Num(item["workH"]),
                    Primary = Bool(item["primary"]),
                    Enabled = item["enabled"] == null || Bool(item["enabled"]),
                    Scale = Num(item["scale"], 100),
                    Orientation = Num(item["orientation"]),
                    Device = Str(item["device"]),
                    Path = Str(item["path"]),
                    Edid = Str(item["edid"])
                });
        if (node["windows"] is JsonArray windows)
            foreach (var item in windows.OfType<JsonObject>())
                profile.Apps.Add(new ProfileApp
                {
                    Id = Str(item["id"]).Length > 0 ? Str(item["id"]) : NewId(),
                    Process = Str(item["process"]),
                    Path = Str(item["path"]),
                    Title = Str(item["title"]),
                    Class = Str(item["class"]),
                    Name = Str(item["name"]).Length > 0 ? Str(item["name"]) : Str(item["title"]),
                    Monitor = Num(item["monitor"], 1),
                    X = Num(item["x"]),
                    Y = Num(item["y"]),
                    W = Num(item["w"], 800),
                    H = Num(item["h"], 600),
                    State = Str(item["state"]).Length > 0 ? Str(item["state"]) : "normal",
                    TopMost = Bool(item["topmost"]),
                    Launch = Bool(item["launch"])
                });
        if (node["iconItems"] is JsonArray iconItems)
            foreach (var item in iconItems.OfType<JsonObject>())
                profile.Icons.Add(new ProfileIcon { Name = Str(item["name"]), X = Num(item["x"]), Y = Num(item["y"]), Show = item["show"] == null || Bool(item["show"]) });
        if (node["session"] is JsonArray session)
            foreach (var item in session.OfType<JsonObject>())
                profile.Session.Add(new SessionWindow
                {
                    Hwnd = Num(item["hwnd"]),
                    Process = Str(item["process"]), Path = Str(item["path"]), Title = Str(item["title"]), Class = Str(item["class"]),
                    MonitorId = Str(item["monitor"]), X = Num(item["x"]), Y = Num(item["y"]), W = Num(item["w"]), H = Num(item["h"]),
                    State = Str(item["state"]).Length > 0 ? Str(item["state"]) : "normal", Z = Num(item["z"]), Dpi = Num(item["dpi"], 100)
                });
        string space = Str(node["windowSpace"]);
        profile.LiftCanvas = space != "pixels" && profile.Apps.Count > 0 && profile.Apps.All(ProfileLegacy.InCanvas);
        return profile;
    }

    static string Str(JsonNode? node) => node == null ? "" : node.GetValue<string>() ?? "";
    static int Num(JsonNode? node, int fallback = 0) { try { return node == null ? fallback : node.GetValue<int>(); } catch { return fallback; } }
    static bool Bool(JsonNode? node) { try { return node != null && node.GetValue<bool>(); } catch { return false; } }
    static void Strings(JsonNode? node, List<string> into)
    {
        if (node is not JsonArray arr) return;
        foreach (var item in arr)
        {
            string s = item?.GetValue<string>() ?? "";
            if (s.Length > 0) into.Add(s);
        }
    }
}

public static class PresetApplyPolicy
{
    public static bool IsCurrent(string? active, string? requested)
        => !string.IsNullOrEmpty(active) && string.Equals(active, requested, StringComparison.Ordinal);

    public static string Toast(string name) => "이미 " + name + " 상태입니다!";
}

public static class PresetPlan
{
    /// <summary>Same order as 3.4.x: enable, load layout, primary, disable. Nothing is executed.</summary>
    public static List<string> Commands(MonitorPreset preset)
    {
        var cmds = new List<string>();
        if (preset.Enable != null)
            foreach (var id in preset.Enable)
                if (!string.IsNullOrEmpty(id)) cmds.Add("enable " + id);
        if (!string.IsNullOrEmpty(preset.Layout)) cmds.Add("load " + preset.Layout);
        if (!string.IsNullOrEmpty(preset.Primary)) cmds.Add("primary " + preset.Primary);
        if (preset.Disable != null && preset.Disable.Count > 0)
        {
            var ids = preset.Disable.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (ids.Count > 0) cmds.Add("disable " + string.Join(" ", ids));
        }
        return cmds;
    }
}

public sealed class FakeDisplay : IDisplayBackend
{
    public List<string> Log { get; } = new();
    public void Apply(IReadOnlyList<string> commands)
    {
        if (commands == null) return;
        Log.AddRange(commands);
    }
}

/// <summary>The real command sink. Constructing it does nothing. <see cref="Apply"/> demands the gate before any commit.</summary>
public sealed class RealDisplay : IDisplayBackend
{
    public void Apply(IReadOnlyList<string> commands)
    {
        DisplaySystem.Touch();
        if (DisplaySink.Commit == null) return;
        DisplaySink.Commit(commands ?? Array.Empty<string>());
    }
}

/// <summary>Brightness door. The dxva2 calls are not in this type.</summary>
public sealed class RealBright : IBrightBackend
{
    public List<string> Monitors()
    {
        DisplaySystem.BrightnessAttempts++;
        DisplayGate.Demand();
        if (BrightSink.Monitors == null) return new List<string>();
        DisplaySystem.BrightnessCalls++;
        return BrightSink.Monitors();
    }

    public bool Set(string id, int pct, out string how)
    {
        DisplaySystem.BrightnessAttempts++;
        DisplayGate.Demand();
        if (BrightSink.Set == null) { how = "DDC/CI 미지원"; return false; }
        DisplaySystem.BrightnessCalls++;
        return BrightSink.Set(id, pct, out how);
    }
}

public sealed class FakeBright : IBrightBackend
{
    public List<string> Mons = new();
    public Dictionary<string, int> Set_ = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unsupported = new(StringComparer.OrdinalIgnoreCase);
    public int Calls;
    public Func<int, List<string>>? Wake;
    public List<string> Monitors() => Wake != null ? Wake(Calls) : Mons;
    public bool Set(string id, int pct, out string how)
    {
        Calls++;
        if (Unsupported.Contains(id)) { how = "DDC/CI 미지원"; return false; }
        if (!Monitors().Contains(id)) { how = "모니터가 아직 켜지지 않음"; return false; }
        Set_[id] = pct;
        how = "mock";
        return true;
    }
}

public sealed class ThrowBright : IBrightBackend
{
    public List<string> Monitors() => new() { "A" };
    public bool Set(string id, int pct, out string how) => throw new InvalidOperationException("boom");
}

public static class PresetCodec
{
    public static MonitorPreset ReadFirst(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root?["Presets"] is JsonArray arr)
            foreach (var node in arr)
                if (node is JsonObject o) return FromObject(o);
        return FromObject(null);
    }

    public static MonitorPreset FromObject(JsonObject? o)
    {
        var p = new MonitorPreset();
        if (o == null) return p;
        p.Name = Str(o["Name"]);
        p.Hotkey = Str(o["Hotkey"]);
        p.Primary = Str(o["Primary"]);
        p.Layout = Str(o["Layout"]);
        p.DeskLayout = Str(o["DeskLayout"]);
        p.Workspace = Str(o["Workspace"]);
        Strings(o["Enable"], p.Enable);
        Strings(o["Disable"], p.Disable);
        if (o["Brightness"] is JsonArray bright)
            foreach (var node in bright)
            {
                if (node is not JsonObject b) continue;
                p.Brightness.Add(new BrightTarget { Monitor = Str(b["Monitor"]), Value = Clamp(b["Value"]) });
            }
        return p;
    }

    public static MonitorPreset Clone(MonitorPreset src)
    {
        var p = new MonitorPreset
        {
            Name = src.Name, Hotkey = src.Hotkey, Primary = src.Primary, Layout = src.Layout,
            DeskLayout = src.DeskLayout, Workspace = src.Workspace
        };
        p.Enable.AddRange(src.Enable);
        p.Disable.AddRange(src.Disable);
        foreach (var b in src.Brightness) p.Brightness.Add(new BrightTarget { Monitor = b.Monitor, Value = b.Value });
        return p;
    }

    static void Strings(JsonNode? node, List<string> into)
    {
        if (node is not JsonArray arr) return;
        foreach (var item in arr)
        {
            var s = Str(item);
            if (s.Length > 0) into.Add(s);
        }
    }

    static string Str(JsonNode? n)
    {
        if (n is JsonValue jv && jv.TryGetValue<string>(out string? s)) return s ?? "";
        return "";
    }

    static int Clamp(JsonNode? n)
    {
        if (n is not JsonValue jv) return -1;
        int v;
        if (jv.TryGetValue<int>(out int i)) v = i;
        else if (jv.TryGetValue<double>(out double d)) v = (int)d;
        else return -1;
        if (v < -1) v = -1;
        if (v > 100) v = 100;
        return v;
    }
}

/// <summary>
/// Brightness, linked icon layout, and linked workspace. Defaults never touch a monitor,
/// the desktop list view, or a process. Tests replace the delegates.
/// </summary>
public static class PresetExtras
{
    public static IBrightBackend Bright = new RealBright();
    public static Func<string, LinkedDesk?> FindDesk = _ => null;
    public static Func<LinkedDesk, DeskLinkResult> RestoreDesk = _ => new DeskLinkResult { Ok = false, Error = "아이콘 복원 실패" };
    public static Func<string> Signature = () => "unset";
    public static Action<int> Sleep = _ => { };
    public static int StableMs = 1500, StableMax = 8000, RetryDelay = 1500, Retries = 4;
    public static DateTime LinkedAt = DateTime.MinValue;
    public static string LastBright = "", LastDesk = "", LastWork = "";
    public static Func<string, LinkedWork?> FindWork = _ => null;
    public static Func<bool> WorkLaunchMissing = () => false;
    public static Func<LinkedWork, bool, string> LaunchWork = (_, _) => "";
    public static int WorkRuns, DeskRestores;
    public static string WorkMutexName = "BongHubLinkedWorkTest";
    public static string WorkStampFile = "";
    public static string StampFile = "";

    public static bool HasWork(MonitorPreset p)
        => (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0)) || !string.IsNullOrEmpty(p.DeskLayout) || !string.IsNullOrEmpty(p.Workspace);

    public static void WaitStable()
    {
        string last = Signature();
        int same = 0, waited = 0;
        while (same < StableMs && waited < StableMax)
        {
            Sleep(250);
            waited += 250;
            string s = Signature();
            if (s == last) same += 250;
            else { same = 0; last = s; }
        }
    }

    public static Dictionary<string, int> Targets(MonitorPreset p, IEnumerable<string> mons)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (p.Brightness == null) return d;
        var all = p.Brightness.FirstOrDefault(b => b.Monitor == "");
        foreach (var m in mons)
        {
            var one = p.Brightness.FirstOrDefault(b => string.Equals(b.Monitor, m, StringComparison.OrdinalIgnoreCase));
            int v = one != null ? one.Value : all != null ? all.Value : -1;
            if (v >= 0) d[m] = Math.Min(100, v);
        }
        foreach (var b in p.Brightness)
            if (b.Monitor != "" && b.Value >= 0 && !d.ContainsKey(b.Monitor) && (p.Disable == null || !p.Disable.Contains(b.Monitor)))
                d[b.Monitor] = Math.Min(100, b.Value);
        return d;
    }

    public static string BrightSummary(MonitorPreset p)
    {
        var l = (p.Brightness ?? new List<BrightTarget>()).Where(x => x.Value >= 0).OrderBy(x => x.Monitor, StringComparer.Ordinal)
            .Select(x => (x.Monitor == "" ? "전체" : x.Monitor) + " " + x.Value + "%").ToList();
        return l.Count == 0 ? "밝기는 바꾸지 않아요" : "적용: " + string.Join(", ", l);
    }

    public static void Run(MonitorPreset p, Action<string> status)
    {
        LastBright = ""; LastDesk = ""; LastWork = "";
        if (!HasWork(p)) return;
        try { status("화면 안정되기를 기다리는 중…"); WaitStable(); } catch { }
        try
        {
            if (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0))
            {
                status("모니터 밝기 맞추는 중…");
                var todo = Targets(p, Bright.Monitors());
                var done = new List<string>();
                var fail = new Dictionary<string, string>();
                for (int a = 0; a < Retries && todo.Count > 0; a++)
                {
                    if (a > 0)
                    {
                        Sleep(RetryDelay);
                        foreach (var m in Targets(p, Bright.Monitors()))
                            if (!todo.ContainsKey(m.Key) && !done.Contains(m.Key)) todo[m.Key] = m.Value;
                    }
                    foreach (var t in todo.ToList())
                    {
                        string how;
                        bool ok = false;
                        try { ok = Bright.Set(t.Key, t.Value, out how); }
                        catch (Exception ex) { how = ex.Message; }
                        if (ok) { done.Add(t.Key + " " + t.Value + "% (" + how + ")"); todo.Remove(t.Key); fail.Remove(t.Key); }
                        else fail[t.Key] = how;
                    }
                }
                LastBright = (done.Count > 0 ? "밝기 " + string.Join(", ", done) : "")
                    + (fail.Count > 0 ? (done.Count > 0 ? " · " : "") + "실패: " + string.Join(", ", fail.Select(f => f.Key + "(" + f.Value + ")")) : "");
                if (fail.Count > 0) status("일부 모니터 밝기를 바꾸지 못했어요");
            }
        }
        catch (Exception ex) { LastBright = "밝기 실패: " + ex.Message; }
        try
        {
            if (!string.IsNullOrEmpty(p.DeskLayout))
            {
                var l = FindDesk(p.DeskLayout);
                if (l == null) LastDesk = "연계된 바탕화면 아이콘 배치가 삭제됐어요. 프리셋 편집에서 다시 골라 주세요.";
                else
                {
                    status("바탕화면 아이콘 배치 ‘" + l.Name + "’ 복원 중…");
                    Stamp();
                    DeskRestores++;
                    var r = RestoreDesk(l);
                    Stamp();
                    LastDesk = r == null ? "아이콘 복원 실패" : r.Ok ? "‘" + l.Name + "’ " + r.Summary() : "아이콘 복원 실패: " + r.Error;
                }
            }
        }
        catch (Exception ex) { LastDesk = "아이콘 복원 실패: " + ex.Message; }
        try
        {
            if (!string.IsNullOrEmpty(p.Workspace))
            {
                var w = FindWork(p.Workspace);
                if (w == null) LastWork = "연계된 작업 영역이 삭제됐어요. 프리셋 편집에서 다시 골라 주세요.";
                else RunWork(p, w, status);
            }
        }
        catch (Exception ex) { LastWork = "창 배치 실패: " + ex.Message; }
    }

    static void RunWork(MonitorPreset p, LinkedWork w, Action<string> status)
    {
        bool mine = false;
        using var mx = new Mutex(false, WorkMutexName);
        try { mine = mx.WaitOne(0); }
        catch (AbandonedMutexException) { mine = true; }
        try
        {
            if (!mine || RecentWork(p.Name + "|" + w.Id))
                LastWork = "작업 영역 ‘" + w.Name + "’은 다른 전환에서 이미 실행 중이라 건너뛰었어요";
            else
            {
                StampWork(p.Name + "|" + w.Id);
                status("창 배치 ‘" + w.Name + "’ 적용 중…");
                WorkRuns++;
                string r = LaunchWork(w, WorkLaunchMissing());
                LastWork = r ?? "";
                if (r != null && r.Contains("실패")) status("일부 창을 배치하지 못했어요");
            }
        }
        finally { if (mine) { try { mx.ReleaseMutex(); } catch { } } }
    }

    static bool RecentWork(string key)
    {
        try
        {
            if (WorkStampFile.Length == 0 || !File.Exists(WorkStampFile)) return false;
            return (DateTime.Now - File.GetLastWriteTime(WorkStampFile)).TotalSeconds < 10 && File.ReadAllText(WorkStampFile) == key;
        }
        catch { return false; }
    }

    static void StampWork(string key)
    {
        try { if (WorkStampFile.Length > 0) File.WriteAllText(WorkStampFile, key); } catch { }
    }

    static void Stamp()
    {
        LinkedAt = DateTime.Now;
        try { if (StampFile.Length > 0) File.WriteAllText(StampFile, DateTime.Now.ToString("o")); } catch { }
    }

    public static bool LinkedRecently
    {
        get
        {
            if ((DateTime.Now - LinkedAt).TotalSeconds < 20) return true;
            try { return StampFile.Length > 0 && File.Exists(StampFile) && (DateTime.Now - File.GetLastWriteTime(StampFile)).TotalSeconds < 20; }
            catch { return false; }
        }
    }
}

/// <summary>What Apply will change. Pure. An empty live list means the monitor arrangement is not compared.</summary>
public static class ProfileDiff
{
    public static string Summary(MonitorProfile profile, IReadOnlyList<ScreenInfo>? live)
    {
        if (profile == null) return "적용할 변경이 없어요";
        int moved = 0;
        if (live != null)
            foreach (var m in profile.Monitors)
            {
                if (!m.Connected) continue;
                var s = live.FirstOrDefault(x => x.Number == m.Number);
                if (s != null && (s.X != m.X || s.Y != m.Y || s.W != m.W || s.H != m.H)) moved++;
            }
        int wins = profile.Apps?.Count ?? 0;
        int icons = profile.RestoreIcons ? (profile.Icons?.Count(i => i.Show) ?? 0) : 0;
        int bright = profile.Brightness?.Count(b => b.Value >= 0) ?? 0;
        var parts = new List<string>();
        if (moved > 0) parts.Add("모니터 " + moved + "곳");
        if (wins > 0) parts.Add("창 " + wins + "개");
        if (icons > 0) parts.Add("아이콘 " + icons + "개");
        if (bright > 0) parts.Add("밝기 " + bright + "곳");
        return parts.Count == 0 ? "적용할 변경이 없어요" : "적용하면 " + string.Join(" · ", parts);
    }
}

/// <summary>The profile document from just before the last real apply. Taking it does not touch a display.</summary>
public static class ProfileUndo
{
    public sealed class Shot
    {
        public string Id = "";
        public string Json = "";
    }

    public static Shot? Last { get; private set; }

    public static void Remember(MonitorProfile profile)
    {
        if (profile == null) { Last = null; return; }
        Last = new Shot { Id = profile.Id ?? "", Json = ProfileDoc.ToJson(new[] { profile }) };
    }

    public static MonitorProfile? Take()
    {
        var shot = Last;
        Last = null;
        if (shot == null || shot.Json.Length == 0) return null;
        var profile = ProfileDoc.Parse(shot.Json).FirstOrDefault();
        if (profile != null && profile.Id.Length == 0) profile.Id = shot.Id;
        return profile;
    }
}

public readonly record struct IconCompare(int Same, int Moved, int Missing, int Extra, int Hidden);

/// <summary>Desktop-icon preview math. Positions are virtual pixels, including negatives. No explorer calls.</summary>
public static class IconLayout
{
    public static int Host(int x, int y, IReadOnlyList<ProfileBox> monitors)
    {
        if (monitors == null) return -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (x >= m.X && y >= m.Y && x < m.Right && y < m.Bottom) return i;
        }
        return -1;
    }

    public static IconCompare Compare(IReadOnlyList<ProfileIcon>? saved, IReadOnlyList<DeskIcon>? current)
    {
        var want = new Dictionary<string, Queue<(int X, int Y)>>(StringComparer.OrdinalIgnoreCase);
        int hidden = 0;
        foreach (var s in saved ?? Array.Empty<ProfileIcon>())
        {
            if (!s.Show) { hidden++; continue; }
            string name = s.Name ?? "";
            if (!want.TryGetValue(name, out var q)) want[name] = q = new Queue<(int, int)>();
            q.Enqueue((s.X, s.Y));
        }
        int same = 0, moved = 0, extra = 0;
        foreach (var c in current ?? Array.Empty<DeskIcon>())
        {
            string name = c.Name ?? "";
            if (!want.TryGetValue(name, out var q) || q.Count == 0) { extra++; continue; }
            var t = q.Dequeue();
            if (t.X == c.X && t.Y == c.Y) same++;
            else moved++;
        }
        int missing = 0;
        foreach (var q in want.Values) missing += q.Count;
        return new IconCompare(same, moved, missing, extra, hidden);
    }
}

/// <summary>
/// Chord text shared by the note hotkey and profile hotkeys.
/// <c>RegisterHotKey</c> lives elsewhere. This type only parses.
/// </summary>
public static class HotkeyChord
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    public static bool TryParse(string? s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (var raw in s.Split('+'))
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            string u = t.ToLowerInvariant();
            if (u is "ctrl" or "control") mods |= Ctrl;
            else if (u == "alt") mods |= Alt;
            else if (u == "shift") mods |= Shift;
            else if (u is "win" or "windows") mods |= Win;
            else if (!TryVk(t, out vk)) return false;
        }
        return vk != 0;
    }

    public static string Normalize(string? s)
    {
        if (!TryParse(s, out uint mods, out uint vk)) return "";
        var parts = new List<string>();
        if ((mods & Win) != 0) parts.Add("Win");
        if ((mods & Ctrl) != 0) parts.Add("Ctrl");
        if ((mods & Alt) != 0) parts.Add("Alt");
        if ((mods & Shift) != 0) parts.Add("Shift");
        parts.Add(VkName(vk));
        return string.Join("+", parts);
    }

    public const string ListenPrompt = "키를 누르세요…";

    /// <summary>Modifiers held during capture, drawn as key caps. The ellipsis waits for the key.</summary>
    public static string Caps(bool win, bool ctrl, bool alt, bool shift)
    {
        var parts = new List<string>();
        if (win) parts.Add("Win");
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (parts.Count == 0) return "";
        return string.Join(" + ", parts) + " + …";
    }

    static bool TryVk(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = token[0];
            if (c is >= '0' and <= '9') { vk = (uint)('0' + (c - '0')); return true; }
            if (c is >= 'a' and <= 'z') { vk = (uint)('A' + (c - 'a')); return true; }
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
        }
        string u = token.ToLowerInvariant();
        if (u.Length == 4 && u.StartsWith("num", StringComparison.Ordinal) && u[3] is >= '0' and <= '9')
        { vk = (uint)(0x60 + (u[3] - '0')); return true; }
        if (u.Length is >= 2 and <= 3 && u[0] == 'f' && int.TryParse(u[1..], out int fn) && fn is >= 1 and <= 24)
        { vk = (uint)(0x70 + fn - 1); return true; }
        if (Names.TryGetValue(u, out vk)) return true;
        return false;
    }

    static string VkName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        foreach (var kv in Names) if (kv.Value == vk) return Pretty(kv.Key);
        return "Key" + vk.ToString("X");
    }

    static string Pretty(string lower)
    {
        if (lower.StartsWith("oem", StringComparison.Ordinal)) return "Oem" + lower[3..];
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20, ["tab"] = 0x09, ["escape"] = 0x1B, ["esc"] = 0x1B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["back"] = 0x08,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["prior"] = 0x21,
        ["pagedown"] = 0x22, ["next"] = 0x22, ["insert"] = 0x2D, ["delete"] = 0x2E,
        ["snapshot"] = 0x2C, ["pause"] = 0x13,
        ["oemplus"] = 0xBB, ["oemminus"] = 0xBD, ["oemcomma"] = 0xBC, ["oemperiod"] = 0xBE,
        ["oem1"] = 0xBA, ["oem2"] = 0xBF, ["oem3"] = 0xC0, ["oem4"] = 0xDB,
        ["oem5"] = 0xDC, ["oem6"] = 0xDD, ["oem7"] = 0xDE,
    };
}

/// <summary>Press-to-record result. Escape keeps the previous chord. Backspace clears. Conflicts are not stored.</summary>
public static class HotkeyEdit
{
    public static (string Value, string Error) Apply(string? previous, string? gesture, IEnumerable<string>? taken, string? noteHotkey)
    {
        string prev = previous ?? "";
        string g = (gesture ?? "").Trim();
        if (g.Equals("escape", StringComparison.OrdinalIgnoreCase) || g.Equals("esc", StringComparison.OrdinalIgnoreCase)) return (prev, "");
        if (g.Equals("back", StringComparison.OrdinalIgnoreCase) || g.Equals("backspace", StringComparison.OrdinalIgnoreCase)) return ("", "");
        if (!HotkeyChord.TryParse(g, out _, out _)) return (prev, "단축키로 쓸 수 없어요.");
        string norm = HotkeyChord.Normalize(g);
        if (norm.Length == 0) return (prev, "단축키로 쓸 수 없어요.");
        string note = HotkeyChord.Normalize(noteHotkey);
        if (note.Length > 0 && note == norm) return (prev, "새 메모 단축키와 같아요.");
        if (taken != null)
            foreach (var t in taken)
                if (HotkeyChord.Normalize(t) == norm) return (prev, "다른 프로필이 이 단축키를 쓰고 있어요.");
        return (norm, "");
    }
}

public readonly record struct DipRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Hits(DipRect other)
    {
        double w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        double h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w > 0.5 && h > 0.5;
    }
}

/// <summary>
/// How the monitor diagram uses the space it is given. Decisions are in DIPs, so 96 DPI and 120 DPI agree.
/// A line that does not fit is omitted instead of being cut through a glyph.
/// </summary>
public static class ProfileChrome
{
    /// <summary>Page width, and the tallest box <see cref="ProfileSnap.Fit"/> may use. The painted canvas is shorter.</summary>
    public static (int Width, int FitHeight) Stage(double availW, double availH)
    {
        double w = double.IsNaN(availW) || availW < 280 ? 680 : availW;
        double h = double.IsNaN(availH) || availH < 240 ? 560 : availH;
        int width = (int)Math.Clamp(Math.Floor(w), 520, 1200);
        int fit = (int)Math.Clamp(Math.Floor(h * 0.42), 160, 340);
        return (width, fit);
    }

    /// <summary>Height of the diagram: the scaled arrangement plus padding. Top-aligned, no empty band.</summary>
    public static int PaintedHeight(double contentHeight, double scale, int pad)
    {
        if (scale < 0.01 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 0.01;
        if (contentHeight < 1 || double.IsNaN(contentHeight)) contentHeight = 1;
        int h = pad + (int)Math.Ceiling(contentHeight * scale) + pad;
        return Math.Max(pad * 2 + 36, h);
    }

    /// <summary>11px digit line. Conservative so the label is hidden before it would clip.</summary>
    public static bool Fits(string? text, double slot)
    {
        if (string.IsNullOrEmpty(text)) return true;
        return text.Length * 6.4 <= slot;
    }

    public static double NumberSize(double tileW, double tileH)
    {
        if (tileW < 52 || tileH < 40) return 13;
        if (tileW < 96 || tileH < 72) return 16;
        return 22;
    }

    /// <summary>Resolution line for a monitor tile. Empty means show the Identify number only.</summary>
    public static string Detail(double tileW, double tileH, int w, int h, int scale, int orientation, bool primary, bool connected)
    {
        if (tileW < 78 || tileH < 52) return "";
        int pct = scale <= 0 ? 100 : scale;
        string res = w + "×" + h;
        string pctText = pct + "%";
        string tail = (orientation == 0 ? "" : " · " + orientation + "°") + (primary ? " · 주" : "") + (connected ? "" : " · 끊김");
        string full = res + " · " + pctText + tail;
        string mid = res + " · " + pctText;
        double slot = tileW - 18;
        if (tileH >= 68 && Fits(full, slot)) return full;
        if (tileH >= 56 && Fits(mid, slot)) return mid;
        if (Fits(res, slot)) return res;
        return "";
    }

    /// <summary>Pixels reserved for the Identify number and the info line. Chips start under this.</summary>
    public static double BadgeBand(double tileW, double tileH, bool hasDetail)
    {
        double num = Math.Ceiling(NumberSize(tileW, tileH) * 1.6);
        double band = 8 + num + (hasDetail ? 18 : 0) + 8;
        double cap = Math.Max(24, tileH - 16);
        return Math.Min(band, cap);
    }

    /// <summary>The remaining strip cannot hold a real window chip, so one count chip is used instead.</summary>
    public static bool NeedsCountChip(double tileW, double tileH, double badgeH, int windows)
    {
        if (windows <= 0) return false;
        double inner = tileH - badgeH - 8;
        return inner < 26 || tileW < 72;
    }

    public static DipRect CountChip(double tileX, double tileY, double tileW, double tileH, double badgeH)
    {
        double top = tileY + badgeH + 2;
        double bottom = tileY + tileH - 4;
        double h = Math.Min(18, bottom - top);
        if (h < 14 || tileW < 36) return new DipRect(tileX, top, 0, 0);
        double w = Math.Min(52, tileW - 12);
        return new DipRect(tileX + 6, top, w, h);
    }

    /// <summary>Window chip at the real rectangle's scale on the tile. The frame may cover the number; <see cref="ChipContent"/> keeps the icon clear of it.</summary>
    public static DipRect WindowChip(int localX, int localY, int localW, int localH, int monW, int monH, double tileX, double tileY, double tileW, double tileH)
    {
        if (monW < 1) monW = 1;
        if (monH < 1) monH = 1;
        if (tileW < 1 || tileH < 1) return new DipRect(tileX, tileY, 0, 0);
        double x = tileX + localX / (double)monW * tileW;
        double y = tileY + localY / (double)monH * tileH;
        double w = localW / (double)monW * tileW;
        double h = localH / (double)monH * tileH;
        double right = Math.Min(tileX + tileW, x + Math.Max(0, w));
        double bottom = Math.Min(tileY + tileH, y + Math.Max(0, h));
        x = Math.Max(tileX, x);
        y = Math.Max(tileY, y);
        w = right - x;
        h = bottom - y;
        if (w < 1 || h < 1) return new DipRect(x, y, 0, 0);
        return new DipRect(x, y, w, h);
    }

    /// <summary>The identify number drawn at the tile's top-left. Matches the badge in <c>DrawProfileCanvas</c>.</summary>
    /// <param name="primary">The primary badge also holds the star, so the pill is wider and chip text starts further right.</param>
    public static DipRect NumberPill(double tileX, double tileY, bool primary = false) => new(tileX + 4, tileY + 4, primary ? 52 : 32, 24);

    /// <summary>
    /// Where the chip icon sits. <see cref="ChipInk.Show"/> is false when the chip is swallowed by the number
    /// and no icon can sit beside or under it. A tall chip still drops below <see cref="BadgeBand"/>.
    /// </summary>
    public readonly record struct ChipInk(bool Show, double Left, double Top, double Icon);

    public static ChipInk ChipContent(DipRect chip, double tileX, double tileY, double tileW, double badgeH, bool primary = false)
    {
        if (chip.W < 8 || chip.H < 8) return new ChipInk(false, 0, 0, 0);
        var pill = NumberPill(tileX, tileY, primary);
        if (!chip.Hits(pill))
        {
            double overlap = tileY + badgeH - chip.Y;
            if (overlap > 0.5 && chip.H - overlap >= 16) return new ChipInk(true, 0, overlap, 16);
            double icon = chip.W >= 28 && chip.H >= 18 ? 16 : 0;
            return new ChipInk(true, 0, 0, icon);
        }
        double rightW = chip.Right - pill.Right;
        double belowH = chip.Bottom - pill.Bottom;
        bool rightOk = rightW >= 14 && chip.H >= 14;
        bool belowOk = belowH >= 14 && chip.W >= 18;
        if (rightOk && (!belowOk || rightW * Math.Max(1, chip.H) >= chip.W * belowH))
        {
            double icon = Math.Min(16, Math.Min(rightW - 4, chip.H - 4));
            if (icon < 10) return new ChipInk(false, 0, 0, 0);
            return new ChipInk(true, pill.Right - chip.X, 0, icon);
        }
        if (belowOk)
        {
            double icon = Math.Min(16, Math.Min(chip.W - 8, belowH - 4));
            if (icon < 10) return new ChipInk(false, 0, 0, 0);
            return new ChipInk(true, 0, pill.Bottom - chip.Y, icon);
        }
        return new ChipInk(false, 0, 0, 0);
    }

    /// <summary>
    /// A window whose real rectangle is swallowed by the identify number. The compact chip sits in the
    /// number's row, to the right of <see cref="NumberPill"/>, and stops above the info line (frame margin 28)
    /// so the number and the resolution text stay readable. Empty when the tile has no room for the edit buttons.
    /// </summary>
    public static DipRect ParkedChip(double tileX, double tileY, double tileW, double tileH, bool primary = false)
    {
        var pill = NumberPill(tileX, tileY, primary);
        const double gap = 4;
        const double edge = 4;
        double x = pill.Right + gap;
        double roomW = tileX + tileW - edge - x;
        if (roomW < 56 || tileH < 36) return new DipRect(x, pill.Y, 0, 0);
        double y = pill.Y + 2;
        double bottomLimit = Math.Min(tileY + tileH - edge, tileY + 26);
        double h = Math.Min(20, bottomLimit - y);
        if (h < 18) return new DipRect(x, y, 0, 0);
        double w = Math.Min(128, roomW);
        var chip = new DipRect(x, y, w, h);
        if (chip.Hits(pill)) return new DipRect(x, y, 0, 0);
        return chip;
    }

    /// <summary>Icon dot at the icon's real position on the tile.</summary>
    public static DipRect IconDot(int iconX, int iconY, int monX, int monY, int monW, int monH, double tileX, double tileY, double tileW, double tileH)
    {
        const double size = 6;
        if (monW < 1) monW = 1;
        if (monH < 1) monH = 1;
        if (tileW < size || tileH < size) return new DipRect(0, 0, 0, 0);
        double x = tileX + (iconX - monX) / (double)monW * tileW - size / 2;
        double y = tileY + (iconY - monY) / (double)monH * tileH - size / 2;
        x = Math.Max(tileX, Math.Min(x, tileX + tileW - size));
        y = Math.Max(tileY, Math.Min(y, tileY + tileH - size));
        return new DipRect(x, y, size, size);
    }
}

/// <summary>Names of the profile-page symbols drawn as vectors, not as a font glyph.</summary>
public static class ProfileGlyph
{
    public static readonly string[] Kinds = { "ellipsis", "trash", "pencil", "pin", "min", "app", "eye", "eyeoff", "undo", "lock", "unlock", "power", "star", "expand" };
    public static bool Known(string? kind) => kind != null && Kinds.Contains(kind);
}

/// <summary>
/// Explorer's list view is per-monitor DPI aware, so item positions are already physical pixels.
/// <c>LVM_GETITEMPOSITION</c> is a view point. Client = view − <c>LVM_GETORIGIN</c>.
/// The list's client origin on the screen is the virtual-screen origin. The point stored is the icon image's top-left;
/// the image is 48px at 96 DPI. The dot uses the image center.
/// </summary>
public static class IconMap
{
    public static int ImagePx(int scalePercent)
    {
        int s = scalePercent < 50 ? 100 : scalePercent;
        return Math.Max(8, (int)Math.Round(48.0 * s / 100.0));
    }

    public static (int X, int Y) ToScreen(int viewX, int viewY, int virtualLeft, int virtualTop, int originX, int originY, int iconPx)
    {
        int half = Math.Max(0, iconPx) / 2;
        return (viewX - originX + virtualLeft + half, viewY - originY + virtualTop + half);
    }

    /// <summary><c>LVM_GETITEMRECT(LVIR_ICON)</c> is already in client coordinates. Its center plus the virtual origin is the screen point.</summary>
    public static (int X, int Y) Center(int left, int top, int right, int bottom, int virtualLeft, int virtualTop)
        => (virtualLeft + (left + right) / 2, virtualTop + (top + bottom) / 2);

    public static (int X, int Y) ToClient(int screenX, int screenY, int virtualLeft, int virtualTop, int originX, int originY, int iconPx)
    {
        int half = Math.Max(0, iconPx) / 2;
        return (screenX - half - virtualLeft + originX, screenY - half - virtualTop + originY);
    }

    public static (int Left, int Top, int Scale) Frame(IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (monitors == null || monitors.Count == 0) return (0, 0, 100);
        int left = monitors.Min(m => m.X);
        int top = monitors.Min(m => m.Y);
        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        int scale = primary.Scale <= 0 ? 100 : primary.Scale;
        return (left, top, scale);
    }
}

/// <summary>Executable and window class identify a window. The title only breaks ties, so a Discord server rename still matches.</summary>
public static class WindowIdentity
{
    public const int MatchFloor = 100;

    public static string Suffix(string? title)
    {
        string t = title ?? "";
        int i = t.LastIndexOf(" - ", StringComparison.Ordinal);
        return i >= 0 ? t[(i + 3)..].Trim() : t.Trim();
    }

    public static string ProcessKey(string? process)
    {
        string p = process ?? "";
        if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
        return p;
    }

    public static bool SameProcess(string? a, string? b)
    {
        string pa = ProcessKey(a), pb = ProcessKey(b);
        return pa.Length > 0 && pb.Length > 0 && string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
    }

    public static int Score(ProfileApp app, TrackedWindow window)
    {
        if (app == null || window == null) return 0;
        bool wantProc = ProcessKey(app.Process).Length > 0;
        bool wantClass = (app.Class ?? "").Length > 0;
        if (wantProc && !SameProcess(app.Process, window.Process)) return 0;
        if (wantClass && !string.Equals(app.Class, window.Class ?? "", StringComparison.OrdinalIgnoreCase)) return 0;
        if (!wantProc && !wantClass)
        {
            if ((app.Title ?? "").Length == 0) return 0;
            string title = app.Title ?? "";
            return title.Length > 0 && window.Title != null && window.Title.Contains(title, StringComparison.OrdinalIgnoreCase) ? 40 : 0;
        }
        int score = (wantProc ? 100 : 0) + (wantClass ? 40 : 0);
        score += TitleTie(app.Title, window.Title);
        return score;
    }

    public static int TitleTie(string? saved, string? live)
    {
        string s = saved ?? "", l = live ?? "";
        if (s.Length == 0 || l.Length == 0) return 0;
        if (string.Equals(s, l, StringComparison.OrdinalIgnoreCase)) return 30;
        string ss = Suffix(s), ls = Suffix(l);
        if (ss.Length > 0 && string.Equals(ss, ls, StringComparison.OrdinalIgnoreCase)) return 20;
        if (s.Length > 0 && l.Contains(s, StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    public static bool Matches(ProfileApp app, TrackedWindow window) => Score(app, window) >= (ProcessKey(app.Process).Length == 0 && (app.Class ?? "").Length == 0 ? 40 : MatchFloor);

    public static TrackedWindow? Best(ProfileApp app, IEnumerable<TrackedWindow>? windows)
    {
        TrackedWindow? best = null;
        int score = 0;
        if (windows == null) return null;
        foreach (var w in windows)
        {
            int s = Score(app, w);
            if (s > score) { score = s; best = w; }
        }
        return score >= MatchFloor || (best != null && ProcessKey(app.Process).Length == 0 && (app.Class ?? "").Length == 0 && score >= 40) ? best : null;
    }
}

public readonly record struct AimSpot(int Monitor, int X, int Y, int W, int H);

public static class AimPlace
{
    public static AimSpot Of(int x, int y, int w, int h, IReadOnlyList<ProfileMonitor> monitors)
    {
        if (w < 1) w = 800;
        if (h < 1) h = 600;
        if (monitors == null || monitors.Count == 0) return new AimSpot(1, x, y, w, h);
        int cx = x + Math.Max(1, w) / 2;
        int cy = y + Math.Max(1, h) / 2;
        var host = monitors.FirstOrDefault(m => cx >= m.X && cy >= m.Y && cx < m.X + m.W && cy < m.Y + m.H) ?? monitors[0];
        int lx = Math.Clamp(x - host.X, 0, Math.Max(0, host.W - 1));
        int ly = Math.Clamp(y - host.Y, 0, Math.Max(0, host.H - 1));
        int lw = Math.Clamp(w, 120, Math.Max(120, host.W - lx));
        int lh = Math.Clamp(h, 80, Math.Max(80, host.H - ly));
        return new AimSpot(host.Number, lx, ly, lw, lh);
    }
}

/// <summary>Open windows that are not already stored on the profile. Callers supply the list; this does not enumerate.</summary>
public static class GhostWindows
{
    public static Func<IReadOnlyList<TrackedWindow>>? Source;

    public static int Argb(string? key)
    {
        int h = 17;
        foreach (char c in key ?? "") h = unchecked(h * 31 + c);
        double hue = Math.Abs(h % 360);
        return Hsv(hue, 0.42, 0.55, 0.55);
    }

    public static List<TrackedWindow> Unregistered(IReadOnlyList<TrackedWindow>? open, IReadOnlyList<ProfileApp>? apps)
    {
        var list = new List<TrackedWindow>();
        foreach (var w in open ?? Array.Empty<TrackedWindow>())
        {
            if (w == null || w.W < 40 || w.H < 40) continue;
            if (SessionLayout.OwnProcess(w)) continue;
            bool known = false;
            foreach (var app in apps ?? Array.Empty<ProfileApp>())
                if (WindowIdentity.Score(app, w) >= WindowIdentity.MatchFloor) { known = true; break; }
            if (!known) list.Add(w);
        }
        return list;
    }

    static int Hsv(double hue, double sat, double val, double alpha)
    {
        double c = val * sat;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = val - c;
        double r = 0, g = 0, b = 0;
        if (hue < 60) { r = c; g = x; }
        else if (hue < 120) { r = x; g = c; }
        else if (hue < 180) { g = c; b = x; }
        else if (hue < 240) { g = x; b = c; }
        else if (hue < 300) { r = x; b = c; }
        else { r = c; b = x; }
        int R = (int)Math.Round((r + m) * 255);
        int G = (int)Math.Round((g + m) * 255);
        int B = (int)Math.Round((b + m) * 255);
        int A = (int)Math.Round(alpha * 255);
        return (A << 24) | (R << 16) | (G << 8) | B;
    }
}

/// <summary>Fake idle age for tests. Null means the app may read the system clock outside tests.</summary>
public static class IdleClock
{
    public static Func<long>? AgeMs;
}

/// <summary>Idle auto-switch decision. It never applies a profile.</summary>
public readonly record struct IdleDecision(string Action, string Preset);

public static class IdlePlan
{
    public static IdleDecision Tick(bool enabled, int minutes, string? away, string? current, string? previous, long idleMs, bool awayActive)
    {
        string target = away ?? "";
        if (!enabled || target.Length == 0 || minutes < 1) return new IdleDecision("none", "");
        long need = (long)minutes * 60_000L;
        if (idleMs >= need)
        {
            if (awayActive || string.Equals(current ?? "", target, StringComparison.OrdinalIgnoreCase)) return new IdleDecision("none", "");
            return new IdleDecision("away", target);
        }
        if (awayActive && !string.IsNullOrEmpty(previous)) return new IdleDecision("back", previous!);
        return new IdleDecision("none", "");
    }
}

/// <summary>Identify plan. <paramref name="real"/> false never touches a monitor.</summary>
public static class MonitorIdentify
{
    public static int NativeCalls;

    public static string Plan(IReadOnlyList<ProfileMonitor>? monitors, bool real)
    {
        var nums = (monitors ?? Array.Empty<ProfileMonitor>()).Select(m => m.Number).ToList();
        if (!real) return string.Join(",", nums);
        NativeCalls++;
        return "live:" + string.Join(",", nums);
    }
}

/// <summary>Physical-pixel card on a monitor. No Win32 calls. Mixed DPI uses each monitor's own scale.</summary>
public readonly record struct ScreenCard(int Number, bool Primary, int X, int Y, int W, int H);

/// <summary>
/// One line of canvas text that must not meet a badge, button, icon, or another line.
/// An empty width means the line is hidden.
/// </summary>
public static class CanvasInk
{
    public readonly record struct Mark(string Kind, DipRect Box);

    public static DipRect Line(DipRect host, IReadOnlyList<DipRect>? reserved, double lineH, double pad = 4)
    {
        if (host.W < 8 || host.H < 8 || lineH < 8) return new DipRect(host.X, host.Y, 0, 0);
        double p = host.H < lineH + pad * 2 ? 1 : pad;
        double x0 = host.X + p, y0 = host.Y + p, x1 = host.Right - p, y1 = host.Bottom - p;
        if (x1 - x0 < 8 || y1 - y0 < lineH - 1)
        {
            if (p > 1) return Line(host, reserved, Math.Min(lineH, Math.Max(8, host.H - 2)), 1);
            return new DipRect(host.X, host.Y, 0, 0);
        }
        var blocks = new List<DipRect>();
        if (reserved != null)
            foreach (var r in reserved)
                if (r.W > 0.5 && r.H > 0.5 && host.Hits(r)) blocks.Add(r);
        var candidates = new List<DipRect> { new(x0, y0, x1 - x0, lineH) };
        foreach (var b in blocks)
        {
            double rx = Math.Max(x0, b.Right + 2);
            if (x1 - rx >= 8) candidates.Add(new DipRect(rx, y0, x1 - rx, lineH));
            double lx = Math.Min(x1, b.X - 2);
            if (lx - x0 >= 8) candidates.Add(new DipRect(x0, y0, lx - x0, lineH));
            double by = Math.Max(y0, b.Bottom + 2);
            if (y1 - by >= lineH - 1 && x1 - x0 >= 8) candidates.Add(new DipRect(x0, by, x1 - x0, lineH));
        }
        DipRect best = default;
        double score = -1;
        foreach (var c in candidates)
        {
            double left = Math.Max(x0, c.X);
            double top = Math.Max(y0, c.Y);
            double right = Math.Min(x1, c.X + c.W);
            double bottom = Math.Min(y1, c.Y + c.H);
            var clipped = new DipRect(left, top, right - left, bottom - top);
            if (clipped.W < 8 || clipped.H < lineH - 1) continue;
            if (blocks.Any(b => clipped.Hits(b))) continue;
            double s = clipped.W * 1000 - clipped.Y;
            if (s > score) { score = s; best = clipped; }
        }
        return score < 0 ? new DipRect(host.X, host.Y, 0, 0) : best;
    }

    /// <summary>Fake canvas: number pill, chip text, ghost text, and a monitor line. Returned boxes do not meet each other or the pill.</summary>
    public static List<Mark> Arrange(DipRect tile, bool primary, IReadOnlyList<DipRect> chips)
    {
        var marks = new List<Mark>();
        var reserved = new List<DipRect> { ProfileChrome.NumberPill(tile.X, tile.Y, primary) };
        foreach (var chip in chips)
        {
            if (chip.W >= 88 && chip.H >= 36)
            {
                double bw = Math.Min(36, chip.W * 0.4);
                double bh = Math.Min(18, chip.H * 0.45);
                reserved.Add(new DipRect(chip.Right - bw - 3, chip.Bottom - bh - 3, bw, bh));
            }
        }
        var pill = reserved[0];
        var labelHost = new DipRect(tile.X + 6, pill.Bottom + 2, Math.Max(0, tile.W - 12), 14);
        var labelObs = new List<DipRect>(reserved);
        labelObs.AddRange(chips);
        var label = Line(labelHost, labelObs, 12, 1);
        if (label.W >= 8) { marks.Add(new Mark("monitor", label)); reserved.Add(label); }
        foreach (var chip in chips)
        {
            double line = Math.Min(14, Math.Max(8, chip.H - 4));
            var text = Line(chip, reserved, line, chip.H < 30 ? 2 : 4);
            if (text.W >= 8) { marks.Add(new Mark("chip", text)); reserved.Add(text); }
        }
        return marks;
    }
}

public static class IdentifyLayout
{
    public const int BaseW = 220;
    public const int BaseH = 148;
    public const int BaseMargin = 72;

    public static int Px(int dip, int scalePercent)
    {
        int s = scalePercent < 50 ? 100 : scalePercent;
        return Math.Max(1, (int)Math.Round(dip * s / 100.0));
    }

    /// <summary>Bottom-left card inside the monitor, in the same physical pixels as <see cref="ProfileMonitor.X"/>.</summary>
    public static ScreenCard Of(ProfileMonitor m)
    {
        int scale = m.Scale <= 0 ? 100 : m.Scale;
        int w = Math.Min(Px(BaseW, scale), Math.Max(1, m.W));
        int h = Math.Min(Px(BaseH, scale), Math.Max(1, m.H));
        int margin = Math.Min(Px(BaseMargin, scale), Math.Max(0, Math.Min(m.W - w, m.H - h)));
        int x = m.X + margin;
        int y = m.Y + m.H - h - margin;
        if (x + w > m.X + m.W) x = m.X + m.W - w;
        if (y < m.Y) y = m.Y;
        return new ScreenCard(m.Number, m.Primary, x, y, w, h);
    }

    /// <summary>Centered card, for the profile-switch overlay on one monitor.</summary>
    public static ScreenCard Center(ProfileMonitor m, int baseW, int baseH)
    {
        int scale = m.Scale <= 0 ? 100 : m.Scale;
        int w = Math.Min(Px(baseW, scale), Math.Max(1, m.W));
        int h = Math.Min(Px(baseH, scale), Math.Max(1, m.H));
        int x = m.X + Math.Max(0, (m.W - w) / 2);
        int y = m.Y + Math.Max(0, (m.H - h) / 2);
        return new ScreenCard(m.Number, m.Primary, x, y, w, h);
    }

    public static ProfileMonitor? HostOf(IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (monitors == null || monitors.Count == 0) return null;
        return monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
    }
}

/// <summary>Where a dragged window or its snapped edges land. Canvas drawing is separate.</summary>
public readonly record struct SnapCue(ProfileBox Box, bool Left, bool Right, bool Top, bool Bottom, string Kind);

public static class SnapCueMath
{
    public static SnapCue Window(ProfileBox raw, ProfileBox monitor, IReadOnlyList<ProfileBox>? others, int grid, int edgeSlop, string? zoneKind, ProfileBox zoneBox)
    {
        if (!string.IsNullOrEmpty(zoneKind))
        {
            string k = zoneKind;
            return new SnapCue(zoneBox,
                k is "left" or "tl" or "bl",
                k is "right" or "tr" or "br",
                k is "top" or "tl" or "tr" or "max",
                k is "bl" or "br",
                k);
        }
        var snapped = ProfileSnap.SnapWindow(raw, monitor, others ?? Array.Empty<ProfileBox>(), grid < 1 ? 1 : grid, edgeSlop);
        return new SnapCue(snapped,
            snapped.X == monitor.X,
            snapped.Right == monitor.Right,
            snapped.Y == monitor.Y,
            snapped.Bottom == monitor.Bottom,
            "");
    }
}
