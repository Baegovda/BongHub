using System.Text;
using System.Text.Json.Nodes;

namespace BongHub;

public sealed class NotesSettings { public string DefaultColor = "g:blocks"; public bool Gradient = true; public float FontSize = 11; public int Opacity = 100; public bool OpenOnStartup = true; public string Hotkey = "Ctrl+Alt+N"; }
public sealed class RuleSnapshot { public string Name = ""; public string Process = ""; public bool Enabled = true; }
public sealed class WorkspaceSnapshot { public string Id = ""; public string Name = ""; public List<WorkspaceItem> Apps = new(); }
public sealed class ToySnapshot
{
    public string Name = "";
    public bool Enabled;
    public string Hotkey = "";
    public Dictionary<string, string> Values = new(StringComparer.Ordinal);
    public bool Flag(string key, bool fallback) => Values.TryGetValue(key, out var s) ? s == "true" : fallback;
    public string Text(string key) => Values.TryGetValue(key, out var s) ? s : "";
    public int Number(string key, int fallback) => Values.TryGetValue(key, out var s) && int.TryParse(s, out int n) ? n : fallback;
}

/// <summary>
/// Reads 3.4.x config.json. A Notes change replaces that one JSON value and leaves every other byte alone,
/// so instant-apply settings do not rewrite the file the stable app and the cloud sync both own.
/// </summary>
public static class ConfigStore
{
    public static List<MonitorPreset> Presets { get; } = new();
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
    public static bool KakaoCompact { get; private set; } = true;
    /// <summary>Read and written so a 3.4.x config keeps the key. The helper is never started with it.</summary>
    public static bool KakaoDark { get; private set; }
    public static bool KakaoAutoApply { get; private set; } = true;
    public static bool CloudEnabled { get; private set; }
    public static string CloudRepo { get; private set; } = "";
    public static string CloudBranch { get; private set; } = "main";
    public static int CloudPullMinutes { get; private set; } = 2;
    public static string DeskHotkeySave { get; private set; } = "";
    public static string DeskHotkeyRestore { get; private set; } = "";
    public static bool DeskAutoSave { get; private set; } = true;
    public static int DeskAutoSaveMinutes { get; private set; } = 10;
    public static bool DeskSaveOnExit { get; private set; } = true;
    public static bool DeskSaveBeforeSwitch { get; private set; } = true;
    public static bool DeskRestoreOnChange { get; private set; } = true;
    public static bool DeskRestoreOnStartup { get; private set; }
    public static List<ToySnapshot> PowerToys { get; } = new();
    public static bool AirEnabled { get; set; }
    public static string AirHotkey { get; set; } = "";
    public static int AirIcon { get; set; } = 56;
    public static List<AirProgram> AirItems { get; } = new();
    public static string LoadError = "";
    public static int Writes;

    public static void Load()
    {
        Presets.Clear(); Notes = new NotesSettings(); TabOrder.Clear(); Rules.Clear(); WindowRules.Clear(); Workspaces.Clear(); PowerToys.Clear(); AirItems.Clear();
        AirEnabled = false; AirHotkey = ""; AirIcon = 56;
        IdleEnabled = true; IdleMinutes = 30; IdlePreset = ""; SkipIfAudio = true; OverlayEnabled = true; OverlayOpacity = 82;
        KakaoAdBlock = false; KakaoCompact = true; KakaoDark = false; KakaoAutoApply = true;
        CloudEnabled = false; CloudRepo = ""; CloudBranch = "main"; CloudPullMinutes = 2;
        DeskHotkeySave = ""; DeskHotkeyRestore = "";
        DeskAutoSave = true; DeskAutoSaveMinutes = 10; DeskSaveOnExit = true; DeskSaveBeforeSwitch = true; DeskRestoreOnChange = true; DeskRestoreOnStartup = false;
        LoadError = "";
        try
        {
            if (!File.Exists(AppEnv.ConfigPath)) { Defaults(); return; }
            var root = JsonNode.Parse(File.ReadAllText(AppEnv.ConfigPath, Encoding.UTF8)) as JsonObject;
            if (root?["Presets"] is JsonArray arr)
                foreach (var p in arr.OfType<JsonObject>()) Presets.Add(PresetCodec.FromObject(p));
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
            if (root?["Kakao"] is JsonObject k)
            {
                KakaoAdBlock = Bool(k["AdBlock"], false);
                KakaoCompact = Bool(k["Compact"], true);
                KakaoDark = Bool(k["Dark"], false);
                KakaoAutoApply = Bool(k["AutoApply"], true);
            }
            if (root?["Cloud"] is JsonObject cloud)
            {
                CloudEnabled = Bool(cloud["Enabled"], false);
                CloudRepo = Str(cloud["Repo"]);
                string branch = Str(cloud["Branch"]);
                CloudBranch = branch.Length > 0 ? branch : "main";
                CloudPullMinutes = Math.Clamp((int)Num(cloud["PullMinutes"], 2), 1, 240);
            }
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
            if (root?["AirDock"] is JsonObject air)
            {
                AirEnabled = Bool(air["Enabled"], false);
                AirHotkey = HotkeyChord.Normalize(Str(air["Hotkey"]));
                AirIcon = AirDockLayout.ClampIcon((int)Num(air["Icon"], 56));
                if (air["Items"] is JsonArray airItems)
                    foreach (var item in airItems.OfType<JsonObject>())
                    {
                        string path = Str(item["Path"]);
                        if (!AirList.Accept(path)) continue;
                        string name = Str(item["Name"]);
                        AirItems.Add(new AirProgram { Name = name.Length > 0 ? name : AirList.Title(path), Path = path });
                    }
            }
            if (root?["Pt"] is JsonObject pt)
            {
                Toy("Zones", pt["Zones"]); Toy("Grab", pt["Grab"]); Toy("Work", pt["Work"]);
                Toy("Palette", pt["Palette"]); Toy("Paste", pt["Paste"]); Toy("Peek", pt["Peek"]); Toy("Resizer", pt["Resizer"]);
                if (pt["Work"] is JsonObject work && work["Items"] is JsonArray items)
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        var ws = new WorkspaceSnapshot { Id = Str(item["Id"]), Name = Str(item["Name"]) };
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
        var toy = new ToySnapshot { Name = name };
        if (node is JsonObject o)
        {
            foreach (var kv in o)
            {
                if (kv.Value is not JsonValue v) continue;
                if (v.TryGetValue<bool>(out bool b)) toy.Values[kv.Key] = b ? "true" : "false";
                else if (v.TryGetValue<string>(out string? s)) toy.Values[kv.Key] = s ?? "";
                else if (v.TryGetValue<double>(out double d)) toy.Values[kv.Key] = ((int)Math.Round(d)).ToString();
            }
            toy.Enabled = toy.Flag("Enabled", false);
            toy.Hotkey = toy.Text("Hotkey");
        }
        PowerToys.Add(toy);
    }
    static void Defaults() { if (Presets.Count == 0) { Presets.Add(new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1" }); Presets.Add(new MonitorPreset { Name = "자리비움", Hotkey = "Ctrl+Alt+0" }); } }
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

    public static bool SaveAir()
    {
        var items = new JsonArray();
        foreach (var item in AirItems)
            items.Add(new JsonObject { ["Name"] = item.Name ?? "", ["Path"] = item.Path ?? "" });
        bool ok = SetSectionValue("AirDock", "Enabled", JsonValue.Create(AirEnabled));
        ok = SetSectionValue("AirDock", "Hotkey", JsonValue.Create(AirHotkey ?? "")) && ok;
        ok = SetSectionValue("AirDock", "Icon", JsonValue.Create(AirIcon)) && ok;
        ok = SetSectionValue("AirDock", "Items", items) && ok;
        return ok;
    }
    public static bool SetPtEnabled(string child, bool enabled) => SetPt(child, "Enabled", JsonValue.Create(enabled));

    public static bool SetPt(string child, string field, JsonNode value)
    {
        if (AppEnv.DenyWrite()) return false;
        if (!PtCatalog.Known(child) || !PtField(child, field)) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteNested(AppEnv.ConfigPath, "Pt", child, field, value, out bool changed))
                return false;
            var toy = PowerToys.FirstOrDefault(t => t.Name == child);
            if (toy != null)
            {
                string stored = value is JsonValue v && v.TryGetValue<bool>(out bool b) ? (b ? "true" : "false")
                    : value is JsonValue s && s.TryGetValue<string>(out string? text) ? text ?? ""
                    : value?.ToJsonString() ?? "";
                toy.Values[field] = stored;
                if (field == "Enabled") toy.Enabled = stored == "true";
                if (field == "Hotkey") toy.Hotkey = stored;
            }
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
    }

    static bool PtField(string child, string field) => field is "Enabled" or "Hotkey" or "ShiftDrag" or "ShowOnDrag" or "Span" or "WinArrows" or "RestoreSize" or "AllMonitors" or "Opacity" or "Numbers" or "Modifier" or "Resize" or "SkipFullscreen" or "LaunchMissing" or "MoveExisting" or "Apps" or "Calc" or "Web" or "Units" or "Time" or "Windows" or "Files" or "Shell" or "Terminal" or "System" or "Settings" or "PlainHotkey" or "JsonHotkey" or "MarkdownHotkey" or "CloseOnFocusLoss" or "RenderHtml" or "ContextMenu" or "Fit" or "ShrinkOnly" or "Replace" or "Quality" or "IgnoreOrientation" or "KeepDate" or "RemoveMeta" or "NameFormat" or "Fallback";
    public static KakaoSettings Kakao() => new() { AdBlock = KakaoAdBlock, Compact = KakaoCompact, AutoApply = KakaoAutoApply };
    public static CloudSettings Cloud() => new() { Enabled = CloudEnabled, Repo = CloudRepo, Branch = CloudBranch, PullMinutes = CloudPullMinutes };
    public static bool SetCloudEnabled(bool enabled)
    {
        if (AppEnv.DenyWrite()) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteFile(AppEnv.ConfigPath, "Cloud", "Enabled", JsonValue.Create(enabled), out bool changed))
                return false;
            CloudEnabled = enabled;
            if (changed) Writes++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("config patch failed: " + e.Message); return false; }
    }
    public static bool SetKakaoFlag(string key, bool value)
    {
        if (AppEnv.DenyWrite()) return false;
        if (key is not ("AdBlock" or "Compact" or "Dark" or "AutoApply")) return false;
        try
        {
            if (!NotesConfigPatch.TryWriteFile(AppEnv.ConfigPath, "Kakao", key, JsonValue.Create(value), out bool changed))
                return false;
            switch (key)
            {
                case "AdBlock": KakaoAdBlock = value; break;
                case "Compact": KakaoCompact = value; break;
                case "Dark": KakaoDark = value; break;
                case "AutoApply": KakaoAutoApply = value; break;
            }
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

/// <summary>Preferences kept outside the shared config.json.</summary>
public static class AppSettings
{
    public static bool ReduceMotion { get; private set; }
    /// <summary>When on, monitor tiles on the profile canvas do not drag. Missing JSON means on.</summary>
    public static bool MonitorLocked { get; private set; } = true;
    /// <summary>Registered window chips on the profile canvas. Missing JSON means on. Ghost chips stay either way.</summary>
    public static bool ChipsVisible { get; private set; } = true;
    public static void Load()
    {
        try
        {
            if (File.Exists(AppEnv.SettingsPath) && JsonNode.Parse(File.ReadAllText(AppEnv.SettingsPath)) is JsonObject o)
            {
                ReduceMotion = o["ReduceMotion"]?.GetValue<bool>() ?? false;
                MonitorLocked = o["MonitorLocked"] == null || (o["MonitorLocked"]?.GetValue<bool>() ?? true);
                ChipsVisible = o["ChipsVisible"] == null || (o["ChipsVisible"]?.GetValue<bool>() ?? true);
            }
        }
        catch { }
        Fx.Enabled = !ReduceMotion;
    }
    public static void SetReduceMotion(bool v)
    {
        ReduceMotion = v; Fx.Enabled = !v;
        Write();
    }
    public static void SetMonitorLocked(bool v)
    {
        MonitorLocked = v;
        Write();
    }
    public static void SetChipsVisible(bool v)
    {
        ChipsVisible = v;
        Write();
    }
    static void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppEnv.SettingsPath)!);
            File.WriteAllText(AppEnv.SettingsPath, new JsonObject { ["ReduceMotion"] = ReduceMotion, ["MonitorLocked"] = MonitorLocked, ["ChipsVisible"] = ChipsVisible }.ToJsonString());
        }
        catch { }
    }
}
