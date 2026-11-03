using System.Diagnostics;

namespace BongHub;

public sealed class AirProgram
{
    public string Name = "";
    public string Path = "";
}

/// <summary>Which files may sit on the dock. Only an exe or a shortcut. Nothing here starts a process.</summary>
public static class AirList
{
    public static bool Accept(string? path)
    {
        string file = FileName(path);
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    public static string Title(string path)
    {
        string file = FileName(path);
        int dot = file.LastIndexOf('.');
        if (dot > 0) file = file[..dot];
        return file.Length > 0 ? file : (path ?? "");
    }

    static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    public static bool Add(List<AirProgram> items, string? path)
    {
        if (items == null || !Accept(path)) return false;
        if (items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) return false;
        items.Add(new AirProgram { Name = Title(path!), Path = path! });
        return true;
    }

    public static bool RemoveAt(List<AirProgram> items, int index)
    {
        if (items == null || index < 0 || index >= items.Count) return false;
        items.RemoveAt(index);
        return true;
    }

    public static bool Move(List<AirProgram> items, int from, int to)
    {
        if (items == null || from < 0 || to < 0 || from >= items.Count || to >= items.Count || from == to) return false;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
        return true;
    }
}

public readonly record struct AirBox(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public bool Hits(AirBox other)
    {
        int w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        int h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w > 0 && h > 0;
    }
    public bool Contains(double x, double y) => x >= X && y >= Y && x < Right && y < Bottom;
}

public sealed class AirCells
{
    public int Width, Height, Icon;
    public AirBox[] Icons = Array.Empty<AirBox>();
    public AirBox[] Labels = Array.Empty<AirBox>();

    public int Hit(double x, double y)
    {
        for (int i = 0; i < Icons.Length; i++)
            if (Icons[i].Contains(x, y) || Labels[i].Contains(x, y)) return i;
        return -1;
    }
}

public sealed class AirFrame
{
    public int X, Y, W, H;
    public int Right => X + W;
    public int Bottom => Y + H;
}

/// <summary>
/// Dock geometry in device-independent pixels, then placed in physical pixels on the monitor under the cursor.
/// A 125% monitor grows the same layout. The dock is pushed back inside that monitor.
/// </summary>
public static class AirDockLayout
{
    public const int Pad = 16, Gap = 14, LabelGap = 6, LabelH = 16;

    public static int ClampIcon(int dip) => Math.Clamp(dip, 40, 88);

    public static AirCells Cells(int iconDip, int count)
    {
        int icon = ClampIcon(iconDip);
        int n = Math.Max(0, count);
        var cells = new AirCells { Icon = icon };
        cells.Icons = new AirBox[n];
        cells.Labels = new AirBox[n];
        for (int i = 0; i < n; i++)
        {
            int x = Pad + i * (icon + Gap);
            cells.Icons[i] = new AirBox(x, Pad, icon, icon);
            cells.Labels[i] = new AirBox(x, Pad + icon + LabelGap, icon, LabelH);
        }
        cells.Width = Pad * 2 + (n == 0 ? icon : n * icon + Math.Max(0, n - 1) * Gap);
        cells.Height = Pad * 2 + icon + LabelGap + LabelH;
        return cells;
    }

    public static AirFrame Place(int cursorX, int cursorY, int monX, int monY, int monW, int monH, int dpiPercent, int iconDip, int count)
    {
        var cells = Cells(iconDip, count);
        double scale = Math.Max(96, dpiPercent) / 100.0;
        int width = Math.Max(1, (int)Math.Round(cells.Width * scale));
        int height = Math.Max(1, (int)Math.Round(cells.Height * scale));
        int margin = Math.Max(1, (int)Math.Round(8 * scale));
        int x = cursorX - width / 2;
        int y = cursorY - height / 2;
        int left = monX + margin;
        int top = monY + margin;
        int right = monX + Math.Max(1, monW) - margin;
        int bottom = monY + Math.Max(1, monH) - margin;
        if (width >= monW) x = monX;
        else
        {
            if (x < left) x = left;
            if (x + width > right) x = right - width;
        }
        if (height >= monH) y = monY;
        else
        {
            if (y < top) y = top;
            if (y + height > bottom) y = bottom - height;
        }
        return new AirFrame { X = x, Y = y, W = width, H = height };
    }

    public static string Story()
    {
        var cells = Cells(56, 3);
        if (cells.Icon != 56 || cells.Icons.Length != 3) return "cells";
        for (int i = 0; i < 3; i++)
        {
            if (cells.Icons[i].Hits(cells.Labels[i])) return "label covers its icon";
            for (int j = i + 1; j < 3; j++)
            {
                if (cells.Icons[i].Hits(cells.Icons[j]) || cells.Labels[i].Hits(cells.Labels[j])) return "neighbors overlap";
                if (cells.Icons[i].Hits(cells.Labels[j]) || cells.Labels[i].Hits(cells.Icons[j])) return "label meets another icon";
            }
        }
        if (cells.Hit(cells.Icons[1].X + 4, cells.Icons[1].Y + 4) != 1) return "missed the second icon";
        if (cells.Hit(4, 4) != -1) return "padding counted as an item";
        var high = Place(1960, 20, 1920, 0, 1920, 1080, 125, 56, 3);
        if (high.X < 1920 || high.Right > 1920 + 1920 || high.Y < 0 || high.Bottom > 1080) return "left the 125% monitor " + high.X + "," + high.Y + " " + high.W + "x" + high.H;
        int iconPx = (int)Math.Round(56 * 1.25);
        if (high.W < iconPx * 3) return "dpi did not grow the dock";
        var low = Place(40, 40, 0, 0, 1920, 1080, 100, 56, 3);
        if (low.X < 0 || low.Right > 1920) return "left the 100% monitor";
        if (low.W >= high.W) return "100% dock was not smaller";
        var neg = Place(-100, -100, -1920, -1080, 1920, 1080, 100, 56, 3);
        if (neg.X < -1920 || neg.Right > 0 || neg.Y < -1080 || neg.Bottom > 0) return "left the negative monitor";
        var list = new List<AirProgram>();
        if (!AirList.Add(list, @"C:\Fake\Notes.exe") || list[0].Name != "Notes") return "exe";
        if (AirList.Add(list, @"C:\Fake\Notes.exe")) return "duplicate";
        if (AirList.Add(list, @"C:\Fake\readme.txt")) return "text file";
        if (!AirList.Add(list, @"C:\Fake\Calc.lnk") || !AirList.Move(list, 0, 1) || list[0].Name != "Calc" || list[1].Name != "Notes") return "reorder";
        var session = new AirDockSession();
        session.OpenWith(list.Count);
        session.Move(1);
        if (session.Release(list) != list[1].Path || session.Open) return "release did not launch the hovered program";
        session.OpenWith(list.Count);
        session.Move(-1);
        if (session.Release(list) != null || session.Open) return "empty release launched";
        session.OpenWith(list.Count);
        session.Move(0);
        session.Escape();
        if (session.Open || session.Launched != null) return "esc launched";
        session.OpenWith(list.Count);
        if (session.Click(0, list) != list[0].Path || session.Open) return "click";
        if (AirDockLaunch.NativeStarts != 0 || AirDockLaunch.Attempts != 0) return "a real launch was counted";
        return "";
    }
}

/// <summary>Open, hover, click, and release. The path is returned to the caller. This type never starts a process.</summary>
public sealed class AirDockSession
{
    public bool Open { get; private set; }
    public int Hover { get; private set; } = -1;
    public string? Launched { get; private set; }

    public void OpenWith(int count)
    {
        Open = count > 0;
        Hover = -1;
        Launched = null;
    }

    public void Move(int hit) { if (Open) Hover = hit; }

    public string? Click(int index, IReadOnlyList<AirProgram> items)
    {
        if (!Open || items == null || index < 0 || index >= items.Count) return null;
        Launched = items[index].Path;
        Open = false;
        return Launched;
    }

    public string? Release(IReadOnlyList<AirProgram> items)
    {
        if (!Open) return null;
        string? path = Hover >= 0 && items != null && Hover < items.Count ? items[Hover].Path : null;
        Launched = path;
        Open = false;
        return path;
    }

    public void Escape()
    {
        Open = false;
        Hover = -1;
        Launched = null;
    }
}

/// <summary>The only place a dock item is started. Tests leave <see cref="AllowNative"/> false.</summary>
public static class AirDockLaunch
{
    public static int Attempts, NativeStarts;
    public static bool AllowNative { get; set; }
    public static Func<string, bool>? Fake;

    public static bool Run(string? path)
    {
        Attempts++;
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Fake != null) return Fake(path);
        if (!AllowNative) return false;
        NativeStarts++;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
