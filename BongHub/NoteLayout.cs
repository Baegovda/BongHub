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

    static Box Intersect(Box a, Box b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 <= x1 || y2 <= y1) return new Box(0, 0, 0, 0);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }
}
