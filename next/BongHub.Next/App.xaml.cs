using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace BongHub.Next;

public partial class App : Application
{
    public static App Instance => (App)Current;
    /// <summary>True only after a real exit was requested (tray 종료, --quit, session end). Window closes before that just hide.</summary>
    public static bool ShuttingDown { get; private set; }
    public const string MutexName = @"Local\BongHub.Next.SingleInstance";
    public const string QuitEventName = @"Local\BongHub.Next.Quit";
    public const string ShowEventName = @"Local\BongHub.Next.Show";
    Mutex? mutex; EventWaitHandle? quitEvt, showEvt; RegisteredWaitHandle? quitWait, showWait;
    TrayService? tray;
    public MainWindow? MainWin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppEnv.Init(e.Args);
        AppEnv.Mark("onstartup");
        DispatcherUnhandledException += (_, ex) => { AppEnv.Log("unhandled: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppEnv.Log("fatal: " + ex.ExceptionObject);

        if (AppEnv.Has("--quit"))
        {
            try { using var q = EventWaitHandle.OpenExisting(QuitEventName); q.Set(); } catch { }
            Shutdown(); return;
        }
        bool test = AppEnv.Smoke || AppEnv.NoteTest;
        if (!test)
        {
            mutex = new Mutex(true, MutexName, out bool first);
            if (!first)
            {
                try { using var s = EventWaitHandle.OpenExisting(ShowEventName); s.Set(); } catch { }
                Shutdown(); return;
            }
            quitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvt, (_, _) => Dispatcher.BeginInvoke(RequestShutdown), null, -1, false);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvt, (_, _) => Dispatcher.BeginInvoke(() => ShowSettings(null)), null, -1, false);
            AppEnv.ClaimWriterOrReadOnly();
        }

        NextSettings.Load();
        ConfigStore.Load();
        NoteStore.Load();
        AppEnv.Mark("data_loaded");

        if (AppEnv.Smoke) { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SmokeTest.Run(this)); return; }
        if (AppEnv.NoteTest) { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => NoteTest.Run(this)); return; }

        if (!AppEnv.Off) tray = new TrayService(this);   // never register a tray icon from a test/off-screen run
        SessionEnding += (_, _) => RequestShutdown();
        // settings window first; notes then open one per idle tick so the first frame is not held up by big RTFs
        if (!AppEnv.Has("--tray")) ShowSettings(null);
        NoteManager.OpenStartup();
    }

    public MainWindow EnsureMain() { if (MainWin == null) { AppEnv.Mark("main_ctor_start"); MainWin = new MainWindow(); AppEnv.Mark("main_ctor_done"); } return MainWin; }

    public void ShowSettings(string? page)
    {
        var w = EnsureMain();
        if (page != null) w.Navigate(page);
        bool first = !w.EverShown;
        if (!w.IsVisible) { w.Show(); if (first) AppEnv.Mark("show_returned"); if (!first) w.Reappear(); }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        if (!AppEnv.Off) w.Activate();
        if (first && AppEnv.OutDir != null)
            WindowFx.Of(w).AfterFirstFrame += () =>
            {
                var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                try { File.WriteAllText(Path.Combine(AppEnv.OutDir, "next_startup.txt"), $"first_frame_ms={ms:0}\r\nnotes_open_at_first_frame={NoteManager.Windows.Count}\r\nreadonly={AppEnv.ReadOnly}\r\nrender_tier={System.Windows.Media.RenderCapability.Tier >> 16}\r\nmanaged_heap_mb={GC.GetTotalMemory(false) / 1048576}\r\nphases: {AppEnv.Marks}\r\n"); } catch { }
            };
    }

    public void RequestShutdown()
    {
        if (ShuttingDown) return;
        ShuttingDown = true;
        NoteManager.FlushAll();
        tray?.Dispose(); tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!ShuttingDown) { ShuttingDown = true; try { NoteManager.FlushAll(); } catch { } }
        tray?.Dispose();
        AppEnv.ReleaseWriter();
        quitWait?.Unregister(null); showWait?.Unregister(null);
        quitEvt?.Dispose(); showEvt?.Dispose();
        try { mutex?.ReleaseMutex(); } catch { }
        mutex?.Dispose();
        base.OnExit(e);
    }
}
