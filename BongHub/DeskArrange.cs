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

    public static int Restore(IReadOnlyList<SessionWindow>? shot, IReadOnlyList<PickProbe>? probes, IWindowSurface surface, IReadOnlyList<ProfileMonitor>? monitors)
    {
        if (shot == null || probes == null || monitors == null) return 0;
        int n = 0;
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
            n++;
        }
        return n;
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
