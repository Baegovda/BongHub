using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace BongHub.Next;

/// <summary>
/// Monitor preset planning. This file records commands and brightness targets.
/// The live switch and brightness calls live in <c>DisplayNative</c> and run only after
/// <see cref="DisplayGate.Demand"/> from a normal-mode click.
/// </summary>
public sealed class MonitorPreset
{
    public string Name = "";
    public string Hotkey = "";
    public List<string> Enable = new();
    public List<string> Disable = new();
    public string Primary = "";
    public string Layout = "";
    public List<BrightTarget> Brightness = new();
    public string DeskLayout = "";
    public string Workspace = "";
}

public sealed class BrightTarget
{
    public string Monitor = "";
    public int Value = -1;
}

public sealed class LinkedDesk
{
    public string Id = "";
    public string Name = "";
}

public sealed class DeskLinkResult
{
    public bool Ok;
    public string Error = "";
    public int Moved;
    public string Summary() => Ok ? "아이콘 " + Moved + "개 위치 복원" : Error;
}

public sealed class LinkedWork
{
    public string Id = "";
    public string Name = "";
}

/// <summary>One display command string. The fake records it. The real backend is not in this file.</summary>
public interface IDisplayBackend
{
    void Apply(IReadOnlyList<string> commands);
}

public interface IBrightBackend
{
    List<string> Monitors();
    bool Set(string id, int pct, out string how);
}

/// <summary>Unset <see cref="Allow"/> means blocked, so a test host that only compiles this file cannot change a display.</summary>
public static class DisplayGate
{
    public static bool Armed { get; private set; }
    public static Func<bool>? Allow;
    public static bool RealAllowed => Armed && Allow?.Invoke() == true;
    public static void Arm() { if (Allow?.Invoke() == true) Armed = true; }
    public static void Demand()
    {
        if (!RealAllowed) throw new InvalidOperationException("테스트 모드에서는 모니터와 밝기를 바꾸지 않아요.");
    }
}

/// <summary>
/// Counters for the real display path. A call that throws in <see cref="DisplayGate.Demand"/>
/// leaves the native counters at zero.
/// </summary>
public static class DisplaySystem
{
    public static int Attempts, NativeCalls, MmtStarts;
    public static int ChangeDisplaySettingsExCalls, SetDisplayConfigCalls;
    public static int BrightnessAttempts, BrightnessCalls, DdcCalls;

    public static void Touch()
    {
        Attempts++;
        DisplayGate.Demand();
        NativeCalls++;
    }

    public static void NoteMmt()
    {
        DisplayGate.Demand();
        MmtStarts++;
    }

    public static void NoteDdc()
    {
        DisplayGate.Demand();
        DdcCalls++;
    }
}

public static class DisplaySink
{
    public static Action<IReadOnlyList<string>>? Commit;
}

public delegate bool BrightSet(string id, int pct, out string how);

public static class BrightSink
{
    public static Func<List<string>>? Monitors;
    public static BrightSet? Set;
}

public static class PresetPlan
{
    /// <summary>Same order as 3.4.x: enable, load layout, primary, disable. Nothing is executed.</summary>
    public static List<string> Commands(MonitorPreset preset)
    {
        var cmds = new List<string>();
        if (preset.Enable != null)
            foreach (var id in preset.Enable)
                if (!string.IsNullOrEmpty(id)) cmds.Add("enable " + id);
        if (!string.IsNullOrEmpty(preset.Layout)) cmds.Add("load " + preset.Layout);
        if (!string.IsNullOrEmpty(preset.Primary)) cmds.Add("primary " + preset.Primary);
        if (preset.Disable != null && preset.Disable.Count > 0)
        {
            var ids = preset.Disable.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (ids.Count > 0) cmds.Add("disable " + string.Join(" ", ids));
        }
        return cmds;
    }
}

public sealed class FakeDisplay : IDisplayBackend
{
    public List<string> Log { get; } = new();
    public void Apply(IReadOnlyList<string> commands)
    {
        if (commands == null) return;
        Log.AddRange(commands);
    }
}

/// <summary>The real command sink. Constructing it does nothing. <see cref="Apply"/> demands the gate before any commit.</summary>
public sealed class RealDisplay : IDisplayBackend
{
    public void Apply(IReadOnlyList<string> commands)
    {
        DisplaySystem.Touch();
        if (DisplaySink.Commit == null) return;
        DisplaySink.Commit(commands ?? Array.Empty<string>());
    }
}

/// <summary>Brightness door. The dxva2 calls are not in this type.</summary>
public sealed class RealBright : IBrightBackend
{
    public List<string> Monitors()
    {
        DisplaySystem.BrightnessAttempts++;
        DisplayGate.Demand();
        if (BrightSink.Monitors == null) return new List<string>();
        DisplaySystem.BrightnessCalls++;
        return BrightSink.Monitors();
    }

    public bool Set(string id, int pct, out string how)
    {
        DisplaySystem.BrightnessAttempts++;
        DisplayGate.Demand();
        if (BrightSink.Set == null) { how = "DDC/CI 미지원"; return false; }
        DisplaySystem.BrightnessCalls++;
        return BrightSink.Set(id, pct, out how);
    }
}

public sealed class FakeBright : IBrightBackend
{
    public List<string> Mons = new();
    public Dictionary<string, int> Set_ = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unsupported = new(StringComparer.OrdinalIgnoreCase);
    public int Calls;
    public Func<int, List<string>>? Wake;
    public List<string> Monitors() => Wake != null ? Wake(Calls) : Mons;
    public bool Set(string id, int pct, out string how)
    {
        Calls++;
        if (Unsupported.Contains(id)) { how = "DDC/CI 미지원"; return false; }
        if (!Monitors().Contains(id)) { how = "모니터가 아직 켜지지 않음"; return false; }
        Set_[id] = pct;
        how = "mock";
        return true;
    }
}

public sealed class ThrowBright : IBrightBackend
{
    public List<string> Monitors() => new() { "A" };
    public bool Set(string id, int pct, out string how) => throw new InvalidOperationException("boom");
}

public static class PresetCodec
{
    public static MonitorPreset ReadFirst(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root?["Presets"] is JsonArray arr)
            foreach (var node in arr)
                if (node is JsonObject o) return FromObject(o);
        return FromObject(null);
    }

    public static MonitorPreset FromObject(JsonObject? o)
    {
        var p = new MonitorPreset();
        if (o == null) return p;
        p.Name = Str(o["Name"]);
        p.Hotkey = Str(o["Hotkey"]);
        p.Primary = Str(o["Primary"]);
        p.Layout = Str(o["Layout"]);
        p.DeskLayout = Str(o["DeskLayout"]);
        p.Workspace = Str(o["Workspace"]);
        Strings(o["Enable"], p.Enable);
        Strings(o["Disable"], p.Disable);
        if (o["Brightness"] is JsonArray bright)
            foreach (var node in bright)
            {
                if (node is not JsonObject b) continue;
                p.Brightness.Add(new BrightTarget { Monitor = Str(b["Monitor"]), Value = Clamp(b["Value"]) });
            }
        return p;
    }

    public static MonitorPreset Clone(MonitorPreset src)
    {
        var p = new MonitorPreset
        {
            Name = src.Name, Hotkey = src.Hotkey, Primary = src.Primary, Layout = src.Layout,
            DeskLayout = src.DeskLayout, Workspace = src.Workspace
        };
        p.Enable.AddRange(src.Enable);
        p.Disable.AddRange(src.Disable);
        foreach (var b in src.Brightness) p.Brightness.Add(new BrightTarget { Monitor = b.Monitor, Value = b.Value });
        return p;
    }

    static void Strings(JsonNode? node, List<string> into)
    {
        if (node is not JsonArray arr) return;
        foreach (var item in arr)
        {
            var s = Str(item);
            if (s.Length > 0) into.Add(s);
        }
    }

    static string Str(JsonNode? n)
    {
        if (n is JsonValue jv && jv.TryGetValue<string>(out string? s)) return s ?? "";
        return "";
    }

    static int Clamp(JsonNode? n)
    {
        if (n is not JsonValue jv) return -1;
        int v;
        if (jv.TryGetValue<int>(out int i)) v = i;
        else if (jv.TryGetValue<double>(out double d)) v = (int)d;
        else return -1;
        if (v < -1) v = -1;
        if (v > 100) v = 100;
        return v;
    }
}

/// <summary>
/// Brightness, linked icon layout, and linked workspace. Defaults never touch a monitor,
/// the desktop list view, or a process. Tests replace the delegates.
/// </summary>
public static class PresetExtras
{
    public static IBrightBackend Bright = new RealBright();
    public static Func<string, LinkedDesk?> FindDesk = _ => null;
    public static Func<LinkedDesk, DeskLinkResult> RestoreDesk = _ => new DeskLinkResult { Ok = false, Error = "아이콘 복원 실패" };
    public static Func<string> Signature = () => "unset";
    public static Action<int> Sleep = _ => { };
    public static int StableMs = 1500, StableMax = 8000, RetryDelay = 1500, Retries = 4;
    public static DateTime LinkedAt = DateTime.MinValue;
    public static string LastBright = "", LastDesk = "", LastWork = "";
    public static Func<string, LinkedWork?> FindWork = _ => null;
    public static Func<bool> WorkLaunchMissing = () => false;
    public static Func<LinkedWork, bool, string> LaunchWork = (_, _) => "";
    public static int WorkRuns, DeskRestores;
    public static string WorkMutexName = "BongHubNextLinkedWorkTest";
    public static string WorkStampFile = "";
    public static string StampFile = "";

    public static bool HasWork(MonitorPreset p)
        => (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0)) || !string.IsNullOrEmpty(p.DeskLayout) || !string.IsNullOrEmpty(p.Workspace);

    public static void WaitStable()
    {
        string last = Signature();
        int same = 0, waited = 0;
        while (same < StableMs && waited < StableMax)
        {
            Sleep(250);
            waited += 250;
            string s = Signature();
            if (s == last) same += 250;
            else { same = 0; last = s; }
        }
    }

    public static Dictionary<string, int> Targets(MonitorPreset p, IEnumerable<string> mons)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (p.Brightness == null) return d;
        var all = p.Brightness.FirstOrDefault(b => b.Monitor == "");
        foreach (var m in mons)
        {
            var one = p.Brightness.FirstOrDefault(b => string.Equals(b.Monitor, m, StringComparison.OrdinalIgnoreCase));
            int v = one != null ? one.Value : all != null ? all.Value : -1;
            if (v >= 0) d[m] = Math.Min(100, v);
        }
        foreach (var b in p.Brightness)
            if (b.Monitor != "" && b.Value >= 0 && !d.ContainsKey(b.Monitor) && (p.Disable == null || !p.Disable.Contains(b.Monitor)))
                d[b.Monitor] = Math.Min(100, b.Value);
        return d;
    }

    public static string BrightSummary(MonitorPreset p)
    {
        var l = (p.Brightness ?? new List<BrightTarget>()).Where(x => x.Value >= 0).OrderBy(x => x.Monitor, StringComparer.Ordinal)
            .Select(x => (x.Monitor == "" ? "전체" : x.Monitor) + " " + x.Value + "%").ToList();
        return l.Count == 0 ? "밝기는 바꾸지 않아요" : "적용: " + string.Join(", ", l);
    }

    public static void Run(MonitorPreset p, Action<string> status)
    {
        LastBright = ""; LastDesk = ""; LastWork = "";
        if (!HasWork(p)) return;
        try { status("화면 안정되기를 기다리는 중…"); WaitStable(); } catch { }
        try
        {
            if (p.Brightness != null && p.Brightness.Any(b => b.Value >= 0))
            {
                status("모니터 밝기 맞추는 중…");
                var todo = Targets(p, Bright.Monitors());
                var done = new List<string>();
                var fail = new Dictionary<string, string>();
                for (int a = 0; a < Retries && todo.Count > 0; a++)
                {
                    if (a > 0)
                    {
                        Sleep(RetryDelay);
                        foreach (var m in Targets(p, Bright.Monitors()))
                            if (!todo.ContainsKey(m.Key) && !done.Contains(m.Key)) todo[m.Key] = m.Value;
                    }
                    foreach (var t in todo.ToList())
                    {
                        string how;
                        bool ok = false;
                        try { ok = Bright.Set(t.Key, t.Value, out how); }
                        catch (Exception ex) { how = ex.Message; }
                        if (ok) { done.Add(t.Key + " " + t.Value + "% (" + how + ")"); todo.Remove(t.Key); fail.Remove(t.Key); }
                        else fail[t.Key] = how;
                    }
                }
                LastBright = (done.Count > 0 ? "밝기 " + string.Join(", ", done) : "")
                    + (fail.Count > 0 ? (done.Count > 0 ? " · " : "") + "실패: " + string.Join(", ", fail.Select(f => f.Key + "(" + f.Value + ")")) : "");
                if (fail.Count > 0) status("일부 모니터 밝기를 바꾸지 못했어요");
            }
        }
        catch (Exception ex) { LastBright = "밝기 실패: " + ex.Message; }
        try
        {
            if (!string.IsNullOrEmpty(p.DeskLayout))
            {
                var l = FindDesk(p.DeskLayout);
                if (l == null) LastDesk = "연계된 바탕화면 아이콘 배치가 삭제됐어요. 프리셋 편집에서 다시 골라 주세요.";
                else
                {
                    status("바탕화면 아이콘 배치 ‘" + l.Name + "’ 복원 중…");
                    Stamp();
                    DeskRestores++;
                    var r = RestoreDesk(l);
                    Stamp();
                    LastDesk = r == null ? "아이콘 복원 실패" : r.Ok ? "‘" + l.Name + "’ " + r.Summary() : "아이콘 복원 실패: " + r.Error;
                }
            }
        }
        catch (Exception ex) { LastDesk = "아이콘 복원 실패: " + ex.Message; }
        try
        {
            if (!string.IsNullOrEmpty(p.Workspace))
            {
                var w = FindWork(p.Workspace);
                if (w == null) LastWork = "연계된 작업 영역이 삭제됐어요. 프리셋 편집에서 다시 골라 주세요.";
                else RunWork(p, w, status);
            }
        }
        catch (Exception ex) { LastWork = "창 배치 실패: " + ex.Message; }
    }

    static void RunWork(MonitorPreset p, LinkedWork w, Action<string> status)
    {
        bool mine = false;
        using var mx = new Mutex(false, WorkMutexName);
        try { mine = mx.WaitOne(0); }
        catch (AbandonedMutexException) { mine = true; }
        try
        {
            if (!mine || RecentWork(p.Name + "|" + w.Id))
                LastWork = "작업 영역 ‘" + w.Name + "’은 다른 전환에서 이미 실행 중이라 건너뛰었어요";
            else
            {
                StampWork(p.Name + "|" + w.Id);
                status("창 배치 ‘" + w.Name + "’ 적용 중…");
                WorkRuns++;
                string r = LaunchWork(w, WorkLaunchMissing());
                LastWork = r ?? "";
                if (r != null && r.Contains("실패")) status("일부 창을 배치하지 못했어요");
            }
        }
        finally { if (mine) { try { mx.ReleaseMutex(); } catch { } } }
    }

    static bool RecentWork(string key)
    {
        try
        {
            if (WorkStampFile.Length == 0 || !File.Exists(WorkStampFile)) return false;
            return (DateTime.Now - File.GetLastWriteTime(WorkStampFile)).TotalSeconds < 10 && File.ReadAllText(WorkStampFile) == key;
        }
        catch { return false; }
    }

    static void StampWork(string key)
    {
        try { if (WorkStampFile.Length > 0) File.WriteAllText(WorkStampFile, key); } catch { }
    }

    static void Stamp()
    {
        LinkedAt = DateTime.Now;
        try { if (StampFile.Length > 0) File.WriteAllText(StampFile, DateTime.Now.ToString("o")); } catch { }
    }

    public static bool LinkedRecently
    {
        get
        {
            if ((DateTime.Now - LinkedAt).TotalSeconds < 20) return true;
            try { return StampFile.Length > 0 && File.Exists(StampFile) && (DateTime.Now - File.GetLastWriteTime(StampFile)).TotalSeconds < 20; }
            catch { return false; }
        }
    }
}
