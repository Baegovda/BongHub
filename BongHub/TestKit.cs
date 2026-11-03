using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BongHub;

/// <summary>Shared helpers for the in-process tests (render-to-bitmap, pixel contrast, result log).</summary>
public sealed class TestKit
{
    readonly StringBuilder sb = new();
    public int Fails;
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public void Check(string name, bool ok, string detail = "") { if (!ok) Fails++; sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}"); }
    public void Info(string text) => sb.AppendLine("INFO  " + text);
    public void Save(string file)
    {
        sb.Insert(0, $"{(Fails == 0 ? "RESULT PASS" : "RESULT FAIL (" + Fails + ")")}\r\nBongHub {AppEnv.Version}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  data={AppEnv.DataDir}\r\n\r\n");
        File.WriteAllText(Path.Combine(AppEnv.OutDir!, file), sb.ToString(), new UTF8Encoding(true));
    }
    public static Task Delay(int ms) => Task.Delay(ms);
    public static async Task<bool> Until(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond()) { if (sw.ElapsedMilliseconds > timeoutMs) return false; await Task.Delay(15); }
        return true;
    }

    /// <summary>Renders the window's visual (what WPF would hand to DWM) and composites it over <paramref name="under"/>.</summary>
    public static (byte[] px, int w, int h, double scale) Render(Window win, Color under)
    {
        double s = Win32.Scale(win);
        int w = (int)Math.Ceiling(win.ActualWidth * s), h = (int)Math.Ceiling(win.ActualHeight * s);
        var layer = new RenderTargetBitmap(w, h, 96 * s, 96 * s, PixelFormats.Pbgra32);
        layer.Render(win);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(under), null, new Rect(0, 0, win.ActualWidth, win.ActualHeight));
            dc.DrawImage(layer, new Rect(0, 0, win.ActualWidth, win.ActualHeight));
        }
        var rtb = new RenderTargetBitmap(w, h, 96 * s, 96 * s, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4]; rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h, s);
    }
    public static void SavePng(Window win, Color under, string file)
    {
        var (px, w, h, s) = Render(win, under);
        var bmp = BitmapSource.Create(w, h, 96 * s, 96 * s, PixelFormats.Pbgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(Path.Combine(AppEnv.OutDir!, file)); enc.Save(f);
    }
    /// <summary>% of pixels that are pure/near-pure white (all channels ≥ 250). UI text is #F0F0F6/#F5F7FA, so it is not counted.</summary>
    public static double WhitePercent((byte[] px, int w, int h, double s) r)
    {
        long white = 0, n = (long)r.w * r.h;
        for (int i = 0; i < r.px.Length; i += 4) if (r.px[i] >= 250 && r.px[i + 1] >= 250 && r.px[i + 2] >= 250) white++;
        return n == 0 ? 100 : white * 100.0 / n;
    }
    /// <summary>Luminance of the dominant (background) colour.</summary>
    public static double DominantLum((byte[] px, int w, int h, double s) r)
    {
        var hist = new Dictionary<int, int>();
        for (int i = 0; i < r.px.Length; i += 4) { int k = ((r.px[i + 2] >> 3) << 10) | ((r.px[i + 1] >> 3) << 5) | (r.px[i] >> 3); hist[k] = hist.GetValueOrDefault(k) + 1; }
        int top = hist.MaxBy(kv => kv.Value).Key;
        return NoteColors.Lum(Color.FromRgb((byte)((top >> 10) << 3), (byte)(((top >> 5) & 31) << 3), (byte)((top & 31) << 3)));
    }
    public static double MeanLum((byte[] px, int w, int h, double s) r)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < r.px.Length; i += 16) { sum += NoteColors.Lum(Color.FromRgb(r.px[i + 2], r.px[i + 1], r.px[i])); n++; }
        return n == 0 ? 1 : sum / n;
    }

    /// <summary>
    /// Real measured contrast of rendered text: background = most frequent colour inside the element's box,
    /// text = the pixel with the highest contrast against it (fully-covered glyph stems).
    /// </summary>
    public static (double ratio, string fg, string bg) Contrast(Window win, FrameworkElement el, Color under)
    {
        var r = Render(win, under);
        Rect box;
        try { box = el.TransformToAncestor(win).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); } catch { return (0, "-", "-"); }
        int x0 = Math.Max(0, (int)(box.X * r.scale)), y0 = Math.Max(0, (int)(box.Y * r.scale));
        int x1 = Math.Min(r.w, (int)((box.X + box.Width) * r.scale)), y1 = Math.Min(r.h, (int)((box.Y + box.Height) * r.scale));
        if (x1 <= x0 || y1 <= y0) return (0, "-", "-");
        var hist = new Dictionary<int, int>();
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { int i = (y * r.w + x) * 4; int k = (r.px[i + 2] << 16) | (r.px[i + 1] << 8) | r.px[i]; hist[k] = hist.GetValueOrDefault(k) + 1; }
        int bgk = hist.MaxBy(kv => kv.Value).Key;
        var bg = Color.FromRgb((byte)(bgk >> 16), (byte)(bgk >> 8), (byte)bgk);
        double lb = NoteColors.Lum(bg), best = 1; int bestK = bgk;
        foreach (var k in hist.Keys)
        {
            var c = Color.FromRgb((byte)(k >> 16), (byte)(k >> 8), (byte)k); double lc = NoteColors.Lum(c);
            double ratio = (Math.Max(lb, lc) + 0.05) / (Math.Min(lb, lc) + 0.05);
            if (ratio > best) { best = ratio; bestK = k; }
        }
        return (best, "#" + bestK.ToString("X6"), "#" + bgk.ToString("X6"));
    }
    /// <summary>Renders a free element (menu chrome, balloon) at 96dpi and composites it over <paramref name="under"/>.</summary>
    public static (byte[] px, int w, int h, double s) RenderElement(FrameworkElement el, Color under)
    {
        ApplyTemplates(el);
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(0, 0, Math.Max(1, el.DesiredSize.Width), Math.Max(1, el.DesiredSize.Height)));
        el.UpdateLayout();
        int w = Math.Max(1, (int)Math.Ceiling(el.RenderSize.Width));
        int h = Math.Max(1, (int)Math.Ceiling(el.RenderSize.Height));
        var layer = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        layer.Render(el);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(under), null, new Rect(0, 0, w, h));
            dc.DrawImage(layer, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h, 1);
    }
    static void ApplyTemplates(DependencyObject node)
    {
        if (node is FrameworkElement fe) fe.ApplyTemplate();
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) ApplyTemplates(child);
    }

    public static (double ratio, string fg, string bg) ContrastElement(FrameworkElement root, FrameworkElement el, Color under)
    {
        var r = RenderElement(root, under);
        Rect box;
        try { box = el.TransformToAncestor(root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); }
        catch { return (0, "-", "-"); }
        int x0 = Math.Max(0, (int)box.X), y0 = Math.Max(0, (int)box.Y);
        int x1 = Math.Min(r.w, (int)Math.Ceiling(box.Right)), y1 = Math.Min(r.h, (int)Math.Ceiling(box.Bottom));
        if (x1 <= x0 || y1 <= y0) return (0, "-", "-");
        var hist = new Dictionary<int, int>();
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { int i = (y * r.w + x) * 4; int k = (r.px[i + 2] << 16) | (r.px[i + 1] << 8) | r.px[i]; hist[k] = hist.GetValueOrDefault(k) + 1; }
        int bgk = hist.MaxBy(kv => kv.Value).Key;
        var bg = Color.FromRgb((byte)(bgk >> 16), (byte)(bgk >> 8), (byte)bgk);
        double lb = NoteColors.Lum(bg), best = 1; int bestK = bgk;
        foreach (var k in hist.Keys)
        {
            var c = Color.FromRgb((byte)(k >> 16), (byte)(k >> 8), (byte)k); double lc = NoteColors.Lum(c);
            double ratio = (Math.Max(lb, lc) + 0.05) / (Math.Min(lb, lc) + 0.05);
            if (ratio > best) { best = ratio; bestK = k; }
        }
        return (best, "#" + bestK.ToString("X6"), "#" + bgk.ToString("X6"));
    }

    public static bool NotForeground(Window w) => Win32.GetForegroundWindow() != Win32.Handle(w) && !w.IsActive;

    /// <summary>
    /// Passive shows must not be foreground. Waits through the uncloak race, then returns
    /// null on success or a foreground/class/title/process/exstyle line on failure.
    /// </summary>
    public static async Task<string?> WaitNotForeground(Window w, int timeoutMs = 500)
    {
        var sw = Stopwatch.StartNew();
        bool ok = await Until(() => NotForeground(w), timeoutMs);
        return ok ? null : Win32.FocusEvidence(w, sw.ElapsedMilliseconds);
    }
    public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, () => { });
}
