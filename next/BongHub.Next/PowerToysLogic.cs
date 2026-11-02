using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BongHub.Next;

/// <summary>Code defaults. Risky tools stay off, so nothing installs a hook just because the class loaded.</summary>
public static class PtDefaults
{
    public const bool Zones = false, Grab = false, Palette = false, Paste = false, Peek = false;
    public const bool Resizer = true, ResizerContextMenu = false;
}

/// <summary>Unset <see cref="Allow"/> means blocked. This type never calls a hook, hotkey, registry, or brightness API.</summary>
public static class PtGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm() { if (Allow?.Invoke() == true) Armed = true; }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 후킹, 단축키, 밝기, 모니터를 바꾸지 않아요.");
    }
}

public static class PtSystem
{
    public static int Attempts, NativeCalls;
    public static void Touch()
    {
        Attempts++;
        PtGate.Demand();
        NativeCalls++;
    }
}

public readonly record struct PxRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public static PxRect Ltrb(int l, int t, int r, int b) => new(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    public bool Contains(PxRect o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;
    public bool Intersects(PxRect o) => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
}

public sealed class ZoneLayout
{
    public string Kind = "cols";
    public int A = 2, B = 2, Spacing;
    public List<float> Custom = new();
    public List<string> Canvas = new();
}

public static class ZoneMath
{
    public static List<PxRect> Rects(ZoneLayout layout, PxRect area)
    {
        var res = new List<PxRect>();
        int sp = Math.Max(0, layout.Spacing);
        int a = Math.Max(1, Math.Min(8, layout.A)), b = Math.Max(1, Math.Min(8, layout.B));
        switch (layout.Kind)
        {
            case "rows": return Split(area, Repeat(a), false, sp);
            case "priority": return Split(area, new[] { 1f, 2f, 1f }, true, sp);
            case "custom":
                var fr = (layout.Custom ?? new List<float>()).Where(x => x > 0).ToArray();
                return Split(area, fr.Length > 0 ? fr : new[] { 1f, 1f }, true, sp);
            case "canvas":
                foreach (var c in layout.Canvas ?? new List<string>())
                {
                    var f = ParseCanvas(c);
                    if (f == null) continue;
                    var r = f.Value;
                    res.Add(PxRect.Ltrb(
                        area.X + (int)(area.W * r.X) + sp / 2 + (r.X <= 0 ? sp / 2 : 0),
                        area.Y + (int)(area.H * r.Y) + sp / 2 + (r.Y <= 0 ? sp / 2 : 0),
                        area.X + (int)(area.W * r.Right) - sp / 2 - (r.Right >= 1 ? sp / 2 : 0),
                        area.Y + (int)(area.H * r.Bottom) - sp / 2 - (r.Bottom >= 1 ? sp / 2 : 0)));
                }
                return res.Count == 0 ? Split(area, new[] { 1f, 1f }, true, sp) : res;
            case "grid":
                int cw = area.W / a, ch = area.H / b;
                for (int y = 0; y < b; y++)
                    for (int x = 0; x < a; x++)
                    {
                        int x0 = area.X + x * cw, y0 = area.Y + y * ch;
                        int x1 = x == a - 1 ? area.Right : x0 + cw;
                        int y1 = y == b - 1 ? area.Bottom : y0 + ch;
                        res.Add(PxRect.Ltrb(x0 + sp, y0 + sp, x1 - (x == a - 1 ? sp : sp / 2), y1 - (y == b - 1 ? sp : sp / 2)));
                    }
                return res;
            default: return Split(area, Repeat(a), true, sp);
        }
    }

    public static PxRect? ZoneAt(PxRect area, ZoneLayout layout, int index)
    {
        var rs = Rects(layout, area);
        return index >= 0 && index < rs.Count ? rs[index] : null;
    }

    public static void Split(ZoneLayout layout, int index, bool vertical)
    {
        if (layout.Kind != "canvas") { layout.Canvas = ToCanvas(layout); layout.Kind = "canvas"; }
        if (layout.Canvas == null || index < 0 || index >= layout.Canvas.Count) return;
        var f = ParseCanvas(layout.Canvas[index]);
        if (f == null) return;
        var r = f.Value;
        var a = vertical ? new Frac(r.X, r.Y, r.W / 2, r.H) : new Frac(r.X, r.Y, r.W, r.H / 2);
        var b = vertical ? new Frac(r.X + r.W / 2, r.Y, r.W / 2, r.H) : new Frac(r.X, r.Y + r.H / 2, r.W, r.H / 2);
        layout.Canvas[index] = CanvasStr(a);
        layout.Canvas.Insert(index + 1, CanvasStr(b));
    }

    public static bool Merge(ZoneLayout layout, int i, int j)
    {
        if (layout.Kind != "canvas") { layout.Canvas = ToCanvas(layout); layout.Kind = "canvas"; }
        if (layout.Canvas == null || i == j || i < 0 || j < 0 || i >= layout.Canvas.Count || j >= layout.Canvas.Count) return false;
        var a = ParseCanvas(layout.Canvas[i]);
        var b = ParseCanvas(layout.Canvas[j]);
        if (a == null || b == null) return false;
        var u = Union(a.Value, b.Value);
        var keep = new List<string>();
        int at = Math.Min(i, j);
        for (int k = 0; k < layout.Canvas.Count; k++)
        {
            var c = ParseCanvas(layout.Canvas[k]);
            if (c != null && u.Contains(new Frac(c.Value.X + 0.0005f, c.Value.Y + 0.0005f, c.Value.W - 0.001f, c.Value.H - 0.001f)))
            {
                if (k < at) at--;
                continue;
            }
            keep.Add(layout.Canvas[k]);
        }
        keep.Insert(Math.Max(0, Math.Min(at, keep.Count)), CanvasStr(u));
        layout.Canvas = keep;
        return true;
    }

    static List<string> ToCanvas(ZoneLayout layout)
    {
        var tmp = new ZoneLayout { Kind = layout.Kind, A = layout.A, B = layout.B, Custom = layout.Custom, Canvas = layout.Canvas, Spacing = 0 };
        return Rects(tmp, new PxRect(0, 0, 10000, 10000)).Select(r => CanvasStr(new Frac(r.X / 10000f, r.Y / 10000f, r.W / 10000f, r.H / 10000f))).ToList();
    }

    static List<PxRect> Split(PxRect area, float[] fr, bool vert, int sp)
    {
        var o = new List<PxRect>();
        float sum = fr.Sum();
        float acc = 0;
        for (int i = 0; i < fr.Length; i++)
        {
            float a0 = acc / sum, a1 = (acc + fr[i]) / sum;
            acc += fr[i];
            if (vert)
            {
                int x0 = area.X + (int)(area.W * a0), x1 = area.X + (int)(area.W * a1);
                o.Add(PxRect.Ltrb(x0 + sp, area.Y + sp, x1 - (i == fr.Length - 1 ? sp : sp / 2), area.Bottom - sp));
            }
            else
            {
                int y0 = area.Y + (int)(area.H * a0), y1 = area.Y + (int)(area.H * a1);
                o.Add(PxRect.Ltrb(area.X + sp, y0 + sp, area.Right - sp, y1 - (i == fr.Length - 1 ? sp : sp / 2)));
            }
        }
        return o;
    }

    static float[] Repeat(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = 1; return a; }

    readonly record struct Frac(float X, float Y, float W, float H)
    {
        public float Right => X + W;
        public float Bottom => Y + H;
        public bool Contains(Frac o) => o.X >= X - 0.0000001f && o.Y >= Y - 0.0000001f && o.Right <= Right + 0.0000001f && o.Bottom <= Bottom + 0.0000001f;
    }

    static Frac Union(Frac a, Frac b)
    {
        float l = Math.Min(a.X, b.X), t = Math.Min(a.Y, b.Y);
        float r = Math.Max(a.Right, b.Right), bo = Math.Max(a.Bottom, b.Bottom);
        return new Frac(l, t, r - l, bo - t);
    }

    static Frac? ParseCanvas(string s)
    {
        var p = (s ?? "").Split(',');
        if (p.Length == 4
            && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float w)
            && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float h)
            && w > 0.01f && h > 0.01f)
            return new Frac(Math.Max(0, x), Math.Max(0, y), Math.Min(1, x + w) - Math.Max(0, x), Math.Min(1, y + h) - Math.Max(0, y));
        return null;
    }

    static string CanvasStr(Frac r) => r.X.ToString("0.####", CultureInfo.InvariantCulture) + "," + r.Y.ToString("0.####", CultureInfo.InvariantCulture) + "," + r.W.ToString("0.####", CultureInfo.InvariantCulture) + "," + r.H.ToString("0.####", CultureInfo.InvariantCulture);
}

public static class PaletteMath
{
    static readonly Regex CalcChars = new(@"^[\d\s\.\+\-\*/\(\)%^,a-z]+$", RegexOptions.Compiled);
    public static int Score(string q, string t) => string.IsNullOrEmpty(q) ? 1 : string.IsNullOrEmpty(t) ? 0 : ScoreL(q.ToLowerInvariant(), t.ToLowerInvariant());
    static int ScoreL(string b, string a)
    {
        if (b.Length == 0) return 1;
        if (a.Length == 0) return 0;
        if (a.Length == b.Length && string.CompareOrdinal(a, b) == 0) return 1000;
        if (a.StartsWith(b, StringComparison.Ordinal)) return 800;
        int i = a.IndexOf(b, StringComparison.Ordinal);
        if (i >= 0) return 600 - i;
        int k = 0; bool start = true;
        for (int x = 0; x < a.Length && k < b.Length; x++) { char c = a[x]; if (start && c == b[k]) k++; start = c is ' ' or '-' or '_' or '.'; }
        if (k == b.Length) return 500;
        int j = 0, gaps = 0;
        for (int x = 0; x < a.Length; x++) { if (j < b.Length && a[x] == b[j]) j++; else if (j > 0) gaps++; }
        return j == b.Length ? Math.Max(1, 300 - gaps) : 0;
    }

    public static string? Calc(string q)
    {
        string s = (q ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith('=')) s = s[1..];
        if (s.Length == 0 || !CalcChars.IsMatch(s) || !s.Any(char.IsDigit) && !s.Contains("pi", StringComparison.Ordinal)) return null;
        bool hasOp = s.IndexOfAny(new[] { '+', '-', '*', '/', '%', '^', '(' }) >= 0;
        if (!hasOp && !s.StartsWith("0x", StringComparison.Ordinal)) return null;
        try
        {
            string t = s.Replace(",", "");
            int p = 0;
            double v = Expr(t, ref p);
            Ws(t, ref p);
            if (p != t.Length || double.IsNaN(v) || double.IsInfinity(v)) return null;
            return Math.Round(v, 10).ToString("G12", CultureInfo.InvariantCulture);
        }
        catch { return null; }
    }

    public static List<string> Search(string query, IEnumerable<(string Title, string Key)> commands)
    {
        var list = new List<(string Title, int Score)>();
        string? calc = Calc(query);
        if (calc != null) list.Add(("= " + calc, 2000));
        foreach (var c in commands)
        {
            int s = Math.Max(Score(query, c.Title), Score(query, c.Key));
            if (s > 0) list.Add((c.Title, s));
        }
        return list.OrderByDescending(x => x.Score).Select(x => x.Title).ToList();
    }

    public static string? Unit(string q)
    {
        var m = UnitRx.Match(q ?? "");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        if (!Units.TryGetValue(m.Groups[2].Value, out var a) || !Units.TryGetValue(m.Groups[3].Value, out var b) || a.Cat != b.Cat) return null;
        double r = a.Cat == "temp"
            ? TempOut(TempC(v, a.Factor), b.Factor)
            : v * a.Factor / b.Factor;
        return Math.Round(r, 6).ToString("G10", CultureInfo.InvariantCulture) + " " + m.Groups[3].Value;
    }

    public static int TimeCount(string q)
    {
        string s = (q ?? "").Trim().ToLowerInvariant();
        string[] keys = { "time", "now", "시간", "시계", "date", "날짜" };
        return keys.Any(k => s == k) ? 4 : 0;
    }

    static double TempC(double v, double kind) => kind == 0 ? v : kind == 1 ? (v - 32) * 5 / 9 : v - 273.15;
    static double TempOut(double c, double kind) => kind == 0 ? c : kind == 1 ? c * 9 / 5 + 32 : c + 273.15;

    static void Ws(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }
    static double Expr(string s, ref int p) { double v = Term(s, ref p); for (;;) { Ws(s, ref p); if (p < s.Length && (s[p] == '+' || s[p] == '-')) { char o = s[p++]; double r = Term(s, ref p); v = o == '+' ? v + r : v - r; } else return v; } }
    static double Term(string s, ref int p) { double v = Pow(s, ref p); for (;;) { Ws(s, ref p); if (p < s.Length && (s[p] == '*' || s[p] == '/' || s[p] == '%')) { char o = s[p++]; double r = Pow(s, ref p); v = o == '*' ? v * r : o == '/' ? v / r : v % r; } else return v; } }
    static double Pow(string s, ref int p) { double v = Unary(s, ref p); Ws(s, ref p); if (p < s.Length && s[p] == '^') { p++; return Math.Pow(v, Pow(s, ref p)); } return v; }
    static double Unary(string s, ref int p) { Ws(s, ref p); if (p < s.Length && s[p] == '-') { p++; return -Unary(s, ref p); } if (p < s.Length && s[p] == '+') { p++; return Unary(s, ref p); } return Atom(s, ref p); }
    static double Atom(string s, ref int p)
    {
        Ws(s, ref p);
        if (p >= s.Length) throw new FormatException();
        if (s[p] == '(') { p++; double v = Expr(s, ref p); Ws(s, ref p); if (p >= s.Length || s[p] != ')') throw new FormatException(); p++; return v; }
        if (s.Length - p > 2 && s[p] == '0' && s[p + 1] == 'x') { int st = p += 2; while (p < s.Length && Uri.IsHexDigit(s[p])) p++; return Convert.ToInt64(s.Substring(st, p - st), 16); }
        if (char.IsDigit(s[p]) || s[p] == '.') { int st = p; while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.')) p++; return double.Parse(s.Substring(st, p - st), CultureInfo.InvariantCulture); }
        if (char.IsLetter(s[p]))
        {
            int st = p; while (p < s.Length && char.IsLetter(s[p])) p++; string f = s.Substring(st, p - st);
            if (f == "pi") return Math.PI;
            if (f == "e") return Math.E;
            double arg = Atom(s, ref p);
            return f switch { "sqrt" => Math.Sqrt(arg), "sin" => Math.Sin(arg), "cos" => Math.Cos(arg), "tan" => Math.Tan(arg), "abs" => Math.Abs(arg), "ln" => Math.Log(arg), "log" => Math.Log10(arg), "exp" => Math.Exp(arg), "round" => Math.Round(arg), "floor" => Math.Floor(arg), "ceil" => Math.Ceiling(arg), _ => throw new FormatException() };
        }
        throw new FormatException();
    }

    static readonly Regex UnitRx = new(@"^\s*(-?[\d\.,]+)\s*([a-z°²/]+)\s+(?:to|in|->|=)?\s*([a-z°²/]+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    readonly record struct UnitDef(string Cat, double Factor);
    static readonly Dictionary<string, UnitDef> Units = BuildUnits();
    static Dictionary<string, UnitDef> BuildUnits()
    {
        var d = new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase);
        void Add(string cat, double f, params string[] names) { foreach (var n in names) d[n] = new UnitDef(cat, f); }
        Add("len", 1, "m"); Add("len", 1000, "km"); Add("len", 1609.344, "mi", "mile");
        Add("temp", 0, "c"); Add("temp", 1, "f"); Add("temp", 2, "k");
        Add("mass", 1, "kg"); Add("mass", .45359237, "lb");
        return d;
    }
}

public static class PasteText
{
    public static string? HtmlToMarkdown(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        int a = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase), b = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        string h = a >= 0 && b > a ? html.Substring(a + 20, b - a - 20) : html.Substring(Math.Max(0, html.IndexOf('<')));
        h = Regex.Replace(h, @"\r?\n", " ");
        for (int i = 6; i >= 1; i--) h = Regex.Replace(h, "<h" + i + @"[^>]*>(.*?)</h" + i + ">", m => "\n" + new string('#', i) + " " + m.Groups[1].Value + "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<(b|strong)[^>]*>(.*?)</\1>", "**$2**", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<(i|em)[^>]*>(.*?)</\1>", "*$2*", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"</(p|div|ul|ol|tr|table)>", "\n", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, "<[^>]+>", "");
        h = WebUtility.HtmlDecode(h);
        h = Regex.Replace(h, @"[ \t]+\n", "\n");
        h = Regex.Replace(h, @"\n{3,}", "\n\n");
        return h.Trim();
    }

    public static string? ToJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return Pretty(System.Text.Json.Nodes.JsonNode.Parse(trimmed)!.ToJsonString()); } catch { }
        }
        var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim() != "").ToList();
        char sep = lines[0].Contains('\t') ? '\t' : lines[0].Contains(',') ? ',' : '\0';
        if (sep != '\0' && lines.Count >= 2)
        {
            var head = lines[0].Split(sep).Select(x => x.Trim()).ToArray();
            var rows = new List<string>();
            foreach (var line in lines.Skip(1))
            {
                var c = line.Split(sep);
                var bits = new List<string>();
                for (int i = 0; i < head.Length; i++) bits.Add(JsonString(head[i]) + ":" + JsonOf(i < c.Length ? c[i].Trim() : ""));
                rows.Add("{" + string.Join(",", bits) + "}");
            }
            return Pretty("[" + string.Join(",", rows) + "]");
        }
        var kv = lines.Select(l => Regex.Match(l, @"^\s*([^:=]+?)\s*[:=]\s*(.*)$")).ToList();
        if (kv.Count > 0 && kv.All(m => m.Success))
        {
            var bits = kv.Select(m => JsonString(m.Groups[1].Value) + ":" + JsonOf(m.Groups[2].Value.Trim()));
            return Pretty("{" + string.Join(",", bits) + "}");
        }
        return Pretty("[" + string.Join(",", lines.Select(JsonString)) + "]");
    }

    public static string ApplyCustom(string text, string kind, string? pattern = null, string? replace = null)
    {
        switch (kind)
        {
            case "upper": return text.ToUpperInvariant();
            case "lower": return text.ToLowerInvariant();
            case "dedupe": return string.Join("\r\n", text.Replace("\r", "").Split('\n').Distinct());
            default: return Regex.Replace(text, pattern ?? "", (replace ?? "").Replace("\\n", "\n"), RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        }
    }

    static string JsonOf(string s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == Math.Truncate(d) && !double.IsInfinity(d))
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        if (s == "true" || s == "false") return s;
        return JsonString(s);
    }

    static string JsonString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string Pretty(string json)
    {
        var sb = new StringBuilder();
        int ind = 0; bool q = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) q = !q;
            if (q) { sb.Append(c); continue; }
            if (c == '{' || c == '[') { sb.Append(c).Append('\n').Append(' ', (++ind) * 2); }
            else if (c == '}' || c == ']') { sb.Append('\n').Append(' ', (--ind) * 2).Append(c); }
            else if (c == ',') sb.Append(",\n").Append(' ', ind * 2);
            else if (c == ':') sb.Append(": ");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

public static class PeekKind
{
    static readonly string[] Img = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico" };
    static readonly string[] Thumb = { ".webp", ".heic", ".avif" };
    static readonly string[] Md = { ".md", ".markdown" };
    public static string Kind(string path)
    {
        if (Directory.Exists(path)) return "folder";
        string e = Path.GetExtension(path).ToLowerInvariant();
        if (Thumb.Contains(e)) return "thumb";
        if (Img.Contains(e)) return "image";
        if (Md.Contains(e)) return "markdown";
        if (e is ".html" or ".htm") return "html";
        if (e is ".txt" or ".json" or ".log") return "text";
        return "info";
    }
    public static string ReadText(string path)
    {
        var enc = new UTF8Encoding(false, true);
        string s = File.ReadAllText(path, enc);
        if (s.Length > 0 && s[0] == '\uFEFF') s = s[1..];
        return s;
    }
}

public static class ImageFit
{
    public readonly record struct Px(int W, int H);
    public static Px Target(Px src, int pw, int ph, string fit, bool shrinkOnly)
    {
        double sx = (double)pw / src.W, sy = (double)ph / src.H;
        Px t;
        if (fit == "stretch") t = new Px(pw, ph);
        else if (fit == "fill")
        {
            double s = Math.Max(sx, sy);
            if (shrinkOnly && s >= 1) return src;
            t = new Px(pw, ph);
        }
        else
        {
            double s = Math.Min(sx, sy);
            t = new Px(Math.Max(1, (int)Math.Round(src.W * s)), Math.Max(1, (int)Math.Round(src.H * s)));
        }
        if (shrinkOnly && t.W >= src.W && t.H >= src.H) return src;
        return t;
    }

    public static Px Resolve(int pw, int ph, string unit, Px src, double dpiX, double dpiY, bool ignoreOrientation)
    {
        double w = pw, h = ph;
        if (unit == "%") { w = src.W * pw / 100.0; h = src.H * ph / 100.0; }
        else if (unit == "cm") { w = pw * dpiX / 2.54; h = ph * dpiY / 2.54; }
        else if (unit == "in") { w = pw * dpiX; h = ph * dpiY; }
        int rw = Math.Max(1, (int)Math.Round(w)), rh = Math.Max(1, (int)Math.Round(h));
        if (ignoreOrientation && unit != "%" && (src.H > src.W) != (rh > rw) && rw != rh) (rw, rh) = (rh, rw);
        return new Px(rw, rh);
    }

    public static string FileName(string src, string preset, int actualW, int actualH, string fmt)
    {
        string n = Path.GetFileNameWithoutExtension(src), e = Path.GetExtension(src);
        string name = (string.IsNullOrEmpty(fmt) ? "%1 (%2)" : fmt).Replace("%1", n).Replace("%2", preset).Replace("%5", actualW.ToString(CultureInfo.InvariantCulture)).Replace("%6", actualH.ToString(CultureInfo.InvariantCulture));
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + e;
    }
}

public static class GrabMath
{
    public static bool CanGrab(bool hasWindow, bool fullscreen, bool skipFullscreen, bool elevatedTarget, bool weAreElevated)
        => hasWindow && !(skipFullscreen && fullscreen) && (!elevatedTarget || weAreElevated);
}
