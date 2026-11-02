using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BongHub;

/// <summary>
/// New pictures are stored as <c>\pngblip</c> or <c>\jpegblip</c>. Files already at or under 1600px on the long edge
/// keep their bytes. Larger files are scaled once, then those encoded bytes are what the note saves.
/// Display size is <c>\picwgoal</c>/<c>\pichgoal</c> only.
/// </summary>
public static class NoteImages
{
    public const int MaxEdge = 1600;
    public const long WarnBytes = 8L * 1024 * 1024;
    static readonly string[] Ext = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    public static bool IsImagePath(string path)
    {
        var e = Path.GetExtension(path) ?? "";
        return Ext.Contains(e, StringComparer.OrdinalIgnoreCase);
    }

    public static bool OverWarn(long bytes) => bytes > WarnBytes;

    /// <summary>100% size in twips: one source pixel at 96dpi is 15 twips. Matches 3.4.9 <c>SetImageScale</c>.</summary>
    public static (int GoalW, int GoalH) ScaleGoals(int srcW, int srcH, double factor)
    {
        double f = factor <= 0 ? 1 : factor;
        int w = Math.Max(1, (int)Math.Round(Math.Max(1, srcW) * f * 15.0));
        int h = Math.Max(1, (int)Math.Round(Math.Max(1, srcH) * f * 15.0));
        return (w, h);
    }

    /// <summary>Fit the current display width (DIP) to <paramref name="maxDip"/> and keep the ratio.</summary>
    public static (int GoalW, int GoalH) FitGoals(int curGoalW, int curGoalH, double maxDip)
    {
        double w = curGoalW > 0 ? curGoalW / 15.0 : maxDip;
        double h = curGoalH > 0 ? curGoalH / 15.0 : maxDip;
        if (w < 1) w = 1;
        double dip = Math.Max(24, maxDip);
        double nh = h * (dip / w);
        return (Math.Max(1, (int)Math.Round(dip * 15.0)), Math.Max(1, (int)Math.Round(nh * 15.0)));
    }

    public sealed class Built
    {
        public byte[] Bytes = [];
        public bool Jpeg;
        public int PxW, PxH, GoalW, GoalH;
        public string Group = "";
    }

    /// <summary>Embed a png/jpeg file as-is when it is already small enough. Otherwise decode and encode.</summary>
    public static Built? FromFile(string path, int fitDip)
    {
        if (!File.Exists(path) || !IsImagePath(path)) return null;
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 8) return null;
        bool jpegFile = raw.Length > 2 && raw[0] == 0xFF && raw[1] == 0xD8;
        bool pngFile = raw.Length > 8 && raw[0] == 0x89 && raw[1] == (byte)'P';
        var (sw, sh) = Peek(raw);
        if ((jpegFile || pngFile) && sw > 0 && sh > 0 && Math.Max(sw, sh) <= MaxEdge)
        {
            var (gw, gh) = FitDisplay(sw, sh, fitDip);
            return Finish(raw, jpegFile, sw, sh, gw, gh);
        }
        try
        {
            var bmp = Load(raw, MaxEdge);
            return bmp == null ? null : FromBitmap(bmp, fitDip);
        }
        catch (Exception e) { AppEnv.Log("image file: " + e.Message); return null; }
    }

    public static Built? FromBitmap(BitmapSource src, int fitDip)
    {
        try
        {
            var scaled = Scale(src, MaxEdge);
            bool alpha = HasAlpha(scaled);
            byte[] png = EncodePng(scaled);
            byte[]? jpg = null;
            if (!alpha && png.Length > 200 * 1024) jpg = EncodeJpeg(scaled, 86);
            bool useJpg = jpg != null && jpg.Length < png.Length * 0.8;
            byte[] data = useJpg ? jpg! : png;
            var (gw, gh) = FitDisplay(scaled.PixelWidth, scaled.PixelHeight, fitDip);
            return Finish(data, useJpg, scaled.PixelWidth, scaled.PixelHeight, gw, gh);
        }
        catch (Exception e) { AppEnv.Log("image encode: " + e.Message); return null; }
    }

    public static BitmapSource? Display(byte[] data, int srcW, int srcH, int goalW, int goalH, double dpi, float zoom)
    {
        int edge = RtfPict.TargetEdge(goalW, goalH, dpi, zoom, srcW, srcH);
        return RtfPict.DecodeScaled(data, srcW, srcH, edge);
    }

    static Built Finish(byte[] data, bool jpeg, int pxW, int pxH, int goalW, int goalH)
    {
        return new Built
        {
            Bytes = data,
            Jpeg = jpeg,
            PxW = pxW,
            PxH = pxH,
            GoalW = goalW,
            GoalH = goalH,
            Group = RtfPict.BuildGroup(data, jpeg, pxW, pxH, goalW, goalH),
        };
    }

    static (int W, int H) FitDisplay(int pxW, int pxH, int fitDip)
    {
        int fit = Math.Max(24, fitDip);
        double sc = Math.Min(1.0, fit / (double)Math.Max(pxW, 1));
        int dw = Math.Max(1, (int)Math.Round(pxW * sc));
        int dh = Math.Max(1, (int)Math.Round(pxH * (dw / (double)Math.Max(1, pxW))));
        return ((int)Math.Round(dw * 15.0), (int)Math.Round(dh * 15.0));
    }

    static (int W, int H) Peek(byte[] data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P')
            return ((data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19], (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23]);
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 8 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                byte m = data[i + 1];
                if (m is 0xD8 or 0xD9 or 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                int seglen = (data[i + 2] << 8) | data[i + 3];
                if (seglen < 2) break;
                if (m is 0xC0 or 0xC1 or 0xC2 && i + 9 < data.Length)
                    return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
                int next = i + 2 + seglen;
                if (next <= i) break;
                i = next;
            }
        }
        return (0, 0);
    }

    static BitmapSource? Load(byte[] data, int edge)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(data);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.DecodePixelWidth = Math.Max(1, edge);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    static BitmapSource Scale(BitmapSource src, int edge)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        int longEdge = Math.Max(w, h);
        if (longEdge <= edge) { if (src.IsFrozen) return src; var c = src.Clone(); c.Freeze(); return c; }
        double sc = edge / (double)longEdge;
        var tb = new TransformedBitmap(src, new System.Windows.Media.ScaleTransform(sc, sc));
        tb.Freeze();
        return tb;
    }

    static bool HasAlpha(BitmapSource src)
    {
        var f = src.Format;
        if (f != PixelFormats.Bgra32 && f != PixelFormats.Pbgra32 && f != PixelFormats.Rgba64 && f != PixelFormats.Prgba64 && f != PixelFormats.Rgba128Float)
            return false;
        int w = Math.Min(src.PixelWidth, 48), h = Math.Min(src.PixelHeight, 48);
        if (w < 1 || h < 1) return false;
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        int stride = w * 4;
        conv.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), px, stride, 0);
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) return true;
        return false;
    }

    static byte[] EncodePng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static byte[] EncodeJpeg(BitmapSource src, int quality)
    {
        var flat = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(flat));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
