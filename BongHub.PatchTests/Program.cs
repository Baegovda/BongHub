using System.Text;
using System.Text.Json.Nodes;
using BongHub;

int fail = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " — " + detail));
    if (!ok) fail++;
}

// --- real synced shape, read only ---
string? repo = FindRepo();
Check("repo root", repo != null);
if (repo != null)
{
    string path = Path.Combine(repo, "data", "config.json");
    byte[] raw = File.ReadAllBytes(path);
    string text = Encoding.UTF8.GetString(raw);
    Check("synced config has no BOM", raw.Length < 3 || raw[0] != 0xEF);

    Check("Opacity changes one token", OneNumber(text, "Opacity", 81, 82));
    Check("Gradient toggles one token", OneBool(text, "Gradient"));
    Check("FontSize changes one number", OneNumber(text, "FontSize", 17, 19));
    Check("synced tab order keeps its lines", ReorderSyncedTab(text));
}

bool ReorderSyncedTab(string text)
{
    const string oldBlock = "[\r\n      \"presets\",\r\n      \"windows\",\r\n      \"notes\",\r\n      \"kakao\",\r\n      \"desktop\",\r\n      \"powertoys\",\r\n      \"general\",\r\n      \"about\"\r\n    ]";
    const string newBlock = "[\r\n      \"windows\",\r\n      \"notes\",\r\n      \"kakao\",\r\n      \"desktop\",\r\n      \"powertoys\",\r\n      \"general\",\r\n      \"about\",\r\n      \"presets\"\r\n    ]";
    if (!text.Contains(oldBlock)) return false;
    var arr = new JsonArray();
    foreach (var k in new[] { "windows", "notes", "kakao", "desktop", "powertoys", "general", "about", "presets" }) arr.Add(JsonValue.Create(k));
    if (!NotesConfigPatch.TryApply(text, "General", "TabOrder", arr, out string lined)) return false;
    if (lined != text.Replace(oldBlock, newBlock)) return false;
    NotesConfigPatch.ChangedSpan(text, lined, out string removed, out string inserted);
    return NotesConfigPatch.TabOrderSpanOnly(removed, inserted) && !removed.Contains("IdleEnabled") && !inserted.Contains("IdleEnabled");
}

bool OneNumber(string text, string key, int a, int b)
{
    if (!NotesConfigPatch.TryApply(text, key, JsonValue.Create(a), out string first)) return false;
    int probe = first == text ? b : a;
    if (first == text && !NotesConfigPatch.TryApply(text, key, JsonValue.Create(probe), out first)) return false;
    if (!OnlyToken(text, first, out string oldTok, out string newTok)) return false;
    if (newTok != probe.ToString()) return false;
    if (!double.TryParse(oldTok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double live)) return false;
    if (live != Math.Truncate(live) || live < int.MinValue || live > int.MaxValue) return false;
    int at = IndexOfToken(text, first);
    if (at < 0 || text.LastIndexOf(key, at, StringComparison.Ordinal) < 0) return false;
    int n = (int)live;
    return NotesConfigPatch.TryApply(text, key, JsonValue.Create(n), out string same) && same == text
        && NotesConfigPatch.TryApply(text, key, JsonValue.Create((float)n), out string sameF) && sameF == text;
}

bool OneBool(string text, string key)
{
    if (!NotesConfigPatch.TryApply(text, key, JsonValue.Create(false), out string first)) return false;
    bool probe = first != text;
    if (!probe && !NotesConfigPatch.TryApply(text, key, JsonValue.Create(true), out first)) return false;
    if (first == text) return false;
    if (!OnlyToken(text, first, out string oldTok, out string newTok)) return false;
    if (oldTok is not ("true" or "false") || newTok is not ("true" or "false") || oldTok == newTok) return false;
    int at = IndexOfToken(text, first);
    if (text.LastIndexOf(key, at, StringComparison.Ordinal) < 0) return false;
    bool live = oldTok == "true";
    return NotesConfigPatch.TryApply(text, key, JsonValue.Create(live), out string same) && same == text;
}

bool OnlyToken(string before, string after, out string oldTok, out string newTok)
{
    oldTok = ""; newTok = "";
    int start = 0;
    int max = Math.Min(before.Length, after.Length);
    while (start < max && before[start] == after[start]) start++;
    if (start == before.Length && start == after.Length) return false;
    int endB = before.Length, endA = after.Length;
    while (endB > start && endA > start && before[endB - 1] == after[endA - 1]) { endB--; endA--; }
    while (start > 0 && Tok(before[start - 1]) && Tok(after[start - 1])) start--;
    while (endB < before.Length && endA < after.Length && Tok(before[endB]) && Tok(after[endA])) { endB++; endA++; }
    oldTok = before.Substring(start, endB - start);
    newTok = after.Substring(start, endA - start);
    return after == before.Substring(0, start) + newTok + before.Substring(endB);
}

static bool Tok(char c) => char.IsLetterOrDigit(c) || c is '.' or '-' or '+';

int IndexOfToken(string before, string after)
{
    int start = 0;
    int max = Math.Min(before.Length, after.Length);
    while (start < max && before[start] == after[start]) start++;
    return start;
}

// --- synthetic ---
const string Pretty = "{\r\n  \"Version\": 2,\r\n  \"General\": {\r\n    \"OverlayOpacity\": 70\r\n  },\r\n  \"Notes\": {\r\n    \"DefaultColor\": \"g:aurora\",\r\n    \"Gradient\": true,\r\n    \"FontSize\": 11,\r\n    \"Hotkey\": \"Ctrl+Alt+N\"\r\n  },\r\n  \"Name\": \"석봉\"\r\n}";
Check("insert missing Opacity", NotesConfigPatch.TryApply(Pretty, "Opacity", JsonValue.Create(80), out string ins));
Check("insert keeps the Korean name", ins.Contains("\"Name\": \"석봉\""));
Check("insert keeps OverlayOpacity line", ins.Contains("    \"OverlayOpacity\": 70\r\n"));
Check("insert keeps DefaultColor line", ins.Contains("    \"DefaultColor\": \"g:aurora\",\r\n"));
Check("inserted line is indented like its siblings", ins.Contains("    \"Hotkey\": \"Ctrl+Alt+N\",\r\n    \"Opacity\": 80\r\n  }"));

const string Mini = "{\"Version\":2,\"General\":{\"OverlayOpacity\":70},\"Notes\":{\"Opacity\":80,\"Gradient\":true},\"Pt\":{\"Zones\":{\"Opacity\":55}}}";
Check("minified splice", NotesConfigPatch.TryApply(Mini, "Opacity", JsonValue.Create(81), out string mini)
    && mini == "{\"Version\":2,\"General\":{\"OverlayOpacity\":70},\"Notes\":{\"Opacity\":81,\"Gradient\":true},\"Pt\":{\"Zones\":{\"Opacity\":55}}}");

const string Spaced = "{\"Notes\":{\"Opacity\" : 80.0 }}";
Check("80.0 already means 80", NotesConfigPatch.TryApply(Spaced, "Opacity", JsonValue.Create(80), out string spaced) && spaced == Spaced);

const string Quoted = "{\"Notes\":{\"Hotkey\":\"say \\\"Opacity\\\": 1\",\"Gradient\":true}}";
Check("string that mentions Opacity stays", NotesConfigPatch.TryApply(Quoted, "Gradient", JsonValue.Create(false), out string quoted)
    && quoted == "{\"Notes\":{\"Hotkey\":\"say \\\"Opacity\\\": 1\",\"Gradient\":false}}");

Check("broken json is refused", !NotesConfigPatch.TryApply("{", "Opacity", JsonValue.Create(1), out string broken) && broken == "{");
Check("bad key is refused", !NotesConfigPatch.TryApply(Mini, "Opa\"city", JsonValue.Create(1), out _));
Check("scalar Notes is refused", !NotesConfigPatch.TryApply("{\"Notes\":1}", "Opacity", JsonValue.Create(1), out string scalar) && scalar == "{\"Notes\":1}");

Check("missing Notes object", NotesConfigPatch.TryApply("{\r\n  \"Version\": 2\r\n}", "Opacity", JsonValue.Create(40), out string add)
    && add == "{\r\n  \"Version\": 2,\r\n  \"Notes\": {\r\n    \"Opacity\": 40\r\n  }\r\n}");

Check("empty object insert is compact", NotesConfigPatch.TryApply("{\"Notes\":{}}", "OpenOnStartup", JsonValue.Create(true), out string empty)
    && empty == "{\"Notes\":{\"OpenOnStartup\": true}}");

string korean = NotesConfigPatch.Format(JsonValue.Create("안녕"));
Check("Korean string is not escaped", korean == "\"안녕\"");

// --- file bytes, temp directory only ---
string dir = Path.Combine(Path.GetTempPath(), "bonghub_patch_" + Environment.ProcessId);
Directory.CreateDirectory(dir);
try
{
    string file = Path.Combine(dir, "config.json");
    File.WriteAllText(file, Pretty, new UTF8Encoding(false));
    var before = File.GetLastWriteTimeUtc(file);
    Thread.Sleep(20);
    Check("unchanged file is not rewritten", NotesConfigPatch.TryWriteFile(file, "FontSize", JsonValue.Create(11), out bool changed) && !changed);
    Check("mtime stays", File.GetLastWriteTimeUtc(file) == before);
    Check("bytes stay", File.ReadAllText(file) == Pretty);

    Check("write changes one token", NotesConfigPatch.TryWriteFile(file, "FontSize", JsonValue.Create(13), out bool wrote) && wrote);
    string after = File.ReadAllText(file);
    Check("file splice is 11 to 13", after == Pretty.Replace("\"FontSize\": 11", "\"FontSize\": 13"));

    string bomFile = Path.Combine(dir, "bom.json");
    File.WriteAllBytes(bomFile, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Mini)).ToArray());
    Check("BOM preserved", NotesConfigPatch.TryWriteFile(bomFile, "Opacity", JsonValue.Create(81), out bool bomChanged) && bomChanged);
    byte[] bomOut = File.ReadAllBytes(bomFile);
    Check("BOM still there and body spliced", bomOut.Length >= 3 && bomOut[0] == 0xEF && bomOut[1] == 0xBB && bomOut[2] == 0xBF
        && Encoding.UTF8.GetString(bomOut, 3, bomOut.Length - 3).Contains("\"Opacity\":81")
        && Encoding.UTF8.GetString(bomOut, 3, bomOut.Length - 3).Contains("\"Opacity\":55"));

    string bad = Path.Combine(dir, "bad.json");
    File.WriteAllText(bad, "{ not json");
    Check("invalid file is not replaced", !NotesConfigPatch.TryWriteFile(bad, "Opacity", JsonValue.Create(1), out _) && File.ReadAllText(bad) == "{ not json");

    string fresh = Path.Combine(dir, "sub", "config.json");
    Check("creates a missing file", NotesConfigPatch.TryWriteFile(fresh, "Opacity", JsonValue.Create(70), out bool made) && made);
    Check("new file is pretty CRLF", File.ReadAllText(fresh) == "{\r\n  \"Version\": 2,\r\n  \"Notes\": {\r\n    \"Opacity\": 70\r\n  }\r\n}");
}
finally
{
    try { Directory.Delete(dir, true); } catch { }
}

const string Shell = "{\"Version\":2,\"Presets\":[{\"Marker\":\"PRESET_KEEP\"}],\"Pt\":{\"Marker\":\"PT_KEEP\"},\"Desk\":{\"Marker\":\"DESK_KEEP\"},\"General\":{\"OverlayOpacity\":70}}";
var order = new JsonArray();
order.Add(JsonValue.Create("about"));
order.Add(JsonValue.Create("presets"));
Check("general tab order splice", NotesConfigPatch.TryApply(Shell, "General", "TabOrder", order, out string shelled)
    && shelled.Contains("PRESET_KEEP") && shelled.Contains("PT_KEEP") && shelled.Contains("DESK_KEEP")
    && shelled.Contains("\"OverlayOpacity\":70") && shelled.Contains("\"TabOrder\""));
NotesConfigPatch.ChangedSpan(Shell, shelled, out string tabRemoved, out string tabInserted);
Check("tab order byte span leaves foreign keys", NotesConfigPatch.TabOrderSpanOnly(tabRemoved, tabInserted) && tabRemoved.Length == 0);
const string PrettyOrder = "{\r\n  \"Version\": 2,\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }],\r\n  \"Pt\": { \"Marker\": \"PT_KEEP\" },\r\n  \"Desk\": { \"Marker\": \"DESK_KEEP\" },\r\n  \"General\": {\r\n    \"TabOrder\": [\r\n      \"presets\",\r\n      \"about\"\r\n    ],\r\n    \"OverlayOpacity\": 70,\r\n    \"IdleEnabled\": true\r\n  }\r\n}\r\n";
var swapped = new JsonArray();
swapped.Add(JsonValue.Create("about"));
swapped.Add(JsonValue.Create("presets"));
string expectedPretty = PrettyOrder.Replace(
    "[\r\n      \"presets\",\r\n      \"about\"\r\n    ]",
    "[\r\n      \"about\",\r\n      \"presets\"\r\n    ]");
Check("pretty tab order replaces only the array", NotesConfigPatch.TryApply(PrettyOrder, "General", "TabOrder", swapped, out string prettyOut)
    && prettyOut == expectedPretty);
NotesConfigPatch.ChangedSpan(PrettyOrder, prettyOut, out string prettyRemoved, out string prettyInserted);
Check("pretty tab order byte span", prettyOut == expectedPretty && NotesConfigPatch.TabOrderSpanOnly(prettyRemoved, prettyInserted)
    && prettyOut.Contains("\"IdleEnabled\": true") && prettyOut.Contains("\"OverlayOpacity\": 70"));
Check("notes patch still leaves Presets", NotesConfigPatch.TryApply(Shell, "Opacity", JsonValue.Create(9), out string notesOnly)
    && notesOnly.Contains("\"Opacity\": 9") && notesOnly.Contains("PRESET_KEEP") && notesOnly.Contains("PT_KEEP") && notesOnly.Contains("DESK_KEEP"));

const string DeskCfg = "{\"Version\":2,\"Presets\":[{\"Marker\":\"PRESET_KEEP\"}],\"Pt\":{\"Marker\":\"PT_KEEP\"},\"Desk\":{\"AutoSave\":true,\"Marker\":\"DESK_KEEP\",\"HotkeySave\":\"Ctrl+Alt+S\"},\"General\":{\"OverlayOpacity\":70}}";
const string DeskPretty = "{\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }],\r\n  \"Pt\": { \"Marker\": \"PT_KEEP\" },\r\n  \"Desk\": {\r\n    \"AutoSave\": true,\r\n    \"Marker\": \"DESK_KEEP\",\r\n    \"HotkeySave\": \"Ctrl+Alt+S\"\r\n  },\r\n  \"General\": { \"OverlayOpacity\": 70 }\r\n}\r\n";
Check("desk autosave splices one token", NotesConfigPatch.TryApply(DeskCfg, "Desk", "AutoSave", JsonValue.Create(false), out string desked)
    && desked == DeskCfg.Replace("\"AutoSave\":true", "\"AutoSave\":false"));
Check("desk autosave keeps pretty formatting", NotesConfigPatch.TryApply(DeskPretty, "Desk", "AutoSave", JsonValue.Create(false), out string deskPretty)
    && deskPretty == DeskPretty.Replace("\"AutoSave\": true", "\"AutoSave\": false"));
NotesConfigPatch.ChangedSpan(DeskCfg, desked, out string deskRemoved, out string deskInserted);
Check("desk autosave byte span", OnlyToken(DeskCfg, desked, out string deskOld, out string deskNew) && deskOld == "true" && deskNew == "false"
    && !deskRemoved.Contains("DESK_KEEP") && !deskInserted.Contains("DESK_KEEP") && !deskRemoved.Contains("PRESET_KEEP") && !deskInserted.Contains("PT_KEEP"),
    "removed=[" + deskRemoved + "] inserted=[" + deskInserted + "]");

Check("screen description puts the primary first", ScreenMap.Describe("1920x1080@0,0;2560x1440@1920,0*") == "모니터 2대 · 2560×1440(주) + 1920×1080");
string screen = ScreenMap.Key(new[] { new MonitorSpec(1920, 1080, 0, 0, false), new MonitorSpec(2560, 1440, 1920, 0, true) });
Check("screen key sorts and marks the primary", screen == "1920x1080@0,0;2560x1440@1920,0*");

var mem = new MemoryIconSurface();
mem.Icons.Add(new DeskIcon { Name = "Readme", X = 10, Y = 10 });
mem.Icons.Add(new DeskIcon { Name = "readme", X = 20, Y = 20 });
mem.Icons.Add(new DeskIcon { Name = "Other", X = 30, Y = 30 });
var iconTarget = new List<DeskIcon>
{
    new() { Name = "README", X = 11, Y = 12 },
    new() { Name = "ReadMe", X = 21, Y = 22 },
    new() { Name = "없는 아이콘", X = 1, Y = 1 }
};
var applied = IconMatch.Apply(mem, iconTarget);
Check("duplicate icon names match in order", applied.Ok && applied.Moved == 2 && applied.Missing == 1 && applied.Extra == 1
    && mem.Icons[0].X == 11 && mem.Icons[0].Y == 12 && mem.Icons[1].X == 21 && mem.Icons[2].X == 30 && mem.Refreshes == 1);
mem.AutoArrange = true;
int movesBefore = mem.Moves;
var refused = IconMatch.Apply(mem, iconTarget);
Check("auto-arrange refuses without moving", !refused.Ok && refused.Error.Contains("자동 정렬") && mem.Moves == movesBefore && mem.Icons[0].X == 11 && mem.Refreshes == 1);

string iconDir = Path.Combine(Path.GetTempPath(), "bonghub_icons_" + Environment.ProcessId);
try
{
    var store = new LayoutStore(iconDir);
    var icons = new List<DeskIcon> { new() { Name = "휴지통", X = 14, Y = 2 } };
    Check("auto save writes one layout", IconLibrary.SaveAuto(store, icons, "1920x1080@0,0*"));
    Check("auto save skips when unchanged", !IconLibrary.SaveAuto(store, icons, "1920x1080@0,0*"));
    Check("one auto layout per screen", store.Load().Count(x => x.Auto && x.Key == "1920x1080@0,0*") == 1);
    icons[0].Y = 9;
    Check("auto save replaces the same screen", IconLibrary.SaveAuto(store, icons, "1920x1080@0,0*"));
    Check("still one auto layout", store.Load().Count(x => x.Auto) == 1 && store.Load().First(x => x.Auto).Items[0].Y == 9);
    Check("second screen keeps its own auto layout", IconLibrary.SaveAuto(store, new List<DeskIcon> { new() { Name = "휴지통", X = 1, Y = 1 } }, "2560x1440@1920,0*"));
    Check("two screens two autos", store.Load().Count(x => x.Auto) == 2);
    var named = store.Load().First(x => x.Key == "1920x1080@0,0*");
    IconLibrary.Rename(store, named.Id, "이름 바꿈");
    var renamed = store.Load().First(x => x.Id == named.Id);
    Check("rename clears the auto flag", renamed.Name == "이름 바꿈" && !renamed.Auto);
    IconLibrary.Delete(store, named.Id);
    Check("delete drops the layout", store.Load().All(x => x.Id != named.Id));
}
finally { try { Directory.Delete(iconDir, true); } catch { } }

var when = DateTimeOffset.FromUnixTimeMilliseconds(1793585924140).UtcDateTime;
string layoutJson = LayoutStore.Serialize(new List<DeskLayoutFile>
{
    new()
    {
        Id = "abc", Name = "배치", Key = "1920x1080@0,0*", Desc = "모니터 1대 · 1920×1080(주)", Auto = true, Saved = when,
        Items = new List<DeskIcon> { new() { Name = "휴지통", X = 14, Y = 2 } }
    }
});
int idAt = layoutJson.IndexOf("\"Id\"", StringComparison.Ordinal), nameAt = layoutJson.IndexOf("\"Name\"", StringComparison.Ordinal), keyAt = layoutJson.IndexOf("\"Key\"", StringComparison.Ordinal);
int descAt = layoutJson.IndexOf("\"Desc\"", StringComparison.Ordinal), autoAt = layoutJson.IndexOf("\"Auto\"", StringComparison.Ordinal), savedAt = layoutJson.IndexOf("\"Saved\"", StringComparison.Ordinal), itemsAt = layoutJson.IndexOf("\"Items\"", StringComparison.Ordinal);
Check("layout field order and ajax date", idAt >= 0 && idAt < nameAt && nameAt < keyAt && keyAt < descAt && descAt < autoAt && autoAt < savedAt && savedAt < itemsAt
    && layoutJson.Contains("\\/Date(1793585924140)\\/") && layoutJson.Contains("배치") && layoutJson.Contains("휴지통") && !layoutJson.Contains("\\u"));
var layoutBack = LayoutStore.Parse(layoutJson);
Check("layout date round trip", layoutBack.Count == 1 && layoutBack[0].Items[0].Name == "휴지통" && layoutBack[0].Items[0].X == 14
    && new DateTimeOffset(layoutBack[0].Saved).ToUnixTimeMilliseconds() == 1793585924140);

DeskLayoutFile Stamp(string id, long ms) => new() { Id = id, Name = id, Key = "1920x1080@0,0*", Saved = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, Items = new() };
var merged = LayoutMerge.Merge(new[] { Stamp("A", 100), Stamp("C", 50) }, new[] { Stamp("A", 200), Stamp("B", 10) }, new[] { "A", "B" });
Check("layout merge keeps the newer snapshot", merged.Count == 2 && merged[0].Id == "A" && merged[0].Saved == DateTimeOffset.FromUnixTimeMilliseconds(200).UtcDateTime && merged[1].Id == "C");
var mergedLocal = LayoutMerge.Merge(new[] { Stamp("A", 300) }, new[] { Stamp("A", 200) }, new[] { "A" });
Check("layout merge prefers a newer local snapshot", mergedLocal.Count == 1 && mergedLocal[0].Saved == DateTimeOffset.FromUnixTimeMilliseconds(300).UtcDateTime);

if (repo != null)
{
    string layoutsPath = Path.Combine(repo, "data", "desktop", "layouts.json");
    byte[] rawLayouts = File.ReadAllBytes(layoutsPath);
    var parsedLayouts = LayoutStore.Parse(Encoding.UTF8.GetString(rawLayouts));
    Check("synced layouts parse", parsedLayouts.Count >= 1 && parsedLayouts[0].Items.Count > 0 && parsedLayouts[0].Id.Length == 32);
    Check("synced layouts.json bytes stay", File.ReadAllBytes(layoutsPath).AsSpan().SequenceEqual(rawLayouts));
}

Check("desktop gate stays shut", !DesktopGate.RealAllowed && ExplorerIconSurface.NativeCalls == 0);
bool desktopThrew = false;
try { using var blocked = new ExplorerIconSurface(); } catch (InvalidOperationException) { desktopThrew = true; }
Check("desktop backend refuses before any native call", desktopThrew && ExplorerIconSurface.NativeCalls == 0 && ExplorerIconSurface.Attempts == 1);

var ruleWindows = new FakeWindowSurface();
ruleWindows.Areas["SAM"] = new WorkRect(1920, 0, 1920, 1080);
ruleWindows.Windows.Add(new TrackedWindow { Id = 1, Process = "Discord", Title = "친구 - Discord", Class = "Chrome_WidgetWin_1", X = 0, Y = 0, W = 400, H = 300 });
ruleWindows.Windows.Add(new TrackedWindow { Id = 2, Process = "notepad", Title = "제목 없음", X = 8, Y = 8, W = 300, H = 200 });
var firstRules = new List<WindowRule>
{
    new() { Enabled = false, Process = "Discord", Action = "min" },
    new() { Process = "Discord", Title = "친구", Action = "pos", X = 30, Y = 40, W = 900, H = 700, Pin = true },
    new() { Process = "Discord", Action = "min" },
    new() { Process = "notepad.exe", Action = "pos", X = 15, Y = 18, W = 500, H = 400 }
};
Check("first matching window rule wins", RuleEngine.Apply(ruleWindows, firstRules) == 2
    && ruleWindows.Windows[0].X == 30 && ruleWindows.Windows[0].Y == 40 && ruleWindows.Windows[0].W == 900 && ruleWindows.Windows[0].TopMost
    && !ruleWindows.Windows[0].Min && ruleWindows.Windows[1].X == 15 && !ruleWindows.Ops.Contains("min 1"));
Check("rule without criteria does not match", !RuleMatch.Match(new WindowRule { Title = "" }, ruleWindows.Windows[0]));
Check("bad title regex does not match", !RuleMatch.Match(new WindowRule { Process = "Discord", Title = "(", TitleRegex = true }, ruleWindows.Windows[0]));
var keepRules = new KeepWatch();
keepRules.SetRules(new[] { new WindowRule { Keep = true, Process = "Discord", Action = "pos", X = 30, Y = 40, W = 900, H = 700 } });
int keepOps = ruleWindows.Ops.Count;
keepRules.Check(ruleWindows, 1);
Check("keep watch leaves an in-place window", keepRules.Active && keepRules.Applied == 0 && ruleWindows.Ops.Count == keepOps);
ruleWindows.Windows[0].X = 400;
keepRules.Check(ruleWindows, 1);
Check("keep watch moves only an out-of-place window", keepRules.Applied == 1 && ruleWindows.Windows[0].X == 30);
var workspaceMoves = WorkspacePlan.Plan(
    new[] { new TrackedWindow { Id = 3, Process = "Code", Title = "a" }, new TrackedWindow { Id = 4, Process = "Code", Title = "b" } },
    new[] { new WorkspaceItem { Process = "Code.exe", Title = "b", X = 100, Y = 120, W = 800, H = 600 }, new WorkspaceItem { Process = "absent", Title = "nope" } },
    out int workspaceMissing);
Check("workspace plan prefers the title and does not launch", workspaceMoves.Count == 1 && workspaceMoves[0].WindowId == 4 && workspaceMoves[0].X == 100 && workspaceMissing == 1);

const string RuleCfg = "{\"Presets\":[{\"Marker\":\"PRESET_KEEP\",\"Rules\":[{\"Enabled\":true,\"Name\":\"프리셋\"}]}],\"Pt\":{\"Marker\":\"PT_KEEP\"},\"Desk\":{\"Marker\":\"DESK_KEEP\"},\"GlobalRules\":[{\"Enabled\":true,\"Name\":\"Discord\",\"Process\":\"Discord\",\"Keep\":false},{\"Enabled\":true,\"Name\":\"notepad\"}]}";
const string RulePretty = "{\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }],\r\n  \"GlobalRules\": [\r\n    {\r\n      \"Enabled\": true,\r\n      \"Name\": \"Discord\",\r\n      \"Process\": \"Discord\"\r\n    }\r\n  ],\r\n  \"Desk\": { \"Marker\": \"DESK_KEEP\" }\r\n}\r\n";
Check("global rule enabled splices one token", NotesConfigPatch.TryApplyArrayField(RuleCfg, "GlobalRules", 0, "Enabled", JsonValue.Create(false), out string ruled)
    && ruled == RuleCfg.Replace("\"Name\":\"Discord\",\"Process\":\"Discord\",\"Keep\":false", "\"Name\":\"Discord\",\"Process\":\"Discord\",\"Keep\":false").Replace("{\"Enabled\":true,\"Name\":\"Discord\"", "{\"Enabled\":false,\"Name\":\"Discord\"")
    && ruled.Contains("\"Enabled\":true,\"Name\":\"프리셋\"") && ruled.Contains("\"Enabled\":true,\"Name\":\"notepad\"") && ruled.Contains("PRESET_KEEP") && ruled.Contains("PT_KEEP") && ruled.Contains("DESK_KEEP"));
Check("global rule enabled keeps pretty formatting", NotesConfigPatch.TryApplyArrayField(RulePretty, "GlobalRules", 0, "Enabled", JsonValue.Create(false), out string ruledPretty)
    && ruledPretty == RulePretty.Replace("\"Enabled\": true", "\"Enabled\": false"));
Check("global rule enabled byte span", OnlyToken(RuleCfg, ruled, out string ruleOld, out string ruleNew) && ruleOld == "true" && ruleNew == "false");
Check("window gate stays shut", !WindowGate.RealAllowed && LiveWindowSurface.NativeCalls == 0);
bool windowThrew = false;
try { using var blockedWindows = new LiveWindowSurface(); } catch (InvalidOperationException) { windowThrew = true; }
Check("window backend refuses before any native call", windowThrew && LiveWindowSurface.NativeCalls == 0 && LiveWindowSurface.Attempts == 1);

var area = new PxRect(0, 0, 1920, 1080);
var cols = ZoneMath.Rects(new ZoneLayout { Kind = "cols", A = 2, Spacing = 0 }, area);
Check("2 columns", cols.Count == 2 && cols[0].W == 960 && cols[1].Right == 1920);
var grid = ZoneMath.Rects(new ZoneLayout { Kind = "grid", A = 2, B = 2, Spacing = 8 }, area);
Check("2x2 grid inside area, no overlap", grid.Count == 4 && grid.All(r => area.Contains(r)) && !grid[0].Intersects(grid[3]));
var pri = ZoneMath.Rects(new ZoneLayout { Kind = "priority", Spacing = 0 }, new PxRect(0, 0, 4000, 1000));
Check("priority 25/50/25", pri.Count == 3 && pri[1].W == 2000 && pri[0].W == 1000);
var custom = ZoneMath.Rects(new ZoneLayout { Kind = "custom", Custom = new List<float> { 1, 3 }, Spacing = 0 }, area);
Check("custom ratio 1:3", custom.Count == 2 && custom[0].W == 480);
var rows = ZoneMath.Rects(new ZoneLayout { Kind = "rows", A = 3, Spacing = 0 }, area);
Check("3 rows", rows.Count == 3 && rows[2].Bottom == 1080);
var editable = new ZoneLayout { Kind = "cols", A = 2 };
ZoneMath.Split(editable, 0, false);
Check("editor split -> 3 zones", editable.Kind == "canvas" && ZoneMath.Rects(editable, new PxRect(0, 0, 1000, 1000)).Count == 3);
Check("editor merge -> 2 zones", ZoneMath.Merge(editable, 0, 1) && ZoneMath.Rects(editable, new PxRect(0, 0, 1000, 1000)).Count == 2);
Check("bad zone index is empty", ZoneMath.ZoneAt(area, new ZoneLayout { Kind = "cols", A = 2 }, 99) == null && ZoneMath.ZoneAt(area, new ZoneLayout { Kind = "cols", A = 2 }, 0) != null);
Check("risky tools default off", !PtDefaults.Zones && !PtDefaults.Grab && !PtDefaults.Palette && !PtDefaults.Paste && !PtDefaults.Peek && !PtDefaults.ResizerContextMenu && PtDefaults.Resizer);
Check("calculator", PaletteMath.Calc("12*3+4") == "40" && PaletteMath.Calc("(1+2)/4") == "0.75" && PaletteMath.Calc("2^10+sqrt(16)*(3-1)") == "1032" && PaletteMath.Calc("10 % 3") == "1" && PaletteMath.Calc("0xff") == "255" && PaletteMath.Calc("hello") == null && PaletteMath.Calc("123") == null);
Check("fuzzy score", PaletteMath.Score("메모", "새 스티커 메모") > 0 && PaletteMath.Score("ntp", "notepad") > 0 && PaletteMath.Score("xyz", "notepad") == 0);
var found = PaletteMath.Search("작업", new[] { ("모니터 프리셋: 작업", "작업") });
Check("palette finds preset", found.Count >= 1 && found[0].Contains("작업"));
Check("palette calc first", PaletteMath.Search("7*6", Array.Empty<(string, string)>())[0] == "= 42");
string km = PaletteMath.Unit("10 km to mi") ?? "", temp = PaletteMath.Unit("100 c to f") ?? "";
Check("unit km->mi and c->f", km.StartsWith("6.21") && temp.StartsWith("212"));
string html = "Version:0.9\r\n<html><body><!--StartFragment--><h2>제목</h2><p>이건 <b>굵게</b>, <i>기울임</i>, <a href=\"https://x.y\">링크</a></p><ul><li>하나</li><li>둘</li></ul><!--EndFragment--></body></html>";
string markdown = PasteText.HtmlToMarkdown(html) ?? "";
Check("html -> markdown", markdown.Contains("## 제목") && markdown.Contains("**굵게**") && markdown.Contains("*기울임*") && markdown.Contains("[링크](https://x.y)") && markdown.Contains("- 하나"));
string csv = PasteText.ToJson("name,age\n봉,30\n석,25") ?? "";
Check("csv -> json", csv.Contains("\"name\": \"봉\"") && csv.Contains("\"age\": 30"));
Check("tsv -> json", (PasteText.ToJson("a\tb\n1\t2") ?? "").Contains("\"a\": 1"));
string prettyJson = PasteText.ToJson("{\"x\":[1,2],\"y\":\"z\"}") ?? "";
Check("json pretty", prettyJson.Contains("\n") && prettyJson.Contains("\"y\": \"z\""));
Check("key:value -> json", (PasteText.ToJson("host: a.b\nport = 80") ?? "").Contains("\"port\": 80"));
Check("custom regex action", PasteText.ApplyCustom("010-1234-5678", "regex", "[^0-9]", "") == "01012345678");
Check("custom dedupe", PasteText.ApplyCustom("b\na\nb", "dedupe").Replace("\r", "") == "b\na");
string peekDir = Path.Combine(Path.GetTempPath(), "bonghub_peek_" + Environment.ProcessId);
Directory.CreateDirectory(peekDir);
try
{
    string note = Path.Combine(peekDir, "readme.md");
    File.WriteAllText(note, "# BongHub\n미리보기 텍스트 테스트\n- 한글 OK\n", new UTF8Encoding(false));
    string pdf = Path.Combine(peekDir, "doc.pdf");
    File.WriteAllBytes(pdf, new byte[] { 0x25, 0x50, 0x44, 0x46 });
    Check("peek kinds", PeekKind.Kind(Path.Combine(peekDir, "photo.jpg")) == "image" && PeekKind.Kind(note) == "markdown" && PeekKind.Kind(pdf) == "info" && PeekKind.Kind(peekDir) == "folder");
    Check("peek reads utf8", PeekKind.ReadText(note).Contains("한글 OK"));
}
finally { try { Directory.Delete(peekDir, true); } catch { } }
Check("fit keeps ratio", ImageFit.Target(new ImageFit.Px(2400, 1600), 1366, 768, "fit", true) == new ImageFit.Px(1152, 768));
Check("shrink-only skips small", ImageFit.Target(new ImageFit.Px(800, 600), 1920, 1080, "fit", true) == new ImageFit.Px(800, 600));
Check("stretch", ImageFit.Target(new ImageFit.Px(2400, 1600), 854, 480, "stretch", false) == new ImageFit.Px(854, 480));
Check("resize % unit", ImageFit.Resolve(50, 50, "%", new ImageFit.Px(2400, 1600), 96, 96, true) == new ImageFit.Px(1200, 800));
Check("ignore orientation swaps for portrait", ImageFit.Resolve(1920, 1080, "px", new ImageFit.Px(1000, 2000), 96, 96, true) == new ImageFit.Px(1080, 1920));
Check("resize cm unit", ImageFit.Resolve(254, 254, "cm", new ImageFit.Px(4000, 4000), 96, 96, false).W == 9600);
Check("resize name format", ImageFit.FileName(Path.Combine(Path.GetTempPath(), "photo.jpg"), "t", 7, 9, "%1_%5x%6") == "photo_7x9.jpg");
Check("grab ignores a missing window and a fullscreen window", !GrabMath.CanGrab(false, false, true, false, false) && !GrabMath.CanGrab(true, true, true, false, false) && GrabMath.CanGrab(true, false, true, false, false));
const string PtCfg = "{\"Presets\":[{\"Marker\":\"PRESET_KEEP\"}],\"Pt\":{\"Zones\":{\"Enabled\":false,\"Marker\":\"ZONE_KEEP\"},\"Paste\":{\"Enabled\":false,\"Marker\":\"PASTE_KEEP\"}},\"Desk\":{\"Marker\":\"DESK_KEEP\"}}";
const string PtPretty = "{\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }],\r\n  \"Pt\": {\r\n    \"Zones\": {\r\n      \"Enabled\": false,\r\n      \"Marker\": \"ZONE_KEEP\"\r\n    },\r\n    \"Paste\": { \"Enabled\": false, \"Marker\": \"PASTE_KEEP\" }\r\n  },\r\n  \"Desk\": { \"Marker\": \"DESK_KEEP\" }\r\n}\r\n";
Check("zones enabled splices one token", NotesConfigPatch.TryApplyNested(PtCfg, "Pt", "Zones", "Enabled", JsonValue.Create(true), out string zoned)
    && zoned == PtCfg.Replace("\"Zones\":{\"Enabled\":false", "\"Zones\":{\"Enabled\":true")
    && zoned.Contains("ZONE_KEEP") && zoned.Contains("PASTE_KEEP") && zoned.Contains("\"Paste\":{\"Enabled\":false") && zoned.Contains("PRESET_KEEP") && zoned.Contains("DESK_KEEP"));
Check("zones enabled keeps pretty formatting", NotesConfigPatch.TryApplyNested(PtPretty, "Pt", "Zones", "Enabled", JsonValue.Create(true), out string zonePretty)
    && zonePretty == PtPretty.Replace("\"Zones\": {\r\n      \"Enabled\": false", "\"Zones\": {\r\n      \"Enabled\": true") && zonePretty.Contains("\"Paste\": { \"Enabled\": false"));
Check("zones enabled byte span", OnlyToken(PtCfg, zoned, out string zoneOld, out string zoneNew) && zoneOld == "false" && zoneNew == "true");
Check("powertoys gate stays shut", !PtGate.RealAllowed && PtSystem.NativeCalls == 0);
bool ptThrew = false;
try { PtSystem.Touch(); } catch (InvalidOperationException) { ptThrew = true; }
Check("powertoys backend refuses before any native call", ptThrew && PtSystem.NativeCalls == 0 && PtSystem.Attempts == 1);

var workPlan = new MonitorPreset { Name = "작업", Primary = "SAM73D3", Layout = "work_layout.cfg" };
workPlan.Enable.Add("SAM747A");
workPlan.Enable.Add("SAM73D3");
var awayPlan = new MonitorPreset { Name = "자리비움", Primary = "GSM76F5" };
awayPlan.Disable.Add("SAM73D3");
awayPlan.Disable.Add("DRS2555");
var fakeDisplay = new FakeDisplay();
fakeDisplay.Apply(PresetPlan.Commands(workPlan));
fakeDisplay.Apply(PresetPlan.Commands(awayPlan));
Check("fake display records commands only", string.Join("|", fakeDisplay.Log) == "enable SAM747A|enable SAM73D3|load work_layout.cfg|primary SAM73D3|primary GSM76F5|disable SAM73D3 DRS2555" && DisplaySystem.NativeCalls == 0 && DisplaySystem.MmtStarts == 0);
bool displayThrew = false;
try { new RealDisplay().Apply(PresetPlan.Commands(new MonitorPreset { Name = "작업" })); } catch (InvalidOperationException) { displayThrew = true; }
bool brightThrew = false;
try { new RealBright().Set("SAM73D3", 30, out _); } catch (InvalidOperationException) { brightThrew = true; }
Check("display and brightness refuse before any api", displayThrew && brightThrew && !DisplayGate.RealAllowed && DisplaySystem.NativeCalls == 0 && DisplaySystem.MmtStarts == 0 && DisplaySystem.ChangeDisplaySettingsExCalls == 0 && DisplaySystem.SetDisplayConfigCalls == 0 && DisplaySystem.BrightnessCalls == 0 && DisplaySystem.DdcCalls == 0);
string ccdStory = CcdLayout.DisconnectStory();
Check("disabled monitor deactivates and the primary returns to the origin", ccdStory.Length == 0 && DisplaySystem.SetDisplayConfigCalls == 0 && DisplaySystem.MmtStarts == 0, ccdStory);
string allPaths = CcdLayout.AllPathsStory();
Check("ccd all-paths keeps one path per monitor and does not call a display api", allPaths.Length == 0 && DisplayNative.TargetNameBytes == 420 && DisplayNative.SourceNameBytes == 84 && DisplayNative.ModeBytes == 64 && DisplayNative.PathBytes == 72 && DisplaySystem.SetDisplayConfigCalls == 0 && DisplaySystem.MmtStarts == 0 && DisplaySystem.NativeCalls == 0, allPaths + " sizes " + DisplayNative.TargetNameBytes + "/" + DisplayNative.SourceNameBytes + "/" + DisplayNative.ModeBytes + "/" + DisplayNative.PathBytes);
string holdStory = SwitchHold.Story();
Check("windows keep their snapshot when a monitor turns off", holdStory.Length == 0 && LiveWindowSurface.NativeCalls == 0, holdStory);
byte[]? dataBefore = null;
string? dataPath = null;
if (repo != null)
{
    string logic = File.ReadAllText(Path.Combine(repo, "BongHub", "DisplayPresets.cs"));
    dataPath = Path.Combine(repo, "data", "config.json");
    dataBefore = File.ReadAllBytes(dataPath);
    Check("preset logic file does not call display apis", !logic.Contains("DllImport") && !logic.Contains("Process.Start") && !logic.Contains("MultiMonitorTool") && !logic.Contains("dxva2.dll") && !logic.Contains("user32.dll") && !logic.Contains("SetMonitorBrightness(") && !logic.Contains("SetVCPFeature("));
}
RunExtras();
if (dataBefore != null && dataPath != null)
    Check("preset tests do not write data/", File.ReadAllBytes(dataPath).AsSpan().SequenceEqual(dataBefore));
const string PresetCfg = "{\"Version\":2,\"Presets\":[{\"Name\":\"작업\",\"Hotkey\":\"Ctrl+Alt+1\",\"Enable\":[\"SAM747A\"],\"Primary\":\"SAM73D3\",\"Marker\":\"PRESET_KEEP\",\"Brightness\":[{\"Monitor\":\"SAM73D3\",\"Value\":30}]},{\"Name\":\"자리비움\",\"Hotkey\":\"Ctrl+Alt+0\",\"Marker\":\"AWAY_KEEP\"}],\"Pt\":{\"Marker\":\"PT_KEEP\"},\"Desk\":{\"Marker\":\"DESK_KEEP\"}}";
const string PresetPretty = "{\r\n  \"Version\": 2,\r\n  \"Presets\": [\r\n    {\r\n      \"Name\": \"작업\",\r\n      \"Hotkey\": \"Ctrl+Alt+1\",\r\n      \"Marker\": \"PRESET_KEEP\"\r\n    },\r\n    {\r\n      \"Name\": \"자리비움\",\r\n      \"Hotkey\": \"Ctrl+Alt+0\",\r\n      \"Marker\": \"AWAY_KEEP\"\r\n    }\r\n  ],\r\n  \"Pt\": { \"Marker\": \"PT_KEEP\" },\r\n  \"Desk\": { \"Marker\": \"DESK_KEEP\" }\r\n}\r\n";
Check("preset hotkey splices one value", NotesConfigPatch.TryApplyArrayField(PresetCfg, "Presets", 0, "Hotkey", JsonValue.Create("Ctrl+Alt+2"), out string preseted)
    && preseted == PresetCfg.Replace("\"Hotkey\":\"Ctrl+Alt+1\"", "\"Hotkey\":\"Ctrl+Alt+2\"")
    && preseted.Contains("PRESET_KEEP") && preseted.Contains("AWAY_KEEP") && preseted.Contains("\"Hotkey\":\"Ctrl+Alt+0\"") && preseted.Contains("PT_KEEP") && preseted.Contains("DESK_KEEP") && preseted.Contains("\"Value\":30"));
Check("preset hotkey keeps pretty formatting", NotesConfigPatch.TryApplyArrayField(PresetPretty, "Presets", 0, "Hotkey", JsonValue.Create("Ctrl+Alt+2"), out string presetPretty)
    && presetPretty == PresetPretty.Replace("\"Hotkey\": \"Ctrl+Alt+1\"", "\"Hotkey\": \"Ctrl+Alt+2\"") && presetPretty.Contains("\"Hotkey\": \"Ctrl+Alt+0\"") && presetPretty.Contains("AWAY_KEEP"));
Check("preset hotkey byte span", OnlyToken(PresetCfg, preseted, out string presetOld, out string presetNew) && presetOld == "Ctrl+Alt+1" && presetNew == "Ctrl+Alt+2");

var talk = new List<KakaoWindow>
{
    new() { Id = 1, Class = "EVA_Window_Dblclk", Title = "카카오톡", Process = "KakaoTalk.exe", Y = 0, H = 700, W = 400 },
    new() { Id = 2, ParentId = 1, Class = "EVA_ChildWindow", Process = "KakaoTalk.exe", Y = 620, H = 80, W = 400 },
    new() { Id = 3, ParentId = 1, Title = "OnlineMainView", Process = "KakaoTalk.exe", Y = 38, H = 660, W = 400 },
    new() { Id = 4, ParentId = 3, Title = "ChatRoomListView", Process = "KakaoTalk.exe", Y = 30, H = 600, W = 400 },
    new() { Id = 6, ParentId = 4, Title = "ChatRoomListCtrl", Process = "KakaoTalk.exe", X = 8, Y = 143, W = 360, H = 400 },
    new() { Id = 9, Class = "Notepad", Title = "AdView", Process = "notepad.exe", W = 100, H = 100 }
};
var kakaoCmds = KakaoPlan.Commands(talk, true);
Check("kakao plan hides only the kakao ad", kakaoCmds.Contains("hide 2") && !kakaoCmds.Any(c => c.Contains(" 9")) && kakaoCmds.Contains("place 3 0 30 400 669") && kakaoCmds.Contains("place 6 8 129 360 414"));
Check("ad title matches the helper list", KakaoArgs.IsAdTitle("하단 광고") && KakaoArgs.IsAdTitle("BannerAd") && KakaoArgs.IsAdTitle("MOMENT") && !KakaoArgs.IsAdTitle("친구") && !KakaoArgs.IsKakaoProcess("notepad.exe"));
var host = new FakeKakaoHost();
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true });
Check("start compact -> 1 helper", host.Running == 1 && host.LastArgs == "" && KakaoArgs.Format(new KakaoSettings { AdBlock = true, Compact = true }) == "");
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = false });
Check("restart /nocompact stays one helper", host.Running == 1 && host.LastArgs == "/nocompact" && !host.LastArgs.Contains("dark"));
KakaoSync.Apply(host, new KakaoSettings { AdBlock = false });
Check("restore stops the helper", host.Running == 0 && host.LastArgs == null && host.Log[host.Log.Count - 1] == "restore");
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true });
Check("sync on starts the helper", host.Running == 1 && host.LastArgs == "");
int starts = host.Log.Count(x => x.StartsWith("start ", StringComparison.Ordinal));
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true });
Check("sync again does not restart", host.Running == 1 && host.Log.Count(x => x.StartsWith("start ", StringComparison.Ordinal)) == starts);
KakaoSync.Apply(host, new KakaoSettings { AdBlock = false });
Check("final restore stops the helper", host.Running == 0);
bool kakaoThrew = false;
try { KakaoSystem.Touch(); } catch (InvalidOperationException) { kakaoThrew = true; }
Check("kakao gate stays shut", kakaoThrew && !KakaoGate.RealAllowed && KakaoSystem.NativeCalls == 0 && KakaoSystem.ProcessStarts == 0 && KakaoSystem.WindowCalls == 0 && KakaoSystem.ProcessQueries == 0);
if (repo != null)
{
    string kakaoLogic = File.ReadAllText(Path.Combine(repo, "BongHub", "KakaoLogic.cs"));
    Check("kakao logic file does not call windows", !kakaoLogic.Contains("DllImport") && !kakaoLogic.Contains("Process.Start") && !kakaoLogic.Contains("GetProcessesByName") && !kakaoLogic.Contains("FindWindow") && !kakaoLogic.Contains("EnumWindows") && !kakaoLogic.Contains("ShowWindow") && !kakaoLogic.Contains("SetWindowPos") && !kakaoLogic.Contains("/dark"));
    string helperPath = Path.Combine(repo, "BongHub", "res", "BongAd.exe");
    byte[] helper = File.ReadAllBytes(helperPath);
    string hash;
    using (var sha = System.Security.Cryptography.SHA256.Create()) hash = Convert.ToHexString(sha.ComputeHash(helper));
    Check("helper bytes match 3.4.x", hash == KakaoFiles.Sha256 && helper.Length > 0);
    string tempHelper = Path.Combine(Path.GetTempPath(), "bonghub_kakao_" + Environment.ProcessId, "helper.bin");
    try
    {
        bool wrote = KakaoFiles.WriteIfChanged(tempHelper, new byte[] { 1, 2, 3 });
        bool again = KakaoFiles.WriteIfChanged(tempHelper, new byte[] { 1, 2, 3 });
        Check("helper write skips identical bytes", wrote && !again && File.ReadAllBytes(tempHelper).Length == 3);
    }
    finally { try { Directory.Delete(Path.GetDirectoryName(tempHelper)!, true); } catch { } }
    byte[] dataAfter = File.ReadAllBytes(Path.Combine(repo, "data", "config.json"));
    Check("kakao tests do not write data/", dataBefore != null && dataAfter.AsSpan().SequenceEqual(dataBefore));
}
const string KakaoCfgJson = "{\"Presets\":[{\"Marker\":\"PRESET_KEEP\"}],\"Kakao\":{\"AdBlock\":true,\"Compact\":true,\"Dark\":false,\"Marker\":\"KAKAO_KEEP\"},\"Pt\":{\"Marker\":\"PT_KEEP\"},\"Desk\":{\"Marker\":\"DESK_KEEP\"}}";
const string KakaoPretty = "{\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }],\r\n  \"Kakao\": {\r\n    \"AdBlock\": true,\r\n    \"Compact\": true,\r\n    \"Dark\": false,\r\n    \"Marker\": \"KAKAO_KEEP\"\r\n  },\r\n  \"Pt\": { \"Marker\": \"PT_KEEP\" },\r\n  \"Desk\": { \"Marker\": \"DESK_KEEP\" }\r\n}\r\n";
Check("kakao adblock splices one value", NotesConfigPatch.TryApply(KakaoCfgJson, "Kakao", "AdBlock", JsonValue.Create(false), out string kakaoPatched)
    && kakaoPatched == KakaoCfgJson.Replace("\"AdBlock\":true", "\"AdBlock\":false")
    && kakaoPatched.Contains("\"Compact\":true") && kakaoPatched.Contains("\"Dark\":false") && kakaoPatched.Contains("KAKAO_KEEP") && kakaoPatched.Contains("PRESET_KEEP") && kakaoPatched.Contains("PT_KEEP") && kakaoPatched.Contains("DESK_KEEP"));
Check("kakao adblock keeps pretty formatting", NotesConfigPatch.TryApply(KakaoPretty, "Kakao", "AdBlock", JsonValue.Create(false), out string kakaoPretty)
    && kakaoPretty == KakaoPretty.Replace("\"AdBlock\": true", "\"AdBlock\": false") && kakaoPretty.Contains("\"Compact\": true") && kakaoPretty.Contains("KAKAO_KEEP"));
Check("kakao adblock byte span", OnlyToken(KakaoCfgJson, kakaoPatched, out string kakaoOld, out string kakaoNew) && kakaoOld == "true" && kakaoNew == "false");
RunCloud();

Console.WriteLine(fail == 0 ? "RESULT PASS" : "RESULT FAIL " + fail);
return fail == 0 ? 0 : 1;

void RunExtras()
{
    var oldB = PresetExtras.Bright; var oldF = PresetExtras.FindDesk; var oldR = PresetExtras.RestoreDesk; var oldS = PresetExtras.Signature; var oldSl = PresetExtras.Sleep;
    var oldSt = PresetExtras.StampFile; var oldWS = PresetExtras.WorkStampFile; var oldMx = PresetExtras.WorkMutexName;
    var oldFW = PresetExtras.FindWork; var oldLW = PresetExtras.LaunchWork; var oldLM = PresetExtras.WorkLaunchMissing;
    string dir = Path.Combine(Path.GetTempPath(), "bonghub_extras_" + Environment.ProcessId);
    try
    {
        Directory.CreateDirectory(dir);
        PresetExtras.StampFile = Path.Combine(dir, "desk.stamp");
        PresetExtras.WorkStampFile = Path.Combine(dir, "work.stamp");
        PresetExtras.WorkMutexName = "BongHubExtras" + Environment.ProcessId;
        try { File.Delete(PresetExtras.StampFile); } catch { }
        try { File.Delete(PresetExtras.WorkStampFile); } catch { }
        int slept = 0; PresetExtras.Sleep = ms => slept += ms;
        int sigCalls = 0; PresetExtras.Signature = () => { sigCalls++; return sigCalls < 5 ? "changing" + sigCalls : "stable"; };
        var old = PresetCodec.ReadFirst("{\"Presets\":[{\"Name\":\"작업\",\"Hotkey\":\"Ctrl+Alt+1\"}]}");
        Check("3.1 preset migrates (no brightness, no link)", old.Brightness != null && old.Brightness.Count == 0 && old.DeskLayout == "");
        Check("3.3 preset migrates (no workspace link)", old.Workspace == "");
        Check("no extras -> nothing runs", !PresetExtras.HasWork(old));
        var bad = PresetCodec.ReadFirst("{\"Presets\":[{\"Name\":\"x\",\"Brightness\":[{\"Monitor\":null,\"Value\":250},null]}]}");
        Check("bad brightness values clamped", bad.Brightness.Count == 1 && bad.Brightness[0].Value == 100 && bad.Brightness[0].Monitor == "");
        var p = new MonitorPreset { Name = "작업" };
        p.Brightness.Add(new BrightTarget { Monitor = "", Value = 60 });
        p.Brightness.Add(new BrightTarget { Monitor = "SAM73D3", Value = 30 });
        p.Brightness.Add(new BrightTarget { Monitor = "GSM76F5", Value = -1 });
        var tg = PresetExtras.Targets(p, new[] { "SAM73D3", "SAM747A", "GSM76F5" });
        Check("per-monitor value wins, '변경 안 함' skipped", tg["SAM73D3"] == 30 && tg["SAM747A"] == 60 && !tg.ContainsKey("GSM76F5"));
        var mb = new FakeBright();
        mb.Unsupported.Add("DRS2555");
        mb.Wake = calls => calls < 2 ? new List<string> { "SAM73D3", "DRS2555" } : new List<string> { "SAM73D3", "DRS2555", "SAM747A" };
        PresetExtras.Bright = mb;
        var statuses = new List<string>();
        var p2 = new MonitorPreset { Name = "작업" };
        p2.Brightness.Add(new BrightTarget { Monitor = "", Value = 80 });
        p2.Brightness.Add(new BrightTarget { Monitor = "SAM747A", Value = 40 });
        PresetExtras.Run(p2, x => statuses.Add(x));
        Check("brightness all -> 80%", mb.Set_.ContainsKey("SAM73D3") && mb.Set_["SAM73D3"] == 80);
        Check("late monitor set on retry", mb.Set_.ContainsKey("SAM747A") && mb.Set_["SAM747A"] == 40);
        Check("unsupported monitor reported", PresetExtras.LastBright.Contains("실패") && PresetExtras.LastBright.Contains("DRS2555"));
        Check("waited for stable screen + retry delay", slept >= PresetExtras.RetryDelay && slept <= 30000);
        Check("status shown on overlay", statuses.Any(x => x.Contains("밝기")));
        PresetExtras.Bright = new ThrowBright();
        bool threw = false;
        try { PresetExtras.Run(p2, _ => { }); } catch { threw = true; }
        Check("backend exception contained", !threw && PresetExtras.LastBright.Contains("실패"));
        PresetExtras.Bright = new FakeBright();
        var lay = new LinkedDesk { Id = "L1", Name = "작업 배치" };
        int restores = 0;
        PresetExtras.FindDesk = id => id == "L1" ? lay : null;
        PresetExtras.RestoreDesk = l => { restores++; return new DeskLinkResult { Ok = true, Moved = 12 }; };
        var p3 = new MonitorPreset { Name = "작업", DeskLayout = "L1" };
        PresetExtras.LinkedAt = DateTime.MinValue;
        Check("no suppression before", !PresetExtras.LinkedRecently);
        PresetExtras.Run(p3, _ => { });
        Check("linked layout restored exactly once", restores == 1 && PresetExtras.LastDesk.Contains("작업 배치"));
        Check("auto-restore suppressed right after linked restore", PresetExtras.LinkedRecently);
        Check("cross-process stamp written", File.Exists(PresetExtras.StampFile));
        PresetExtras.LinkedAt = DateTime.MinValue;
        File.SetLastWriteTime(PresetExtras.StampFile, DateTime.Now.AddMinutes(-5));
        Check("suppression expires", !PresetExtras.LinkedRecently);
        var p4 = new MonitorPreset { Name = "x", DeskLayout = "GONE" };
        restores = 0;
        PresetExtras.Run(p4, _ => { });
        Check("deleted layout -> notice, no restore", restores == 0 && PresetExtras.LastDesk.Contains("삭제"));
        PresetExtras.RestoreDesk = _ => new DeskLinkResult { Ok = false, Error = "바탕화면 자동 정렬이 켜져 있어요" };
        PresetExtras.Run(p3, _ => { });
        Check("restore failure reported", PresetExtras.LastDesk.Contains("실패"));
        var ws = new LinkedWork { Id = "W1", Name = "코딩" };
        var order = new List<string>();
        bool launchArg = true;
        PresetExtras.FindWork = id => id == "W1" ? ws : null;
        PresetExtras.WorkLaunchMissing = () => false;
        PresetExtras.RestoreDesk = _ => { order.Add("desk"); return new DeskLinkResult { Ok = true, Moved = 3 }; };
        PresetExtras.LaunchWork = (w, launch) => { order.Add("work"); launchArg = launch; return "'" + w.Name + "' · 배치 2개"; };
        var p5 = new MonitorPreset { Name = "코딩", DeskLayout = "L1", Workspace = "W1" };
        var st5 = new List<string>();
        PresetExtras.Run(p5, x => st5.Add(x));
        Check("workspace runs once right after icon restore", string.Join(",", order) == "desk,work");
        Check("launch-missing setting followed, status shown", !launchArg && PresetExtras.LastWork.Contains("배치 2개") && st5.Any(x => x.Contains("창 배치")));
        order.Clear();
        PresetExtras.Run(p5, _ => { });
        Check("duplicate run within 10 s skipped", !order.Contains("work") && PresetExtras.LastWork.Contains("건너뛰"));
        File.SetLastWriteTime(PresetExtras.WorkStampFile, DateTime.Now.AddMinutes(-1));
        order.Clear();
        PresetExtras.Run(p5, _ => { });
        Check("runs again on a later switch", order.Contains("work"));
        var p6 = new MonitorPreset { Name = "y", Workspace = "GONE" };
        order.Clear();
        PresetExtras.Run(p6, _ => { });
        Check("deleted workspace -> notice", !order.Contains("work") && PresetExtras.LastWork.Contains("삭제"));
        try { File.Delete(PresetExtras.WorkStampFile); } catch { }
        PresetExtras.LaunchWork = (_, _) => throw new Exception("boom");
        bool thr = false;
        try { PresetExtras.Run(p5, _ => { }); } catch { thr = true; }
        Check("workspace failure contained", !thr && PresetExtras.LastWork.Contains("실패"));
        Check("workspace-only preset has work", PresetExtras.HasWork(new MonitorPreset { Workspace = "W1" }));
        Check("summary text", PresetExtras.BrightSummary(p).Contains("SAM73D3 30%") && (PresetExtras.BrightSummary(p) == "적용: SAM73D3 30%, 전체 60%" || PresetExtras.BrightSummary(p).Contains("전체 60%")));
    }
    catch (Exception ex) { Check("extras exception", false, ex.ToString()); }
    finally
    {
        PresetExtras.Bright = oldB; PresetExtras.FindDesk = oldF; PresetExtras.RestoreDesk = oldR; PresetExtras.Signature = oldS; PresetExtras.Sleep = oldSl;
        PresetExtras.StampFile = oldSt; PresetExtras.WorkStampFile = oldWS; PresetExtras.WorkMutexName = oldMx;
        PresetExtras.FindWork = oldFW; PresetExtras.LaunchWork = oldLW; PresetExtras.WorkLaunchMissing = oldLM;
        try { Directory.Delete(dir, true); } catch { }
    }
}

void RunCloud()
{
    string root = Path.Combine(Path.GetTempPath(), "bonghub_cloud_" + Environment.ProcessId);
    var real = repo == null ? null : File.ReadAllBytes(Path.Combine(repo, "data", "config.json"));
    try
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string A(string rel) => Path.Combine(root, "pcA", rel);
        string B(string rel) => Path.Combine(root, "pcB", rel);
        Directory.CreateDirectory(A("notes"));
        Directory.CreateDirectory(A("desktop"));
        Directory.CreateDirectory(B("notes"));
        Directory.CreateDirectory(B("desktop"));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        void At(string path, string text, DateTime when)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, when);
        }
        At(A("config.json"), "{\"General\":{\"IdleMinutes\":17}}", t0);
        At(A("notes/n1.json"), "{\"Id\":\"n1\",\"Text\":\"A가 쓴 메모\"}", t0);
        At(A("notes/n1.rtf"), "{\\rtf1 A}", t0);
        var layout = new DeskLayoutFile { Id = "L1", Name = "A 배치", Key = "k", Saved = t0, Items = new() };
        At(A("desktop/layouts.json"), LayoutStore.Serialize(new List<DeskLayoutFile> { layout }), t0);
        var remote = new MemoryCloud();
        CloudWorkspace Ws(string side, string name, DateTime utc) => new()
        {
            ConfigFile = Path.Combine(root, side, "config.json"),
            NotesDir = Path.Combine(root, side, "notes"),
            DeskDir = Path.Combine(root, side, "desktop"),
            Device = name,
            AppVersion = "0.12.0",
            UtcNow = utc,
            LocalNow = utc.ToLocalTime(),
            State = new CloudState()
        };
        var stateA = new CloudState();
        var stateB = new CloudState();
        CloudWorkspace Side(string side, string name, DateTime utc, CloudState state)
        {
            var ws = Ws(side, name, utc);
            ws.State = state;
            return ws;
        }
        string a1 = CloudEngine.Run(Side("pcA", "PC-A", t0.AddMinutes(1), stateA), remote, true);
        Check("A uploads through the memory remote", a1.Length == 0 && remote.Commits.Count == 1 && remote.Commits[0].StartsWith("sync: PC-A"));
        Check("commit tree stays under data/", remote.Snapshot().Keys.Any(k => k == "data/config.json") && remote.Snapshot().Keys.Any(k => k.StartsWith("data/notes/", StringComparison.Ordinal)) && remote.Snapshot().Keys.All(k => k == "README.md" || k.StartsWith("data/", StringComparison.Ordinal)));
        At(B("config.json"), "{\"General\":{\"IdleMinutes\":30}}", t0);
        string b1 = CloudEngine.Run(Side("pcB", "PC-B", t0.AddMinutes(2), stateB), remote, true);
        Check("B first link takes cloud config", b1.Length == 0 && File.ReadAllText(B("config.json")).Contains("17"));
        Check("B receives note", File.Exists(B("notes/n1.rtf")) && File.ReadAllText(B("notes/n1.rtf")).Contains("A"));
        Check("B receives desktop layout", File.ReadAllText(B("desktop/layouts.json")).Contains("L1"));
        var tEdit = t0.AddMinutes(10);
        At(B("notes/n1.rtf"), "{\\rtf1 B edit}", tEdit);
        At(B("notes/n2.json"), "{\"Id\":\"n2\"}", tEdit);
        At(B("notes/n2.rtf"), "{\\rtf1 B2}", tEdit);
        Check("B publishes its note", CloudEngine.Run(Side("pcB", "PC-B", tEdit, stateB), remote, true).Length == 0);
        var t3 = t0.AddMinutes(11);
        At(A("notes/n3.json"), "{\"Id\":\"n3\"}", t3);
        At(A("notes/n3.rtf"), "{\\rtf1 A3}", t3);
        Check("A gets B's newer edit and new note", CloudEngine.Run(Side("pcA", "PC-A", t3, stateA), remote, true).Length == 0
            && File.ReadAllText(A("notes/n1.rtf")).Contains("B edit") && File.Exists(A("notes/n2.rtf")));
        Check("B gets A's note", CloudEngine.Run(Side("pcB", "PC-B", t3.AddMinutes(1), stateB), remote, true).Length == 0 && File.Exists(B("notes/n3.rtf")));
        File.Delete(A("notes/n2.json"));
        File.Delete(A("notes/n2.rtf"));
        var tDel = t0.AddMinutes(20);
        Check("A tombstones the deleted note", CloudEngine.Run(Side("pcA", "PC-A", tDel, stateA), remote, true).Length == 0 && !remote.Snapshot().ContainsKey("data/notes/n2.json"));
        Check("deletion travels", CloudEngine.Run(Side("pcB", "PC-B", tDel.AddMinutes(1), stateB), remote, true).Length == 0 && !File.Exists(B("notes/n2.json")) && !File.Exists(B("notes/n2.rtf")));
        var tCfg = t0.AddMinutes(30);
        At(B("config.json"), "{\"General\":{\"IdleMinutes\":44}}", tCfg);
        CloudEngine.Run(Side("pcB", "PC-B", tCfg, stateB), remote, true);
        Check("newer config wins", CloudEngine.Run(Side("pcA", "PC-A", tCfg.AddMinutes(1), stateA), remote, true).Length == 0 && File.ReadAllText(A("config.json")).Contains("44"));
        var offline = new MemoryCloud { Online = false };
        string off = CloudEngine.Run(Side("pcB", "PC-B", tCfg.AddMinutes(2), stateB), offline, true);
        Check("offline keeps the sync pending", off.StartsWith("오프라인") && offline.Commits.Count == 0 && CloudSystem.GitStarts == 0 && CloudSystem.NetworkCalls == 0);
        bool token = remote.Snapshot().Values.Any(f => Encoding.UTF8.GetString(f.Bytes).Contains("ghp_") || Encoding.UTF8.GetString(f.Bytes).Contains("github_pat_"));
        Check("synced bytes contain no token", !token);
        int calls = remote.Calls;
        string refused = CloudEngine.Run(Side("pcA", "PC-A", tCfg, stateA), remote, false);
        Check("reader does not sync", refused.Contains("읽기 전용") && remote.Calls == calls);
        var newer = new MemoryCloud();
        newer.Seed(new Dictionary<string, CloudFile> { ["data/meta.json"] = new CloudFile { Bytes = Encoding.UTF8.GetBytes("{\"schema\":2,\"minApp\":\"9.9.9\"}"), MtimeUtc = t0 } });
        At(A("config.json"), "LOCAL", tCfg);
        string blocked = CloudEngine.Run(Side("pcA", "PC-A", tCfg, new CloudState()), newer, true);
        Check("higher schema stops", blocked.StartsWith("업데이트 필요") && File.ReadAllText(A("config.json")) == "LOCAL" && newer.Commits.Count == 0);
        bool cloudThrew = false;
        try { CloudSystem.Touch(); } catch (InvalidOperationException) { cloudThrew = true; }
        Check("cloud gate stays shut", cloudThrew && !CloudGate.RealAllowed && CloudSystem.NativeCalls == 0 && CloudSystem.GitStarts == 0 && CloudSystem.GhStarts == 0);
        if (repo != null)
        {
            string logic = File.ReadAllText(Path.Combine(repo, "BongHub", "CloudSync.cs"));
            Check("cloud logic does not call git", !logic.Contains("Process.Start") && !logic.Contains("DllImport") && !logic.Contains("git.exe") && !logic.Contains("gh.exe") && !logic.Contains("ghp_"));
            Check("cloud tests do not write data/", real != null && File.ReadAllBytes(Path.Combine(repo, "data", "config.json")).AsSpan().SequenceEqual(real));
        }
        const string CloudJson = "{\"Cloud\":{\"Enabled\":true,\"Repo\":\"https://github.com/Baegovda/BongHub.git\",\"Branch\":\"main\",\"PullMinutes\":2,\"Marker\":\"CLOUD_KEEP\"},\"Presets\":[{\"Marker\":\"PRESET_KEEP\"}]}";
        const string CloudPretty = "{\r\n  \"Cloud\": {\r\n    \"Enabled\": true,\r\n    \"Repo\": \"https://github.com/Baegovda/BongHub.git\",\r\n    \"Branch\": \"main\",\r\n    \"PullMinutes\": 2,\r\n    \"Marker\": \"CLOUD_KEEP\"\r\n  },\r\n  \"Presets\": [{ \"Marker\": \"PRESET_KEEP\" }]\r\n}\r\n";
        Check("cloud enabled splices one value", NotesConfigPatch.TryApply(CloudJson, "Cloud", "Enabled", JsonValue.Create(false), out string cloudPatched)
            && cloudPatched == CloudJson.Replace("\"Enabled\":true", "\"Enabled\":false")
            && cloudPatched.Contains("https://github.com/Baegovda/BongHub.git") && cloudPatched.Contains("CLOUD_KEEP") && cloudPatched.Contains("PRESET_KEEP") && cloudPatched.Contains("\"PullMinutes\":2"));
        Check("cloud enabled keeps pretty formatting", NotesConfigPatch.TryApply(CloudPretty, "Cloud", "Enabled", JsonValue.Create(false), out string cloudPretty)
            && cloudPretty == CloudPretty.Replace("\"Enabled\": true", "\"Enabled\": false") && cloudPretty.Contains("\"PullMinutes\": 2") && cloudPretty.Contains("CLOUD_KEEP"));
        Check("cloud enabled byte span", OnlyToken(CloudJson, cloudPatched, out string cloudOld, out string cloudNew) && cloudOld == "true" && cloudNew == "false");
var clip = NoteClip.Unpack(NoteClip.Pack(new[] { new NoteClipPiece { Text = "안녕 " }, new NoteClipPiece { Png = new byte[] { 0x89, 0x50, 0x4E, 0x47 } }, new NoteClipPiece { Text = "끝" } }));
string clipPlain = NoteClip.Plain(clip);
string clipRtf = NoteClip.ToRtf(clip);
Check("note clip keeps text and png", clip.Count == 3 && clipPlain == "안녕 끝" && !clipPlain.Contains("Image") && clip[1].Png is { Length: 4 } && clipRtf.Contains("\\pngblip") && clipRtf.Contains("89504e47"));
string blockStory = NoteBlocks.Story();
Check("note blocks keep plain text and accept slash and markdown", blockStory.Length == 0, blockStory);
string dockStory = AirDockLayout.Story();
Check("airdock stays on the cursor monitor without launching", dockStory.Length == 0 && AirDockLaunch.NativeStarts == 0, dockStory);
string crossStory = CursorCross.Story();
Check("cursor cross matches physical millimetres on a fake desk", crossStory.Length == 0, crossStory);
if (repo != null)
{
    string xaml = File.ReadAllText(Path.Combine(repo, "BongHub", "App.xaml"));
    int at = xaml.IndexOf("x:Key=\"Switch\"", StringComparison.Ordinal);
    int end = xaml.IndexOf("x:Key=\"SlimThumb\"", StringComparison.Ordinal);
    string body = at >= 0 && end > at ? xaml[at..end] : "";
    Check("switch template does not share a storyboard", body.Contains("TranslateTransform") && !body.Contains("BeginStoryboard"));
}
string clipHtml = NoteClip.ToHtml(clip);
int fragAt = clipHtml.IndexOf("<!--StartFragment-->", StringComparison.Ordinal);
int fragEnd = clipHtml.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
Check("note html keeps text and png", fragAt > 0 && fragEnd > fragAt && clipHtml.Contains("안녕") && clipHtml.Contains("data:image/png;base64") && !clipHtml.Contains(">Image<"));
Check("active preset is not applied again", !PresetApplyPolicy.IsCurrent("", "작업") && PresetApplyPolicy.IsCurrent("작업", "작업") && PresetApplyPolicy.Toast("작업") == "이미 작업 상태입니다!" && !PresetApplyPolicy.IsCurrent("작업", "자리비움"));
var snapped = ProfileLayout.Snap(new ProfileBox(13, 17, 200, 100), new ProfileBox(0, 0, 800, 600), 8);
Check("profile drag snaps inside the monitor", snapped.X == 16 && snapped.Y == 16 && snapped.Right <= 800 && snapped.Bottom <= 600);
var fitted = ProfileLayout.Fit(new ProfileBox(0, 0, 960, 540), new ProfileBox(0, 0, 1920, 1080), new ProfileBox(20, 10, 320, 180));
Check("profile window fits inside the monitor graphic", fitted.X == 20 && fitted.Y == 10 && fitted.W == 160 && fitted.H == 90 && fitted.Right <= 340 && fitted.Bottom <= 190);
var picked = WindowPicker.From(new TrackedWindow { Process = "notepad.exe", Title = "메모장", Class = "Notepad", X = 40, Y = 48, W = 640, H = 480 });
var under = WindowPicker.Hit(new[]
{
    new TrackedWindow { Process = "explorer.exe", Title = "바탕", Class = "Progman", X = 0, Y = 0, W = 800, H = 600 },
    new TrackedWindow { Process = "notepad.exe", Title = "메모장", Class = "Notepad", X = 40, Y = 48, W = 640, H = 480 }
}, 100, 100);
var copy = under == null ? null : WindowPicker.From(under);
Check("crosshair copies a fake window", picked.Process == "notepad.exe" && picked.Title == "메모장" && picked.Class == "Notepad" && picked.W == 640 && picked.H == 480
    && copy != null && copy.Process == "notepad.exe" && copy.Title == "메모장" && copy.Class == "Notepad" && copy.X == 40 && copy.W == 640 && copy.H == 480);
string profileDoc = ProfileCodec.ToJson(new[] { new ProfileRecord { Name = "작업", Icons = "L1", Windows = { new ProfileWindow { Process = "notepad.exe", Title = "메모장", Class = "Notepad", X = 16, Y = 16, W = 96, H = 64 } } } });
var profileBack = ProfileCodec.Parse(profileDoc);
Check("profile json keeps windows and icons", profileBack.Count == 1 && profileBack[0].Name == "작업" && profileBack[0].Icons == "L1" && profileBack[0].Windows.Count == 1 && profileBack[0].Windows[0].Class == "Notepad" && profileBack[0].Windows[0].X == 16 && !profileDoc.Contains("IdleMinutes"));
var beside = ProfileSnap.SnapMonitor(new ProfileBox(1900, 30, 1920, 1080), new[] { new ProfileBox(0, 0, 1920, 1080) }, 48);
var half = ProfileSnap.PreviewAt(10, 540, new ProfileBox(0, 0, 1920, 1080));
var quarter = ProfileSnap.PreviewAt(8, 8, new ProfileBox(0, 0, 1920, 1080));
var right = ProfileSnap.PreviewAt(1910, 8, new ProfileBox(0, 0, 1920, 1080));
var maximized = ProfileSnap.PreviewAt(960, 8, new ProfileBox(0, 0, 1920, 1080));
var resized = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "e", 50, 0, 120);
var clamped = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "e", 5000, 0, 120);
Check("monitor drag snaps to the neighbor", beside.X == 1920 && beside.Y == 0 && beside.W == 1920);
Check("window edge snap is half, quarter, or max", half.Kind == "left" && half.Box == new ProfileBox(0, 0, 960, 1080) && quarter.Kind == "tl" && quarter.Box.H == 540 && right.Kind == "tr" && maximized.Kind == "top" && maximized.Box.H == 1080 && resized.W == 450 && clamped.Right == 1920);
var crossed = ProfileSnap.Transfer(new ProfileBox(1800, 100, 400, 300), new[] { new ProfileBox(0, 0, 1920, 1080), new ProfileBox(1920, 0, 1920, 1080) });
var neighbor = ProfileSnap.SnapWindow(new ProfileBox(205, 40, 100, 80), new ProfileBox(0, 0, 800, 600), new[] { new ProfileBox(0, 0, 208, 200) }, 1);
Check("window can cross a monitor and snap to another window", crossed.Index == 1 && crossed.Local.X == -120 && neighbor.X == 208 && neighbor.Right <= 800);
var board = ProfileDoc.Migrate(
    new[] { new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1", Primary = "SAM1", DeskLayout = "L1", Workspace = "W1", Enable = new List<string> { "SAM1" }, Brightness = new List<BrightTarget> { new BrightTarget { Monitor = "SAM1", Value = 40 } } } },
    new[] { new WindowRule { Name = "Discord", Process = "discord.exe", Title = "친구", X = 10, Y = 20, W = 300, H = 200 } },
    "{\"profiles\":[{\"name\":\"작업\",\"icons\":\"L1\",\"windows\":[{\"process\":\"notepad.exe\",\"title\":\"메모장\",\"class\":\"Notepad\",\"x\":16,\"y\":16,\"w\":640,\"h\":480}]}]}");
var round = ProfileDoc.Parse(ProfileDoc.ToJson(board));
int primaryNumber = board[0].Monitors.First(m => m.Primary).Number;
bool keptPrimary = !ProfileDoc.SetEnabled(board[0], primaryNumber, false);
var second = ProfileDoc.Add(board);
ProfileDoc.Rename(second, "집");
var duplicated = ProfileDoc.Duplicate(board, second.Id);
bool deleted = duplicated != null && ProfileDoc.Delete(board, duplicated.Id);
Check("profile migration is lossless and editable", board[0].Hotkey == "Ctrl+Alt+1" && board[0].Workspace == "W1" && board[0].DeskLayout == "L1" && board[0].Apps.Any(a => a.Class == "Notepad" && a.X == 16) && board[0].Apps.Any(a => a.Process == "discord.exe" && a.W == 300) && board[0].Monitors[0].Id == "SAM1" && round[0].Brightness.Count == 1 && round[0].Brightness[0].Value == 40 && round[0].Workspace == "W1" && keptPrimary && second.Name == "집" && deleted);
var icons = new MemoryIconSurface();
icons.Icons.Add(new DeskIcon { Name = "휴지통", X = 1, Y = 1 });
var captured = ProfileDoc.CaptureIcons(icons.Read());
captured[0].X = 40; captured[0].Y = 12;
var restored = ProfileDoc.RestoreIcons(captured, icons);
bool explorerBlocked = false;
try { _ = new ExplorerIconSurface(); } catch (InvalidOperationException) { explorerBlocked = true; }
Check("profile icons restore on a fake desktop", restored.Ok && icons.Icons[0].X == 40 && icons.Icons[0].Y == 12 && icons.Moves == 1 && explorerBlocked && ExplorerIconSurface.NativeCalls == 0);
string legacy = "{\"profiles\":[{\"name\":\"작업\",\"windows\":["
    + "{\"process\":\"a.exe\",\"title\":\"A\",\"class\":\"A\",\"x\":36,\"y\":28,\"w\":96,\"h\":64},"
    + "{\"process\":\"b.exe\",\"title\":\"B\",\"class\":\"B\",\"x\":140,\"y\":28,\"w\":96,\"h\":64},"
    + "{\"process\":\"c.exe\",\"title\":\"C\",\"class\":\"C\",\"x\":244,\"y\":28,\"w\":96,\"h\":64},"
    + "{\"process\":\"d.exe\",\"title\":\"D\",\"class\":\"D\",\"x\":36,\"y\":100,\"w\":96,\"h\":64},"
    + "{\"process\":\"e.exe\",\"title\":\"E\",\"class\":\"E\",\"x\":140,\"y\":100,\"w\":96,\"h\":64},"
    + "{\"process\":\"f.exe\",\"title\":\"F\",\"class\":\"F\",\"x\":244,\"y\":100,\"w\":96,\"h\":64}]}]}";
var lifted = ProfileDoc.Migrate(new[] { new MonitorPreset { Name = "작업" } }, Array.Empty<WindowRule>(), legacy);
var liftedApps = lifted[0].Apps.OrderBy(a => a.Title).ToList();
Check("1.1.0 canvas windows become monitor pixels", liftedApps.Count == 6 && liftedApps.All(a => a.W == 576 && a.H == 384) && liftedApps[0].X == 96 && liftedApps[1].X == 720 && liftedApps[5].Y == 540 && liftedApps.Select(a => a.X).Distinct().Count() == 3);
var screens = new List<MonitorSpec> { new(3840, 2160, 0, 0, true), new(1920, 1080, 3840, 0, false), new(1920, 1080, 3840, 1080, false), new(3840, 2160, -3840, 0, false), new(960, 640, 5760, 0, false) };
var seeded = ProfileDoc.Migrate(new[] { new MonitorPreset { Name = "작업", Primary = "SAM1", Enable = new List<string> { "SAM1" } } }, Array.Empty<WindowRule>(), null, screens);
var seedPrimary = seeded[0].Monitors.First(m => m.Primary);
Check("profile monitors follow the screen list", seeded[0].Monitors.Count == 5 && seedPrimary.W == 3840 && seedPrimary.H == 2160 && seedPrimary.X == 0 && seedPrimary.Id == "SAM1" && seeded[0].Monitors.Any(m => m.W == 960 && m.H == 640) && ProfileSnap.ContentHeight(seeded[0].Monitors.Select(m => m.Box).ToList(), 560, 16) < 200);
Check("windows identify number comes from the display name", ScreenBind.WindowsNumber(@"\\.\DISPLAY1") == 1 && ScreenBind.WindowsNumber(@"\\.\DISPLAY12") == 12);
var adopted = new MonitorProfile();
adopted.Monitors.Add(new ProfileMonitor { Number = 1, X = 0, Y = 0, W = 1920, H = 1080 });
adopted.Monitors.Add(new ProfileMonitor { Number = 1, X = 1920, Y = 0, W = 3840, H = 2160 });
adopted.Apps.Add(new ProfileApp { Title = "메모", Monitor = 1 });
ScreenBind.Adopt(adopted, new[]
{
    new ScreenInfo { Number = 1, Device = @"\\.\DISPLAY1", Path = "P3840", W = 3840, H = 2160, X = 1920, Y = 0, Primary = true },
    new ScreenInfo { Number = 2, Device = @"\\.\DISPLAY2", Path = "P1080", W = 1920, H = 1080, X = 0, Y = 0 }
});
var orphan = new MonitorProfile();
orphan.Monitors.Add(new ProfileMonitor { Number = 3, W = 800, H = 600, Path = "GONE" });
orphan.Apps.Add(new ProfileApp { Title = "남음", Monitor = 3, W = 100, H = 100 });
var only = new[] { new ScreenInfo { Number = 4, Device = @"\\.\DISPLAY4", Path = "P1080", W = 1920, H = 1080, X = 0, Y = 0 } };
ScreenBind.Adopt(orphan, only);
bool mismatch = ScreenBind.Mismatch(orphan, only);
ScreenBind.MatchCurrent(orphan, only);
Check("saved monitors take windows numbers and keep a disconnected screen", adopted.Monitors.First(m => m.W == 1920).Number == 2 && adopted.Monitors.First(m => m.W == 3840).Number == 1 && adopted.Apps[0].Monitor == 2 && orphan.Apps[0].Monitor == 4 && mismatch && !ScreenBind.Mismatch(orphan, only));
var wideLeft = new ProfileBox(-3840, 0, 3840, 2160);
var besideNeg = ProfileSnap.SnapMonitorWithGuide(new ProfileBox(-60, 40, 2400, 1350), new[] { wideLeft }, 80);
var belowWide = ProfileSnap.SnapMonitorWithGuide(new ProfileBox(100, 2100, 1200, 800), new[] { new ProfileBox(0, 0, 3840, 2160) }, 80);
var gapped = ProfileSnap.SnapMonitorWithGuide(new ProfileBox(0, 4000, 800, 600), new[] { new ProfileBox(0, 0, 1920, 1080) }, 40);
Check("monitor edges snap flush, including negative and mixed sizes", besideNeg.Box.X == 0 && besideNeg.Box.Y == 0 && belowWide.Box.Y == 2160 && belowWide.Box.X == 100 && gapped.Box.Y == 4000 && !gapped.Guide.Active && besideNeg.Guide.X1 == 0 && besideNeg.Guide.Y2 > besideNeg.Guide.Y1 && belowWide.Guide.Y1 == 2160 && belowWide.Guide.X2 > belowWide.Guide.X1);
var nearWin = ProfileSnap.SnapWindow(new ProfileBox(205, 40, 100, 80), new ProfileBox(0, 0, 800, 600), new[] { new ProfileBox(0, 0, 208, 200) }, 1);
var farWin = ProfileSnap.SnapWindow(new ProfileBox(190, 40, 100, 80), new ProfileBox(0, 0, 800, 600), new[] { new ProfileBox(0, 0, 208, 200) }, 1);
var generous = ProfileSnap.PreviewAt(300, 540, new ProfileBox(0, 0, 1920, 1080));
var middle = ProfileSnap.PreviewAt(960, 540, new ProfileBox(0, 0, 1920, 1080));
var cornerResize = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "nw", -30, -20, 120);
var seResize = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "se", 15, 10, 120);
var docked = ProfileSnap.SnapWindow(new ProfileBox(1510, 770, 400, 300), new ProfileBox(0, 0, 1920, 1080), Array.Empty<ProfileBox>(), 1, 16);
Check("window snap is a few pixels and edge zones are small", nearWin.X == 208 && farWin.X == 190 && generous.Kind.Length == 0 && middle.Kind.Length == 0 && docked.W == 400 && docked.H == 300 && docked.Right == 1920 && docked.Bottom == 1080 && cornerResize.X == 70 && cornerResize.W == 430 && cornerResize.H == 320 && seResize.W == 415 && seResize.H == 310);
bool chord = HotkeyChord.TryParse("Ctrl+Alt+1", out uint chordMods, out uint chordVk) && chordMods == (HotkeyChord.Ctrl | HotkeyChord.Alt) && chordVk == 0x31;
var recorded = HotkeyEdit.Apply("", "ctrl+alt+1", null, "Ctrl+Alt+N");
var cancelled = HotkeyEdit.Apply("Ctrl+Alt+1", "escape", null, null);
var cleared = HotkeyEdit.Apply("Ctrl+Alt+1", "back", null, null);
var noteClash = HotkeyEdit.Apply("Ctrl+Alt+2", "Ctrl+Alt+N", null, "alt+ctrl+n");
var profileClash = HotkeyEdit.Apply("", "Ctrl+Alt+1", new[] { "ctrl+alt+1" }, "Ctrl+Alt+N");
Check("profile hotkey capture records, cancels, clears, and reports conflicts", chord && recorded.Value == "Ctrl+Alt+1" && recorded.Error.Length == 0 && cancelled.Value == "Ctrl+Alt+1" && cleared.Value.Length == 0 && noteClash.Error.Contains("메모") && profileClash.Error.Contains("프로필"));
var probes = new List<PickProbe>
{
    new() { Z = 1, Window = new TrackedWindow { Id = 1, Process = "Code", Title = "BongHub", X = 0, Y = 0, W = 400, H = 300 } },
    new() { Z = 8, Cloaked = true, Window = new TrackedWindow { Id = 2, Title = "cloak", X = 10, Y = 10, W = 200, H = 200 } },
    new() { Z = 9, Own = true, Window = new TrackedWindow { Id = 3, Title = "self", X = 10, Y = 10, W = 200, H = 200 } },
    new() { Z = 7, Shadow = true, Window = new TrackedWindow { Id = 4, Title = "shadow", X = 10, Y = 10, W = 220, H = 220 } },
    new() { Z = 4, OwnerId = 1, Window = new TrackedWindow { Id = 5, Process = "popup", Title = "popup", X = 20, Y = 20, W = 80, H = 40 } },
};
var chosen = WindowPicker.Choose(probes, 30, 30);
var at125 = WindowPicker.Physical(100, 80, 1.25);
var at96 = WindowPicker.Physical(100, 80, 1);
Check("crosshair ignores cloaked, own, and shadow windows and keeps DPI", chosen != null && chosen.Id == 1 && chosen.Title == "BongHub" && at125.X == 125 && at125.Y == 100 && at96.X == 100 && at96.Y == 80);
var iconFiles = IconBits.FilesToTry("Code", @"C:\Apps\Code.exe");
var blankIcon = IconBits.RepairAlpha(new byte[16]);
var repaired = IconBits.RepairAlpha(new byte[] { 10, 20, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 0 });
Check("icon extraction prefers the exe and drops a blank image", iconFiles.Count >= 2 && iconFiles[0].EndsWith("Code.exe") && iconFiles.Contains("Code") && blankIcon == null && repaired != null && repaired[3] == 255 && repaired[7] == 0);
var iconMonitors = new[] { new ProfileBox(-3840, 0, 3840, 2160), new ProfileBox(0, 0, 3840, 2160) };
var savedIcons = new List<ProfileIcon>
{
    new() { Name = "휴지통", X = -100, Y = 40 },
    new() { Name = "문서", X = 10, Y = 10 },
    new() { Name = "숨김", X = 20, Y = 20, Show = false },
    new() { Name = "없는", X = 30, Y = 30 },
};
var currentIcons = new List<DeskIcon>
{
    new() { Name = "휴지통", X = -100, Y = 40 },
    new() { Name = "문서", X = 80, Y = 80 },
    new() { Name = "새", X = 1, Y = 1 },
};
var compared = IconLayout.Compare(savedIcons, currentIcons);
var iconSurface2 = new MemoryIconSurface();
iconSurface2.Icons.Add(new DeskIcon { Name = "휴지통", X = 0, Y = 0 });
iconSurface2.Icons.Add(new DeskIcon { Name = "문서", X = 0, Y = 0 });
iconSurface2.Icons.Add(new DeskIcon { Name = "숨김", X = 5, Y = 5 });
var partialRestore = ProfileDoc.RestoreIcons(savedIcons, iconSurface2);
Check("icon preview compares a layout and restores every saved icon", IconLayout.Host(-100, 40, iconMonitors) == 0 && IconLayout.Host(50, 50, iconMonitors) == 1 && compared.Same == 1 && compared.Moved == 1 && compared.Missing == 1 && compared.Extra == 1 && compared.Hidden == 1 && partialRestore.Ok && iconSurface2.Icons[0].X == -100 && iconSurface2.Icons[0].Y == 40 && iconSurface2.Icons[1].X == 10 && iconSurface2.Icons[1].Y == 10 && iconSurface2.Icons[2].X == 20 && iconSurface2.Icons[2].Y == 20);
var ownerDesk = new List<ScreenInfo>
{
    new() { Number = 3, Device = @"\\.\DISPLAY3", Path = "P3", W = 3840, H = 2160, X = -3840, Y = 0, Scale = 125 },
    new() { Number = 1, Device = @"\\.\DISPLAY1", Path = "P1", W = 3840, H = 2160, X = 0, Y = 0, Scale = 125, Primary = true },
    new() { Number = 5, Device = @"\\.\DISPLAY5", Path = "P5", W = 2400, H = 1350, X = 3840, Y = 0, Scale = 100 },
    new() { Number = 2, Device = @"\\.\DISPLAY2", Path = "P2", W = 2400, H = 1350, X = 6240, Y = -200, Scale = 100 },
    new() { Number = 4, Device = @"\\.\DISPLAY4", Path = "P4", W = 1200, H = 800, X = 8640, Y = 0, Scale = 150, Orientation = 90 },
};
var followed = new MonitorProfile { Name = "작업" };
followed.Monitors.Add(new ProfileMonitor { Id = "1", Number = 1, X = 0, Y = 0, W = 1920, H = 1080, Primary = true });
followed.Apps.Add(new ProfileApp { Process = "notepad.exe", Title = "메모", Monitor = 1, X = 12, Y = 12, W = 400, H = 300 });
ScreenBind.FollowLive(followed, ownerDesk);
var kept = new MonitorProfile();
kept.Monitors.Add(new ProfileMonitor { Id = "old", Number = 9, Path = "P1", W = 800, H = 600, X = 50, Y = 50 });
kept.Monitors.Add(new ProfileMonitor { Id = "gone", Number = 8, Path = "GONE", W = 640, H = 480 });
kept.Apps.Add(new ProfileApp { Title = "남음", Monitor = 8, W = 100, H = 100 });
ScreenBind.FollowLive(kept, new[] { ownerDesk[1] });
var ordered = string.Join(",", followed.Monitors.OrderBy(m => m.X).Select(m => m.Number));
Check("live layout keeps identify numbers, scale, orientation, and a disconnected screen", ordered == "3,1,5,2,4" && followed.Monitors.First(m => m.Number == 3).X == -3840 && followed.Monitors.First(m => m.Number == 4).Orientation == 90 && followed.Apps[0].Monitor == 1 && kept.Monitors.Any(m => !m.Connected && m.Path == "GONE") && kept.Monitors.First(m => m.Path == "P1").Scale == 125 && kept.Apps[0].Monitor == 8);
string oldProfile = "{\"v\":2,\"profiles\":[{\"name\":\"작업\",\"hotkey\":\"Ctrl+Alt+1\",\"workspace\":\"W1\",\"brightness\":[{\"monitor\":\"SAM1\",\"value\":40}],\"monitors\":[{\"id\":\"SAM1\",\"number\":1,\"w\":1920,\"h\":1080,\"primary\":true}],\"windows\":[{\"process\":\"notepad.exe\",\"title\":\"메모\",\"w\":640,\"h\":480}],\"iconItems\":[{\"name\":\"휴지통\",\"x\":1,\"y\":2}]}]}";
var aged = ProfileDoc.Parse(oldProfile);
aged[0].Apps[0].TopMost = true;
aged[0].Apps[0].Launch = true;
aged[0].Apps[0].Path = @"C:\Windows\notepad.exe";
aged[0].Monitors[0].Scale = 125;
aged[0].Icons[0].Show = false;
var agedBack = ProfileDoc.Parse(ProfileDoc.ToJson(aged));
var missingLaunch = ProfileDoc.MissingLaunches(aged[0], Array.Empty<TrackedWindow>());
var presentLaunch = ProfileDoc.MissingLaunches(aged[0], new[] { new TrackedWindow { Process = "notepad", Title = "메모" } });
var placed = new FakeWindowSurface();
placed.Windows.Add(new TrackedWindow { Id = 7, Process = "notepad", Title = "메모", W = 100, H = 100 });
ProfileDoc.Place(aged[0], placed);
ProfileUndo.Remember(aged[0]);
aged[0].Name = "바뀜";
var undone = ProfileUndo.Take();
var diff = ProfileDiff.Summary(agedBack[0], ownerDesk);
Check("older profiles keep their fields and gain topmost, launch, and icon visibility", agedBack[0].Workspace == "W1" && agedBack[0].Hotkey == "Ctrl+Alt+1" && agedBack[0].Brightness[0].Value == 40 && agedBack[0].Monitors[0].Scale == 125 && agedBack[0].Apps[0].TopMost && !agedBack[0].Icons[0].Show && ProfileDoc.Parse(oldProfile)[0].Monitors[0].Scale == 100 && ProfileDoc.Parse(oldProfile)[0].Icons[0].Show && missingLaunch.Count == 1 && presentLaunch.Count == 0 && placed.Windows[0].TopMost && undone != null && undone.Name == "작업" && diff.Contains("창"));
var stage = ProfileChrome.Stage(700, 600);
var narrowDetail = ProfileChrome.Detail(56, 40, 1200, 800, 150, 90, false, true);
var midDetail = ProfileChrome.Detail(120, 70, 2400, 1350, 100, 0, false, true);
var wideDetail = ProfileChrome.Detail(220, 130, 3840, 2160, 125, 0, true, true);
var chipBox = ProfileChrome.WindowChip(0, 0, 1920, 1080, 3840, 2160, 10, 20, 400, 200);
var iconDot = ProfileChrome.IconDot(40, 20, 0, 0, 3840, 2160, 10, 20, 400, 200);
double headerBand = ProfileChrome.BadgeBand(180, 110, true);
var underChip = ProfileChrome.WindowChip(0, 0, 1920, 1080, 3840, 2160, 0, 0, 180, 110);
var wideBoxes = ownerDesk.Select(s => new ProfileBox(s.X, s.Y, s.W, s.H)).ToList();
var deskScene = ProfileSnap.Fit(wideBoxes, stage.Width, stage.FitHeight, 16);
int painted = ProfileChrome.PaintedHeight(deskScene.Bounds.H, deskScene.Scale, 16);
var chipInset = ProfileChrome.ChipContent(underChip, 0, 0, 180, headerBand);
var below = ProfileChrome.WindowChip(0, 1400, 800, 600, 3840, 2160, 0, 80, 180, 110);
var belowInset = ProfileChrome.ChipContent(below, 0, 80, 180, 36);
Check("small tiles drop detail and chips keep the real ratio", stage.Width == 700 && stage.FitHeight == 252 && narrowDetail.Length == 0 && midDetail == "2400×1350" && wideDetail.Contains("3840") && wideDetail.Contains("주") && Math.Abs(chipBox.W - 200) < 1.5 && Math.Abs(chipBox.H - 100) < 1.5 && chipBox.Y < 22 && iconDot.W == 6 && headerBand >= 60 && Math.Abs(underChip.W - 90) < 1.5 && underChip.Y < 2 && painted < stage.FitHeight && painted > 70 && ProfileGlyph.Kinds.Length == 14 && ProfileGlyph.Known("lock") && ProfileGlyph.Known("trash") && ProfileGlyph.Known("power") && ProfileGlyph.Known("star") && ProfileGlyph.Known("expand"));
var swallowed = ProfileChrome.ChipContent(new DipRect(2, 2, 24, 18), 0, 0, 180, 70);
var smokeDesk = new[]
{
    new ProfileBox(-3840, 0, 3840, 2160),
    new ProfileBox(0, 0, 3840, 2160),
    new ProfileBox(3840, 0, 2400, 1350),
    new ProfileBox(6240, -200, 2400, 1350),
    new ProfileBox(8640, 0, 1200, 800),
};
bool smokePark = true;
string smokeParkDetail = "";
foreach (int cw in new[] { 520, 656, 720 })
{
    foreach (int fh in new[] { 160, 210, 252 })
    {
        var scene = ProfileSnap.Fit(smokeDesk, cw, fh, 16);
        var tile3 = ProfileSnap.ToCanvas(smokeDesk[0], scene, 8, 8);
        var tile1 = ProfileSnap.ToCanvas(smokeDesk[1], scene, 8, 8);
        var noteChip = ProfileChrome.WindowChip(80, 80, 720, 480, 3840, 2160, tile3.X, tile3.Y, tile3.W, tile3.H);
        bool noteDetail = ProfileChrome.Detail(tile3.W, tile3.H, 3840, 2160, 125, 0, false, true).Length > 0;
        var noteInk = ProfileChrome.ChipContent(noteChip, tile3.X, tile3.Y, tile3.W, ProfileChrome.BadgeBand(tile3.W, tile3.H, noteDetail));
        var notePark = ProfileChrome.ParkedChip(tile3.X, tile3.Y, tile3.W, tile3.H);
        var notePill = ProfileChrome.NumberPill(tile3.X, tile3.Y);
        var codeChip = ProfileChrome.WindowChip(160, 120, 1400, 900, 3840, 2160, tile1.X, tile1.Y, tile1.W, tile1.H);
        bool codeDetail = ProfileChrome.Detail(tile1.W, tile1.H, 3840, 2160, 125, 0, true, true).Length > 0;
        var codeInk = ProfileChrome.ChipContent(codeChip, tile1.X, tile1.Y, tile1.W, ProfileChrome.BadgeBand(tile1.W, tile1.H, codeDetail));
        bool ok = !noteInk.Show && notePark.W >= 56 && notePark.H >= 18 && !notePark.Hits(notePill) && notePark.Bottom <= tile3.Y + 26 && notePark.Right <= tile3.Right - 3 && codeInk.Show;
        if (!ok)
        {
            smokePark = false;
            smokeParkDetail = $"cw={cw} fh={fh} tile3 {tile3.W}x{tile3.H} note {noteChip.X:0},{noteChip.Y:0} {noteChip.W:0}x{noteChip.H:0} show={noteInk.Show} park {notePark.X:0},{notePark.Y:0} {notePark.W:0}x{notePark.H:0} codeShow={codeInk.Show}";
            break;
        }
    }
    if (!smokePark) break;
}
Check("smoke desk parks the swallowed notepad chip", smokePark, smokeParkDetail);
var parkedChip = ProfileChrome.ParkedChip(0, 0, 180, 100);
var parkedPill = ProfileChrome.NumberPill(0, 0);
var parkedTight = ProfileChrome.ParkedChip(0, 0, 48, 40);
var primPill = ProfileChrome.NumberPill(0, 0, true);
var underPrim = ProfileChrome.ChipContent(new DipRect(0, 0, 140, 48), 0, 0, 200, 80, true);
Check("primary star stays inside the badge and chip text starts after it", primPill.W >= 50 && underPrim.Show && underPrim.Left >= primPill.Right - 1, $"pill {primPill.W:0} left {underPrim.Left:0}");
Check("chip icon clears the identify band", chipInset.Show && (chipInset.Top >= headerBand - 1 || chipInset.Left >= 24) && belowInset.Show && belowInset.Left == 0 && belowInset.Top == 0 && !swallowed.Show && parkedChip.W >= 56 && parkedChip.H >= 18 && parkedChip.X >= parkedPill.Right + 3 && !parkedChip.Hits(parkedPill) && parkedChip.Bottom <= 26 && parkedChip.Right <= 176 && parkedTight.W == 0, $"inset {chipInset.Left:0},{chipInset.Top:0} show={chipInset.Show} below {below.Y:0} {belowInset.Left:0},{belowInset.Top:0} swallowed={swallowed.Show} parked {parkedChip.X:0},{parkedChip.Y:0} {parkedChip.W:0}x{parkedChip.H:0} tight={parkedTight.W:0}");
var iconMon = new List<ProfileMonitor>
{
    new() { Number = 3, X = -3840, Y = -1440, W = 3840, H = 2160, Scale = 100, Primary = false },
    new() { Number = 2, X = 0, Y = 0, W = 3840, H = 2160, Scale = 125, Primary = true }
};
var iconFrame = IconMap.Frame(iconMon);
var shifted = IconMap.ToScreen(3857, 1098, -3840, -1096, 0, -5, IconMap.ImagePx(125));
var fromRect = IconMap.Center(3840, 1101, 3934, 1165, -3840, -1096);
Check("icon list positions are physical pixels", iconFrame.Left == -3840 && iconFrame.Top == -1440 && iconFrame.Scale == 125 && IconMap.ImagePx(125) == 60 && shifted.X == 47 && shifted.Y == 37 && fromRect.X == 47 && fromRect.Y == 37, $"shifted {shifted.X},{shifted.Y} rect {fromRect.X},{fromRect.Y}");
string probePath = repo == null ? "" : Path.Combine(repo, "BongHub.PatchTests", "fixtures", "bh_iconprobe.json");
int onPrimary = 0, onLeft = 0, onTop = 0, probeN = 0;
if (probePath.Length > 0 && File.Exists(probePath))
{
    var probe = JsonNode.Parse(File.ReadAllText(probePath))!;
    var probeScreens = probe["monitors"]!.AsArray().Select(m =>
    {
        var r = m!["rcMonitor"]!;
        return (Device: m["device"]!.GetValue<string>(), X: r["left"]!.GetValue<int>(), Y: r["top"]!.GetValue<int>(), W: r["w"]!.GetValue<int>(), H: r["h"]!.GetValue<int>());
    }).ToList();
    var origin = probe["desktop"]!["LVM_GETORIGIN"]!.GetValue<string>().Split(',');
    int ox = int.Parse(origin[0]), oy = int.Parse(origin[1]);
    int vx = probe["metrics"]!["SM_XVIRTUALSCREEN"]!.GetValue<int>();
    int vy = probe["metrics"]!["SM_YVIRTUALSCREEN"]!.GetValue<int>();
    foreach (var item in probe["desktop"]!["items"]!.AsArray())
    {
        var pos = item!["pos"]!.GetValue<string>().Split(',');
        var screen = IconMap.ToScreen(int.Parse(pos[0]), int.Parse(pos[1]), vx, vy, ox, oy, 60);
        probeN++;
        string? host = null;
        foreach (var s in probeScreens)
            if (screen.X >= s.X && screen.Y >= s.Y && screen.X < s.X + s.W && screen.Y < s.Y + s.H) { host = s.Device; break; }
        if (host != null && host.EndsWith("DISPLAY2", StringComparison.OrdinalIgnoreCase)) onPrimary++;
        else if (host != null && host.EndsWith("DISPLAY1", StringComparison.OrdinalIgnoreCase)) onLeft++;
        else onTop++;
    }
}
Check("owner icon probe lands on the lower monitors", probeN == 78 && onPrimary == 69 && onLeft == 9 && onTop == 0, $"n={probeN} primary={onPrimary} left={onLeft} other={onTop}");
var savedDiscord = new ProfileApp { Process = "Discord.exe", Class = "Chrome_WidgetWin_1", Title = "옛서버 - Discord" };
var renamed = new TrackedWindow { Id = 1, Process = "Discord", Class = "Chrome_WidgetWin_1", Title = "새서버 - Discord" };
var other = new TrackedWindow { Id = 2, Process = "Discord", Class = "Chrome_WidgetWin_1", Title = "다른 - Discord" };
var exact = new TrackedWindow { Id = 3, Process = "Discord", Class = "Chrome_WidgetWin_1", Title = "옛서버 - Discord" };
var chrome = new TrackedWindow { Id = 4, Process = "chrome", Class = "Chrome_WidgetWin_1", Title = "옛서버 - Discord", W = 800, H = 600 };
var best = WindowIdentity.Best(savedDiscord, new[] { other, renamed, chrome });
var exactHit = WindowIdentity.Best(savedDiscord, new[] { other, exact, renamed });
Check("discord title changes still match the exe and class", WindowIdentity.Suffix("옛서버 - Discord") == "Discord" && WindowIdentity.Score(savedDiscord, renamed) >= WindowIdentity.MatchFloor && WindowIdentity.Score(savedDiscord, chrome) == 0 && best != null && best.Id != 4 && exactHit != null && exactHit.Id == 3);
var aimMon = new List<ProfileMonitor> { new() { Number = 2, X = 100, Y = 40, W = 1920, H = 1080 } };
var aimed = AimPlace.Of(180, 90, 640, 480, aimMon);
Check("aim keeps the window's current rectangle", aimed.Monitor == 2 && aimed.X == 80 && aimed.Y == 50 && aimed.W == 640 && aimed.H == 480);
var ghosts = GhostWindows.Unregistered(new[] { renamed, chrome, new TrackedWindow { Process = "BongHub", Class = "Window", Title = "BongHub", W = 800, H = 600 } }, new[] { savedDiscord });
int tintA = GhostWindows.Argb("Discord|Chrome_WidgetWin_1");
int tintB = GhostWindows.Argb("chrome|Chrome_WidgetWin_1");
Check("unregistered windows stay ghosts with a stable tint", ghosts.Count == 1 && ghosts[0].Id == 4 && tintA == GhostWindows.Argb("Discord|Chrome_WidgetWin_1") && tintA != tintB);
var away = IdlePlan.Tick(true, 15, "자리비움", "작업", "작업", 15 * 60_000, false);
var hold = IdlePlan.Tick(true, 15, "자리비움", "자리비움", "작업", 15 * 60_000, true);
var back = IdlePlan.Tick(true, 15, "자리비움", "자리비움", "작업", 1000, true);
var quiet = IdlePlan.Tick(false, 15, "자리비움", "작업", "", 15 * 60_000, false);
Check("idle plan switches away and back without applying", away.Action == "away" && away.Preset == "자리비움" && hold.Action == "none" && back.Action == "back" && back.Preset == "작업" && quiet.Action == "none" && MonitorIdentify.Plan(iconMon, false) == "3,2" && MonitorIdentify.NativeCalls == 0);
var loose = ProfileSnap.SnapWindow(new ProfileBox(6, 40, 100, 80), new ProfileBox(0, 0, 800, 600), Array.Empty<ProfileBox>(), 1);
var corner = ProfileSnap.SnapMonitorWithGuide(new ProfileBox(1890, 1050, 400, 300), new[] { new ProfileBox(0, 0, 1920, 1080), new ProfileBox(1920, 0, 1920, 1080), new ProfileBox(0, 1080, 1920, 800) }, 40);
Check("window edge magnet is tiny and corners snap on both axes", loose.X == 6 && corner.Box.X == 1920 && corner.Box.Y == 1080 && corner.Guide.Corner && !ProfileSnap.ArmZone("left", "left", true) && ProfileSnap.ArmZone("", "left", true), $"loose={loose.X} corner={corner.Box.X},{corner.Box.Y} guide={corner.Guide.X1},{corner.Guide.Y1}-{corner.Guide.X2},{corner.Guide.Y2}/{corner.Guide.X3},{corner.Guide.Y3}-{corner.Guide.X4},{corner.Guide.Y4}");
string deskStory = ProfileSwitch.DisplayStory();
string sessionStory = ProfileSwitch.SessionStory();
string placeStory = PlacementSpace.Story();
string noteStory = NoteLayout.TopologyStory();
Check("display apply is one commit and keeps the owner desk", deskStory.Length == 0, deskStory);
Check("leaving a profile restores that session", sessionStory.Length == 0, sessionStory);
Check("restore uses workspace coordinates and stays on the monitor", placeStory.Length == 0, placeStory);
var wideBefore = new[]
{
    new PlacementSpace.Monitor("L", -3840, 0, 3840, 2160),
    new PlacementSpace.Monitor("P", 0, 0, 2560, 1440, true)
};
var primaryOnly = new[] { new PlacementSpace.Monitor("P", 0, 0, 1920, 1080, true) };
var mixedAfter = new[]
{
    new PlacementSpace.Monitor("P", 0, 0, 1920, 1080, true),
    new PlacementSpace.Monitor("R", 1920, 0, 2400, 1350)
};
var moved = new[]
{
    new PlacementSpace.Monitor("L", 1920, 0, 3840, 2160),
    new PlacementSpace.Monitor("P", 0, 0, 1920, 1080, true)
};
var parked = new FakeWindowSurface { WorkOriginY = 40 };
parked.Windows.Add(new TrackedWindow { Id = 4, Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = -3000, NormalY = 80, NormalW = 1600, NormalH = 900 });
parked.Windows.Add(new TrackedWindow { Id = 5, Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = -5000, NormalY = -2000, NormalW = 3200, NormalH = 1800 });
parked.Windows.Add(new TrackedWindow { Id = 6, X = 200, Y = 120, W = 800, H = 600, NormalX = 200, NormalY = 80, NormalW = 800, NormalH = 600 });
parked.Windows.Add(new TrackedWindow { Id = 7, Max = true, X = -3840, Y = 0, W = 3840, H = 2160, NormalX = -2200, NormalY = 40, NormalW = 900, NormalH = 700, MaxX = -3840, MaxY = -40 });
int settled = parked.Settle(wideBefore, primaryOnly);
bool stillMin = parked.Windows.First(w => w.Id == 4).Min && parked.Windows.First(w => w.Id == 5).Min && parked.Windows.First(w => w.Id == 7).Max && !parked.Windows.First(w => w.Id == 7).Min;
parked.ShowRestored(4);
parked.ShowRestored(5);
var backOn = parked.Windows.First(w => w.Id == 4);
var shrunk = parked.Windows.First(w => w.Id == 5);
var stayed = parked.Windows.First(w => w.Id == 6);
var maxed = parked.Windows.First(w => w.Id == 7);
bool backInside = backOn.X >= 0 && backOn.Y >= 0 && backOn.X + backOn.W <= 1920 && backOn.Y + backOn.H <= 1120 && backOn.W == 1600;
bool sizeClamped = shrunk.W <= 1920 && shrunk.H <= 1080 && shrunk.X >= 0 && shrunk.Y >= 0 && shrunk.X + shrunk.W <= 1920 && shrunk.Y + shrunk.H <= 1120;
bool untouched = stayed.NormalX == 200 && stayed.NormalY == 80 && stayed.Min == false;
bool maxStayed = maxed.Max && !maxed.Min && maxed.MaxX == 0 && maxed.MaxY == 0;
var ride = new FakeWindowSurface();
ride.Windows.Add(new TrackedWindow { Id = 8, Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = -3000, NormalY = 100, NormalW = 800, NormalH = 600 });
ride.Settle(wideBefore, moved);
ride.ShowRestored(8);
var rode = ride.Windows[0];
bool rodeLeft = rode.X == 1920 + (-3000 - (-3840)) && rode.Y == 100 && rode.W == 800 && !rode.Min;
var dpi = new FakeWindowSurface();
dpi.Windows.Add(new TrackedWindow { Id = 9, Min = true, X = -32000, Y = -32000, W = 160, H = 28, NormalX = -2500, NormalY = 60, NormalW = 3000, NormalH = 1800 });
dpi.Settle(wideBefore, mixedAfter);
bool dpiStill = dpi.Windows[0].Min;
dpi.ShowRestored(9);
var dpiWin = dpi.Windows[0];
bool dpiClamp = dpiStill && dpiWin.X >= 0 && dpiWin.W == 1920 && dpiWin.H == 1080 && dpiWin.X + dpiWin.W <= 1920 && dpiWin.Y + dpiWin.H <= 1080;
var sliver = PlacementSpace.Settle(new PlacementSpace.Box(-1800, 40, 1920, 800), 0, 0, false, 0, 0, wideBefore, primaryOnly);
bool halfOn = PlacementSpace.MostlyInside(new PlacementSpace.Box(-400, 40, 800, 600), primaryOnly);
Check("minimized restores land on a live monitor and stay minimized until shown", settled >= 3 && stillMin && backInside && sizeClamped && untouched && maxStayed && rodeLeft && dpiClamp && sliver.Changed && sliver.Normal.X >= 0 && halfOn, $"settled={settled} back={backOn.X},{backOn.Y} {backOn.W}x{backOn.H} clamp={shrunk.X},{shrunk.Y} {shrunk.W}x{shrunk.H} max={maxed.MaxX},{maxed.MaxY} rode={rode.X},{rode.Y} dpi={dpiWin.X},{dpiWin.Y} {dpiWin.W}x{dpiWin.H} sliver={sliver.Normal.X}");
var hiMon = new ProfileMonitor { Number = 1, X = 0, Y = 0, W = 3840, H = 2160, Scale = 125, Primary = true };
var loMon = new ProfileMonitor { Number = 2, X = 3840, Y = 0, W = 1920, H = 1080, Scale = 100 };
var cardHi = IdentifyLayout.Of(hiMon);
var cardLo = IdentifyLayout.Of(loMon);
var osdCard = IdentifyLayout.Center(hiMon, 360, 108);
bool cardsIn = cardHi.X >= 0 && cardHi.Y >= 0 && cardHi.X + cardHi.W <= 3840 && cardHi.Y + cardHi.H <= 2160 && cardLo.X >= 3840 && cardLo.X + cardLo.W <= 5760 && cardLo.Y + cardLo.H <= 1080;
bool cardsBig = cardHi.W > cardLo.W && cardHi.H > cardLo.H && cardHi.Y > 2160 / 2 && cardHi.X < 800;
bool osdMid = Math.Abs((osdCard.X + osdCard.W / 2) - 1920) <= 2;
Check("identify cards use each monitor's physical pixels", cardsIn && cardsBig && osdMid && cardHi.W == IdentifyLayout.Px(IdentifyLayout.BaseW, 125) && MonitorIdentify.NativeCalls == 0);
int inkClash = 0;
foreach (int tw in new[] { 40, 72, 110, 180, 260 })
foreach (int th in new[] { 36, 64, 100, 150 })
{
    var tileInk = new DipRect(8, 12, tw, th);
    var samples = new List<DipRect>
    {
        new(tileInk.X + 2, tileInk.Y + 2, Math.Max(8, Math.Min(78, tw - 4)), Math.Max(8, Math.Min(26, th - 4))),
        ProfileChrome.WindowChip(40, 40, 900, 600, 1920, 1080, tileInk.X, tileInk.Y, tileInk.W, tileInk.H)
    };
    var marks = CanvasInk.Arrange(tileInk, tw > 140, samples);
    var taken = new List<DipRect> { ProfileChrome.NumberPill(tileInk.X, tileInk.Y, tw > 140) };
    foreach (var mark in marks)
    {
        foreach (var box in taken)
            if (mark.Box.W > 1 && mark.Box.Hits(box)) inkClash++;
        if (mark.Box.W > 1) taken.Add(mark.Box);
    }
}
var ghostUnder = CanvasInk.Line(new DipRect(0, 0, 30, 22), new[] { ProfileChrome.NumberPill(0, 0) }, 12, 2);
var ghostBeside = CanvasInk.Line(new DipRect(0, 0, 120, 36), new[] { ProfileChrome.NumberPill(0, 0) }, 12, 2);
Check("canvas text never meets a badge or another line", inkClash == 0 && ghostUnder.W < 1 && ghostBeside.W >= 8 && !ghostBeside.Hits(ProfileChrome.NumberPill(0, 0)), "clash " + inkClash + " under " + ghostUnder.W + " beside " + ghostBeside.X);
var snapZone = ProfileSnap.ZoneBox("left", new ProfileBox(0, 0, 1920, 1080));
var snapCue = SnapCueMath.Window(new ProfileBox(12, 40, 400, 300), new ProfileBox(0, 0, 1920, 1080), null, 1, 8, "left", snapZone);
var snapFlush = SnapCueMath.Window(new ProfileBox(3, 40, 400, 300), new ProfileBox(0, 0, 1920, 1080), null, 1, 8, "", default);
Check("snap preview marks the zone and the flush edge", snapCue.Kind == "left" && snapCue.Left && snapCue.Box.W == 960 && snapFlush.Left && snapFlush.Box.X == 0);
Check("hotkey capture shows held modifiers", HotkeyChord.Caps(false, true, true, false) == "Ctrl + Alt + …" && HotkeyChord.Caps(false, false, false, false).Length == 0 && HotkeyChord.ListenPrompt == "키를 누르세요…");
string[] captureApis = { "GraphicsCapture", "CreateForMonitor", "Direct3D11CaptureFramePool", "IDXGIOutputDuplication", "DwmRegisterThumbnail", "CopyFromScreen", "IsBorderRequired" };
var captureHits = new List<string>();
if (repo != null)
    foreach (var file in Directory.GetFiles(Path.Combine(repo, "BongHub"), "*.cs"))
        foreach (var api in captureApis)
            if (File.ReadAllText(file).Contains(api, StringComparison.Ordinal))
                captureHits.Add(Path.GetFileName(file) + ":" + api);
Check("app does not capture a monitor or a window", captureHits.Count == 0, string.Join(", ", captureHits));
var barBottom = new ProfileMonitor { Number = 1, X = 0, Y = 0, W = 1920, H = 1080, Primary = true, WorkX = 0, WorkY = 0, WorkW = 1920, WorkH = 1040 };
var barLeft = new ProfileMonitor { Number = 2, X = 1920, Y = 0, W = 1600, H = 1200, Primary = false, WorkX = 1968, WorkY = 0, WorkW = 1552, WorkH = 1200 };
var barTop = new ProfileMonitor { Number = 3, X = -2560, Y = -1440, W = 2560, H = 1440, Primary = false, WorkX = -2560, WorkY = -1400, WorkW = 2560, WorkH = 1400 };
var bottomHalf = ProfileSnap.ZoneBox("left", barBottom.Work);
var bottomMax = ProfileSnap.ZoneBox("top", barBottom.Work);
var bottomDock = ProfileSnap.SnapWindow(new ProfileBox(40, 980, 400, 80), barBottom.Work, Array.Empty<ProfileBox>(), 1, 8);
var leftHalf = ProfileSnap.ZoneBox("left", barLeft.Work);
var leftQuarter = ProfileSnap.PreviewAt(1980, 20, barLeft.Work);
var inTaskbar = ProfileSnap.PreviewAt(1930, 400, barLeft.Work);
var topMax = ProfileSnap.ZoneBox("top", barTop.Work);
var topQuarter = ProfileSnap.ZoneBox("tl", barTop.Work);
var stripsBottom = ProfileSnap.TaskbarStrips(barBottom);
var stripsLeft = ProfileSnap.TaskbarStrips(barLeft);
var stripsTop = ProfileSnap.TaskbarStrips(barTop);
var workProfile = new MonitorProfile { Name = "작업" };
workProfile.Monitors.Add(barBottom);
var workBack = ProfileDoc.Parse(ProfileDoc.ToJson(new[] { workProfile }))[0].Monitors[0];
bool workOk = bottomHalf.X == 0 && bottomHalf.Y == 0 && bottomHalf.W == 960 && bottomHalf.H == 1040
    && bottomMax.H == 1040 && bottomMax.Y == 0 && bottomMax.Bottom == 1040
    && bottomDock.Y + bottomDock.H == 1040
    && leftHalf.X == 1968 && leftHalf.W == 776 && leftHalf.H == 1200
    && leftQuarter.Kind == "tl" && leftQuarter.Box.X == 1968
    && inTaskbar.Kind.Length == 0
    && topMax.Y == -1400 && topMax.H == 1400
    && topQuarter.Y == -1400 && topQuarter.X == -2560
    && stripsBottom.Count == 1 && stripsBottom[0].Y == 1040 && stripsBottom[0].H == 40
    && stripsLeft.Count == 1 && stripsLeft[0].X == 1920 && stripsLeft[0].W == 48
    && stripsTop.Count == 1 && stripsTop[0].Y == -1440 && stripsTop[0].H == 40
    && workBack.WorkH == 1040 && workBack.WorkY == 0 && workBack.H == 1080;
Check("snap zones use each monitor's work area, including a taskbar on a side monitor", workOk, $"half {bottomHalf.W}x{bottomHalf.H} dock {bottomDock.Y} left {leftHalf.X},{leftHalf.W} top {topMax.Y} strip {stripsLeft[0].W}");
Check("notes ignore a transient work area", noteStory.Length == 0, noteStory);
string profileRoot = Path.Combine(Path.GetTempPath(), "bonghub_profile_" + Environment.ProcessId);
try
{
    Directory.CreateDirectory(Path.Combine(profileRoot, "notes"));
    Directory.CreateDirectory(Path.Combine(profileRoot, "desktop"));
    File.WriteAllText(Path.Combine(profileRoot, "config.json"), "{\"General\":{\"IdleMinutes\":1}}");
    var remoteProfiles = new MemoryCloud();
    string profileJson = "{\"profiles\":[{\"name\":\"작업\",\"icons\":\"L1\"}]}";
    string ran = CloudEngine.Run(new CloudWorkspace
    {
        ConfigFile = Path.Combine(profileRoot, "config.json"),
        NotesDir = Path.Combine(profileRoot, "notes"),
        DeskDir = Path.Combine(profileRoot, "desktop"),
        Device = "PC-A",
        AppVersion = "1.1.0",
        UtcNow = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        LocalNow = new DateTime(2026, 1, 2, 9, 0, 0),
        ProfilesJson = profileJson
    }, remoteProfiles, true);
    var tree = remoteProfiles.Snapshot();
    Check("profiles.json is additive", ran.Length == 0 && tree.ContainsKey("data/profiles.json") && tree.ContainsKey("data/config.json") && Encoding.UTF8.GetString(tree["data/profiles.json"].Bytes) == profileJson);
    string titledNote = "{\"Id\":\"n9\",\"Color\":\"yellow\",\"X\":0,\"Y\":0,\"W\":10,\"H\":10,\"TopMost\":false,\"Open\":true,\"Zoom\":1,\"Text\":\"본문\",\"Modified\":\"\\/Date(0)\\/\",\"Title\":\"회의\"}";
    File.WriteAllText(Path.Combine(profileRoot, "notes", "n9.json"), titledNote);
    var titledRemote = new MemoryCloud();
    string titledRun = CloudEngine.Run(new CloudWorkspace
    {
        ConfigFile = Path.Combine(profileRoot, "config.json"),
        NotesDir = Path.Combine(profileRoot, "notes"),
        DeskDir = Path.Combine(profileRoot, "desktop"),
        Device = "PC-A",
        AppVersion = "1.1.1",
        UtcNow = new DateTime(2026, 1, 2, 0, 5, 0, DateTimeKind.Utc),
        LocalNow = new DateTime(2026, 1, 2, 9, 5, 0)
    }, titledRemote, true);
    var titledTree = titledRemote.Snapshot();
    Check("note title syncs beside the 3.4.x fields", titledRun.Length == 0 && titledTree.ContainsKey("data/notes/n9.json") && Encoding.UTF8.GetString(titledTree["data/notes/n9.json"].Bytes).Contains("\"Title\":\"회의\"") && titledTree.ContainsKey("data/config.json"));
}
finally { try { Directory.Delete(profileRoot, true); } catch { } }
    }
    catch (Exception ex) { Check("cloud exception", false, ex.ToString()); }
    finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
}

static string? FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null)
    {
        if (File.Exists(Path.Combine(d.FullName, "data", "config.json")) && File.Exists(Path.Combine(d.FullName, "BongHub", "BongHub.csproj")))
            return d.FullName;
        d = d.Parent;
    }
    return null;
}
