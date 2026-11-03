using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BongHub;

public partial class App : Application
{
    static App()
    {
        // Before any HWND. A later AirDock window must not start the tablet thread:
        // shutting the dispatcher down calls ReleaseCapture, and that waits forever on PenThreadWorker.
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", true);
    }

    public static App Instance => (App)Current;
    /// <summary>True only after a real exit was requested (tray 종료, --quit, session end). Window closes before that just hide.</summary>
    public static bool ShuttingDown { get; private set; }
    public const string MutexName = @"Local\BongHub.SingleInstance";
    public const string QuitEventName = @"Local\BongHub.Quit";
    public const string ShowEventName = @"Local\BongHub.Show";
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
        ScreenCatalog.Live = Win32.DisplayIdentities;
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
            // Off-screen runs (--off, --idle-hidden, the memory baseline) must not signal a running BongHub to show its window.
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
                showWait = ThreadPool.RegisterWaitForSingleObject(showEvt, (_, _) => Dispatcher.BeginInvoke(() => ShowSettings(null, user: true)), null, -1, false);
            }
            quitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvt, (_, _) => Dispatcher.BeginInvoke(RequestShutdown), null, -1, false);
            AppEnv.ClaimWriterOrReadOnly();
        }

        AppSettings.Load();
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
            try
            {
                string path = System.IO.Path.Combine(AppEnv.DataDir, "profiles.json");
                string? json = File.Exists(path) ? File.ReadAllText(path) : null;
                ProfileHotkey.Bind(ProfileDoc.Migrate(ConfigStore.Presets, ConfigStore.WindowRules, json, null), ConfigStore.Notes.Hotkey);
                AirDockLaunch.AllowNative = true;
                AirDockHotkey.Bind();
            }
            catch { }
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

    /// <param name="user">Tray, second instance, or an in-app menu. Passive startup and <c>--off</c> pass false and must not steal the foreground.</param>
    public void ShowSettings(string? page, bool user = false)
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
        bool bring = user && !AppEnv.Off;
        // Captured before Show. Under --off the window keeps WS_EX_NOACTIVATE and is never activated.
        IntPtr previous = bring ? IntPtr.Zero : Win32.GetForegroundWindow();
        if (!bring && AppEnv.Off)
        {
            w.ShowActivated = false;
            Win32.KeepInactive(new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle());
        }
        if (first && !bring)
            WindowFx.Of(w).AfterFirstFrame += () => PassiveKeep(w, previous);
        if (!w.IsVisible) { AppEnv.Mark("show_begin"); w.Show(); if (first) AppEnv.Mark("show_returned"); if (!first) w.Reappear(); }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        var hwnd = Win32.Handle(w);
        if (bring)
        {
            Win32.BringToForeground(hwnd);
            KeepBringing(w);
        }
        else
        {
            // Reopen after hide is not the first frame, but Show can still activate. Same give-back retry.
            Win32.ResetPassive(previous);
            Win32.ReleaseIfStole(hwnd, previous);
            PassiveKeep(w, previous);
        }
    }

    int passiveGen;
    /// <summary>The shell activates the first window when the cloak drops. Give that focus back a few times.</summary>
    void PassiveKeep(Window w, IntPtr previous)
    {
        int gen = ++passiveGen;
        Win32.ResetPassive(previous);
        Win32.ReleaseIfStole(Win32.Handle(w), previous);
        int n = 0;
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            if (gen != passiveGen) { timer.Stop(); return; }
            n++;
            var hwnd = Win32.Handle(w);
            if (AppEnv.Off) Win32.KeepInactive(hwnd);
            bool foreground = hwnd != IntPtr.Zero && Win32.GetForegroundWindow() == hwnd;
            if (foreground) Win32.ReleaseIfStole(hwnd, previous);
            Win32.NotePassive(n, clock.ElapsedMilliseconds, foreground);
            if (n >= 8) timer.Stop();
        };
        timer.Start();
    }

    int bringGen;
    /// <summary>A user open can lose the foreground race to the app that was in front. Try again briefly.</summary>
    void KeepBringing(Window w)
    {
        int gen = ++bringGen;
        int n = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            if (gen != bringGen) { timer.Stop(); return; }
            n++;
            var hwnd = Win32.Handle(w);
            bool ours = hwnd != IntPtr.Zero && Win32.GetForegroundWindow() == hwnd && w.IsActive;
            if (!ours) Win32.BringToForeground(hwnd);
            if (n >= 6 || ours) timer.Stop();
        };
        timer.Start();
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
                ProfileCloud.Attach(ws, Path.Combine(AppEnv.DataDir, "profiles.json"));
                CloudEngine.Run(ws, transport, writer: true);
            }
            catch { }
        }, null, 5000, minutes * 60000);
    }

    public void RequestShutdown() => RequestShutdown(Environment.ExitCode);

    public void RequestShutdown(int exitCode)
    {
        if (ShuttingDown) return;
        ShuttingDown = true;
        Environment.ExitCode = exitCode;
        ReleasePointer();
        // Tray 종료 and --quit both land here. Flush uses the image-guard path, including a pending sidecar.
        NoteManager.FlushAll();
        tray?.Dispose(); tray = null;
        Shutdown(exitCode);
    }

    /// <summary>
    /// Smoke already wrote its log. Close every dock, drop mouse capture, then leave.
    /// <see cref="Shutdown"/> disposes mouse providers and can deadlock in <c>PenThreadWorker.WorkerGetTabletsInfo</c>
    /// while a window still holds capture.
    /// </summary>
    public void ExitSmoke(int exitCode)
    {
        if (ShuttingDown) return;
        ShuttingDown = true;
        Environment.ExitCode = exitCode;
        ReleasePointer();
        try { NoteManager.FlushAll(); } catch { }
        try { NoteHotkey.Stop(); } catch { }
        try { ProfileHotkey.Stop(); } catch { }
        try { AirDockHotkey.Stop(); } catch { }
        try { tray?.Dispose(); } catch { }
        tray = null;
        AppEnv.ReleaseWriter();
        Environment.Exit(exitCode);
    }

    static void ReleasePointer()
    {
        try { AirDockWindow.ShutdownForExit(); } catch { }
        try { Mouse.Capture(null); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!ShuttingDown) { ShuttingDown = true; try { NoteManager.FlushAll(); } catch { } }
        try { NoteHotkey.Stop(); } catch { }
        try { ProfileHotkey.Stop(); } catch { }
        try { AirDockHotkey.Stop(); } catch { }
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
