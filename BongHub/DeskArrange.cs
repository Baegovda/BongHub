using System.Collections.Generic;
using System.Linq;

namespace BongHub;

/// <summary>One screen in a topology commit. <see cref="StableId"/> is the target path, never the GDI name.</summary>
public sealed class DeskScreen
{
    public string StableId = "";
    public int Number;
    public string Device = "";
    public int X, Y, W, H;
    public bool Primary;
    public bool Enabled = true;
    public int Scale = 100;
}

/// <summary>One monitor inside a single commit. <see cref="Device"/> is the GDI name resolved at apply time.</summary>
public readonly record struct DeskStep(string StableId, string Device, int Number, int X, int Y, int W, int H, bool Primary, bool Enabled, int Scale);

/// <summary>
/// Builds one display commit. Positions are physical pixels.
/// Matching uses the target path, then the EDID id, then the Identify number. The GDI name is only an output.
/// </summary>
public static class DeskArrange
{
    /// <summary>Gaps of a few pixels (the owner's desk has a 5px seam) still count as touching.</summary>
    public const int Gap = 48;

    public static List<DeskScreen> FromProfile(MonitorProfile profile)
    {
        var list = new List<DeskScreen>();
        if (profile?.Monitors == null) return list;
        foreach (var m in profile.Monitors)
        {
            if (!m.Connected && m.W <= 0) continue;
            list.Add(new DeskScreen
            {
                StableId = Stable(m),
                Number = m.Number,
                Device = m.Device ?? "",
                X = m.X, Y = m.Y, W = m.W, H = m.H,
                Primary = m.Primary,
                Enabled = m.Enabled && m.Connected,
                Scale = m.Scale <= 0 ? 100 : m.Scale
            });
        }
        return list;
    }

    public static string Stable(ProfileMonitor m)
    {
        if (m.Path.Length > 0) return m.Path;
        if (m.Edid.Length > 0) return m.Edid;
        if (m.Id.Length > 0) return m.Id;
        return "display:" + m.Number;
    }

    public static string Validate(IReadOnlyList<DeskScreen>? screens)
    {
        var on = (screens ?? Array.Empty<DeskScreen>()).Where(s => s.Enabled && s.W > 0 && s.H > 0).ToList();
        if (on.Count == 0) return "켤 모니터가 없어요.";
        for (int i = 0; i < on.Count; i++)
            for (int j = i + 1; j < on.Count; j++)
                if (Interiors(on[i], on[j])) return on[i].Number + "번과 " + on[j].Number + "번이 겹쳐요.";
        if (!Touches(on)) return "모니터가 서로 떨어져 있어요.";
        return "";
    }

    /// <summary>Pairs each desired screen with the live screen of the same stable id. The live GDI name is copied through.</summary>
    public static List<DeskStep> Steps(IReadOnlyList<DeskScreen> desired, IReadOnlyList<DeskScreen> live)
    {
        var steps = new List<DeskStep>();
        foreach (var want in desired)
        {
            var hit = Match(want, live);
            steps.Add(new DeskStep(want.StableId, hit?.Device ?? "", want.Number, want.X, want.Y, want.W, want.H, want.Primary, want.Enabled, want.Scale <= 0 ? 100 : want.Scale));
        }
        return steps;
    }

    public static DeskScreen? Match(DeskScreen want, IReadOnlyList<DeskScreen>? live)
    {
        if (live == null) return null;
        DeskScreen? byPath = null, byNumber = null;
        foreach (var s in live)
        {
            if (want.StableId.Length > 0 && string.Equals(s.StableId, want.StableId, StringComparison.OrdinalIgnoreCase)) byPath = s;
            if (want.Number > 0 && s.Number == want.Number) byNumber ??= s;
        }
        return byPath ?? byNumber;
    }

    /// <summary>Writes every step in one shot. An invalid layout leaves <paramref name="current"/> unchanged and returns false.</summary>
    public static bool Commit(List<DeskScreen> current, IReadOnlyList<DeskStep> steps, out string error)
    {
        var desired = steps.Select(s => new DeskScreen
        {
            StableId = s.StableId, Number = s.Number, Device = s.Device,
            X = s.X, Y = s.Y, W = s.W, H = s.H, Primary = s.Primary, Enabled = s.Enabled, Scale = s.Scale
        }).ToList();
        error = Validate(desired);
        if (error.Length > 0 || current == null) return false;
        foreach (var step in steps)
        {
            var hit = current.FirstOrDefault(s => string.Equals(s.StableId, step.StableId, StringComparison.OrdinalIgnoreCase))
                ?? current.FirstOrDefault(s => s.Number == step.Number && step.Number > 0);
            if (hit == null)
            {
                current.Add(new DeskScreen
                {
                    StableId = step.StableId, Number = step.Number, Device = step.Device,
                    X = step.X, Y = step.Y, W = step.W, H = step.H, Primary = step.Primary, Enabled = step.Enabled, Scale = step.Scale
                });
                continue;
            }
            hit.X = step.X; hit.Y = step.Y; hit.W = step.W; hit.H = step.H;
            hit.Primary = step.Primary; hit.Enabled = step.Enabled; hit.Scale = step.Scale;
            if (hit.Number <= 0) hit.Number = step.Number;
        }
        return true;
    }

    static bool Interiors(DeskScreen a, DeskScreen b)
    {
        int w = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X);
        int h = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0;
    }

    static bool Touches(List<DeskScreen> on)
    {
        if (on.Count <= 1) return true;
        var seen = new HashSet<int> { 0 };
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            for (int j = 0; j < on.Count; j++)
            {
                if (seen.Contains(j) || !Near(on[i], on[j])) continue;
                seen.Add(j);
                stack.Push(j);
            }
        }
        return seen.Count == on.Count;
    }

    static bool Near(DeskScreen a, DeskScreen b)
    {
        int gapX = Math.Max(a.X, b.X) - Math.Min(a.X + a.W, b.X + b.W);
        int gapY = Math.Max(a.Y, b.Y) - Math.Min(a.Y + a.H, b.Y + b.H);
        if (gapX > Gap || gapY > Gap) return false;
        bool xOverlap = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X) > -Gap;
        bool yOverlap = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y) > -Gap;
        return xOverlap || yOverlap || (gapX <= Gap && gapY <= Gap);
    }
}

/// <summary>In-memory desk. One <see cref="Apply"/> is one commit, never one commit per monitor.</summary>
public sealed class FakeDesk
{
    public List<DeskScreen> Screens = new();
    public int Commits;
    public string LastError = "";

    public bool Apply(IReadOnlyList<DeskStep> steps)
    {
        if (!DeskArrange.Commit(Screens, steps, out string error))
        {
            LastError = error;
            return false;
        }
        Commits++;
        LastError = "";
        return true;
    }
}

/// <summary>A window remembered when leaving a profile. Coordinates are physical pixels relative to <see cref="MonitorId"/>.</summary>
public sealed class SessionWindow
{
    public long Hwnd;
    public string Process = "";
    public string Path = "";
    public string Title = "";
    public string Class = "";
    public string MonitorId = "";
    public int X, Y, W, H;
    public string State = "normal";
    public int Z;
    public int Dpi = 100;
}

public static class SessionLayout
{
    public static bool Skip(PickProbe? probe)
    {
        if (probe == null || !WindowPicker.Accept(probe)) return true;
        if (probe.Tool) return true;
        return OwnProcess(probe.Window);
    }

    public static bool OwnProcess(TrackedWindow? window)
    {
        string p = window?.Process ?? "";
        return p.Equals("BongHub", StringComparison.OrdinalIgnoreCase) || p.Equals("BongHub.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static List<SessionWindow> Capture(IReadOnlyList<PickProbe>? probes, IReadOnlyList<ProfileMonitor>? monitors)
    {
        var list = new List<SessionWindow>();
        if (probes == null || monitors == null) return list;
        foreach (var probe in probes.OrderBy(p => p.Z))
        {
            if (Skip(probe)) continue;
            var w = probe.Window;
            int rx = w.X, ry = w.Y, rw = w.W, rh = w.H;
            if (w.Max && w.NormalW > 0) { rx = w.NormalX; ry = w.NormalY; rw = w.NormalW; rh = w.NormalH; }
            var mon = Host(rx + rw / 2, ry + Math.Max(1, rh) / 2, monitors) ?? monitors.FirstOrDefault(m => m.Connected);
            if (mon == null) continue;
            list.Add(new SessionWindow
            {
                Hwnd = w.Id,
                Process = w.Process ?? "",
                Path = w.Path ?? "",
                Title = w.Title ?? "",
                Class = w.Class ?? "",
                MonitorId = DeskArrange.Stable(mon),
                X = rx - mon.X,
                Y = ry - mon.Y,
                W = rw,
                H = rh,
                State = w.Max ? "max" : w.Min ? "min" : "normal",
                Z = probe.Z,
                Dpi = mon.Scale <= 0 ? 100 : mon.Scale
            });
        }
        return list;
    }

    public static List<long> Restore(IReadOnlyList<SessionWindow>? shot, IReadOnlyList<PickProbe>? probes, IWindowSurface surface, IReadOnlyList<ProfileMonitor>? monitors)
    {
        var placed = new List<long>();
        if (shot == null || probes == null || monitors == null) return placed;
        foreach (var item in shot.OrderBy(s => s.Z))
        {
            var hit = Match(item, probes);
            if (hit?.Window == null) continue;
            var mon = monitors.FirstOrDefault(m => string.Equals(DeskArrange.Stable(m), item.MonitorId, StringComparison.OrdinalIgnoreCase) && m.Enabled)
                ?? monitors.FirstOrDefault(m => m.Enabled);
            if (mon == null) continue;
            int x = mon.X + item.X, y = mon.Y + item.Y;
            if (item.State == "min")
            {
                surface.Place(hit.Window.Id, x, y, item.W, item.H);
                surface.Minimize(hit.Window.Id);
            }
            else if (item.State == "max")
            {
                surface.Place(hit.Window.Id, x, y, item.W, item.H);
                surface.Maximize(hit.Window.Id, new WorkRect(mon.X, mon.Y, mon.W, mon.H));
            }
            else surface.Place(hit.Window.Id, x, y, item.W, item.H);
            placed.Add(hit.Window.Id);
        }
        return placed;
    }

    public static List<PlacementSpace.Monitor> MonitorsOf(IEnumerable<ProfileMonitor>? monitors)
    {
        var list = new List<PlacementSpace.Monitor>();
        if (monitors == null) return list;
        foreach (var m in monitors)
            list.Add(new PlacementSpace.Monitor(DeskArrange.Stable(m), m.X, m.Y, m.W, m.H, m.Primary));
        return list;
    }

    public static PickProbe? Match(SessionWindow item, IReadOnlyList<PickProbe> probes)
    {
        foreach (var probe in probes)
            if (!Skip(probe) && probe.Window.Id == item.Hwnd && item.Hwnd != 0) return probe;
        PickProbe? best = null;
        int bestScore = 0;
        foreach (var probe in probes)
        {
            if (Skip(probe)) continue;
            int score = 0;
            var w = probe.Window;
            if (item.Path.Length > 0 && string.Equals(item.Path, w.Path, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (item.Class.Length > 0 && string.Equals(item.Class, w.Class, StringComparison.OrdinalIgnoreCase)) score += 2;
            if (item.Process.Length > 0 && SameExe(item.Process, w.Process)) score += 2;
            if (item.Title.Length > 0 && (w.Title ?? "").IndexOf(item.Title, StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
            if (score > bestScore) { bestScore = score; best = probe; }
        }
        return bestScore >= 2 ? best : null;
    }

    static bool SameExe(string a, string b)
    {
        string Strip(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        return string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);
    }

    static ProfileMonitor? Host(int x, int y, IReadOnlyList<ProfileMonitor> monitors)
    {
        foreach (var m in monitors)
            if (m.Connected && x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H) return m;
        return null;
    }

    /// <summary>Windows may resize a window when a monitor's DPI changes. The second pass puts the physical size back.</summary>
    public static void NudgeDpi(IEnumerable<TrackedWindow> windows, IReadOnlyList<SessionWindow> shot, IReadOnlyList<ProfileMonitor> monitors, int transientDpi)
    {
        if (transientDpi <= 0) return;
        foreach (var item in shot)
        {
            if (item.State != "normal" || item.Dpi <= 0 || item.Dpi == transientDpi) continue;
            var win = windows.FirstOrDefault(w => w.Id == item.Hwnd || (item.Path.Length > 0 && string.Equals(w.Path, item.Path, StringComparison.OrdinalIgnoreCase)));
            if (win == null || win.Max) continue;
            double ratio = transientDpi / (double)item.Dpi;
            win.W = Math.Max(1, (int)Math.Round(win.W * ratio));
            win.H = Math.Max(1, (int)Math.Round(win.H * ratio));
        }
    }
}

/// <summary>Leave A, apply B, come back. Display commits are atomic. Window restore uses the snapshot taken before the commit.</summary>
public static class ProfileSwitch
{
    public static string Run(MonitorProfile? leaving, MonitorProfile entering, IReadOnlyList<PickProbe> probes, FakeDesk desk, FakeWindowSurface windows, int transientDpi)
    {
        if (entering == null) return "프로필이 없어요.";
        if (leaving != null && leaving.RememberWindows)
        {
            leaving.Session = SessionLayout.Capture(probes, leaving.Monitors);
            leaving.SessionSaved = DateTime.Now.ToString("M월 d일 HH:mm");
        }
        var desired = DeskArrange.FromProfile(entering);
        string error = DeskArrange.Validate(desired);
        if (error.Length > 0) return error;
        var steps = DeskArrange.Steps(desired, desk.Screens);
        if (!desk.Apply(steps)) return desk.LastError.Length > 0 ? desk.LastError : "배치를 적용하지 못했어요.";
        var shot = entering.Session != null && entering.Session.Count > 0 ? entering.Session : FromApps(entering);
        SessionLayout.Restore(shot, probes, windows, entering.Monitors);
        if (transientDpi > 0) SessionLayout.NudgeDpi(windows.Windows, shot, entering.Monitors, transientDpi);
        SessionLayout.Restore(shot, probes, windows, entering.Monitors);
        return "";
    }

    public static List<SessionWindow> FromApps(MonitorProfile profile)
    {
        var list = new List<SessionWindow>();
        foreach (var app in profile.Apps)
        {
            if (SessionLayout.OwnProcess(new TrackedWindow { Process = app.Process })) continue;
            var mon = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors.FirstOrDefault();
            if (mon == null) continue;
            list.Add(new SessionWindow
            {
                Process = app.Process, Path = app.Path, Title = app.Title, Class = app.Class,
                MonitorId = DeskArrange.Stable(mon),
                X = app.X, Y = app.Y, W = app.W, H = app.H,
                State = app.State == "min" ? "min" : app.State is "max" or "top" ? "max" : "normal",
                Dpi = mon.Scale <= 0 ? 100 : mon.Scale
            });
        }
        return list;
    }

    public static string DisplayStory()
    {
        var live = Scrambled();
        var desk = new FakeDesk { Screens = live };
        var work = WorkProfile();
        var away = AwayProfile();
        int before = desk.Commits;
        string err = ProfileSwitch.Run(null, work, Array.Empty<PickProbe>(), desk, new FakeWindowSurface(), 0);
        if (err.Length > 0) return err;
        if (desk.Commits != before + 1) return "apply committed more than once";
        string pos = Expect(desk, WorkLayout());
        if (pos.Length > 0) return pos;
        var gdi = desk.Screens.First(s => s.Device == @"\\.\DISPLAY1");
        if (gdi.StableId == "P1" || gdi.X != 0) return "GDI name DISPLAY1 was treated as the left monitor";
        err = ProfileSwitch.Run(work, away, Array.Empty<PickProbe>(), desk, new FakeWindowSurface(), 0);
        if (err.Length > 0) return err;
        if (desk.Screens.First(s => s.StableId == "P3").X != -2400 || desk.Screens.First(s => s.StableId == "P4").X != 0) return "swapped profile did not land";
        err = ProfileSwitch.Run(away, work, Array.Empty<PickProbe>(), desk, new FakeWindowSurface(), 0);
        if (err.Length > 0) return err;
        pos = Expect(desk, WorkLayout());
        if (pos.Length > 0) return "return " + pos;
        if (desk.Commits != 3) return "commits " + desk.Commits;
        int frozen = desk.Commits;
        var overlap = WorkProfile();
        overlap.Monitors.First(m => m.Path == "P5").X = 0;
        overlap.Monitors.First(m => m.Path == "P5").Y = 0;
        string refused = ProfileSwitch.Run(work, overlap, Array.Empty<PickProbe>(), desk, new FakeWindowSurface(), 0);
        if (!refused.Contains("겹") || desk.Commits != frozen) return "overlap was applied: " + refused;
        var island = WorkProfile();
        island.Monitors.First(m => m.Path == "P5").X = 20000;
        island.Monitors.First(m => m.Path == "P5").Y = 20000;
        string apart = ProfileSwitch.Run(work, island, Array.Empty<PickProbe>(), desk, new FakeWindowSurface(), 0);
        if (!apart.Contains("떨어져") || desk.Commits != frozen) return "gap was applied: " + apart;
        pos = Expect(desk, WorkLayout());
        return pos;
    }

    public static string SessionStory()
    {
        var desk = new FakeDesk { Screens = Scrambled() };
        var work = WorkProfile();
        var away = AwayProfile();
        away.Apps.Add(new ProfileApp { Process = "notepad", Path = @"C:\Windows\notepad.exe", Title = "메모장", Class = "Notepad", Monitor = 2, X = 40, Y = 50, W = 700, H = 500, State = "normal" });
        var surface = new FakeWindowSurface();
        var code = new TrackedWindow { Id = 10, Process = "Code", Path = @"C:\Apps\Code.exe", Title = "편집", Class = "Chrome_WidgetWin_1", X = -4000, Y = 200, W = 800, H = 600 };
        var browser = new TrackedWindow { Id = 11, Process = "chrome", Path = @"C:\Apps\chrome.exe", Title = "브라우저", Class = "Chrome_WidgetWin_1", X = 0, Y = 0, W = 3840, H = 2160, Max = true, NormalX = 100, NormalY = 80, NormalW = 1100, NormalH = 700 };
        var note = new TrackedWindow { Id = 12, Process = "notepad", Path = @"C:\Windows\notepad.exe", Title = "메모장", Class = "Notepad", X = 300, Y = 200, W = 640, H = 480 };
        var own = new TrackedWindow { Id = 13, Process = "BongHub", Path = @"C:\Apps\BongHub.exe", Title = "메모", Class = "HwndWrapper", X = -3000, Y = 400, W = 420, H = 560 };
        surface.Windows.Add(code);
        surface.Windows.Add(browser);
        surface.Windows.Add(note);
        var probes = new List<PickProbe>
        {
            new() { Window = code, Visible = true, Z = 3 },
            new() { Window = browser, Visible = true, Z = 2 },
            new() { Window = note, Visible = true, Z = 1 },
            new() { Window = own, Visible = true, Own = true, Z = 4 },
            new() { Window = new TrackedWindow { Id = 14, Process = "cloak", Title = "숨김", W = 100, H = 100 }, Visible = true, Cloaked = true, Z = 5 },
        };
        string err = ProfileSwitch.Run(null, work, probes, desk, surface, 0);
        if (err.Length > 0) return err;
        err = ProfileSwitch.Run(work, away, probes, desk, surface, 0);
        if (err.Length > 0) return err;
        if (work.Session.Count != 3) return "session count " + work.Session.Count;
        if (work.Session.Any(s => s.Process == "BongHub")) return "own window was captured";
        if (work.SessionSaved.Length == 0) return "session has no time";
        var codeShot = work.Session.First(s => s.Process == "Code");
        if (codeShot.X != 800 || codeShot.Y != 195 || codeShot.W != 800 || codeShot.H != 600 || codeShot.Dpi != 120 || codeShot.MonitorId != "P1")
            return "code snapshot " + codeShot.X + "," + codeShot.Y + " " + codeShot.W + "x" + codeShot.H;
        var maxShot = work.Session.First(s => s.State == "max");
        if (maxShot.X != 100 || maxShot.Y != 80 || maxShot.W != 1100 || maxShot.H != 700 || maxShot.MonitorId != "P2") return "max snapshot lost the restored rect";
        if (note.X != 40 || note.Y != 50 || note.W != 700) return "configured window was not the fallback";
        code.Id = 910;
        err = ProfileSwitch.Run(away, work, probes, desk, surface, 100);
        if (err.Length > 0) return err;
        if (code.X != -4000 || code.Y != 200 || code.W != 800 || code.H != 600) return "code came back " + code.X + "," + code.Y + " " + code.W + "x" + code.H;
        if (!browser.Max || browser.X != 0 || browser.Y != 0 || browser.W != 3840 || browser.H != 2160) return "maximized window was not restored";
        if (browser.NormalW != 1100 || browser.NormalH != 700) return "maximized window lost its normal rect";
        if (note.X != 300 || note.Y != 200 || note.W != 640 || note.H != 480) return "notepad was not restored from the session";
        string pos = Expect(desk, WorkLayout());
        return pos;
    }

    static string Expect(FakeDesk desk, (string Id, int X, int Y, int W, int H)[] want)
    {
        foreach (var item in want)
        {
            var hit = desk.Screens.FirstOrDefault(s => s.StableId == item.Id);
            if (hit == null) return item.Id + " missing";
            if (hit.X != item.X || hit.Y != item.Y || hit.W != item.W || hit.H != item.H)
                return item.Id + " is " + hit.X + "," + hit.Y + " " + hit.W + "x" + hit.H;
        }
        return "";
    }

    static (string Id, int X, int Y, int W, int H)[] WorkLayout() => new[]
    {
        ("P1", -4800, 5, 3840, 2160),
        ("P2", 0, 0, 3840, 2160),
        ("P3", 0, -1350, 2400, 1350),
        ("P4", -2400, -1350, 2400, 1350),
        ("P5", 2400, -1365, 1200, 800),
    };

    static List<DeskScreen> Scrambled()
    {
        var list = new List<DeskScreen>();
        foreach (var item in WorkLayout())
            list.Add(new DeskScreen { StableId = item.Id, Number = item.Id[1] - '0', X = 0, Y = 0, W = item.W, H = item.H, Device = @"\\.\DISPLAY" + (item.Id == "P2" ? "1" : item.Id == "P1" ? "5" : item.Id[1].ToString()), Primary = item.Id == "P2", Scale = item.Id == "P1" ? 120 : 100 });
        return list;
    }

    static MonitorProfile WorkProfile()
    {
        var profile = new MonitorProfile { Id = "work", Name = "작업", Hotkey = "Ctrl+Alt+1", RememberWindows = true };
        foreach (var item in WorkLayout())
            profile.Monitors.Add(new ProfileMonitor
            {
                Id = item.Id, Path = item.Id, Number = item.Id[1] - '0',
                X = item.X, Y = item.Y, W = item.W, H = item.H,
                Primary = item.Id == "P2", Enabled = true, Connected = true,
                Scale = item.Id == "P1" ? 120 : 100,
                Device = @"\\.\DISPLAY9"
            });
        return profile;
    }

    static MonitorProfile AwayProfile()
    {
        var profile = WorkProfile();
        profile.Id = "away";
        profile.Name = "자리비움";
        profile.Hotkey = "Ctrl+Alt+0";
        profile.Session = new List<SessionWindow>();
        var third = profile.Monitors.First(m => m.Path == "P3");
        var fourth = profile.Monitors.First(m => m.Path == "P4");
        (third.X, fourth.X) = (fourth.X, third.X);
        return profile;
    }
}

/// <summary>One CCD path. <see cref="StableId"/> is the device-interface path or the EDID id, never <c>\\.\DISPLAYn</c>.</summary>
public sealed class CcdPath
{
    public string StableId = "";
    public string Gdi = "";
    public int X, Y, W, H;
    public bool Active = true;
    public bool Primary;
    public bool Available = true;
}

/// <summary>One <c>SetDisplayConfig</c> commit. Positions are source positions. The primary is at (0,0).</summary>
public sealed class CcdCommit
{
    public List<CcdPath> Paths = new();
    public uint Flags;
    /// <summary><c>supplied</c> writes the path and mode arrays. <c>topology</c> restores a saved path with <c>SDC_TOPOLOGY_SUPPLIED</c>.</summary>
    public string Kind = "supplied";
}

/// <summary>
/// Plans one CCD commit. Disabled monitors are deactivated. The enabled primary moves to (0,0)
/// and every other enabled monitor keeps its offset from that primary. Matching uses <see cref="CcdPath.StableId"/> only.
/// </summary>
public static class CcdLayout
{
    public const uint SdcApply = 0x00000080;
    public const uint SdcUseSupplied = 0x00000020;
    public const uint SdcSave = 0x00000200;
    public const uint SdcAllow = 0x00000400;
    public const uint SdcTopology = 0x00000010;
    public const uint ApplySupplied = SdcApply | SdcUseSupplied | SdcSave | SdcAllow;
    public const uint ApplyTopology = SdcApply | SdcTopology | SdcSave | SdcAllow;

    public static CcdCommit Build(IReadOnlyList<CcdPath>? live, IReadOnlyList<DeskScreen>? desired, IReadOnlyList<CcdPath>? saved)
    {
        var commit = new CcdCommit();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool revive = false;
        DeskScreen? primary = null;
        if (desired != null)
        {
            foreach (var s in desired)
                if (s.Enabled && s.W > 0 && s.H > 0 && s.Primary) { primary = s; break; }
            if (primary == null)
                foreach (var s in desired)
                    if (s.Enabled && s.W > 0 && s.H > 0) { primary = s; break; }
        }
        int ox = primary?.X ?? 0, oy = primary?.Y ?? 0;
        if (desired != null)
        {
            foreach (var want in desired)
            {
                if (want.StableId.Length == 0) continue;
                var current = Find(want.StableId, live);
                var src = current ?? Find(want.StableId, saved);
                if (want.Enabled && current?.Active != true) revive = true;
                var path = new CcdPath
                {
                    StableId = want.StableId,
                    Gdi = src?.Gdi ?? "",
                    X = want.Enabled ? want.X - ox : src?.X ?? want.X,
                    Y = want.Enabled ? want.Y - oy : src?.Y ?? want.Y,
                    W = want.W > 0 ? want.W : src?.W ?? 0,
                    H = want.H > 0 ? want.H : src?.H ?? 0,
                    Active = want.Enabled && want.W > 0 && want.H > 0,
                    Primary = want.Enabled && primary != null && string.Equals(want.StableId, primary.StableId, StringComparison.OrdinalIgnoreCase),
                    Available = src?.Available ?? true
                };
                commit.Paths.Add(path);
                seen.Add(want.StableId);
            }
        }
        if (live != null)
        {
            foreach (var p in live)
            {
                if (p.StableId.Length == 0 || seen.Contains(p.StableId)) continue;
                commit.Paths.Add(Clone(p));
            }
        }
        commit.Kind = revive ? "topology" : "supplied";
        commit.Flags = revive ? ApplyTopology : ApplySupplied;
        return commit;
    }

    public static CcdPath? Find(string? id, IReadOnlyList<CcdPath>? list)
    {
        if (list == null || string.IsNullOrEmpty(id)) return null;
        foreach (var p in list)
            if (string.Equals(p.StableId, id, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static CcdPath Clone(CcdPath p) => new()
    {
        StableId = p.StableId, Gdi = p.Gdi, X = p.X, Y = p.Y, W = p.W, H = p.H,
        Active = p.Active, Primary = p.Primary, Available = p.Available
    };

    /// <summary>Empty when a disabled monitor is deactivated, the primary lands at (0,0), and a later profile turns it back on. No display API.</summary>
    public static string DisconnectStory()
    {
        const string A = @"\\?\DISPLAY#DELAAA#4&111#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string B = @"\\?\DISPLAY#DELBBB#4&222#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string C = @"\\?\DISPLAY#DELCCC#4&333#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string D = @"\\?\DISPLAY#DELDDD#4&444#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string E = @"\\?\DISPLAY#DELEEE#4&555#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var ccd = new FakeCcd();
        ccd.Paths.Add(new CcdPath { StableId = A, Gdi = @"\\.\DISPLAY5", X = 0, Y = 0, W = 1920, H = 1080, Active = true, Primary = true });
        ccd.Paths.Add(new CcdPath { StableId = B, Gdi = @"\\.\DISPLAY2", X = 1920, Y = 0, W = 1920, H = 1080, Active = true });
        ccd.Paths.Add(new CcdPath { StableId = C, Gdi = @"\\.\DISPLAY1", X = 3840, Y = 0, W = 1920, H = 1080, Active = true });
        ccd.Paths.Add(new CcdPath { StableId = D, Gdi = @"\\.\DISPLAY3", X = 0, Y = 1080, W = 1920, H = 1080, Active = true });
        ccd.Paths.Add(new CcdPath { StableId = E, Gdi = @"\\.\DISPLAY4", X = -1920, Y = -1080, W = 1920, H = 1080, Active = true });
        var away = new List<DeskScreen>
        {
            new() { StableId = E, Device = @"\\.\DISPLAY1", X = -1921, Y = -1080, W = 1920, H = 1080, Primary = true, Enabled = true },
            new() { StableId = A, Device = @"\\.\DISPLAY4", X = 0, Y = 0, W = 1920, H = 1080, Enabled = false },
            new() { StableId = B, Device = @"\\.\DISPLAY2", X = 1920, Y = 0, W = 1920, H = 1080, Enabled = false },
            new() { StableId = C, Device = @"\\.\DISPLAY1", X = 3840, Y = 0, W = 1920, H = 1080, Enabled = false },
            new() { StableId = D, Device = @"\\.\DISPLAY3", X = 0, Y = 1080, W = 1920, H = 1080, Enabled = false }
        };
        var off = Build(ccd.Paths, away, ccd.Saved);
        if (off.Kind != "supplied" || off.Flags != ApplySupplied) return "disable flags " + off.Flags.ToString("X");
        var kept = off.Paths.Where(p => p.Active).ToList();
        if (kept.Count != 1 || kept[0].StableId != E || !kept[0].Primary || kept[0].X != 0 || kept[0].Y != 0)
            return "kept " + string.Join(",", kept.Select(p => p.StableId + "@" + p.X + "," + p.Y));
        if (kept[0].StableId.StartsWith(@"\\.\", StringComparison.Ordinal)) return "primary was a GDI name";
        var gone = off.Paths.Where(p => !p.Active).Select(p => p.StableId).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var expectGone = new[] { A, B, C, D }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (!gone.SequenceEqual(expectGone)) return "deactivated " + string.Join(",", gone);
        if (off.Paths.Any(p => string.Equals(p.StableId, p.Gdi, StringComparison.OrdinalIgnoreCase))) return "path id copied the GDI name";
        if (ccd.Apply(off) != 0) return "apply failed";
        ccd.Paths.RemoveAll(p => !p.Active);
        if (ccd.Paths.Count != 1 || ccd.Saved == null || ccd.Saved.Count != 5) return "saved topology " + (ccd.Saved?.Count ?? 0);
        var work = new List<DeskScreen>
        {
            new() { StableId = A, Device = @"\\.\DISPLAY9", X = 100, Y = 50, W = 1920, H = 1080, Primary = true, Enabled = true },
            new() { StableId = B, Device = @"\\.\DISPLAY9", X = 2020, Y = 50, W = 1920, H = 1080, Enabled = true },
            new() { StableId = C, Device = @"\\.\DISPLAY9", X = 3940, Y = 50, W = 1920, H = 1080, Enabled = true },
            new() { StableId = D, Device = @"\\.\DISPLAY9", X = 100, Y = 1130, W = 1920, H = 1080, Enabled = true },
            new() { StableId = E, Device = @"\\.\DISPLAY9", X = -1820, Y = -1030, W = 1920, H = 1080, Enabled = true }
        };
        var back = Build(ccd.Paths, work, ccd.Saved);
        if (back.Kind != "topology" || back.Flags != ApplyTopology) return "re-enable used " + back.Kind + " " + back.Flags.ToString("X");
        if (back.Paths.Count(p => p.Active) != 5) return "re-enable paths " + back.Paths.Count(p => p.Active);
        var origin = back.Paths.First(p => p.StableId == A);
        var shifted = back.Paths.First(p => p.StableId == E);
        if (!origin.Primary || origin.X != 0 || origin.Y != 0) return "round-trip primary " + origin.X + "," + origin.Y;
        if (shifted.X != -1920 || shifted.Y != -1080 || !shifted.Active) return "round-trip shift " + shifted.X + "," + shifted.Y;
        if (ccd.Apply(back) != 0) return "re-enable apply failed";
        if (ccd.Paths.Any(p => !p.Active) || ccd.Paths.Count(p => p.Primary) != 1) return "a monitor stayed off";
        int alive = ccd.Paths.Count(p => p.Active);
        ccd.FailCode = 87;
        int rc = ccd.Apply(Build(ccd.Paths, away, ccd.Saved));
        if (rc != 87 || ccd.Paths.Count(p => p.Active) != alive) return "failed commit changed the desk " + rc;
        return "";
    }
}

/// <summary>Last full topology. The real apply saves it before a monitor is turned off. Tests leave it null.</summary>
public static class CcdBank
{
    public static List<CcdPath>? Saved { get; private set; }

    public static void RememberIfFuller(IReadOnlyList<CcdPath>? paths)
    {
        if (paths == null) return;
        int active = 0;
        foreach (var p in paths) if (p.Active) active++;
        int prev = -1;
        if (Saved != null)
        {
            prev = 0;
            foreach (var p in Saved) if (p.Active) prev++;
        }
        if (prev >= 0 && active < prev) return;
        var copy = new List<CcdPath>(paths.Count);
        foreach (var p in paths) copy.Add(CcdLayout.Clone(p));
        Saved = copy;
    }
}

/// <summary>Records CCD commits. <see cref="Apply"/> never calls a display API.</summary>
public sealed class FakeCcd
{
    public List<CcdPath> Paths = new();
    public List<CcdPath>? Saved;
    public List<CcdCommit> Log = new();
    public int Applies;
    public int LastCode;
    public int FailCode;

    public int Apply(CcdCommit commit)
    {
        Applies++;
        Log.Add(commit);
        LastCode = FailCode;
        if (FailCode != 0) return FailCode;
        if (Saved == null)
        {
            Saved = new List<CcdPath>(Paths.Count);
            foreach (var p in Paths) Saved.Add(CcdLayout.Clone(p));
        }
        foreach (var p in commit.Paths)
        {
            var hit = CcdLayout.Find(p.StableId, Paths);
            if (hit == null)
            {
                Paths.Add(CcdLayout.Clone(p));
                continue;
            }
            hit.X = p.X; hit.Y = p.Y; hit.W = p.W; hit.H = p.H;
            hit.Active = p.Active; hit.Primary = p.Primary;
            if (p.Gdi.Length > 0) hit.Gdi = p.Gdi;
        }
        CcdPath? prim = null;
        foreach (var p in Paths)
            if (p.Active && p.Primary) { prim = p; break; }
        if (prim != null)
            foreach (var p in Paths)
                if (!ReferenceEquals(p, prim)) p.Primary = false;
        return 0;
    }
}

/// <summary>One top-level window taken before a mode switch. Coordinates are physical pixels.</summary>
public sealed class HeldWindow
{
    public long Id;
    public string MonitorId = "";
    public int RelX, RelY, W, H;
    public int Dpi = 100;
    public string State = "normal";
    public int NormalX, NormalY, NormalW, NormalH;
    public bool Own;
    public int AbsX, AbsY;
    public string Process = "";
    public string Title = "";
    public string Class = "";
}

/// <summary>One restore. <see cref="NormalX"/> is a screen pixel. <see cref="Move"/> is false for a minimized window, so its iconic rect stays put.</summary>
public sealed class HoldMove
{
    public long Id;
    public int X, Y, W, H;
    public string State = "normal";
    public int NormalX, NormalY, NormalW, NormalH;
    public bool Move = true;
}

/// <summary>
/// Snapshot before a topology change, and the single restore after it settles.
/// A window whose monitor is still there keeps its relative rect. A window on a monitor that turned off
/// uses its profile rule, or the nearest remaining monitor at the same relative size, clamped to the work area.
/// BongHub's own windows keep their absolute rect.
/// </summary>
public static class SwitchHold
{
    public static List<HeldWindow> Capture(IReadOnlyList<PickProbe>? probes, IReadOnlyList<ProfileMonitor>? monitors, int originX, int originY)
    {
        var list = new List<HeldWindow>();
        if (probes == null) return list;
        foreach (var probe in probes)
        {
            if (probe?.Window == null || !probe.Visible || probe.Cloaked || probe.Tool) continue;
            var w = probe.Window;
            bool own = probe.Own || SessionLayout.OwnProcess(w);
            bool placed = (w.Min || w.Max) && w.NormalW > 0 && w.NormalH > 0;
            int sx = placed ? w.NormalX + originX : w.X;
            int sy = placed ? w.NormalY + originY : w.Y;
            int sw = placed ? w.NormalW : w.W;
            int sh = placed ? w.NormalH : w.H;
            if (sw < 1) sw = 1;
            if (sh < 1) sh = 1;
            var host = Host(sx + sw / 2, sy + sh / 2, monitors);
            string id = host == null ? "" : DeskArrange.Stable(host);
            list.Add(new HeldWindow
            {
                Id = w.Id,
                MonitorId = id,
                RelX = host == null ? sx : sx - host.X,
                RelY = host == null ? sy : sy - host.Y,
                W = sw,
                H = sh,
                Dpi = host == null || host.Scale <= 0 ? 100 : host.Scale,
                State = w.Min ? "min" : w.Max ? "max" : "normal",
                NormalX = sx,
                NormalY = sy,
                NormalW = sw,
                NormalH = sh,
                Own = own,
                AbsX = w.X,
                AbsY = w.Y,
                Process = w.Process ?? "",
                Title = w.Title ?? "",
                Class = w.Class ?? ""
            });
        }
        return list;
    }

    public static List<HoldMove> Plan(IReadOnlyList<HeldWindow>? shot, IReadOnlyList<ProfileMonitor>? before, IReadOnlyList<ProfileMonitor>? after, IReadOnlyList<ProfileApp>? rules)
    {
        var list = new List<HoldMove>();
        if (shot == null) return list;
        foreach (var held in shot)
        {
            if (held.Own)
            {
                list.Add(new HoldMove
                {
                    Id = held.Id, X = held.AbsX, Y = held.AbsY, W = held.W, H = held.H,
                    State = held.State, NormalX = held.AbsX, NormalY = held.AbsY, NormalW = held.W, NormalH = held.H,
                    Move = held.State != "min"
                });
                continue;
            }
            var stay = Alive(held.MonitorId, after);
            int x, y, w, h;
            string state = held.State;
            if (stay != null)
            {
                x = stay.X + held.RelX;
                y = stay.Y + held.RelY;
                w = held.W;
                h = held.H;
                if (state == "max")
                {
                    var area = WorkOf(stay);
                    x = area.X; y = area.Y; w = area.W; h = area.H;
                }
            }
            else
            {
                var rule = RuleFor(held, rules);
                var home = rule == null ? null : AliveNumber(rule.Monitor, after);
                if (rule != null && home != null)
                {
                    x = home.X + rule.X;
                    y = home.Y + rule.Y;
                    w = rule.W > 0 ? rule.W : held.W;
                    h = rule.H > 0 ? rule.H : held.H;
                    if (rule.State == "min") state = "min";
                    else if (rule.State is "max" or "top") state = "max";
                    var clamped = Clamp(x, y, w, h, WorkOf(home));
                    x = clamped.X; y = clamped.Y; w = clamped.W; h = clamped.H;
                }
                else
                {
                    var near = Nearest(held, before, after);
                    if (near == null) continue;
                    var clamped = Clamp(near.X + held.RelX, near.Y + held.RelY, held.W, held.H, WorkOf(near));
                    x = clamped.X; y = clamped.Y; w = clamped.W; h = clamped.H;
                }
            }
            list.Add(new HoldMove
            {
                Id = held.Id, X = x, Y = y, W = w, H = h, State = state,
                NormalX = state == "max" ? (stay == null ? x : stay.X + held.RelX) : x,
                NormalY = state == "max" ? (stay == null ? y : stay.Y + held.RelY) : y,
                NormalW = state == "max" ? held.W : w,
                NormalH = state == "max" ? held.H : h,
                Move = state != "min"
            });
        }
        return list;
    }

    /// <summary>Empty when a window on a removed monitor is parked, a registered window uses its rule, and a note stays at its absolute rect. No window API.</summary>
    public static string Story()
    {
        const string A = @"\\?\DISPLAY#AAA#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string B = @"\\?\DISPLAY#BBB#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var before = new List<ProfileMonitor>
        {
            new() { Path = A, Id = A, Number = 1, X = 0, Y = 0, W = 1920, H = 1080, WorkX = 0, WorkY = 0, WorkW = 1920, WorkH = 1080, Primary = true, Enabled = true, Connected = true, Scale = 100, Device = @"\\.\DISPLAY1" },
            new() { Path = B, Id = B, Number = 2, X = 1920, Y = 0, W = 1920, H = 1080, WorkX = 1920, WorkY = 0, WorkW = 1920, WorkH = 1040, Primary = false, Enabled = true, Connected = true, Scale = 125, Device = @"\\.\DISPLAY2" }
        };
        var after = new List<ProfileMonitor>
        {
            new() { Path = A, Id = A, Number = 1, X = 100, Y = 0, W = 1920, H = 1080, WorkX = 100, WorkY = 40, WorkW = 1920, WorkH = 1040, Primary = true, Enabled = true, Connected = true, Scale = 100, Device = @"\\.\DISPLAY9" }
        };
        var code = new TrackedWindow { Id = 1, Process = "Code", Title = "편집", Class = "Chrome_WidgetWin_1", X = 40, Y = 50, W = 800, H = 600 };
        var noteApp = new TrackedWindow { Id = 2, Process = "notepad", Title = "메모장", Class = "Notepad", X = 2020, Y = 80, W = 640, H = 480 };
        var calc = new TrackedWindow { Id = 3, Process = "calc", Title = "계산기", Class = "ApplicationFrameWindow", X = 2020, Y = 80, W = 3000, H = 900 };
        var hidden = new TrackedWindow { Id = 4, Process = "chrome", Title = "브라우저", Class = "Chrome_WidgetWin_1", Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = 2100, NormalY = 100, NormalW = 800, NormalH = 600 };
        var own = new TrackedWindow { Id = 5, Process = "BongHub", Title = "메모", Class = "HwndWrapper", X = -3000, Y = 400, W = 420, H = 560 };
        var probes = new List<PickProbe>
        {
            new() { Window = code, Visible = true },
            new() { Window = noteApp, Visible = true },
            new() { Window = calc, Visible = true },
            new() { Window = hidden, Visible = true },
            new() { Window = own, Visible = true, Own = true }
        };
        var shot = Capture(probes, before, 0, 0);
        if (shot.Count != 5) return "captured " + shot.Count;
        var codeShot = shot.First(s => s.Id == 1);
        if (codeShot.MonitorId != A || codeShot.RelX != 40 || codeShot.Dpi != 100) return "code monitor " + codeShot.MonitorId;
        var noteShot = shot.First(s => s.Id == 2);
        if (noteShot.MonitorId != B || noteShot.RelX != 100 || noteShot.RelY != 80 || noteShot.Dpi != 125) return "note rel " + noteShot.RelX + "," + noteShot.RelY;
        if (noteShot.MonitorId.StartsWith(@"\\.\", StringComparison.Ordinal)) return "snapshot used a GDI name";
        var minShot = shot.First(s => s.Id == 4);
        if (minShot.State != "min" || minShot.RelX != 180 || minShot.NormalW != 800) return "min snapshot " + minShot.RelX;
        var rules = new List<ProfileApp> { new() { Process = "notepad.exe", Class = "Notepad", Title = "메모장", Monitor = 1, X = 40, Y = 50, W = 700, H = 500, State = "normal" } };
        var plan = Plan(shot, before, after, rules);
        var surface = new FakeWindowSurface();
        surface.Windows.Add(code);
        surface.Windows.Add(noteApp);
        surface.Windows.Add(calc);
        surface.Windows.Add(hidden);
        surface.Windows.Add(own);
        if (surface.ApplyHold(plan) != 5) return "hold apply";
        if (code.X != 140 || code.Y != 50 || code.W != 800 || code.H != 600) return "code moved to " + code.X + "," + code.Y + " " + code.W + "x" + code.H;
        if (noteApp.X != 140 || noteApp.Y != 50 || noteApp.W != 700 || noteApp.H != 500) return "rule missed " + noteApp.X + "," + noteApp.Y + " " + noteApp.W + "x" + noteApp.H;
        if (calc.X != 100 || calc.Y != 80 || calc.W != 1920 || calc.H != 900) return "clamp " + calc.X + "," + calc.Y + " " + calc.W + "x" + calc.H;
        if (!hidden.Min || hidden.X != -32000 || hidden.NormalX != 280 || hidden.NormalY != 100 || hidden.NormalW != 800) return "min restore " + hidden.NormalX + "," + hidden.NormalY + " min=" + hidden.Min;
        if (own.X != -3000 || own.Y != 400 || own.W != 420 || own.H != 560) return "note jumped " + own.X + "," + own.Y;
        code.W = 1000; code.H = 750;
        surface.ApplyHold(plan);
        if (code.W != 800 || code.H != 600 || code.X != 140) return "dpi resize stuck " + code.W + "x" + code.H;
        surface.Windows.Remove(own);
        int settled = surface.Settle(SessionLayout.MonitorsOf(before), SessionLayout.MonitorsOf(after));
        if (settled != 0 || !hidden.Min) return "settle fought the snapshot " + settled;
        surface.ShowRestored(4);
        if (hidden.X != 280 || hidden.Y != 100 || hidden.Min) return "show restored off-screen " + hidden.X + "," + hidden.Y;
        return "";
    }

    static ProfileMonitor? Host(int x, int y, IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (monitors == null) return null;
        foreach (var m in monitors)
            if (m.Connected && m.W > 0 && m.H > 0 && x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H) return m;
        return null;
    }

    static ProfileMonitor? Alive(string id, IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (monitors == null || id.Length == 0) return null;
        foreach (var m in monitors)
            if (m.Enabled && m.Connected && m.W > 0 && m.H > 0 && string.Equals(DeskArrange.Stable(m), id, StringComparison.OrdinalIgnoreCase))
                return m;
        return null;
    }

    static ProfileMonitor? AliveNumber(int number, IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (monitors == null) return null;
        foreach (var m in monitors)
            if (m.Enabled && m.Connected && m.W > 0 && m.Number == number) return m;
        return null;
    }

    static ProfileApp? RuleFor(HeldWindow held, IReadOnlyList<ProfileApp>? rules)
    {
        if (rules == null) return null;
        ProfileApp? best = null;
        int bestScore = 0;
        foreach (var app in rules)
        {
            int score = 0;
            if (app.Process.Length > 0)
            {
                if (!SameExe(app.Process, held.Process)) continue;
                score += 2;
            }
            if (app.Class.Length > 0)
            {
                if (!string.Equals(app.Class, held.Class, StringComparison.OrdinalIgnoreCase)) continue;
                score += 2;
            }
            if (app.Title.Length > 0)
            {
                if ((held.Title ?? "").IndexOf(app.Title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                score += 1;
            }
            if (score > bestScore) { bestScore = score; best = app; }
        }
        return bestScore >= 2 ? best : null;
    }

    static bool SameExe(string a, string b)
    {
        string Strip(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        return string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);
    }

    static ProfileMonitor? Nearest(HeldWindow held, IReadOnlyList<ProfileMonitor>? before, IReadOnlyList<ProfileMonitor>? after)
    {
        if (after == null) return null;
        ProfileMonitor? old = null;
        if (before != null)
            foreach (var m in before)
                if (string.Equals(DeskArrange.Stable(m), held.MonitorId, StringComparison.OrdinalIgnoreCase)) { old = m; break; }
        int ax = old?.X ?? held.AbsX;
        int ay = old?.Y ?? held.AbsY;
        ProfileMonitor? best = null;
        long bestD = long.MaxValue;
        foreach (var m in after)
        {
            if (!m.Enabled || !m.Connected || m.W <= 0 || m.H <= 0) continue;
            long dx = (long)m.X - ax, dy = (long)m.Y - ay;
            long d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    static ProfileBox WorkOf(ProfileMonitor m)
        => m.WorkW > 8 && m.WorkH > 8 ? new ProfileBox(m.WorkX, m.WorkY, m.WorkW, m.WorkH) : new ProfileBox(m.X, m.Y, m.W, m.H);

    static (int X, int Y, int W, int H) Clamp(int x, int y, int w, int h, ProfileBox area)
    {
        int limitW = Math.Max(1, area.W);
        int limitH = Math.Max(1, area.H);
        int cw = w < 1 ? Math.Min(800, limitW) : Math.Min(w, limitW);
        int ch = h < 1 ? Math.Min(600, limitH) : Math.Min(h, limitH);
        if (x < area.X) x = area.X;
        if (y < area.Y) y = area.Y;
        if (x + cw > area.X + area.W) x = area.X + area.W - cw;
        if (y + ch > area.Y + area.H) y = area.Y + area.H - ch;
        return (x, y, cw, ch);
    }
}
