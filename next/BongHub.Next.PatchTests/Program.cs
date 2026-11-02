using System.Text;
using System.Text.Json.Nodes;
using BongHub.Next;

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

Console.WriteLine(fail == 0 ? "RESULT PASS" : "RESULT FAIL " + fail);
return fail == 0 ? 0 : 1;

static string? FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null)
    {
        if (File.Exists(Path.Combine(d.FullName, "data", "config.json")) && File.Exists(Path.Combine(d.FullName, "app", "BongHub.cs")))
            return d.FullName;
        d = d.Parent;
    }
    return null;
}
