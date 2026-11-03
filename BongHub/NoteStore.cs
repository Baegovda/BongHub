using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace BongHub;

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
    /// <summary>Optional note title. Omitted when empty so a 3.4.x reader keeps the earlier fields and ignores this one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Title { get; set; } = "";
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
    public static string PendingRtfPath(string id, string stamp) => Path.Combine(Dir, id + ".rtf.pending-" + stamp);
    public static string[] PendingRtfs(string id) => Directory.Exists(Dir) ? Directory.GetFiles(Dir, id + ".rtf.pending-*") : [];

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
        foreach (var m in All) PrunePending(m.Id);
    }
    public static NoteMeta? Find(string id) => All.FirstOrDefault(n => n.Id == id);
    public static string Serialize(NoteMeta m) => JsonSerializer.Serialize(m, Js);
    public static NoteMeta? Parse(string json) => JsonSerializer.Deserialize<NoteMeta>(json, Js);

    public static bool SaveMeta(NoteMeta m) { m.Modified = DateTime.Now; var ok = Write(MetaPath(m.Id), new UTF8Encoding(false).GetBytes(Serialize(m))); Changed?.Invoke(); return ok; }
    public static bool SaveRtf(NoteMeta m, byte[] rtf) => Write(RtfPath(m.Id), rtf);
    /// <summary>The edit the image guard would not put over the existing file. The old <c>.rtf</c> stays. A note keeps one pending file.</summary>
    public static bool SavePendingRtf(NoteMeta m, byte[] rtf)
    {
        var path = PendingRtfPath(m.Id, DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        if (!Write(path, rtf)) return false;
        foreach (var file in PendingRtfs(m.Id))
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                TryDeletePending(file);
        PrunePending(m.Id);
        return true;
    }
    /// <summary>Deletes pending files older than the note RTF, and any extra pending so one remains.</summary>
    public static void PrunePending(string id)
    {
        if (AppEnv.DenyWrite()) return;
        var files = PendingRtfs(id);
        if (files.Length == 0) return;
        bool hasMain = false;
        DateTime mainTime = DateTime.MinValue;
        var main = RtfPath(id);
        if (File.Exists(main))
        {
            try { mainTime = File.GetLastWriteTimeUtc(main); hasMain = true; }
            catch { hasMain = false; }
        }
        string? keep = null;
        DateTime keepTime = DateTime.MinValue;
        foreach (var file in files)
        {
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(file); }
            catch { continue; }
            if (hasMain && t < mainTime) { TryDeletePending(file); continue; }
            if (keep == null || t >= keepTime)
            {
                if (keep != null) TryDeletePending(keep);
                keep = file;
                keepTime = t;
            }
            else TryDeletePending(file);
        }
    }
    public static string? NewestPendingRtf(string id)
    {
        string? best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (var file in PendingRtfs(id))
        {
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(file); }
            catch { continue; }
            if (best == null || t >= bestTime) { best = file; bestTime = t; }
        }
        return best;
    }
    public static void ClearPendingRtf(string id)
    {
        if (AppEnv.DenyWrite()) return;
        foreach (var file in PendingRtfs(id)) TryDeletePending(file);
    }
    static void TryDeletePending(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception e) { AppEnv.Log($"pending delete {file}: {e.Message}"); }
    }
    public static string? LoadRtf(string id) { try { return File.ReadAllText(RtfPath(id), Encoding.UTF8); } catch { return null; } }

    public static NoteMeta Create(string defaultColor, int x, int y, int w, int h)
    {
        var m = new NoteMeta { Id = Guid.NewGuid().ToString("N")[..12], Color = NoteColors.Random(defaultColor), X = x, Y = y, W = w, H = h };
        All.Insert(0, m); SaveRtf(m, []); SaveMeta(m); return m;
    }

    /// <summary>Removes the json and rtf. Refuses while read-only. Does not write a tombstone (cloud is a later phase).</summary>
    public static bool Delete(NoteMeta m)
    {
        if (AppEnv.DenyWrite()) { BlockedWrites++; return false; }
        try
        {
            All.Remove(m);
            if (File.Exists(MetaPath(m.Id))) File.Delete(MetaPath(m.Id));
            if (File.Exists(RtfPath(m.Id))) File.Delete(RtfPath(m.Id));
            ClearPendingRtf(m.Id);
            Changed?.Invoke();
            return true;
        }
        catch (Exception e) { LastError = e.Message; AppEnv.Log($"delete failed {m.Id}: {e.Message}"); return false; }
    }

    /// <summary>No visible text and no embedded picture. An image-only note is not blank.</summary>
    public static bool IsBlank(NoteMeta m)
    {
        if (!string.IsNullOrWhiteSpace(m.Text)) return false;
        try
        {
            var path = RtfPath(m.Id);
            if (!File.Exists(path)) return true;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return true;
            return Encoding.ASCII.GetString(bytes).IndexOf("\\pict", StringComparison.Ordinal) < 0;
        }
        catch { return string.IsNullOrWhiteSpace(m.Text); }
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
    static readonly Color[] swatch = { C(214, 197, 143), C(132, 184, 144), C(217, 155, 186), C(197, 182, 226), C(144, 174, 218), C(173, 173, 179), C(52, 52, 58) };
    static readonly Color[] partner = { C(224, 169, 148), C(144, 193, 189), C(193, 162, 230), C(211, 152, 220), C(155, 180, 228), C(181, 183, 191), C(40, 41, 54) };
    public static readonly string[][] Gradients = {
        new[] { "g:blocks", "블록", "93AEE2", "D9A8E1" }, new[] { "g:aurora", "오로라", "86BCDF", "ADA6E7" }, new[] { "g:grape", "포도", "C2AEEA", "D398DC" },
        new[] { "g:lagoon", "라군", "83C8C4", "93AEE2" }, new[] { "g:mint", "민트", "6AC39F", "7FB2D7" }, new[] { "g:forest", "숲", "90C59C", "73B9B1" },
        new[] { "g:lemon", "레몬", "CBBF6C", "9AC087" }, new[] { "g:peach", "복숭아", "DBAD76", "E396B6" }, new[] { "g:sunset", "노을", "DE9F82", "D79DB9" },
        new[] { "g:ember", "불씨", "E09B8A", "D2ADC7" }, new[] { "g:rose", "장미", "E396AF", "C4A6E7" }, new[] { "g:ocean", "바다", "86B4DF", "ACA6E7" },
        new[] { "g:night", "밤하늘", "464B69", "33334F" }, new[] { "g:slate", "슬레이트", "484B52", "464B57" },
        new[] { "g:sage", "세이지", "A9AEA3", "B2B6AF" }, new[] { "g:dustrose", "더스티 로즈", "BCA9AC", "C1B8BB" }, new[] { "g:mauve", "모브", "524950", "524B54" },
        new[] { "g:bluegray", "블루 그레이", "A4ADB2", "B0B6BA" }, new[] { "g:sand", "샌드", "B3AB9D", "B9B4AC" }, new[] { "g:clay", "클레이", "BBA99F", "C1B6AE" },
        new[] { "g:olive", "올리브", "AEAD9E", "B7B6A9" }, new[] { "g:ink", "잉크", "494E55", "474C54" }, new[] { "g:lavendergray", "라벤더 그레이", "ADACB9", "B9B9C1" },
        new[] { "g:petrol", "페트롤", "414D4B", "3F4E4B" }, new[] { "g:cocoa", "코코아", "554C47", "544A45" }, new[] { "g:rosetaupe", "로즈 토프", "B8ABA8", "BFB7B5" },
        new[] { "g:mist", "미스트", "989894", "70706C" }, new[] { "g:mustard", "머스터드", "B3AC93", "BBB59F" }, new[] { "g:deepteal", "딥 틸", "3E4D4B", "404E4C" },
        new[] { "g:plumgray", "플럼 그레이", "504C52", "4F4B52" }, new[] { "g:smoke", "스모크", "AAADB0", "B5B8BA" }, new[] { "g:terracotta", "테라코타", "594A43", "594A43" } };
    public static readonly Color Text = C(240, 240, 246);
    public static readonly Color DarkText = C(28, 28, 30);
    /// <summary>Foreground stored by 1.2 notes beside <see cref="Text"/>. It is not a chosen color.</summary>
    public static readonly Color LegacyText = C(236, 236, 240);
    /// <summary>True for the automatic inks. A run in one of these colors follows the note instead of keeping the stored value.</summary>
    public static bool IsAutomaticInk(Color c)
        => Rgb(c, Text) || Rgb(c, DarkText) || Rgb(c, LegacyText);
    static bool Rgb(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
    /// <summary>Dark ink on a light note, light ink on a dark note. Title and body share it.</summary>
    public static Color Ink(string? key)
    {
        string k = string.IsNullOrEmpty(key) ? "g:blocks" : key!;
        var a = SurfA(k);
        var b = SurfB(k);
        return Cover(DarkText, a, b) >= Cover(Text, a, b) ? DarkText : Text;
    }
    /// <summary>iOS blue, darkened on light notes and lightened on dark notes.</summary>
    public static Color LinkInk(string? key)
    {
        string k = string.IsNullOrEmpty(key) ? "g:blocks" : key!;
        bool onLight = Lum(Ink(k)) < 0.4;
        Color ink = onLight ? C(0x0A, 0x4E, 0xC4) : C(0x64, 0xD2, 0xFF);
        Color toward = onLight ? C(0x00, 0x2A, 0x6E) : Colors.White;
        var bgA = SurfA(k);
        var bgB = SurfB(k);
        for (int n = 0; n < 24 && Cover(ink, bgA, bgB) < 4.5; n++)
            ink = Mix(ink, toward, 0.14);
        return ink;
    }
    public static double Contrast(Color a, Color b)
    {
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
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
    public static Color Body(string k) => Mix(SurfA(k), SurfB(k), 0.5);
    public static Color SurfA(string k) { Raw(k, out var a, out var b); return Face(a, b).a; }
    public static Color SurfB(string k) { Raw(k, out var a, out var b); return Face(a, b).b; }
    public static Color Swatch(string k) { Raw(k, out var a, out var b); return Face(a, b).a; }
    public static Color SwatchB(string k) { Raw(k, out var a, out var b); return Face(a, b).b; }
    /// <summary>Solid ratio the pixel sampler still clears. Rendered text reads a little under this.</summary>
    const double InkNeed = 7.35;
    static readonly Dictionary<(int, int), (Color a, Color b)> faces = new();
    static double Cover(Color ink, Color a, Color b)
        => Math.Min(Contrast(ink, a), Math.Min(Contrast(ink, b), Contrast(ink, Mix(a, b, 0.5))));
    /// <summary>
    /// A mid stop where neither black nor the light ink reaches 7:1 is mixed toward white
    /// until dark text does. A dark note mixes the light end toward black so light text does.
    /// The mix stays on the line to white or black, so the hue holds.
    /// </summary>
    static (Color a, Color b) Face(Color a, Color b)
    {
        var key = ((a.R << 16) | (a.G << 8) | a.B, (b.R << 16) | (b.G << 8) | b.B);
        if (faces.TryGetValue(key, out var hit)) return hit;
        double dark = Cover(DarkText, a, b), light = Cover(Text, a, b);
        if (dark < InkNeed && light < InkNeed)
        {
            bool useDark = dark >= light;
            Color ink = useDark ? DarkText : Text;
            Color toward = useDark ? Colors.White : Colors.Black;
            for (int n = 0; n < 48 && Cover(ink, a, b) < InkNeed; n++)
            {
                if (Contrast(ink, a) <= Contrast(ink, b)) a = Nudge(a, toward);
                else b = Nudge(b, toward);
            }
        }
        var made = (a, b);
        faces[key] = made;
        return made;
    }
    static Color Nudge(Color x, Color toward)
    {
        var n = Mix(x, toward, 0.04);
        return n != x ? n : C(Step(x.R, toward.R), Step(x.G, toward.G), Step(x.B, toward.B));
    }
    static byte Step(byte v, byte dest) => v == dest ? v : (byte)(v + (dest > v ? 1 : -1));
    public static string Name(string k) { int i = I(k); if (i >= 0) return Names[i]; var g = Gradients.FirstOrDefault(x => x[0] == k); return g != null ? g[1] : k.StartsWith("c:") ? "맞춤" : "블록"; }
    public static string Custom(Color a, Color b) => "c:" + a.R.ToString("X2") + a.G.ToString("X2") + a.B.ToString("X2") + ":" + b.R.ToString("X2") + b.G.ToString("X2") + b.B.ToString("X2");
    public static bool TryCustom(string textA, string textB, out string key)
    {
        key = "";
        if (textA.Length != 6 || textB.Length != 6) return false;
        try
        {
            var k = "c:" + textA.ToUpperInvariant() + ":" + textB.ToUpperInvariant();
            if (!Valid(k)) return false;
            key = k;
            return true;
        }
        catch { return false; }
    }
}
