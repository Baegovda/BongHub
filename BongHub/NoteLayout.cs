using System.Collections.Generic;
using System.Linq;

namespace BongHub;

/// <summary>Same clamp as 3.4.9 <c>Notes.Clamp</c>: keep the title strip on a work area, otherwise move into the nearest one.</summary>
public static class NoteLayout
{
    public readonly record struct Box(int X, int Y, int W, int H)
    {
        public int Right => X + W;
        public int Bottom => Y + H;
        public bool Contains(Box o) => o.W >= 0 && o.H >= 0 && o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;
    }

    public static Box Clamp(Box r, Box[] areas, int titleH)
    {
        if (areas.Length == 0) return r;
        var title = new Box(r.X, r.Y, r.W, Math.Max(8, titleH));
        foreach (var a in areas)
        {
            var i = Intersect(a, title);
            if ((long)i.W * i.H * 2 >= (long)title.W * title.H && i.H >= title.H) return r;
        }
        var best = areas[0];
        double bestD = double.MaxValue;
        int cx = r.X + r.W / 2, cy = r.Y + r.H / 2;
        foreach (var a in areas)
        {
            int dx = Math.Max(a.X - cx, Math.Max(0, cx - a.Right));
            int dy = Math.Max(a.Y - cy, Math.Max(0, cy - a.Bottom));
            double d = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (d < bestD) { bestD = d; best = a; }
        }
        int w = Math.Min(r.W, best.W), h = Math.Min(r.H, best.H);
        int x = Math.Max(best.X, Math.Min(r.X, best.Right - w));
        int y = Math.Max(best.Y, Math.Min(r.Y, best.Bottom - h));
        return new Box(x, y, w, h);
    }

    /// <summary>A work-area list that appears while Windows is still moving monitors. Empty, or only a short full-width band.</summary>
    public static bool IsTransient(Box[]? areas)
    {
        if (areas == null || areas.Length == 0) return true;
        return areas.All(a => a.W < 160 || a.H < 160);
    }

    /// <summary>
    /// Puts a note back after the topology settles. The saved width and height are never reduced.
    /// A transient or empty work area leaves the rectangle untouched. The anchor is a stable monitor id, not a GDI index.
    /// </summary>
    public static Box Rehome(Box saved, string? anchorId, IReadOnlyList<ProfileMonitor>? before, IReadOnlyList<ProfileMonitor>? after, bool transientWork)
    {
        if (transientWork || after == null || after.Count == 0) return saved;
        var oldMon = FindMonitor(anchorId, before) ?? Containing(saved, before);
        var next = FindMonitor(oldMon?.Path, after) ?? FindMonitor(anchorId, after) ?? Nearest(saved, after);
        if (next == null || !next.Connected) return saved;
        int relX = oldMon == null ? 0 : saved.X - oldMon.X;
        int relY = oldMon == null ? 0 : saved.Y - oldMon.Y;
        int x = next.X + relX;
        int y = next.Y + relY;
        if (saved.W <= next.W) x = Math.Max(next.X, Math.Min(x, next.X + next.W - saved.W));
        else x = next.X;
        if (saved.H <= next.H) y = Math.Max(next.Y, Math.Min(y, next.Y + next.H - saved.H));
        else y = next.Y;
        return new Box(x, y, saved.W, saved.H);
    }

    static ProfileMonitor? FindMonitor(string? id, IReadOnlyList<ProfileMonitor>? list)
    {
        if (string.IsNullOrEmpty(id) || list == null) return null;
        return list.FirstOrDefault(m => m.Connected && (string.Equals(m.Path, id, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Edid, id, StringComparison.OrdinalIgnoreCase)));
    }

    static ProfileMonitor? Containing(Box saved, IReadOnlyList<ProfileMonitor>? list)
    {
        if (list == null) return null;
        int cx = saved.X + saved.W / 2, cy = saved.Y + saved.H / 2;
        return list.FirstOrDefault(m => m.Connected && cx >= m.X && cy >= m.Y && cx < m.X + m.W && cy < m.Y + m.H);
    }

    static ProfileMonitor? Nearest(Box saved, IReadOnlyList<ProfileMonitor> list)
    {
        ProfileMonitor? best = null;
        long bestD = long.MaxValue;
        int cx = saved.X + saved.W / 2, cy = saved.Y + saved.H / 2;
        foreach (var m in list)
        {
            if (!m.Connected || !m.Enabled) continue;
            long dx = cx - (m.X + m.W / 2);
            long dy = cy - (m.Y + m.H / 2);
            long d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    /// <summary>The owner's note must survive a thin work area and a moved monitor without losing its size.</summary>
    public static string TopologyStory()
    {
        var saved = new Box(-4000, 200, 420, 560);
        var thin = new[] { new Box(-4800, 5, 3840, 40) };
        if (!IsTransient(thin)) return "thin band was treated as a real work area";
        var before = OwnerMonitors();
        var held = Rehome(saved, "P1", before, before, true);
        if (held != saved) return "transient work area changed the note";
        var empty = Rehome(saved, "P1", before, Array.Empty<ProfileMonitor>(), false);
        if (empty != saved) return "empty topology changed the note";
        var settled = Rehome(saved, "P1", before, before, false);
        if (settled.W != 420 || settled.H != 560 || settled.X != -4000 || settled.Y != 200) return "settled note was resized";
        var moved = OwnerMonitors();
        var host = moved.First(m => m.Path == "P1");
        host.X = 100; host.Y = 50;
        var shifted = Rehome(saved, "P1", before, moved, false);
        if (shifted.W != 420 || shifted.H != 560) return "moved monitor shrank the note";
        if (shifted.X != 100 + 800 || shifted.Y != 50 + 195) return "note left its monitor " + shifted.X + "," + shifted.Y;
        return "";
    }

    static List<ProfileMonitor> OwnerMonitors() => new()
    {
        new ProfileMonitor { Id = "P1", Path = "P1", Number = 1, X = -4800, Y = 5, W = 3840, H = 2160, Scale = 120 },
        new ProfileMonitor { Id = "P2", Path = "P2", Number = 2, X = 0, Y = 0, W = 3840, H = 2160, Primary = true, Scale = 100 },
    };

    static Box Intersect(Box a, Box b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 <= x1 || y2 <= y1) return new Box(0, 0, 0, 0);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }
}
