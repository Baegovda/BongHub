using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BongHub.Next;

public sealed class PresetSnapshot { public string Name { get; set; } = ""; public string Hotkey { get; set; } = ""; }
public sealed class NotesSettings { public string DefaultColor = "g:blocks"; public bool Gradient = true; public float FontSize = 11; public int Opacity = 100; public bool OpenOnStartup = true; public string Hotkey = "Ctrl+Alt+N"; }

/// <summary>
/// Reads 3.4.7's config.json and patches single keys in place (every other key is preserved verbatim),
/// so instant-apply settings never drop data the stable version owns.
/// </summary>
public static class ConfigStore
{
    public static List<PresetSnapshot> Presets { get; } = new();
    public static NotesSettings Notes { get; private set; } = new();
    public static string LoadError = "";
    public static int Writes;

    public static void Load()
    {
        Presets.Clear(); Notes = new NotesSettings(); LoadError = "";
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
            if (Presets.Count == 0) Defaults();
        }
        catch (Exception e) { LoadError = e.Message; Defaults(); }
    }
    static void Defaults() { if (Presets.Count == 0) { Presets.Add(new() { Name = "작업", Hotkey = "Ctrl+Alt+1" }); Presets.Add(new() { Name = "자리비움", Hotkey = "Ctrl+Alt+0" }); } }
    static string Str(JsonNode? n) { try { return n?.GetValue<string>() ?? ""; } catch { return n?.ToString() ?? ""; } }
    static bool Bool(JsonNode? n, bool d) { try { return n?.GetValue<bool>() ?? d; } catch { return d; } }
    static double Num(JsonNode? n, double d) { try { return n?.GetValue<double>() ?? d; } catch { return d; } }

    /// <summary>Instant apply: patch Notes.&lt;key&gt; in config.json (atomic). Returns false when read-only or failed.</summary>
    public static bool SetNotesValue(string key, JsonNode value)
    {
        if (AppEnv.DenyWrite()) return false;
        try
        {
            JsonObject root = File.Exists(AppEnv.ConfigPath) ? (JsonNode.Parse(File.ReadAllText(AppEnv.ConfigPath, Encoding.UTF8)) as JsonObject ?? new JsonObject()) : new JsonObject { ["Version"] = 2 };
            if (root["Notes"] is not JsonObject notes) { notes = new JsonObject(); root["Notes"] = notes; }
            notes[key] = value;
            var json = root.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Directory.CreateDirectory(AppEnv.DataDir);
            var tmp = AppEnv.ConfigPath + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(AppEnv.ConfigPath)) File.Replace(tmp, AppEnv.ConfigPath, null); else File.Move(tmp, AppEnv.ConfigPath);
            Writes++; return true;
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
