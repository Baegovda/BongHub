using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
    public TrayService? Tray => tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Before any HWND. The first hardware window pulls in D3D9On12 (descriptor heaps and, on Intel, a driver allocation counted in this process). That is the settings-window jump past the tray; the managed heap stays a few MB. Software raster keeps the same tree, fonts, and note colors.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        AppEnv.RenderModeLabel = "software";
        base.OnStartup(e);
        AppEnv.Init(e.Args);
        LayoutStore.DataFolder = () => AppEnv.DataDir;
        DesktopGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden;
        WindowGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden;
        PtGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden;
        DisplayGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden;
        KakaoGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden;
        CloudGate.Allow = () => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && !AppEnv.IdleHidden && !AppEnv.ReadOnly;
        ScreenMap.Live = () => Win32.Monitors().Select(m => new MonitorSpec(m.W, m.H, m.X, m.Y, m.Primary)).ToList();
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
            // Off-screen runs (--off, --idle-hidden, the memory baseline) must not signal a running Next to show its window.
            // They still take the notes writer lock when the data folder is the real one. Temp --data does not.
            if (!AppEnv.Off)
            {
                mutex = new Mutex(true, MutexName, out bool first);
                if (!first)
                {
                    try { using var s = EventWaitHandle.OpenExisting(ShowEventName); s.Set(); } catch { }
                    Shutdown(); return;
                }
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                showWait = ThreadPool.RegisterWaitForSingleObject(showEvt, (_, _) => Dispatcher.BeginInvoke(() => ShowSettings(null)), null, -1, false);
            }
            quitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvt, (_, _) => Dispatcher.BeginInvoke(RequestShutdown), null, -1, false);
            AppEnv.ClaimWriterOrReadOnly();
        }

        NextSettings.Load();
        ConfigStore.Load();
        NoteStore.Load();
        AppEnv.Mark("data_loaded");
        AppEnv.MarkMemory("data_loaded");

        if (AppEnv.Smoke) { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SmokeTest.Run(this)); return; }
        if (AppEnv.NoteTest) { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => NoteTest.Run(this)); return; }

        // Tests never register a shell icon. --off alone is the settings-window baseline (no tray).
        // --tray --off is the tray-only baseline and does register the icon.
        if (!AppEnv.Off || AppEnv.Has("--tray")) tray = new TrayService(this, registerShell: true);
        // --off (tests and the memory baseline) must not register a global hotkey or create the message window.
        // Arming only allows a later save/restore click to open the desktop list view. It does not open it now.
        if (!AppEnv.Off)
        {
            NoteHotkey.Start(tray);
            DesktopGate.Arm(); WindowGate.Arm(); PtGate.Arm(); DisplayGate.Arm(); DisplayNative.Install();
            KakaoGate.Arm();
            KakaoNative.Install();
            if (ConfigStore.KakaoAdBlock && ConfigStore.KakaoAutoApply)
            {
                try { KakaoNative.Sync(ConfigStore.Kakao()); } catch { }
            }
            CloudGate.Arm();
            if (CloudGate.RealAllowed && ConfigStore.CloudEnabled) StartCloud();
        }
        SessionEnding += (_, _) => RequestShutdown();
        // settings window first; notes then open one per idle tick so the first frame is not held up by big RTFs
        if (AppEnv.IdleHidden) ShowThenHide();
        else if (!AppEnv.Has("--tray")) ShowSettings(null);
        NoteManager.OpenStartup();
    }

    void ShowThenHide()
    {
        ShowSettings(null);
        var w = MainWin;
        if (w == null) return;
        void HideAfter()
        {
            if (!ShuttingDown && w.IsVisible) w.Close();
            AppEnv.NoteSettingsHidden();
        }
        var st = WindowFx.Of(w);
        if (st.Revealed) HideAfter();
        else st.AfterFirstFrame += HideAfter;
    }

    public MainWindow EnsureMain()
    {
        if (MainWin == null)
        {
            AppEnv.Mark("main_ctor_start");
            MainWin = new MainWindow();
            AppEnv.NoteMainWindowCreated();
            AppEnv.Mark("main_ctor_done");
        }
        return MainWin;
    }

    public void ShowSettings(string? page)
    {
        var w = EnsureMain();
        if (page != null) w.Navigate(page);
        bool first = !w.EverShown;
        if (first)
        {
            WindowFx.Of(w).AfterFirstFrame += () =>
            {
                int tier = 0;
                RenderMode? hwndRender = null;
                try { tier = RenderCapability.Tier >> 16; } catch { }
                try { hwndRender = HwndSource.FromHwnd(Win32.Handle(w))?.CompositionTarget?.RenderMode; } catch { }
                AppEnv.NoteFirstFrame(NoteManager.Windows.Count, tier, GC.GetTotalMemory(false) / 1048576, hwndRender?.ToString() ?? "null");
                AppEnv.MarkMemory("first_frame", NoteManager.EditingCount);
            };
            // After the timestamp, so the radial-glow raster is not inside first_frame_ms.
            WindowFx.Of(w).AfterFirstFrame += w.ShowDecorations;
        }
        if (!w.IsVisible) { AppEnv.Mark("show_begin"); w.Show(); if (first) AppEnv.Mark("show_returned"); if (!first) w.Reappear(); }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        if (!AppEnv.Off) w.Activate();
    }

    Timer? cloudTimer;
    void StartCloud()
    {
        int minutes = Math.Max(1, ConfigStore.CloudPullMinutes);
        cloudTimer = new Timer(_ =>
        {
            try
            {
                if (!CloudGate.RealAllowed || !ConfigStore.CloudEnabled) return;
                var transport = new GitCloudTransport(ConfigStore.CloudRepo, ConfigStore.CloudBranch, Environment.MachineName);
                var ws = new CloudWorkspace
                {
                    ConfigFile = AppEnv.ConfigPath,
                    NotesDir = AppEnv.NotesDir,
                    DeskDir = Path.Combine(AppEnv.DataDir, "desktop"),
                    Device = Environment.MachineName,
                    AppVersion = AppEnv.Version,
                    UtcNow = DateTime.UtcNow,
                    LocalNow = DateTime.Now
                };
                CloudEngine.Run(ws, transport, writer: true);
            }
            catch { }
        }, null, 5000, minutes * 60000);
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
        try { NoteHotkey.Stop(); } catch { }
        try { if (KakaoNative.Launched) KakaoNative.Restore(); } catch { }
        try { cloudTimer?.Dispose(); } catch { }
        tray?.Dispose();
        AppEnv.ReleaseWriter();
        quitWait?.Unregister(null); showWait?.Unregister(null);
        quitEvt?.Dispose(); showEvt?.Dispose();
        try { mutex?.ReleaseMutex(); } catch { }
        mutex?.Dispose();
        base.OnExit(e);
    }
}
