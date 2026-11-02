using System.Text;
using System.Text.Json.Nodes;

namespace BongHub.Next;

public sealed class PresetSnapshot { public string Name { get; set; } = ""; public string Hotkey { get; set; } = ""; }
public sealed class NotesSettings { public string DefaultColor = "g:blocks"; public bool Gradient = true; public float FontSize = 11; public int Opacity = 100; public bool OpenOnStartup = true; public string Hotkey = "Ctrl+Alt+N"; }
public sealed class RuleSnapshot { public string Name = ""; public string Process = ""; public bool Enabled = true; }
public sealed class WorkspaceSnapshot { public string Name = ""; public List<WorkspaceItem> Apps = new(); }
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
    public static List<WindowRule> WindowRules { get; } = new();
    public static List<WorkspaceSnapshot> Workspaces { get; } = new();
    public static bool KakaoAdBlock { get; private set; }
    public static bool KakaoDark { get; private set; }
    public static string DeskHotkeySave { get; private set; } = "";
    public static string DeskHotkeyRestore { get; private set; } = "";
    public static bool DeskAutoSave { get; private set; } = true;
    public static int DeskAutoSaveMinutes { get; private set; } = 10;
    public static bool DeskSaveOnExit { get; private set; } = true;
    public static bool DeskSaveBeforeSwitch { get; private set; } = true;
    public static bool DeskRestoreOnChange { get; private set; } = true;
    public static bool DeskRestoreOnStartup { get; private set; }
    public static List<ToySnapshot> PowerToys { get; } = new();
    public static string LoadError = "";
    public static int Writes;

    public static void Load()
    {
        Presets.Clear(); Notes = new NotesSettings(); TabOrder.Clear(); Rules.Clear(); WindowRules.Clear(); Workspaces.Clear(); PowerToys.Clear();
        IdleEnabled = true; IdleMinutes = 30; IdlePreset = ""; SkipIfAudio = true; OverlayEnabled = true; OverlayOpacity = 82;
        KakaoAdBlock = false; KakaoDark = false; DeskHotkeySave = ""; DeskHotkeyRestore = "";
        DeskAutoSave = true; DeskAutoSaveMinutes = 10; DeskSaveOnExit = true; DeskSaveBeforeSwitch = true; DeskRestoreOnChange = true; DeskRestoreOnStartup = false;
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
                foreach (var r in rules.OfType<JsonObject>())
                {
                    var rule = new WindowRule
                    {
                        Enabled = Bool(r["Enabled"], true), Name = Str(r["Name"]), Process = Str(r["Process"]), Title = Str(r["Title"]),
                        TitleRegex = Bool(r["TitleRegex"], false), Class = Str(r["Class"]), Monitor = Str(r["Monitor"]),
                        Action = Str(r["Action"]).Length > 0 ? Str(r["Action"]) : "pos",
                        X = (int)Num(r["X"], 0), Y = (int)Num(r["Y"], 0), W = (int)Num(r["W"], 1280), H = (int)Num(r["H"], 800),
                        Pin = Bool(r["Pin"], false), Keep = Bool(r["Keep"], false), Zone = (int)Num(r["Zone"], 0)
                    };
                    WindowRules.Add(rule);
                    Rules.Add(new RuleSnapshot { Name = rule.Name, Process = rule.Process, Enabled = rule.Enabled });
                }
            if (root?["Kakao"] is JsonObject k) { KakaoAdBlock = Bool(k["AdBlock"], false); KakaoDark = Bool(k["Dark"], false); }
            if (root?["Desk"] is JsonObject d)
            {
                DeskAutoSave = Bool(d["AutoSave"], true);
                DeskAutoSaveMinutes = Math.Clamp((int)Num(d["AutoSaveMinutes"], 10), 1, 240);
                DeskSaveOnExit = Bool(d["SaveOnExit"], true);
                DeskSaveBeforeSwitch = Bool(d["SaveBeforeSwitch"], true);
                DeskRestoreOnChange = Bool(d["RestoreOnChange"], true);
                DeskRestoreOnStartup = Bool(d["RestoreOnStartup"], false);
                DeskHotkeySave = Str(d["HotkeySave"]);
                DeskHotkeyRestore = Str(d["HotkeyRestore"]);
            }
            if (root?["Pt"] is JsonObject pt)
            {
                Toy("Zones", pt["Zones"]); Toy("Grab", pt["Grab"]); Toy("Workspaces", pt["Work"]);
                Toy("Palette", pt["Palette"]); Toy("Paste", pt["Paste"]); Toy("Peek", pt["Peek"]); Toy("Resizer", pt["Resizer"]);
                if (pt["Work"] is JsonObject work && work["Items"] is JsonArray items)
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        var ws = new WorkspaceSnapshot { Name = Str(item["Name"]) };
                        if (item["Apps"] is JsonArray apps)
                            foreach (var a in apps.OfType<JsonObject>())
                                ws.Apps.Add(new WorkspaceItem { Enabled = Bool(a["Enabled"], true), Process = Str(a["Process"]), Title = Str(a["Title"]), Path = Str(a["Path"]), X = (int)Num(a["X"], 0), Y = (int)Num(a["Y"], 0), W = (int)Num(a["W"], 0), H = (int)Num(a["H"], 0), State = Str(a["State"]).Length > 0 ? Str(a["State"]) : "normal" });
                        Workspaces.Add(ws);
                    }
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
    public static void SetDeskFlag(string key, bool value)
    {
        switch (key)
        {
            case "AutoSave": DeskAutoSave = value; break;
            case "SaveOnExit": DeskSaveOnExit = value; break;
            case "SaveBeforeSwitch": DeskSaveBeforeSwitch = value; break;
            case "RestoreOnChange": DeskRestoreOnChange = value; break;
            case "RestoreOnStartup": DeskRestoreOnStartup = value; break;
        }
    }
    public static void SetDeskMinutes(int minutes) { DeskAutoSaveMinutes = Math.Clamp(minutes, 1, 240); }
    public static bool SetPtEnabled(string child, bool enabled)
    {
        if (AppEnv.DenyWrite()) return false;
        if (child is not ("Zones" or "Grab" or "Palette" or "Paste" or "Peek")) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteNested(AppEnv.ConfigPath, "Pt", child, "Enabled", JsonValue.Create(enabled), out bool changed))
                return false;
            var toy = PowerToys.FirstOrDefault(t => t.Name == child);
            if (toy != null) toy.Enabled = enabled;
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
    }
    public static bool SetRuleEnabled(int index, bool enabled)
    {
        if (AppEnv.DenyWrite()) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteArrayField(AppEnv.ConfigPath, "GlobalRules", index, "Enabled", JsonValue.Create(enabled), out bool changed))
                return false;
            if (index >= 0 && index < Rules.Count) Rules[index].Enabled = enabled;
            if (index >= 0 && index < WindowRules.Count) WindowRules[index].Enabled = enabled;
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
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
