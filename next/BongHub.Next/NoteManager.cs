using System.Runtime;
using System.Windows;
using System.Windows.Threading;

namespace BongHub.Next;

public static class NoteManager
{
    static readonly Dictionary<string, StickyNoteWindow> open = new();
    public static IReadOnlyCollection<StickyNoteWindow> Windows => open.Values;
    public static event Action? Changed;
    static double SysScale => Win32.SystemScale;

    public static bool IsOpen(string id) => open.ContainsKey(id);

    public static StickyNoteWindow? Open(string id, bool activate)
    {
        if (open.TryGetValue(id, out var w)) { if (activate && !AppEnv.Off) { w.Show(); w.Activate(); } return w; }
        var m = NoteStore.Find(id); if (m == null) return null;
        w = new StickyNoteWindow(m);
        open[id] = w;
        if (!m.Open) { m.Open = true; NoteStore.SaveMeta(m); }
        w.Show();
        if (activate && !AppEnv.Off) { w.Activate(); w.Editor.Focus(); }
        Changed?.Invoke();
        return w;
    }

    /// <summary>New note at the right of the work area under the cursor, cascading like 3.4.7.</summary>
    public static StickyNoteWindow? NewNote()
    {
        if (AppEnv.DenyWrite()) return null;
        double s = SysScale; int n = open.Count % 6;
        var a = Win32.WorkAreaAtCursor();
        int w = (int)(330 * s), h = (int)(330 * s);
        int x = a.Right - w - (int)(48 * s) - (int)(n * 30 * s), y = a.Top + (int)(60 * s) + (int)(n * 30 * s);
        var m = NoteStore.Create(ConfigStore.Notes.DefaultColor, x, y, w, h);
        return Open(m.Id, activate: true);
    }

    public static void OpenStartup()
    {
        if (!ConfigStore.Notes.OpenOnStartup) { AppEnv.NoteStartupNotes(0); ScheduleIdleTrim(); return; }
        var queue = new Queue<NoteMeta>(NoteStore.All.Where(m => m.Open).Reverse());
        int opened = 0;
        void next()
        {
            if (queue.Count == 0 || App.ShuttingDown) { AppEnv.NoteStartupNotes(opened); ScheduleIdleTrim(); return; }
            Open(queue.Dequeue().Id, activate: false);
            opened++;
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, next);
        }
        void start() => Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, next);
        // let the settings window reach its first frame first; notes follow one per Background tick (input/render stay responsive)
        var main = App.Instance.MainWin;
        if (main != null && main.IsVisible && !WindowFx.Of(main).Revealed) WindowFx.Of(main).AfterFirstFrame += start; else start();
    }

    static int trimScheduled;
    /// <summary>One compacting collection after startup, so discarded bitmaps are gone before the 20s idle sample. Runs after the first frame is recorded.</summary>
    static void ScheduleIdleTrim()
    {
        if (Interlocked.Exchange(ref trimScheduled, 1) != 0) return;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch (Exception e) { AppEnv.Log("trim: " + e.Message); }
        });
    }

    internal static void Forget(StickyNoteWindow w) { if (open.TryGetValue(w.Meta.Id, out var x) && x == w) { open.Remove(w.Meta.Id); Changed?.Invoke(); } }

    public static void FlushAll() { foreach (var w in open.Values.ToList()) { try { w.FlushAll(); } catch (Exception e) { AppEnv.Log("flush failed: " + e.Message); } } }
    public static void CloseAllWindows() { foreach (var w in open.Values.ToList()) w.Close(); open.Clear(); }
}
