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
    public bool Primary = true;
    public bool Enabled = true;
    public string Device = "";
    public string Path = "";
    public string Edid = "";
    /// <summary>Runtime. False when this saved screen is not in the current display list.</summary>
    public bool Connected = true;
    public ProfileBox Box => new(X, Y, W, H);
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
}

public sealed class ProfileIcon
{
    public string Name = "";
    public int X, Y;
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
}

public readonly record struct SnapPreview(string Kind, ProfileBox Box);

public readonly record struct ProfileScene(double Scale, int OriginX, int OriginY, ProfileBox Bounds);

/// <summary>Monitor-profile geometry. Nothing here talks to a display, a window, or the desktop.</summary>
public static class ProfileSnap
{
    public const int Edge = 28;
    public const int Grid = 8;

    public static ProfileBox SnapMonitor(ProfileBox moving, IReadOnlyList<ProfileBox> others, int threshold)
    {
        int x = moving.X, y = moving.Y;
        int best = threshold + 1;
        foreach (var o in others)
        {
            Consider(o.Right, Align(moving.Y, o.Y, threshold), Math.Abs(moving.X - o.Right));
            Consider(o.X - moving.W, Align(moving.Y, o.Y, threshold), Math.Abs(moving.Right - o.X));
            Consider(Align(moving.X, o.X, threshold), o.Bottom, Math.Abs(moving.Y - o.Bottom));
            Consider(Align(moving.X, o.X, threshold), o.Y - moving.H, Math.Abs(moving.Bottom - o.Y));
        }
        return new ProfileBox(x, y, moving.W, moving.H);

        void Consider(int cx, int cy, int dist)
        {
            if (dist <= threshold && dist < best) { best = dist; x = cx; y = cy; }
        }
    }

    static int Align(int current, int target, int threshold) => Math.Abs(current - target) <= threshold ? target : current;

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
        bool left = x - monitor.X <= Edge;
        bool right = monitor.Right - x <= Edge;
        bool top = y - monitor.Y <= Edge;
        bool bottom = monitor.Bottom - y <= Edge;
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

    public static ProfileBox SnapWindow(ProfileBox window, ProfileBox monitor, IReadOnlyList<ProfileBox> others, int grid)
    {
        int g = grid < 1 ? 1 : grid;
        int x = monitor.X + (int)Math.Round((window.X - monitor.X) / (double)g) * g;
        int y = monitor.Y + (int)Math.Round((window.Y - monitor.Y) / (double)g) * g;
        int w = Math.Max(g, window.W);
        int h = Math.Max(g, window.H);
        int slop = Math.Max(g, 8);
        x = Pull(x, monitor.X, slop);
        x = Pull(x, monitor.Right - w, slop);
        y = Pull(y, monitor.Y, slop);
        y = Pull(y, monitor.Bottom - h, slop);
        if (others != null)
            foreach (var o in others)
            {
                x = Pull(x, o.Right, slop);
                x = Pull(x, o.X - w, slop);
                y = Pull(y, o.Bottom, slop);
                y = Pull(y, o.Y - h, slop);
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

    public static ProfileBox ToCanvas(ProfileBox box, ProfileScene scene)
    {
        int x = scene.OriginX + (int)Math.Round((box.X - scene.Bounds.X) * scene.Scale);
        int y = scene.OriginY + (int)Math.Round((box.Y - scene.Bounds.Y) * scene.Scale);
        int w = Math.Max(28, (int)Math.Round(box.W * scene.Scale));
        int h = Math.Max(22, (int)Math.Round(box.H * scene.Scale));
        return new ProfileBox(x, y, w, h);
    }

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
                Primary = s.Primary, Enabled = true, Connected = true,
                Device = s.Device ?? "", Path = s.Path ?? "", Edid = s.Edid ?? ""
            };
            Remember(old, added);
            profile.Monitors.Add(added);
        }
        Retarget(profile, old);
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

    public static DeskResult RestoreIcons(IReadOnlyList<ProfileIcon> icons, IIconSurface surface)
        => IconMatch.Apply(surface, (icons ?? Array.Empty<ProfileIcon>()).Select(i => new DeskIcon { Name = i.Name, X = i.X, Y = i.Y }).ToList());

    public static int Place(MonitorProfile profile, IWindowSurface surface)
    {
        var live = surface.List();
        int n = 0;
        foreach (var app in profile.Apps)
        {
            var hit = live.FirstOrDefault(w => AppMatch(app, w));
            if (hit == null) continue;
            var mon = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors.FirstOrDefault();
            if (mon == null) continue;
            if (app.State is "top" or "max") surface.Maximize(hit.Id, new WorkRect(mon.X, mon.Y, mon.W, mon.H));
            else if (app.State == "min") surface.Minimize(hit.Id);
            else
            {
                var box = app.State is "left" or "right" or "tl" or "tr" or "bl" or "br"
                    ? ZoneBoxRelative(app.State, mon)
                    : new ProfileBox(mon.X + app.X, mon.Y + app.Y, app.W, app.H);
                surface.Place(hit.Id, box.X, box.Y, box.W, box.H);
            }
            n++;
        }
        return n;
    }

    static ProfileBox ZoneBoxRelative(string state, ProfileMonitor mon)
    {
        var abs = ProfileSnap.ZoneBox(state, mon.Box);
        return abs;
    }

    static bool AppMatch(ProfileApp app, TrackedWindow window)
    {
        if (app.Process.Length > 0)
        {
            string want = app.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app.Process[..^4] : app.Process;
            if (!string.Equals(want, window.Process, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (app.Class.Length > 0 && !string.Equals(app.Class, window.Class, StringComparison.OrdinalIgnoreCase)) return false;
        if (app.Title.Length > 0 && window.Title.IndexOf(app.Title, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return app.Process.Length > 0 || app.Class.Length > 0 || app.Title.Length > 0;
    }

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
            monitors.Add(new JsonObject { ["id"] = m.Id, ["number"] = m.Number, ["x"] = m.X, ["y"] = m.Y, ["w"] = m.W, ["h"] = m.H, ["primary"] = m.Primary, ["enabled"] = m.Enabled, ["device"] = m.Device, ["path"] = m.Path, ["edid"] = m.Edid });
        var windows = new JsonArray();
        foreach (var a in p.Apps)
            windows.Add(new JsonObject
            {
                ["id"] = a.Id, ["process"] = a.Process, ["path"] = a.Path, ["title"] = a.Title, ["class"] = a.Class, ["name"] = a.Name,
                ["monitor"] = a.Monitor, ["x"] = a.X, ["y"] = a.Y, ["w"] = a.W, ["h"] = a.H, ["state"] = a.State
            });
        var icons = new JsonArray();
        foreach (var i in p.Icons) icons.Add(new JsonObject { ["name"] = i.Name, ["x"] = i.X, ["y"] = i.Y });
        var bright = new JsonArray();
        foreach (var b in p.Brightness) bright.Add(new JsonObject { ["monitor"] = b.Monitor, ["value"] = b.Value });
        return new JsonObject
        {
            ["id"] = p.Id, ["name"] = p.Name, ["hotkey"] = p.Hotkey, ["primary"] = p.Primary, ["layout"] = p.Layout, ["windowSpace"] = "pixels", ["iconsSaved"] = p.IconsSaved, ["restoreIcons"] = p.RestoreIcons,
            ["deskLayout"] = p.DeskLayout, ["icons"] = p.DeskLayout, ["workspace"] = p.Workspace,
            ["enable"] = new JsonArray(p.Enable.Select(s => JsonValue.Create(s)).ToArray()),
            ["disable"] = new JsonArray(p.Disable.Select(s => JsonValue.Create(s)).ToArray()),
            ["brightness"] = bright, ["monitors"] = monitors, ["windows"] = windows, ["iconItems"] = icons
        };
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
            RestoreIcons = node["restoreIcons"] == null || Bool(node["restoreIcons"])
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
                    Primary = Bool(item["primary"]),
                    Enabled = item["enabled"] == null || Bool(item["enabled"]),
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
                    State = Str(item["state"]).Length > 0 ? Str(item["state"]) : "normal"
                });
        if (node["iconItems"] is JsonArray iconItems)
            foreach (var item in iconItems.OfType<JsonObject>())
                profile.Icons.Add(new ProfileIcon { Name = Str(item["name"]), X = Num(item["x"]), Y = Num(item["y"]) });
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
