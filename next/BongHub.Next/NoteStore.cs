using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace BongHub.Next;

/// <summary>Same schema and field order as 3.4.7's NoteMeta (JavaScriptSerializer).</summary>
public sealed class NoteMeta
{
    public string Id { get; set; } = "";
    public string Color { get; set; } = "yellow";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 330;
    public int H { get; set; } = 330;
    public bool TopMost { get; set; }
    public bool Open { get; set; } = true;
    public float Zoom { get; set; } = 1f;
    public string Text { get; set; } = "";
    [JsonConverter(typeof(MsDateConverter))] public DateTime Modified { get; set; } = DateTime.Now;
}

/// <summary>Reads/writes the JavaScriptSerializer date form "\/Date(ms)\/" so 3.4.7 keeps reading our files.</summary>
public sealed class MsDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var s = reader.GetString() ?? "";
        if (s.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var body = s.Substring(6, s.IndexOf(')') - 6); int cut = body.IndexOfAny(['+', '-'], 1); if (cut > 0) body = body[..cut];
            return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(body)).LocalDateTime;
        }
        return DateTime.TryParse(s, out var d) ? d : DateTime.Now;
    }
    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o)
    {
        long ms = new DateTimeOffset(v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Local) : v).ToUnixTimeMilliseconds();
        w.WriteRawValue("\"\\/Date(" + ms + ")\\/\"", skipInputValidation: true);
    }
}

public static class NoteStore
{
    public static readonly List<NoteMeta> All = new();
    public static int Writes, BlockedWrites, CorruptMeta; public static string LastError = "";
    public static event Action? Changed;
    static readonly JsonSerializerOptions Js = new() { PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NumberHandling = JsonNumberHandling.AllowReadingFromString };
    static string Dir => AppEnv.NotesDir;
    public static string MetaPath(string id) => Path.Combine(Dir, id + ".json");
    public static string RtfPath(string id) => Path.Combine(Dir, id + ".rtf");

    public static void Load()
    {
        All.Clear(); CorruptMeta = 0;
        if (!Directory.Exists(Dir)) return;
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            if (Path.GetFileName(f).StartsWith('_')) continue;   // cloud tombstones etc.
            try
            {
                var m = JsonSerializer.Deserialize<NoteMeta>(File.ReadAllText(f, Encoding.UTF8), Js);
                if (m == null || string.IsNullOrEmpty(m.Id)) { CorruptMeta++; continue; }
                m.Text ??= ""; if (!NoteColors.Valid(m.Color)) m.Color = "g:blocks";
                All.Add(m);
            }
            catch (Exception e) { CorruptMeta++; AppEnv.Log($"note meta skipped {f}: {e.Message}"); }
        }
        All.Sort((a, b) => b.Modified.CompareTo(a.Modified));
    }
    public static NoteMeta? Find(string id) => All.FirstOrDefault(n => n.Id == id);
    public static string Serialize(NoteMeta m) => JsonSerializer.Serialize(m, Js);

    public static bool SaveMeta(NoteMeta m) { m.Modified = DateTime.Now; var ok = Write(MetaPath(m.Id), new UTF8Encoding(false).GetBytes(Serialize(m))); Changed?.Invoke(); return ok; }
    public static bool SaveRtf(NoteMeta m, byte[] rtf) => Write(RtfPath(m.Id), rtf);
    public static string? LoadRtf(string id) { try { return File.ReadAllText(RtfPath(id), Encoding.UTF8); } catch { return null; } }

    public static NoteMeta Create(string defaultColor, int x, int y, int w, int h)
    {
        var m = new NoteMeta { Id = Guid.NewGuid().ToString("N")[..12], Color = NoteColors.Random(defaultColor), X = x, Y = y, W = w, H = h };
        All.Insert(0, m); SaveRtf(m, []); SaveMeta(m); return m;
    }

    /// <summary>Atomic write (tmp + replace), like 3.4.7. Refuses while read-only.</summary>
    static bool Write(string path, byte[] content)
    {
        if (AppEnv.DenyWrite()) { BlockedWrites++; return false; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp"; File.WriteAllBytes(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            Writes++; return true;
        }
        catch (Exception e) { LastError = e.Message; AppEnv.Log($"write failed {path}: {e.Message}"); return false; }
    }
}

/// <summary>Port of 3.4.7 NoteColors (keys, gradients and the contrast-safe surface colours).</summary>
public static class NoteColors
{
    public static readonly string[] Keys = { "yellow", "green", "pink", "purple", "blue", "gray", "charcoal" };
    public static readonly string[] Names = { "노랑", "초록", "분홍", "보라", "파랑", "회색", "차콜" };
    static readonly Color[] title = { C(104, 88, 32), C(46, 94, 62), C(118, 54, 86), C(84, 62, 132), C(46, 78, 130), C(82, 82, 92), C(48, 48, 55) };
    static readonly Color[] body = { C(58, 52, 30), C(32, 52, 40), C(60, 36, 50), C(44, 38, 66), C(30, 42, 64), C(48, 48, 54), C(30, 30, 34) };
    static readonly Color[] swatch = { C(240, 205, 90), C(120, 205, 140), C(240, 140, 190), C(180, 150, 240), C(120, 170, 245), C(180, 180, 190), C(110, 110, 120) };
    static readonly Color[] partner = { C(255, 150, 110), C(64, 184, 176), C(183, 124, 255), C(228, 126, 244), C(76, 138, 254), C(110, 117, 144), C(46, 48, 72) };
    public static readonly string[][] Gradients = {
        new[] { "g:blocks", "블록", "4C8AFE", "E47EF4" }, new[] { "g:aurora", "오로라", "6EC6FF", "9084FF" }, new[] { "g:grape", "포도", "9D6BFF", "E47EF4" },
        new[] { "g:lagoon", "라군", "40D9D0", "4C8AFE" }, new[] { "g:mint", "민트", "5EE7B0", "3FA9F5" }, new[] { "g:forest", "숲", "7BD88F", "2A9D8F" },
        new[] { "g:lemon", "레몬", "F5E36B", "8FD66B" }, new[] { "g:peach", "복숭아", "FFC37A", "FF7EB3" }, new[] { "g:sunset", "노을", "FF9A6B", "E4589C" },
        new[] { "g:ember", "불씨", "FF7A59", "B03A8F" }, new[] { "g:rose", "장미", "FF7EA8", "B06CFF" }, new[] { "g:ocean", "바다", "2E9BFF", "5B4BFF" },
        new[] { "g:night", "밤하늘", "5560A8", "2A2B58" }, new[] { "g:slate", "슬레이트", "8A93A8", "4A5168" },
        new[] { "g:sage", "세이지", "9AA58E", "747F6D" }, new[] { "g:dustrose", "더스티 로즈", "B58D94", "8C7077" }, new[] { "g:mauve", "모브", "9B8298", "706173" },
        new[] { "g:bluegray", "블루 그레이", "879BA5", "647681" }, new[] { "g:sand", "샌드", "B3A388", "857862" }, new[] { "g:clay", "클레이", "B18670", "826552" },
        new[] { "g:olive", "올리브", "94916D", "6E6C50" }, new[] { "g:ink", "잉크", "778296", "555E70" }, new[] { "g:lavendergray", "라벤더 그레이", "9D9BB3", "77758E" },
        new[] { "g:petrol", "페트롤", "698985", "4F6D68" }, new[] { "g:cocoa", "코코아", "927A6D", "6D594F" }, new[] { "g:rosetaupe", "로즈 토프", "A88982", "806963" },
        new[] { "g:mist", "미스트", "A5A5A0", "7D7D78" }, new[] { "g:mustard", "머스터드", "AC9B63", "817446" }, new[] { "g:deepteal", "딥 틸", "5C7E79", "48615D" },
        new[] { "g:plumgray", "플럼 그레이", "8D8294", "665D6B" }, new[] { "g:smoke", "스모크", "9A9DA1", "72767A" }, new[] { "g:terracotta", "테라코타", "AA806B", "805F50" } };
    public static readonly Color Text = C(240, 240, 246);
    static readonly Color Base = C(20, 19, 34);
    public static bool Grad => ConfigStore.Notes.Gradient;
    static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    public static IEnumerable<string> PaletteKeys => Gradients.Select(g => g[0]).Concat(Keys);
    static readonly Random random = new();
    public static string Random(string except) { var all = PaletteKeys.ToArray(); int s = random.Next(all.Length); for (int n = 0; n < all.Length; n++) { var k = all[(s + n) % all.Length]; if (k != except) return k; } return all[0]; }
    static int I(string k) => Array.IndexOf(Keys, k);
    static Color Hex(string h) { int v = Convert.ToInt32(h, 16); return C((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255)); }
    public static bool Valid(string? k)
    {
        if (string.IsNullOrEmpty(k)) return false;
        if (I(k) >= 0 || Gradients.Any(g => g[0] == k)) return true;
        if (k.StartsWith("c:")) { var p = k.Split(':'); try { return p.Length == 3 && p[1].Length == 6 && p[2].Length == 6 && Hex(p[1]).A == 255 && Hex(p[2]).A == 255; } catch { } }
        return false;
    }
    public static void Raw(string k, out Color a, out Color b)
    {
        int i = I(k); if (i >= 0) { a = swatch[i]; b = partner[i]; return; }
        var g = Gradients.FirstOrDefault(x => x[0] == k); if (g != null) { a = Hex(g[2]); b = Hex(g[3]); return; }
        if (k.StartsWith("c:")) { var p = k.Split(':'); try { a = Hex(p[1]); b = Hex(p[2]); return; } catch { } }
        a = Hex("4C8AFE"); b = Hex("E47EF4");
    }
    public static Color Mix(Color x, Color y, double t) => C((byte)(x.R + (y.R - x.R) * t), (byte)(x.G + (y.G - x.G) * t), (byte)(x.B + (y.B - x.B) * t));
    public static double Lum(Color c) { static double f(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); } return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B); }
    static Color ForText(Color c, double ratio) { double lt = Lum(Text); for (int n = 0; n < 40 && (lt + 0.05) / (Lum(c) + 0.05) < ratio; n++) c = Mix(c, Colors.Black, 0.08); return c; }
    public static Color Body(string k) { if (!Grad && I(k) >= 0) return body[I(k)]; Raw(k, out var a, out var b); return ForText(Mix(Base, Mix(a, b, .5), Grad ? .2 : .24), 9); }
    public static Color SurfA(string k) { if (!Grad) return Body(k); Raw(k, out var a, out _); return ForText(Mix(Base, a, .40), 7); }
    public static Color SurfB(string k) { if (!Grad) return Body(k); Raw(k, out _, out var b); return ForText(Mix(Base, b, .34), 7); }
    public static Color Swatch(string k) { Raw(k, out var a, out _); return a; }
    public static Color SwatchB(string k) { Raw(k, out _, out var b); return b; }
    public static string Name(string k) { int i = I(k); if (i >= 0) return Names[i]; var g = Gradients.FirstOrDefault(x => x[0] == k); return g != null ? g[1] : k.StartsWith("c:") ? "맞춤" : "블록"; }
}
