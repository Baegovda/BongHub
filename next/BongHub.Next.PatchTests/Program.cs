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
