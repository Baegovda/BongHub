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
string dir = Path.Combine(Path.GetTempPath(), "bhnext_patch_" + Environment.ProcessId);
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

string iconDir = Path.Combine(Path.GetTempPath(), "bhnext_icons_" + Environment.ProcessId);
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
string peekDir = Path.Combine(Path.GetTempPath(), "bhnext_peek_" + Environment.ProcessId);
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
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true, Dark = false });
Check("start compact -> 1 helper", host.Running == 1 && host.LastArgs == "" && KakaoArgs.Format(new KakaoSettings { AdBlock = true, Compact = true }) == "");
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = false, Dark = true });
Check("restart /nocompact /dark stays one helper", host.Running == 1 && host.LastArgs == "/nocompact /dark");
KakaoSync.Apply(host, new KakaoSettings { AdBlock = false });
Check("restore stops the helper", host.Running == 0 && host.LastArgs == null && host.Log[host.Log.Count - 1] == "restore");
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true, Dark = false });
Check("sync on starts the helper", host.Running == 1 && host.LastArgs == "");
int starts = host.Log.Count(x => x.StartsWith("start ", StringComparison.Ordinal));
KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true, Dark = false });
Check("sync again does not restart", host.Running == 1 && host.Log.Count(x => x.StartsWith("start ", StringComparison.Ordinal)) == starts);
KakaoSync.Apply(host, new KakaoSettings { AdBlock = false });
Check("final restore stops the helper", host.Running == 0);
bool kakaoThrew = false;
try { KakaoSystem.Touch(); } catch (InvalidOperationException) { kakaoThrew = true; }
Check("kakao gate stays shut", kakaoThrew && !KakaoGate.RealAllowed && KakaoSystem.NativeCalls == 0 && KakaoSystem.ProcessStarts == 0 && KakaoSystem.WindowCalls == 0 && KakaoSystem.ProcessQueries == 0);
if (repo != null)
{
    string kakaoLogic = File.ReadAllText(Path.Combine(repo, "BongHub", "KakaoLogic.cs"));
    Check("kakao logic file does not call windows", !kakaoLogic.Contains("DllImport") && !kakaoLogic.Contains("Process.Start") && !kakaoLogic.Contains("GetProcessesByName") && !kakaoLogic.Contains("FindWindow") && !kakaoLogic.Contains("EnumWindows") && !kakaoLogic.Contains("ShowWindow") && !kakaoLogic.Contains("SetWindowPos"));
    string helperPath = Path.Combine(repo, "BongHub", "res", "BongAd.exe");
    byte[] helper = File.ReadAllBytes(helperPath);
    string hash;
    using (var sha = System.Security.Cryptography.SHA256.Create()) hash = Convert.ToHexString(sha.ComputeHash(helper));
    Check("helper bytes match 3.4.x", hash == KakaoFiles.Sha256 && helper.Length > 0);
    string tempHelper = Path.Combine(Path.GetTempPath(), "bhnext_kakao_" + Environment.ProcessId, "helper.bin");
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
    string dir = Path.Combine(Path.GetTempPath(), "bhnext_extras_" + Environment.ProcessId);
    try
    {
        Directory.CreateDirectory(dir);
        PresetExtras.StampFile = Path.Combine(dir, "desk.stamp");
        PresetExtras.WorkStampFile = Path.Combine(dir, "work.stamp");
        PresetExtras.WorkMutexName = "BongHubNextExtras" + Environment.ProcessId;
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
    string root = Path.Combine(Path.GetTempPath(), "bhnext_cloud_" + Environment.ProcessId);
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
