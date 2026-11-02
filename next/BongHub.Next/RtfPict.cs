using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BongHub.Next;

/// <summary>
/// RichEdit RTF pictures. WPF's RTF loader decodes <c>\jpegblip</c>/<c>\pngblip</c> at the file's pixel size
/// and keeps that bitmap for the life of the FlowDocument. Notes display a screen-sized copy instead.
/// Saves splice the original pict groups back in, so 3.4.x still reads the full-resolution bytes.
/// </summary>
static class RtfPict
{
    public const int MaxEdge = 1280;
    public static readonly Encoding Latin1 = Encoding.Latin1;
    // 1×1 transparent PNG. The display RTF points at this so WPF never decodes the real photo.
    static readonly byte[] TinyPng = Convert.FromHexString(
        "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C489" +
        "0000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082");
    static readonly string TinyHex = Convert.ToHexString(TinyPng).ToLowerInvariant();
    static readonly Regex Blip = new(@"\\(?:jpegblip|pngblip|emfblip|wmetafile\d*)", RegexOptions.CultureInvariant);

    public sealed class Pict
    {
        public string Group = "";
        public byte[] Bytes = [];
        public int GoalW, GoalH, SourceW, SourceH;
    }

    public static List<Pict> Parse(byte[] rtf)
    {
        var s = Latin1.GetString(rtf);
        var list = new List<Pict>();
        foreach (var (a, b) in Find(s))
        {
            var group = s.Substring(a, b - a);
            var bytes = ExtractBytes(group);
            if (bytes == null || bytes.Length < 8) continue;
            var (sw, sh) = PeekSize(bytes);
            list.Add(new Pict
            {
                Group = group,
                Bytes = bytes,
                GoalW = Control(group, "picwgoal"),
                GoalH = Control(group, "pichgoal"),
                SourceW = sw,
                SourceH = sh,
            });
        }
        return list;
    }

    public static long PayloadBytes(byte[] rtf)
    {
        long n = 0;
        foreach (var p in Parse(rtf)) n += p.Bytes.Length;
        return n;
    }

    /// <summary>1×1 PNG used by tests and by the display placeholder. Not a photo.</summary>
    public static byte[] TinyPngBytes => TinyPng;

    /// <summary>Rewrite display size only. The blip hex is copied unchanged.</summary>
    public static string WithGoals(string group, int goalW, int goalH)
    {
        string s = Regex.Replace(group, @"\\picscalex-?\d+", "");
        s = Regex.Replace(s, @"\\picscaley-?\d+", "");
        s = ReplaceOrInsert(s, "picwgoal", Math.Max(1, goalW));
        s = ReplaceOrInsert(s, "pichgoal", Math.Max(1, goalH));
        return s;
    }

    /// <summary>A <c>{\pict...\pngblip|jpegblip...}</c> group. Hex is the file payload, not a second encoding.</summary>
    public static string BuildGroup(byte[] data, bool jpeg, int pxW, int pxH, int goalW, int goalH)
    {
        var sb = new StringBuilder(data.Length * 2 + 80);
        sb.Append(@"{\pict");
        sb.Append(jpeg ? @"\jpegblip" : @"\pngblip");
        sb.Append(@"\picw").Append(Math.Max(1, pxW));
        sb.Append(@"\pich").Append(Math.Max(1, pxH));
        sb.Append(@"\picwgoal").Append(Math.Max(1, goalW));
        sb.Append(@"\pichgoal").Append(Math.Max(1, goalH));
        sb.Append("\r\n");
        const string hx = "0123456789abcdef";
        for (int i = 0; i < data.Length; i++)
        {
            sb.Append(hx[data[i] >> 4]);
            sb.Append(hx[data[i] & 15]);
            if ((i & 63) == 63) sb.Append("\r\n");
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Hex digits of the blip payload, lowercase, no whitespace. Empty when the group has no blip.</summary>
    public static string BlipHex(string group)
    {
        var bytes = ExtractBytes(group);
        return bytes == null ? "" : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string ReplaceOrInsert(string group, string name, int value)
    {
        string token = @"\" + name + value;
        if (Regex.IsMatch(group, @"\\" + name + @"-?\d+"))
            return Regex.Replace(group, @"\\" + name + @"-?\d+", token);
        var m = Blip.Match(group);
        if (!m.Success) return group;
        return group.Insert(m.Index, token);
    }

    /// <summary>Same text, but every pict payload is a 1×1 PNG. Picture count and goal sizes stay.</summary>
    public static byte[] Placeholder(byte[] rtf)
    {
        var s = Latin1.GetString(rtf);
        var groups = Find(s);
        var sb = new StringBuilder(s);
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var (a, b) = groups[i];
            sb.Remove(a, b - a);
            sb.Insert(a, WithTinyPng(s.Substring(a, b - a)));
        }
        return Latin1.GetBytes(sb.ToString());
    }

    /// <summary>Replace each pict group in <paramref name="saved"/> with the original group, in order. Null when the counts differ.</summary>
    public static string? Splice(string saved, List<Pict> originals)
    {
        var groups = Find(saved);
        if (originals.Count == 0 || groups.Count != originals.Count) return null;
        var sb = new StringBuilder(saved);
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var (a, b) = groups[i];
            sb.Remove(a, b - a);
            sb.Insert(a, originals[i].Group);
        }
        return sb.ToString();
    }

    /// <summary>Longest edge, in device pixels, for a picture displayed at <paramref name="goalW"/>×<paramref name="goalH"/> twips.</summary>
    public static int TargetEdge(int goalW, int goalH, double dpi, float zoom, int srcW, int srcH)
    {
        double z = zoom < 0.6f ? 0.6 : zoom;
        double d = dpi < 1 ? 1 : dpi;
        int tw = goalW > 0 ? (int)Math.Ceiling(goalW / 15.0 * d * z) : 0;
        int th = goalH > 0 ? (int)Math.Ceiling(goalH / 15.0 * d * z) : 0;
        int edge = Math.Max(tw, th);
        if (edge < 1) edge = MaxEdge;
        if (srcW > 0 && srcH > 0 && edge > Math.Max(srcW, srcH)) edge = Math.Max(srcW, srcH);
        if (edge > MaxEdge) edge = MaxEdge;
        return Math.Max(1, edge);
    }

    public static BitmapSource? DecodeScaled(byte[] data, int srcW, int srcH, int edge)
    {
        if (data.Length < 8) return null;
        int decodeW = edge;
        if (srcW > 0 && srcH > 0)
        {
            decodeW = srcW >= srcH ? edge : Math.Max(1, (int)Math.Round(edge * (srcW / (double)srcH)));
            if (decodeW > srcW) decodeW = srcW;
        }
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(data);
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (srcW <= 0 || decodeW < srcW) bi.DecodePixelWidth = Math.Max(1, decodeW);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    static List<(int Start, int End)> Find(string s)
    {
        var list = new List<(int, int)>();
        int i = 0;
        while (true)
        {
            int j = s.IndexOf("\\pict", i, StringComparison.Ordinal);
            if (j < 0) break;
            int start = j > 0 && s[j - 1] == '{' ? j - 1 : j;
            if (start >= s.Length || s[start] != '{') { i = j + 5; continue; }
            int depth = 0;
            int k = start;
            bool closed = false;
            while (k < s.Length)
            {
                char c = s[k];
                if (c == '\\' && k + 1 < s.Length && (s[k + 1] == '{' || s[k + 1] == '}')) { k += 2; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) { list.Add((start, k + 1)); i = k + 1; closed = true; break; }
                }
                k++;
            }
            if (!closed) i = j + 5;
        }
        return list;
    }

    static string WithTinyPng(string group)
    {
        var m = Blip.Match(group);
        if (!m.Success) return group;
        int hexStart = SkipToHex(group, m.Index + m.Length);
        int hexEnd = hexStart;
        while (hexEnd < group.Length && IsHexOrSpace(group[hexEnd])) hexEnd++;
        var sb = new StringBuilder(group.Length);
        sb.Append(group, 0, m.Index);
        sb.Append("\\pngblip");
        sb.Append(group, m.Index + m.Length, hexStart - (m.Index + m.Length));
        sb.Append(TinyHex);
        sb.Append(group, hexEnd, group.Length - hexEnd);
        return sb.ToString();
    }

    static byte[]? ExtractBytes(string group)
    {
        var m = Blip.Match(group);
        if (!m.Success) return null;
        int hexStart = SkipToHex(group, m.Index + m.Length);
        int hexEnd = hexStart;
        while (hexEnd < group.Length && IsHexOrSpace(group[hexEnd])) hexEnd++;
        var hex = new StringBuilder(hexEnd - hexStart);
        for (int k = hexStart; k < hexEnd; k++)
            if (Uri.IsHexDigit(group[k])) hex.Append(group[k]);
        if (hex.Length < 16) return null;
        if ((hex.Length & 1) == 1) hex.Length--;
        try { return Convert.FromHexString(hex.ToString()); }
        catch (FormatException) { return null; }
    }

    static int SkipToHex(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length && char.IsLetter(s[i + 1]))
            {
                i += 2;
                while (i < s.Length && char.IsLetter(s[i])) i++;
                if (i < s.Length && s[i] == '-') i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                continue;
            }
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            break;
        }
        return i;
    }

    static bool IsHexOrSpace(char c) => c is ' ' or '\r' or '\n' or '\t' || Uri.IsHexDigit(c);

    static int Control(string group, string name)
    {
        var m = Regex.Match(group, @"\\" + name + @"(-?\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : 0;
    }

    static (int W, int H) PeekSize(byte[] data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
            return (ReadBE(data, 16), ReadBE(data, 20));
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8) return JpegSize(data);
        return (0, 0);
    }

    static int ReadBE(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    static (int W, int H) JpegSize(byte[] data)
    {
        int i = 2;
        while (i + 8 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            byte m = data[i + 1];
            if (m == 0xD8 || m == 0xD9) { i += 2; continue; }
            if (m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            int seglen = (data[i + 2] << 8) | data[i + 3];
            if (seglen < 2) break;
            if ((m is 0xC0 or 0xC1 or 0xC2) && i + 9 < data.Length)
            {
                int h = (data[i + 5] << 8) | data[i + 6];
                int w = (data[i + 7] << 8) | data[i + 8];
                return (w, h);
            }
            int next = i + 2 + seglen;
            if (next <= i) break;
            i = next;
        }
        return (0, 0);
    }
}
