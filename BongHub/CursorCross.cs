namespace BongHub;

/// <summary>One monitor for cursor crossing. Millimeters come from EDID. Tests pass them in; nothing here reads a device.</summary>
public sealed class EdgeMonitor
{
    public string Id = "";
    public int X, Y, W, H;
    public int WidthMm, HeightMm;
    /// <summary>Room width and height in millimetres. Zero keeps the EDID size.</summary>
    public int OverrideWmm, OverrideHmm;
    /// <summary>Extra physical shift, in millimetres, from the pixel layout.</summary>
    public double ShiftXmm, ShiftYmm;
    /// <summary>0, 90, 180, or 270. 90 and 270 swap which EDID side is the room width. Nothing reverses the room axis.</summary>
    public int Orientation;
    public int Right => X + W;
    public int Bottom => Y + H;
}

public readonly record struct CursorPoint(int X, int Y);

/// <summary>
/// When the pointer crosses a shared edge, move it along that edge so it meets the same physical spot.
/// Movement inside one monitor is left alone. A stretch of edge with no neighbor is left alone.
/// </summary>
public static class CursorCross
{
    /// <summary>Monitors on this desk touch, or miss by a pixel. A wider gap is not a shared edge.</summary>
    public const int Touch = 2;

    public static CursorPoint Step(IReadOnlyList<EdgeMonitor>? screens, CursorPoint from, CursorPoint to)
    {
        if (screens == null || screens.Count == 0 || from == to) return to;
        var src = Hit(screens, from);
        var dst = Hit(screens, to);
        if (src == null || dst == null || ReferenceEquals(src, dst)) return to;
        if (AxisMm(src, true) <= 0 || AxisMm(src, false) <= 0 || AxisMm(dst, true) <= 0 || AxisMm(dst, false) <= 0) return to;
        if (TrySide(src, dst, from, out int y)) return new CursorPoint(to.X, y);
        if (TryStack(src, dst, from, out int x)) return new CursorPoint(x, to.Y);
        return to;
    }

    public static string Story()
    {
        var bottomLeft = M("4k-l", -3840, 0, 3840, 2160, 600, 340, 0);
        var bottomRight = M("4k-r", 0, 0, 3840, 2160, 600, 340, 0);
        var topLeft = M("1080-l", -1921, -1080, 1920, 1080, 600, 340, 0);
        var topRight = M("1080-r", -1, -1080, 1920, 1080, 600, 340, 180);
        var small = M("small", 1919, -1096, 960, 640, 210, 140, 0);
        var desk = new[] { bottomLeft, bottomRight, topLeft, topRight, small };

        var inside = Step(desk, new CursorPoint(-2000, 400), new CursorPoint(-1500, 800));
        if (inside != new CursorPoint(-1500, 800)) return "movement inside a monitor was changed";

        // The 1080p sits on the right half of the left 4K. Same physical width, half the pixels.
        // The middle of that shared edge is 150mm into both panels, which is 960px on the 4K and 480px on the 1080p.
        // 960px into the shared edge is 150mm on the 4K and lands 480px (150mm) into the 1080p.
        var up = Step(desk, new CursorPoint(-961, 2), new CursorPoint(-961, -2));
        if (up.Y != -2 || up.X != -1441) return "up cross " + up.X + "," + up.Y;
        topLeft.ShiftXmm = 10;
        var shifted = Step(desk, new CursorPoint(-961, 2), new CursorPoint(-961, -2));
        topLeft.ShiftXmm = 0;
        if (shifted.Y != -2 || shifted.X != -1473) return "shift " + shifted.X + "," + shifted.Y;

        var down = Step(desk, new CursorPoint(-1441, -2), new CursorPoint(-1441, 2));
        if (down.Y != 2 || down.X != -961) return "down cross " + down.X + "," + down.Y;

        // 180 keeps the room axis. The same pixel along the bezel stays put when the panels match.
        var right = Step(desk, new CursorPoint(-2, -540), new CursorPoint(2, -540));
        if (right.X != 2 || right.Y != -540) return "180 edge moved a matching bezel " + right.X + "," + right.Y;

        var rightOff = Step(desk, new CursorPoint(-2, 200), new CursorPoint(2, 200));
        if (rightOff != new CursorPoint(2, 200)) return "matching 4K edges moved " + rightOff.X + "," + rightOff.Y;

        var gap = Step(desk, new CursorPoint(2400, -460), new CursorPoint(2400, 20));
        if (gap != new CursorPoint(2400, 20)) return "a vertical gap was treated as a shared edge";

        var noSize = new[] { M("a", 0, 0, 1920, 1080, 0, 0, 0), M("b", 1920, 0, 1920, 1080, 500, 300, 0) };
        var plain = Step(noSize, new CursorPoint(1910, 100), new CursorPoint(1930, 100));
        if (plain != new CursorPoint(1930, 100)) return "missing EDID still warped";

        var portrait = new[]
        {
            M("land", 0, 0, 1920, 1080, 480, 270, 0),
            M("port", 1920, 0, 1080, 1920, 400, 220, 90)
        };
        var side = Step(portrait, new CursorPoint(1918, 270), new CursorPoint(1922, 270));
        if (side.X != 1922 || side.Y != 324) return "portrait axis was not swapped " + side.X + "," + side.Y;
        var back = Step(portrait, new CursorPoint(1922, 324), new CursorPoint(1918, 324));
        if (back.X != 1918 || back.Y != 270) return "left cross " + back.X + "," + back.Y;

        // A 90° panel above a landscape one. 200px is 40.7mm from the left, which is 163px on the landscape.
        var stacked = new[]
        {
            M("port", 0, -1920, 1080, 1920, 400, 220, 90),
            M("land", 0, 0, 1920, 1080, 480, 270, 0)
        };
        var downPort = Step(stacked, new CursorPoint(200, -2), new CursorPoint(200, 2));
        if (downPort.Y != 2 || downPort.X != 163) return "portrait stack mirrored " + downPort.X + "," + downPort.Y;

        var partial = new[]
        {
            M("tall", 0, 0, 1000, 2000, 500, 400, 0),
            M("short", 1000, 800, 1000, 600, 500, 200, 0)
        };
        var miss = Step(partial, new CursorPoint(990, 100), new CursorPoint(1010, 900));
        if (miss != new CursorPoint(1010, 900)) return "a non-overlapping stretch was warped " + miss.X + "," + miss.Y;
        var shared = Step(partial, new CursorPoint(990, 1000), new CursorPoint(1010, 1000));
        if (shared.X != 1010 || shared.Y != 920) return "shared stretch " + shared.X + "," + shared.Y;

        var seam = new[]
        {
            M("a", 0, 0, 1000, 500, 200, 100, 0),
            M("b", 1001, 0, 1000, 500, 400, 200, 0)
        };
        var across = Step(seam, new CursorPoint(998, 100), new CursorPoint(1003, 100));
        if (across.X != 1003 || across.Y != 50) return "1px seam " + across.X + "," + across.Y;
        var wide = new[]
        {
            M("a", 0, 0, 1000, 500, 200, 100, 0),
            M("b", 1003, 0, 1000, 500, 400, 200, 0)
        };
        var skipped = Step(wide, new CursorPoint(998, 100), new CursorPoint(1010, 100));
        if (skipped != new CursorPoint(1010, 100)) return "a 3px gap was a shared edge " + skipped.X + "," + skipped.Y;

        var bare = new[] { M("a", 0, 0, 1920, 1080, 0, 0, 0), M("b", 1920, 0, 1920, 1080, 0, 0, 0) };
        bare[0].OverrideWmm = 600; bare[0].OverrideHmm = 300;
        bare[1].OverrideWmm = 600; bare[1].OverrideHmm = 150;
        var typed = Step(bare, new CursorPoint(1910, 100), new CursorPoint(1930, 100));
        if (typed.X != 1930 || typed.Y != 200) return "override without EDID " + typed.X + "," + typed.Y;
        var inches = MmFromDiagonal(27, 3840, 2160);
        if (inches.W != 598 || inches.H != 336) return "diagonal " + inches.W + "x" + inches.H;

        var laid = Arrange(desk);
        var leftBox = laid.First(b => b.Id == "4k-l");
        var topBox = laid.First(b => b.Id == "1080-l");
        if (Math.Abs(leftBox.X + 600) > 0.2 || Math.Abs(leftBox.Y) > 0.2) return "layout left " + leftBox.X + "," + leftBox.Y;
        if (Math.Abs(topBox.Y + 340) > 0.2 || Math.Abs(topBox.X + 300.16) > 0.6) return "layout top " + topBox.X + "," + topBox.Y;
        topLeft.ShiftXmm = 12;
        var dragged = Arrange(desk).First(b => b.Id == "1080-l");
        topLeft.ShiftXmm = 0;
        if (Math.Abs(dragged.X - (topBox.X + 12)) > 0.2) return "drag " + dragged.X;
        return "";
    }

    public static EdgeMonitor[] SampleDesk() => new[]
    {
        M("4k-l", -3840, 0, 3840, 2160, 600, 340, 0),
        M("4k-r", 0, 0, 3840, 2160, 600, 340, 0),
        M("1080-l", -1921, -1080, 1920, 1080, 600, 340, 0),
        M("1080-r", -1, -1080, 1920, 1080, 600, 340, 180),
        M("small", 1919, -1096, 960, 640, 210, 140, 0)
    };

    public readonly record struct PanelMm(int W, int H);

    /// <summary>16:9 at 27 inches is 598×336 mm. Pixel width and height are the room size.</summary>
    public static PanelMm MmFromDiagonal(double inches, int pixelW, int pixelH)
    {
        if (inches <= 0 || pixelW <= 0 || pixelH <= 0) return new PanelMm(0, 0);
        double diag = inches * 25.4;
        double aspect = pixelW / (double)pixelH;
        double h = diag / Math.Sqrt(aspect * aspect + 1.0);
        return new PanelMm((int)Math.Round(h * aspect), (int)Math.Round(h));
    }

    public static double DiagonalInches(double widthMm, double heightMm)
    {
        if (widthMm <= 0 || heightMm <= 0) return 0;
        return Math.Sqrt(widthMm * widthMm + heightMm * heightMm) / 25.4;
    }

    public readonly record struct PhysBox(string Id, double X, double Y, double W, double H);

    /// <summary>Places each panel in millimetres from the pixel seams, then adds the user's shift.</summary>
    public static List<PhysBox> Arrange(IReadOnlyList<EdgeMonitor> screens)
    {
        var placed = new Dictionary<EdgeMonitor, (double X, double Y)>();
        if (screens == null || screens.Count == 0) return new List<PhysBox>();
        var root = screens.FirstOrDefault(s => s.X <= 0 && s.Right > 0 && s.Y <= 0 && s.Bottom > 0) ?? screens[0];
        var queue = new Queue<EdgeMonitor>();
        queue.Enqueue(root);
        placed[root] = (root.ShiftXmm, root.ShiftYmm);
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            var (ax, ay) = placed[a];
            foreach (var b in screens)
            {
                if (placed.ContainsKey(b) || ReferenceEquals(a, b)) continue;
                if (!TryOffset(a, b, ax, ay, out double bx, out double by)) continue;
                placed[b] = (bx + b.ShiftXmm, by + b.ShiftYmm);
                queue.Enqueue(b);
            }
        }
        double right = placed.Count == 0 ? 0 : placed.Max(kv => kv.Value.X + Room(kv.Key, true));
        foreach (var s in screens)
        {
            if (placed.ContainsKey(s)) continue;
            placed[s] = (right + s.ShiftXmm, s.ShiftYmm);
            right += Room(s, true) + 12;
        }
        return screens.Select(s =>
        {
            var p = placed[s];
            return new PhysBox(s.Id, p.X, p.Y, Room(s, true), Room(s, false));
        }).ToList();
    }

    static EdgeMonitor M(string id, int x, int y, int w, int h, int wmm, int hmm, int orientation)
        => new() { Id = id, X = x, Y = y, W = w, H = h, WidthMm = wmm, HeightMm = hmm, Orientation = orientation };

    static bool TrySide(EdgeMonitor a, EdgeMonitor b, CursorPoint from, out int y)
    {
        y = from.Y;
        if (!Beside(a, b) && !Beside(b, a)) return false;
        int top = Math.Max(a.Y, b.Y);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        if (bottom - top < 2 || from.Y < top || from.Y > bottom) return false;
        y = Map(a, b, from.Y, top, bottom, horizontal: false);
        if (y < top) y = top;
        if (y > bottom) y = bottom;
        return true;
    }

    static bool TryStack(EdgeMonitor a, EdgeMonitor b, CursorPoint from, out int x)
    {
        x = from.X;
        if (!Stacked(a, b) && !Stacked(b, a)) return false;
        int left = Math.Max(a.X, b.X);
        int right = Math.Min(a.Right, b.Right);
        if (right - left < 2 || from.X < left || from.X > right) return false;
        x = Map(a, b, from.X, left, right, horizontal: true);
        if (x < left) x = left;
        if (x > right) x = right;
        return true;
    }

    static int Map(EdgeMonitor src, EdgeMonitor dst, int pixel, int start, int end, bool horizontal)
    {
        double srcPer = MmPerPx(src, horizontal);
        double dstPer = MmPerPx(dst, horizontal);
        if (srcPer <= 0 || dstPer <= 0) return pixel;
        double srcShift = horizontal ? src.ShiftXmm : src.ShiftYmm;
        double dstShift = horizontal ? dst.ShiftXmm : dst.ShiftYmm;
        double rel = (pixel - start) * srcPer + srcShift - dstShift;
        int alongPx = (int)Math.Round(rel / dstPer);
        int at = start + alongPx;
        if (at < start) at = start;
        if (at > end) at = end;
        return at;
    }

    static double MmPerPx(EdgeMonitor s, bool horizontal)
    {
        double mm = AxisMm(s, horizontal);
        int span = Math.Max(1, horizontal ? s.W : s.H);
        return mm / span;
    }

    static bool Beside(EdgeMonitor left, EdgeMonitor right)
    {
        int gap = right.X - left.Right;
        if (gap > Touch || gap < -Touch) return false;
        return Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y) > 0;
    }

    static bool Stacked(EdgeMonitor above, EdgeMonitor below)
    {
        int gap = below.Y - above.Bottom;
        if (gap > Touch || gap < -Touch) return false;
        return Math.Min(above.Right, below.Right) - Math.Max(above.X, below.X) > 0;
    }

    static EdgeMonitor? Hit(IReadOnlyList<EdgeMonitor> screens, CursorPoint p)
    {
        foreach (var s in screens)
            if (s.W > 0 && s.H > 0 && p.X >= s.X && p.Y >= s.Y && p.X < s.Right && p.Y < s.Bottom)
                return s;
        return null;
    }

    static bool TryOffset(EdgeMonitor a, EdgeMonitor b, double ax, double ay, out double bx, out double by)
    {
        bx = by = 0;
        double aw = Room(a, true), ah = Room(a, false), bw = Room(b, true), bh = Room(b, false);
        if (Beside(a, b))
        {
            bx = ax + aw;
            by = ay + (b.Y - a.Y) * (ah / Math.Max(1, a.H));
            return true;
        }
        if (Beside(b, a))
        {
            bx = ax - bw;
            by = ay + (b.Y - a.Y) * (ah / Math.Max(1, a.H));
            return true;
        }
        if (Stacked(a, b))
        {
            by = ay + ah;
            bx = ax + (b.X - a.X) * (aw / Math.Max(1, a.W));
            return true;
        }
        if (Stacked(b, a))
        {
            by = ay - bh;
            bx = ax + (b.X - a.X) * (aw / Math.Max(1, a.W));
            return true;
        }
        return false;
    }

    static double Room(EdgeMonitor s, bool horizontal)
    {
        double mm = AxisMm(s, horizontal);
        if (mm > 0) return mm;
        return Math.Max(40, (horizontal ? s.W : s.H) * 0.15);
    }

    static double AxisMm(EdgeMonitor s, bool horizontal)
    {
        if (horizontal && s.OverrideWmm > 0) return s.OverrideWmm;
        if (!horizontal && s.OverrideHmm > 0) return s.OverrideHmm;
        int o = ((s.Orientation % 360) + 360) % 360;
        bool portrait = o is 90 or 270;
        double mm = horizontal
            ? (portrait ? s.HeightMm : s.WidthMm)
            : (portrait ? s.WidthMm : s.HeightMm);
        return Math.Max(0, mm);
    }

}
