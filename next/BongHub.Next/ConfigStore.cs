using System.Text;
using System.Text.Json.Nodes;

namespace BongHub.Next;

public sealed class PresetSnapshot { public string Name { get; set; } = ""; public string Hotkey { get; set; } = ""; }
public sealed class NotesSettings { public string DefaultColor = "g:blocks"; public bool Gradient = true; public float FontSize = 11; public int Opacity = 100; public bool OpenOnStartup = true; public string Hotkey = "Ctrl+Alt+N"; }
public sealed class RuleSnapshot { public string Name = ""; public bool Enabled = true; }
public sealed class ToySnapshot { public string Name = ""; public bool Enabled; public string Hotkey = ""; }

/// <summary>
/// Reads 3.4.x config.json. A Notes change replaces that one JSON value and leaves every other byte alone,
/// so instant-apply settings do not rewrite the file the stable app and the cloud sync both own.
/// </summary>
public static class ConfigStore
{
    public static List<PresetSnapshot> Presets { get; } = new();
    public static NotesSettings Notes { get; private set; } = new();
    public static List<string> TabOrder { get; } = new();
    public static bool IdleEnabled { get; private set; } = true;
    public static int IdleMinutes { get; private set; } = 30;
    public static string IdlePreset { get; private set; } = "";
    public static bool SkipIfAudio { get; private set; } = true;
    public static bool OverlayEnabled { get; private set; } = true;
    public static int OverlayOpacity { get; private set; } = 82;
    public static List<RuleSnapshot> Rules { get; } = new();
    public static bool KakaoAdBlock { get; private set; }
    public static bool KakaoDark { get; private set; }
    public static string DeskHotkeySave { get; private set; } = "";
    public static string DeskHotkeyRestore { get; private set; } = "";
    public static bool DeskAutoSave { get; private set; } = true;
    public static List<ToySnapshot> PowerToys { get; } = new();
    public static string LoadError = "";
    public static int Writes;

    public static void Load()
    {
        Presets.Clear(); Notes = new NotesSettings(); TabOrder.Clear(); Rules.Clear(); PowerToys.Clear();
        IdleEnabled = true; IdleMinutes = 30; IdlePreset = ""; SkipIfAudio = true; OverlayEnabled = true; OverlayOpacity = 82;
        KakaoAdBlock = false; KakaoDark = false; DeskHotkeySave = ""; DeskHotkeyRestore = ""; DeskAutoSave = true;
        LoadError = "";
        try
        {
            if (!File.Exists(AppEnv.ConfigPath)) { Defaults(); return; }
            var root = JsonNode.Parse(File.ReadAllText(AppEnv.ConfigPath, Encoding.UTF8)) as JsonObject;
            if (root?["Presets"] is JsonArray arr)
                foreach (var p in arr.OfType<JsonObject>()) Presets.Add(new PresetSnapshot { Name = Str(p["Name"]), Hotkey = Str(p["Hotkey"]) });
            if (root?["Notes"] is JsonObject n)
            {
                Notes.DefaultColor = NoteColors.Valid(Str(n["DefaultColor"])) ? Str(n["DefaultColor"]) : "g:blocks";
                Notes.Gradient = Bool(n["Gradient"], true); Notes.OpenOnStartup = Bool(n["OpenOnStartup"], true);
                Notes.FontSize = (float)Num(n["FontSize"], 11); Notes.Opacity = (int)Num(n["Opacity"], 100);
                Notes.Hotkey = n["Hotkey"] is null ? "Ctrl+Alt+N" : Str(n["Hotkey"]);
            }
            if (root?["General"] is JsonObject g)
            {
                if (g["TabOrder"] is JsonArray order)
                    foreach (var item in order) { var s = Str(item); if (s.Length > 0) TabOrder.Add(s); }
                IdleEnabled = Bool(g["IdleEnabled"], true);
                IdleMinutes = Math.Clamp((int)Num(g["IdleMinutes"], 30), 1, 600);
                IdlePreset = Str(g["IdlePreset"]);
                SkipIfAudio = Bool(g["SkipIfAudio"], true);
                OverlayEnabled = Bool(g["OverlayEnabled"], true);
                OverlayOpacity = Math.Clamp((int)Num(g["OverlayOpacity"], 82), 20, 100);
            }
            if (root?["GlobalRules"] is JsonArray rules)
                foreach (var r in rules.OfType<JsonObject>()) Rules.Add(new RuleSnapshot { Name = Str(r["Name"]), Enabled = Bool(r["Enabled"], true) });
            if (root?["Kakao"] is JsonObject k) { KakaoAdBlock = Bool(k["AdBlock"], false); KakaoDark = Bool(k["Dark"], false); }
            if (root?["Desk"] is JsonObject d)
            {
                DeskAutoSave = Bool(d["AutoSave"], true);
                DeskHotkeySave = Str(d["HotkeySave"]);
                DeskHotkeyRestore = Str(d["HotkeyRestore"]);
            }
            if (root?["Pt"] is JsonObject pt)
            {
                Toy("Zones", pt["Zones"]); Toy("Grab", pt["Grab"]); Toy("Workspaces", pt["Work"]);
                Toy("Palette", pt["Palette"]); Toy("Paste", pt["Paste"]); Toy("Peek", pt["Peek"]); Toy("Resizer", pt["Resizer"]);
            }
            if (Presets.Count == 0) Defaults();
        }
        catch (Exception e) { LoadError = e.Message; Defaults(); }
    }
    static void Toy(string name, JsonNode? node)
    {
        if (node is not JsonObject o) { PowerToys.Add(new ToySnapshot { Name = name }); return; }
        PowerToys.Add(new ToySnapshot { Name = name, Enabled = Bool(o["Enabled"], false), Hotkey = Str(o["Hotkey"]) });
    }
    static void Defaults() { if (Presets.Count == 0) { Presets.Add(new() { Name = "작업", Hotkey = "Ctrl+Alt+1" }); Presets.Add(new() { Name = "자리비움", Hotkey = "Ctrl+Alt+0" }); } }
    static string Str(JsonNode? n) { try { return n?.GetValue<string>() ?? ""; } catch { return n?.ToString() ?? ""; } }
    static bool Bool(JsonNode? n, bool d) { try { return n?.GetValue<bool>() ?? d; } catch { return d; } }
    static double Num(JsonNode? n, double d) { try { return n?.GetValue<double>() ?? d; } catch { return d; } }

    /// <summary>Instant apply: splice Notes.&lt;key&gt; into config.json. Returns false when read-only or the file cannot be patched without a full rewrite.</summary>
    public static bool SetNotesValue(string key, JsonNode value)
    {
        if (AppEnv.DenyWrite()) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteFile(AppEnv.ConfigPath, key, value, out bool changed))
            {
                AppEnv.Log("config patch failed: Notes." + key);
                return false;
            }
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
    }

    /// <summary>Instant apply for one key under <paramref name="section"/>. Does not rewrite sibling keys.</summary>
    public static bool SetSectionValue(string section, string key, JsonNode value)
    {
        if (AppEnv.DenyWrite()) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteFile(AppEnv.ConfigPath, section, key, value, out bool changed))
            {
                AppEnv.Log("config patch failed: " + section + "." + key);
                return false;
            }
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
    }
    public static void SetIdle(bool enabled, int minutes, string preset)
    {
        IdleEnabled = enabled; IdleMinutes = minutes; IdlePreset = preset ?? "";
    }
    public static void SetOverlay(bool enabled, int opacity)
    {
        OverlayEnabled = enabled; OverlayOpacity = opacity;
    }
    public static int CountNotes() => NoteStore.All.Count;
}

/// <summary>Next-only preferences (kept outside 3.4.7's config).</summary>
public static class NextSettings
{
    public static bool ReduceMotion { get; private set; }
    public static void Load()
    {
        try { if (File.Exists(AppEnv.NextSettingsPath) && JsonNode.Parse(File.ReadAllText(AppEnv.NextSettingsPath)) is JsonObject o) ReduceMotion = o["ReduceMotion"]?.GetValue<bool>() ?? false; } catch { }
        Fx.Enabled = !ReduceMotion;
    }
    public static void SetReduceMotion(bool v)
    {
        ReduceMotion = v; Fx.Enabled = !v;
        try { Directory.CreateDirectory(Path.GetDirectoryName(AppEnv.NextSettingsPath)!); File.WriteAllText(AppEnv.NextSettingsPath, new JsonObject { ["ReduceMotion"] = v }.ToJsonString()); } catch { }
    }
}
