using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BongHub;

/// <summary>One desktop icon. Field order matches 3.4.x <c>DeskItem</c>.</summary>
public sealed class DeskIcon
{
    [JsonPropertyOrder(0)] public string Name { get; set; } = "";
    [JsonPropertyOrder(1)] public int X { get; set; }
    [JsonPropertyOrder(2)] public int Y { get; set; }
}

/// <summary>One saved arrangement. Field order matches 3.4.x <c>DeskLayout</c>.</summary>
public sealed class DeskLayoutFile
{
    [JsonPropertyOrder(0)] public string Id { get; set; } = "";
    [JsonPropertyOrder(1)] public string Name { get; set; } = "";
    [JsonPropertyOrder(2)] public string Key { get; set; } = "";
    [JsonPropertyOrder(3)] public string Desc { get; set; } = "";
    [JsonPropertyOrder(4)] public bool Auto { get; set; }
    [JsonPropertyOrder(5)] [JsonConverter(typeof(MsDateConverter))] public DateTime Saved { get; set; }
    [JsonPropertyOrder(6)] public List<DeskIcon> Items { get; set; } = new();
}

/// <summary>Reads/writes the JavaScriptSerializer date form "\/Date(ms)\/" so 3.4.x keeps reading our files.</summary>
public sealed class MsDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var s = reader.GetString() ?? "";
        if (s.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var body = s.Substring(6, s.IndexOf(')') - 6); int cut = body.IndexOfAny(['+', '-'], 1); if (cut > 0) body = body[..cut];
            return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(body)).LocalDateTime;
        }
        return DateTime.TryParse(s, out var d) ? d : DateTime.Now;
    }
    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o)
    {
        long ms = new DateTimeOffset(v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Local) : v).ToUnixTimeMilliseconds();
        w.WriteRawValue("\"\\/Date(" + ms + ")\\/\"", skipInputValidation: true);
    }
}

public sealed class DeskResult
{
    public bool Ok;
    public string Error = "";
    public int Moved, Same, Missing, Extra;
    public string Summary() => Ok
        ? "아이콘 " + (Moved + Same) + "개 위치 복원" + (Missing > 0 ? " · 바탕화면에 없는 아이콘 " + Missing + "개 건너뜀" : "") + (Extra > 0 ? " · 저장 이후 새로 생긴 아이콘 " + Extra + "개는 그대로" : "")
        : Error;
}

public readonly record struct MonitorSpec(int W, int H, int X, int Y, bool Primary);

/// <summary>
/// The only door to the real desktop list view. Tests, <c>--off</c>, and CI never arm it.
/// Arming still does nothing until a save or restore click constructs <see cref="ExplorerIconSurface"/>.
/// </summary>
public static class DesktopGate
{
    public static bool Armed { get; private set; }
    /// <summary>The app sets this from <c>AppEnv</c>. Unset means blocked, so a test host that only compiles this file cannot open the desktop.</summary>
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm()
    {
        if (Allow?.Invoke() != true) return;
        Armed = true;
    }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 바탕화면 아이콘을 읽거나 옮기지 않아요.");
    }
}

public static class ScreenMap
{
    public static Func<IReadOnlyList<MonitorSpec>>? Source;
    /// <summary>Normal app points this at the monitor enumeration. It is not the desktop icon list view, and tests leave it unset.</summary>
    public static Func<IReadOnlyList<MonitorSpec>>? Live;

    public static IReadOnlyList<MonitorSpec> Current()
    {
        if (Source != null) return Source();
        if (!DesktopGate.RealAllowed || Live == null) return Array.Empty<MonitorSpec>();
        return Live();
    }

    public static string Key(IEnumerable<MonitorSpec> monitors)
    {
        var parts = new List<string>();
        foreach (var m in monitors) parts.Add(m.W + "x" + m.H + "@" + m.X + "," + m.Y + (m.Primary ? "*" : ""));
        parts.Sort(StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    public static string Describe(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "알 수 없음";
        var parts = key.Split(';');
        var sizes = new List<string>();
        string? prim = null;
        foreach (var p in parts)
        {
            string sz = p.Split('@')[0].Replace("x", "×");
            if (p.EndsWith("*")) prim = sz;
            else sizes.Add(sz);
        }
        if (prim != null) sizes.Insert(0, prim + "(주)");
        return "모니터 " + parts.Length + "대 · " + string.Join(" + ", sizes);
    }
}

public interface IIconSurface : IDisposable
{
    IReadOnlyList<DeskIcon> Read();
    void Move(int index, int x, int y);
    void Refresh();
    bool AutoArrange { get; }
}

/// <summary>In-memory icons. Tests and the smoke log use this. It has no window and no process handle.</summary>
public sealed class MemoryIconSurface : IIconSurface
{
    public List<DeskIcon> Icons { get; } = new();
    public bool AutoArrange { get; set; }
    public int Moves { get; private set; }
    public int Refreshes { get; private set; }
    public IReadOnlyList<DeskIcon> Read() => Icons.Select(i => new DeskIcon { Name = i.Name, X = i.X, Y = i.Y }).ToList();
    public void Move(int index, int x, int y) { Moves++; Icons[index].X = x; Icons[index].Y = y; }
    public void Refresh() { Refreshes++; }
    public void Dispose() { }
}

public static class IconMatch
{
    public static DeskResult Apply(IIconSurface surface, IReadOnlyList<DeskIcon> target)
    {
        var res = new DeskResult();
        if (surface.AutoArrange)
        {
            res.Error = "‘아이콘 자동 정렬’이 켜져 있어서 위치를 바꿀 수 없어요. 바탕화면 아이콘 탭에서 끌 수 있어요.";
            return res;
        }
        var cur = surface.Read();
        var want = new Dictionary<string, Queue<DeskIcon>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in target)
        {
            if (!want.TryGetValue(t.Name, out var q)) want[t.Name] = q = new Queue<DeskIcon>();
            q.Enqueue(t);
        }
        for (int i = 0; i < cur.Count; i++)
        {
            if (!want.TryGetValue(cur[i].Name, out var q) || q.Count == 0) { res.Extra++; continue; }
            var t = q.Dequeue();
            if (t.X == cur[i].X && t.Y == cur[i].Y) { res.Same++; continue; }
            surface.Move(i, t.X, t.Y);
            res.Moved++;
        }
        res.Missing = want.Values.Sum(q => q.Count);
        if (res.Moved > 0) surface.Refresh();
        res.Ok = true;
        return res;
    }
}

public static class LayoutMerge
{
    /// <summary>Same rule as 3.4.x cloud merge: per id, the newer <c>Saved</c> wins. Ids the local side dropped stay dropped.</summary>
    public static List<DeskLayoutFile> Merge(IEnumerable<DeskLayoutFile> local, IEnumerable<DeskLayoutFile> remote, IEnumerable<string> previouslySyncedIds)
    {
        var here = local.ToList();
        var deleted = new HashSet<string>(previouslySyncedIds.Where(id => here.All(x => x.Id != id)));
        var map = new Dictionary<string, DeskLayoutFile>();
        foreach (var x in remote)
            if (!string.IsNullOrEmpty(x.Id) && !deleted.Contains(x.Id)) map[x.Id] = x;
        foreach (var x in here)
        {
            if (string.IsNullOrEmpty(x.Id)) continue;
            if (!map.TryGetValue(x.Id, out var y) || x.Saved >= y.Saved) map[x.Id] = x;
        }
        return map.Values.OrderByDescending(x => x.Saved).ToList();
    }
}

public sealed class LayoutStore
{
    static readonly JsonSerializerOptions Js = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    public string FilePath { get; }
    public LayoutStore(string folder) { FilePath = Path.Combine(folder, "layouts.json"); }
    /// <summary>The app points this at <c>AppEnv.DataDir</c>. This file does not reference that type, so the patch tests can compile it alone.</summary>
    public static Func<string>? DataFolder;
    public static LayoutStore Default => new(Path.Combine(DataFolder?.Invoke() ?? Path.GetTempPath(), "desktop"));

    public List<DeskLayoutFile> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return Parse(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch { return new(); }
    }

    public void Write(List<DeskLayoutFile> all)
    {
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var enc = new UTF8Encoding(false);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, Serialize(all), enc);
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
        else File.Move(tmp, FilePath);
    }

    public static string Serialize(IReadOnlyList<DeskLayoutFile> all) => JsonSerializer.Serialize(all, Js);

    public static List<DeskLayoutFile> Parse(string json)
    {
        var list = JsonSerializer.Deserialize<List<DeskLayoutFile>>(json, Js) ?? new();
        foreach (var x in list)
        {
            x.Name ??= "";
            x.Key ??= "";
            x.Desc ??= ScreenMap.Describe(x.Key);
            x.Items ??= new();
            if (string.IsNullOrEmpty(x.Id)) x.Id = Guid.NewGuid().ToString("N");
            foreach (var item in x.Items) item.Name ??= "";
        }
        return list.Where(x => x.Items != null).ToList();
    }
}

public static class IconLibrary
{
    public static bool Same(IReadOnlyList<DeskIcon> a, IReadOnlyList<DeskIcon> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Name != b[i].Name || a[i].X != b[i].X || a[i].Y != b[i].Y) return false;
        return true;
    }

    public static DeskLayoutFile? SaveNew(LayoutStore store, IIconSurface surface, string name, string key, out string error)
    {
        error = "";
        IReadOnlyList<DeskIcon> items;
        try { items = surface.Read(); }
        catch (Exception e) { error = e.Message; return null; }
        var layout = new DeskLayoutFile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Key = key,
            Desc = ScreenMap.Describe(key),
            Auto = false,
            Saved = DateTime.Now,
            Items = items.Select(Clone).ToList()
        };
        var all = store.Load();
        all.Insert(0, layout);
        store.Write(all);
        return layout;
    }

    public static bool SaveAuto(LayoutStore store, IReadOnlyList<DeskIcon> items, string key)
    {
        if (items.Count == 0) return false;
        var all = store.Load();
        var old = all.FirstOrDefault(x => x.Auto && x.Key == key);
        if (old != null && Same(old.Items, items)) return false;
        if (old != null) all.Remove(old);
        all.Insert(0, new DeskLayoutFile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "자동 저장",
            Key = key,
            Desc = ScreenMap.Describe(key),
            Auto = true,
            Saved = DateTime.Now,
            Items = items.Select(Clone).ToList()
        });
        store.Write(all);
        return true;
    }

    public static DeskLayoutFile? Best(LayoutStore store, string key) =>
        store.Load().Where(a => a.Key == key).OrderByDescending(a => a.Saved).FirstOrDefault();

    public static DeskResult Restore(LayoutStore store, IIconSurface surface, string key)
    {
        var layout = Best(store, key);
        if (layout == null) return new DeskResult { Error = "현재 화면 구성(" + ScreenMap.Describe(key) + ")으로 저장된 배치가 없어요" };
        return IconMatch.Apply(surface, layout.Items);
    }

    public static void Rename(LayoutStore store, string id, string name)
    {
        var all = store.Load();
        var x = all.FirstOrDefault(a => a.Id == id);
        if (x == null) return;
        x.Name = name;
        x.Auto = false;
        store.Write(all);
    }

    public static void Delete(LayoutStore store, string id)
    {
        var all = store.Load();
        all.RemoveAll(a => a.Id == id);
        store.Write(all);
    }

    static DeskIcon Clone(DeskIcon i) => new() { Name = i.Name, X = i.X, Y = i.Y };
}

/// <summary>
/// The real Progman/WorkerW SysListView32. Constructing it is the explicit user action.
/// The constructor counts a native call only after <see cref="DesktopGate.Demand"/> succeeds.
/// </summary>
public sealed class ExplorerIconSurface : IIconSurface
{
    public static int Attempts;
    public static int NativeCalls;
    IntPtr lv, proc, mem;

    public ExplorerIconSurface()
    {
        Attempts++;
        DesktopGate.Demand();
        NativeCalls++;
        lv = FindListView();
        if (lv == IntPtr.Zero) throw new InvalidOperationException("바탕화면 아이콘 창을 찾지 못했어요 (탐색기가 실행 중인지 확인해 주세요)");
        OpenRemote();
    }

    public bool AutoArrange => lv != IntPtr.Zero && (GetWindowLongW(lv, -16) & 0x0100) != 0;

    public IReadOnlyList<DeskIcon> Read()
    {
        var res = new List<DeskIcon>();
        int n = (int)Send(lv, 0x1004, 0, 0);
        bool x64 = IntPtr.Size == 8;
        const int itemOff = 64, textOff = 512;
        for (int i = 0; i < n; i++)
        {
            Send(lv, 0x1010, i, mem.ToInt64());
            var pt = ReadMem(0, 8);
            var it = new byte[96];
            BitConverter.GetBytes(1u).CopyTo(it, 0);
            BitConverter.GetBytes(i).CopyTo(it, 4);
            long txt = mem.ToInt64() + textOff;
            if (x64) { BitConverter.GetBytes(txt).CopyTo(it, 24); BitConverter.GetBytes(260).CopyTo(it, 32); }
            else { BitConverter.GetBytes((int)txt).CopyTo(it, 20); BitConverter.GetBytes(260).CopyTo(it, 24); }
            WriteMem(itemOff, it);
            WriteMem(textOff, new byte[4]);
            int len = (int)Send(lv, 0x1073, i, mem.ToInt64() + itemOff);
            string name = len > 0 ? Encoding.Unicode.GetString(ReadMem(textOff, Math.Min(len, 259) * 2)) : "";
            res.Add(new DeskIcon { Name = name, X = BitConverter.ToInt32(pt, 0), Y = BitConverter.ToInt32(pt, 4) });
        }
        return res;
    }

    public void Move(int index, int x, int y)
    {
        var b = new byte[8];
        BitConverter.GetBytes(x).CopyTo(b, 0);
        BitConverter.GetBytes(y).CopyTo(b, 4);
        WriteMem(0, b);
        Send(lv, 0x1031, index, mem.ToInt64());
    }

    public void Refresh()
    {
        int n = (int)Send(lv, 0x1004, 0, 0);
        if (n > 0) PostMessage(lv, 0x1015, IntPtr.Zero, new IntPtr(n - 1));
    }

    public void Dispose()
    {
        if (mem != IntPtr.Zero) VirtualFreeEx(proc, mem, UIntPtr.Zero, 0x8000);
        if (proc != IntPtr.Zero) CloseHandle(proc);
        mem = proc = IntPtr.Zero;
    }

    void OpenRemote()
    {
        uint pid;
        GetWindowThreadProcessId(lv, out pid);
        proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
        if (proc == IntPtr.Zero) throw new InvalidOperationException("탐색기 프로세스에 접근할 수 없어요");
        bool w1, w2;
        IsWow64Process(proc, out w1);
        IsWow64Process(GetCurrentProcess(), out w2);
        if (w1 != w2) { CloseHandle(proc); proc = IntPtr.Zero; throw new InvalidOperationException("탐색기와 BongHub의 비트 수가 달라요"); }
        mem = VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x04);
        if (mem == IntPtr.Zero) { CloseHandle(proc); proc = IntPtr.Zero; throw new InvalidOperationException("메모리를 할당할 수 없어요"); }
    }

    byte[] ReadMem(int off, int n)
    {
        var b = new byte[n];
        ReadProcessMemory(proc, IntPtr.Add(mem, off), b, (UIntPtr)n, out _);
        return b;
    }

    void WriteMem(int off, byte[] b) { WriteProcessMemory(proc, IntPtr.Add(mem, off), b, (UIntPtr)b.Length, out _); }

    static long Send(IntPtr h, uint msg, long w, long l)
    {
        SendMessageTimeout(h, msg, new IntPtr(w), new IntPtr(l), 2, 2000, out var r);
        return r.ToInt64();
    }

    static IntPtr FindListView()
    {
        IntPtr def = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (def == IntPtr.Zero)
        {
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                def = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (def != IntPtr.Zero) break;
            }
        }
        return def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLongW(IntPtr h, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr p, out bool wow);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, UIntPtr size, uint type, uint prot);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr written);
}
