using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace BongHub;

public sealed class CloudSettings
{
    public bool Enabled;
    public string Repo = "";
    public string Branch = "main";
    public int PullMinutes = 2;
}

public sealed class CloudState
{
    public DateTime LastSync = DateTime.MinValue;
    public List<string> Notes = new();
    public List<string> Layouts = new();
}

public sealed class CloudFile
{
    public byte[] Bytes = Array.Empty<byte>();
    public DateTime MtimeUtc;
}

/// <summary>Fetch and push of the sync tree. The memory implementation never opens a socket.</summary>
public interface ICloudTransport
{
    bool TryFetch(out Dictionary<string, CloudFile> files, out string error);
    bool TryPush(Dictionary<string, CloudFile> files, string message, out string error);
}

/// <summary>Unset <see cref="Allow"/> means blocked, so tests cannot start git or gh.</summary>
public static class CloudGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm() { if (Allow?.Invoke() == true) Armed = true; }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 GitHub에 연결하거나 git을 실행하지 않아요.");
    }
}

public static class CloudSystem
{
    public static int Attempts, NativeCalls, GitStarts, GhStarts, NetworkCalls;
    public static void Touch()
    {
        Attempts++;
        CloudGate.Demand();
        NativeCalls++;
    }
    public static void NoteGit()
    {
        CloudGate.Demand();
        GitStarts++;
    }
    public static void NoteGh()
    {
        CloudGate.Demand();
        GhStarts++;
    }
    public static void NoteNetwork()
    {
        CloudGate.Demand();
        NetworkCalls++;
    }
}

public sealed class MemoryCloud : ICloudTransport
{
    readonly object gate = new();
    Dictionary<string, CloudFile> files = new(StringComparer.Ordinal);
    int generation;
    int seen = -1;
    public bool Online = true;
    public List<string> Commits { get; } = new();
    public int Calls;

    public bool TryFetch(out Dictionary<string, CloudFile> copy, out string error)
    {
        lock (gate)
        {
            Calls++;
            copy = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
            if (!Online) { error = "Could not resolve host"; return false; }
            foreach (var kv in files) copy[kv.Key] = Clone(kv.Value);
            seen = generation;
            error = "";
            return true;
        }
    }

    public bool TryPush(Dictionary<string, CloudFile> next, string message, out string error)
    {
        lock (gate)
        {
            Calls++;
            if (!Online) { error = "Failed to connect"; return false; }
            if (seen != generation) { error = "fetch first"; return false; }
            files = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
            foreach (var kv in next) files[kv.Key] = Clone(kv.Value);
            generation++;
            seen = generation;
            Commits.Add(message ?? "");
            error = "";
            return true;
        }
    }

    public void Seed(Dictionary<string, CloudFile> next)
    {
        lock (gate)
        {
            files = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
            foreach (var kv in next) files[kv.Key] = Clone(kv.Value);
            generation++;
            seen = -1;
        }
    }

    public Dictionary<string, CloudFile> Snapshot()
    {
        lock (gate)
        {
            var copy = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
            foreach (var kv in files) copy[kv.Key] = Clone(kv.Value);
            return copy;
        }
    }

    static CloudFile Clone(CloudFile f) => new() { Bytes = f.Bytes.ToArray(), MtimeUtc = f.MtimeUtc };
}

public sealed class CloudWorkspace
{
    public string ConfigFile = "";
    public string NotesDir = "";
    public string DeskDir = "";
    public CloudState State = new();
    public string Device = "";
    public string AppVersion = "";
    public DateTime UtcNow = DateTime.UtcNow;
    public DateTime LocalNow = DateTime.Now;
    /// <summary>Additive cloud file. Empty leaves the 3.4.x tree unchanged.</summary>
    public string? ProfilesJson;
}

public static class CloudEngine
{
    public const int Schema = 1;
    public const string Readme = "# BongHub 클라우드 저장\n\nBongHub가 자동으로 관리하는 동기화 데이터예요. `data/config.json`(설정), `data/notes/`(스티커 메모), `data/desktop/`(바탕화면 아이콘 배치). 토큰이나 비밀번호는 저장하지 않아요.\n";

    public static string Run(CloudWorkspace ws, ICloudTransport transport, bool writer)
    {
        if (!writer) return "읽기 전용 · 다른 BongHub가 저장 중이에요";
        if (transport == null) return "동기화 실패: 연결 없음";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!transport.TryFetch(out var files, out string error)) return Fail(error);
            var next = Clone(files);
            try { Merge(ws, next); }
            catch (CloudSchemaException ex) { return ex.Message; }
            if (Same(files, next)) return "";
            string message = "sync: " + ws.Device + " " + ws.LocalNow.ToString("yyyy-MM-dd HH:mm:ss");
            if (transport.TryPush(next, message, out error)) return "";
            if (error.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) < 0 && error.IndexOf("fetch first", StringComparison.OrdinalIgnoreCase) < 0)
                return Fail(error);
        }
        return "다른 PC와 동시에 저장 중이에요. 잠시 뒤 다시 시도해요";
    }

    public static string Fail(string o)
    {
        o ??= "";
        if (o.IndexOf("resolve", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("시간 초과", StringComparison.Ordinal) >= 0 || o.IndexOf("Failed to connect", StringComparison.OrdinalIgnoreCase) >= 0)
            return "오프라인 · 연결되면 자동으로 보내요";
        if (o.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("403", StringComparison.Ordinal) >= 0 || o.IndexOf("could not read Username", StringComparison.OrdinalIgnoreCase) >= 0)
            return "연결 필요 · GitHub 연결을 눌러 로그인해 주세요";
        return "동기화 실패: " + (o.Length > 140 ? o.Substring(0, 140) : o);
    }

    static void Merge(CloudWorkspace ws, Dictionary<string, CloudFile> tree)
    {
        string metaPath = "data/meta.json";
        int remoteSchema = 0;
        string remoteMin = "";
        if (tree.TryGetValue(metaPath, out var meta))
        {
            try
            {
                var node = JsonNode.Parse(Text(meta.Bytes)) as JsonObject;
                if (node?["schema"] != null) remoteSchema = node["schema"]!.GetValue<int>();
                remoteMin = node?["minApp"]?.GetValue<string>() ?? "";
            }
            catch { remoteSchema = 0; }
        }
        if (remoteSchema > Schema)
            throw new CloudSchemaException("업데이트 필요 · 클라우드 데이터가 더 새 BongHub(" + (remoteMin.Length == 0 ? "새 버전" : remoteMin + " 이상") + ")로 저장돼 있어요");
        if (remoteSchema < Schema)
            Put(tree, metaPath, Utf8("{\"schema\":" + Schema + ",\"minApp\":" + Quote(ws.AppVersion) + "}"), ws.UtcNow);

        string configKey = "data/config.json";
        string stampKey = "data/config.stamp.json";
        DateTime localConfig = Mtime(ws.ConfigFile);
        DateTime remoteConfig = DateTime.MinValue;
        if (tree.ContainsKey(configKey))
        {
            var stamp = ReadStamp(tree, stampKey);
            remoteConfig = stamp ?? DateTime.MinValue;
        }
        bool first = ws.State.LastSync == DateTime.MinValue && tree.ContainsKey(configKey);
        if (tree.ContainsKey(configKey) && (first || remoteConfig > localConfig.AddSeconds(1)))
        {
            if (!File.Exists(ws.ConfigFile) || !BytesEqual(File.ReadAllBytes(ws.ConfigFile), tree[configKey].Bytes))
                WriteLocal(ws.ConfigFile, tree[configKey].Bytes, remoteConfig == DateTime.MinValue ? tree[configKey].MtimeUtc : remoteConfig);
        }
        else if (File.Exists(ws.ConfigFile) && localConfig > remoteConfig.AddSeconds(1))
        {
            Put(tree, configKey, File.ReadAllBytes(ws.ConfigFile), localConfig);
            Put(tree, stampKey, Utf8(StampJson(localConfig, ws.Device)), localConfig);
        }

        MergeNotes(ws, tree);
        MergeLayouts(ws, tree);

        Put(tree, "data/devices/" + SafeDevice(ws.Device) + ".json", Utf8("{\"device\":" + Quote(ws.Device) + ",\"app\":" + Quote(ws.AppVersion) + ",\"schema\":" + Schema + "}"), ws.UtcNow);
        if (!tree.ContainsKey("README.md")) Put(tree, "README.md", Utf8(Readme), ws.UtcNow);
        if (!string.IsNullOrEmpty(ws.ProfilesJson))
            Put(tree, "data/profiles.json", Utf8(ws.ProfilesJson), ws.UtcNow);
        ws.State.LastSync = ws.UtcNow;
    }

    static void MergeNotes(CloudWorkspace ws, Dictionary<string, CloudFile> tree)
    {
        var tomb = ReadTomb(tree);
        var local = Ids(ws.NotesDir);
        var remote = RemoteNoteIds(tree);
        foreach (var id in ws.State.Notes)
            if (!local.Contains(id) && remote.Contains(id)) tomb[id] = ws.UtcNow;
        foreach (var id in local.Union(remote).ToList())
        {
            bool dead = tomb.TryGetValue(id, out var deleted);
            DateTime lt = local.Contains(id) ? NoteTime(ws.NotesDir, id) : DateTime.MinValue;
            DateTime rt = remote.Contains(id) ? RemoteNoteTime(tree, id) : DateTime.MinValue;
            if (dead && deleted.ToUniversalTime().AddSeconds(1) >= lt && deleted.ToUniversalTime().AddSeconds(1) >= rt)
            {
                foreach (var ext in new[] { ".json", ".rtf" })
                {
                    tree.Remove("data/notes/" + id + ext);
                    string localFile = Path.Combine(ws.NotesDir, id + ext);
                    if (File.Exists(localFile)) try { File.Delete(localFile); } catch { }
                }
                continue;
            }
            if (dead) tomb.Remove(id);
            if (lt > rt.AddSeconds(1))
            {
                foreach (var ext in new[] { ".json", ".rtf" })
                {
                    string file = Path.Combine(ws.NotesDir, id + ext);
                    if (File.Exists(file)) Put(tree, "data/notes/" + id + ext, File.ReadAllBytes(file), lt);
                }
            }
            else if (rt > lt.AddSeconds(1))
            {
                Directory.CreateDirectory(ws.NotesDir);
                foreach (var ext in new[] { ".json", ".rtf" })
                {
                    string key = "data/notes/" + id + ext;
                    if (tree.TryGetValue(key, out var file)) WriteLocal(Path.Combine(ws.NotesDir, id + ext), file.Bytes, rt);
                }
            }
        }
        foreach (var key in tomb.Keys.ToList())
            if ((ws.UtcNow - tomb[key].ToUniversalTime()).TotalDays > 60) tomb.Remove(key);
        if (tomb.Count > 0) Put(tree, "data/notes/_deleted.json", Utf8(TombJson(tomb)), ws.UtcNow);
        else tree.Remove("data/notes/_deleted.json");
        ws.State.Notes = Ids(ws.NotesDir).ToList();
    }

    static void MergeLayouts(CloudWorkspace ws, Dictionary<string, CloudFile> tree)
    {
        string localFile = Path.Combine(ws.DeskDir, "layouts.json");
        var local = File.Exists(localFile) ? LayoutStore.Parse(File.ReadAllText(localFile, Encoding.UTF8)) : new List<DeskLayoutFile>();
        var remote = tree.TryGetValue("data/desktop/layouts.json", out var rf) ? LayoutStore.Parse(Text(rf.Bytes)) : new List<DeskLayoutFile>();
        var merged = LayoutMerge.Merge(local, remote, ws.State.Layouts);
        string mj = LayoutStore.Serialize(merged);
        if (merged.Count > 0 || remote.Count > 0)
        {
            var bytes = Utf8(mj);
            if (!tree.TryGetValue("data/desktop/layouts.json", out var cur) || !BytesEqual(cur.Bytes, bytes))
                Put(tree, "data/desktop/layouts.json", bytes, ws.UtcNow);
        }
        string localJson = LayoutStore.Serialize(local.OrderByDescending(x => x.Saved).ToList());
        if (localJson != mj && (merged.Count > 0 || local.Count > 0))
        {
            Directory.CreateDirectory(ws.DeskDir);
            File.WriteAllText(localFile, mj, new UTF8Encoding(false));
        }
        ws.State.Layouts = merged.Select(x => x.Id).ToList();
    }

    static HashSet<string> Ids(string dir)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return set;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith("_", StringComparison.Ordinal)) set.Add(name);
        }
        return set;
    }

    static HashSet<string> RemoteNoteIds(Dictionary<string, CloudFile> tree)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in tree.Keys)
        {
            if (!key.StartsWith("data/notes/", StringComparison.Ordinal) || !key.EndsWith(".json", StringComparison.Ordinal)) continue;
            string name = key.Substring("data/notes/".Length, key.Length - "data/notes/".Length - ".json".Length);
            if (name.Contains('/') || name.StartsWith("_", StringComparison.Ordinal)) continue;
            set.Add(name);
        }
        return set;
    }

    static DateTime NoteTime(string dir, string id)
    {
        var a = Mtime(Path.Combine(dir, id + ".json"));
        var b = Mtime(Path.Combine(dir, id + ".rtf"));
        return a > b ? a : b;
    }

    static DateTime RemoteNoteTime(Dictionary<string, CloudFile> tree, string id)
    {
        DateTime a = tree.TryGetValue("data/notes/" + id + ".json", out var j) ? j.MtimeUtc : DateTime.MinValue;
        DateTime b = tree.TryGetValue("data/notes/" + id + ".rtf", out var r) ? r.MtimeUtc : DateTime.MinValue;
        return a > b ? a : b;
    }

    static Dictionary<string, DateTime> ReadTomb(Dictionary<string, CloudFile> tree)
    {
        var tomb = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (!tree.TryGetValue("data/notes/_deleted.json", out var file)) return tomb;
        try
        {
            var node = JsonNode.Parse(Text(file.Bytes)) as JsonObject;
            if (node == null) return tomb;
            foreach (var kv in node)
            {
                long ticks = 0;
                if (kv.Value is JsonValue jv)
                {
                    if (jv.TryGetValue<string>(out var s)) long.TryParse(s, out ticks);
                    else if (jv.TryGetValue<long>(out var n)) ticks = n;
                }
                if (ticks > 0) tomb[kv.Key] = new DateTime(ticks, DateTimeKind.Utc);
            }
        }
        catch { }
        return tomb;
    }

    static string TombJson(Dictionary<string, DateTime> tomb)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var kv in tomb.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Quote(kv.Key)).Append(':').Append(Quote(kv.Value.ToUniversalTime().Ticks.ToString()));
        }
        sb.Append('}');
        return sb.ToString();
    }

    static DateTime? ReadStamp(Dictionary<string, CloudFile> tree, string key)
    {
        if (!tree.TryGetValue(key, out var file)) return null;
        string text = Text(file.Bytes);
        int at = text.IndexOf("Date(", StringComparison.Ordinal);
        if (at < 0) return null;
        int end = text.IndexOf(')', at);
        if (end < 0) return null;
        if (!long.TryParse(text.Substring(at + 5, end - (at + 5)), out long ms)) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }

    public static string StampJson(DateTime utc, string device)
    {
        long ms = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return "{\"T\":\"\\/Date(" + ms + ")\\/\",\"Device\":" + Quote(device) + "}";
    }

    static string SafeDevice(string device)
    {
        var sb = new StringBuilder();
        foreach (char c in device ?? "")
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_') sb.Append(c);
        return sb.Length == 0 ? "device" : sb.ToString();
    }

    static DateTime Mtime(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    static void WriteLocal(string path, byte[] bytes, DateTime mtime)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, bytes);
        if (mtime > DateTime.MinValue) try { File.SetLastWriteTimeUtc(path, DateTime.SpecifyKind(mtime, DateTimeKind.Utc)); } catch { }
    }

    static void Put(Dictionary<string, CloudFile> tree, string key, byte[] bytes, DateTime mtime)
        => tree[key] = new CloudFile { Bytes = bytes, MtimeUtc = DateTime.SpecifyKind(mtime, DateTimeKind.Utc) };

    static Dictionary<string, CloudFile> Clone(Dictionary<string, CloudFile> files)
    {
        var copy = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
        foreach (var kv in files) copy[kv.Key] = new CloudFile { Bytes = kv.Value.Bytes.ToArray(), MtimeUtc = kv.Value.MtimeUtc };
        return copy;
    }

    static bool Same(Dictionary<string, CloudFile> a, Dictionary<string, CloudFile> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !BytesEqual(kv.Value.Bytes, other.Bytes)) return false;
        }
        return true;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);
    static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s ?? "")
        {
            if (c == '\\' || c == '"') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}

public sealed class CloudSchemaException : Exception
{
    public CloudSchemaException(string message) : base(message) { }
}
