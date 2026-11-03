namespace BongHub;

/// <summary>Process-wide environment: data folder (overridable), test flags, read-only guard.</summary>
public static class AppEnv
{
    public const string Version = "1.4.4";
    /// <summary>First live owner is the only writer of the default notes folder. The name stays <c>Local\BongHub_NotesWriter</c>.</summary>
    public const string NotesWriterMutexName = @"Local\BongHub_NotesWriter";
    public static string DefaultDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub");
    public static string DataDir { get; private set; } = DefaultDataDir;
    public static string NotesDir => Path.Combine(DataDir, "notes");
    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string SettingsPath => IsDefaultData ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "settings.json") : Path.Combine(DataDir, "settings.json");
    public static string LogPath => Path.Combine(OutDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub"), "error.log");
    public static bool IsDefaultData => string.Equals(Path.GetFullPath(DataDir).TrimEnd('\\'), Path.GetFullPath(DefaultDataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    public static bool ReadOnly { get; set; }
    public static string ReadOnlyReason { get; private set; } = "";
    public static event Action? ReadOnlyChanged;
    /// <summary>Off-screen mode: every window is shifted by (-30000,-30000) and never activated.</summary>
    public static bool Off { get; private set; }
    public static int OffsetX => Off ? -30000 : 0;
    public static int OffsetY => Off ? -30000 : 0;
    public static bool Smoke { get; private set; }
    public static bool NoteTest { get; private set; }
    /// <summary>Folder passed with <c>--data</c> or <c>BONGHUB_DATA</c> when <c>--smoketest</c> refused it. Never written.</summary>
    public static string? IgnoredDataDir { get; private set; }
    /// <summary>Show the settings window off-screen, then hide it and stay running. Used by the Phase 0 memory baseline.</summary>
    public static bool IdleHidden { get; private set; }
    public static string? OutDir { get; private set; }
    public static string[] Args { get; private set; } = [];
    public static bool Has(string flag) => Args.Contains(flag, StringComparer.OrdinalIgnoreCase);
    static Mutex? notesWriter;
    static bool notesWriterOwned;

    public static void Init(string[] args)
    {
        Args = args;
        bool askedData = false;
        var env = Environment.GetEnvironmentVariable("BONGHUB_DATA");
        if (!string.IsNullOrWhiteSpace(env)) { DataDir = env; askedData = true; }
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            if (a == "--data" && i + 1 < args.Length) { DataDir = args[++i]; askedData = true; }
            else if (a == "--out" && i + 1 < args.Length) OutDir = args[++i];
            else if (a == "--off") Off = true;
            else if (a == "--idle-hidden") { IdleHidden = true; Off = true; }
            else if (a == "--smoketest") Smoke = true;
            else if (a == "--notetest") NoteTest = true;
        }
        if (Smoke || NoteTest)
        {
            Off = true;
            OutDir ??= AppContext.BaseDirectory;
            // tests never touch the real data folder
            if (IsDefaultData) DataDir = Path.Combine(Path.GetTempPath(), "bonghub_test_" + Environment.ProcessId);
        }
        if (Smoke)
        {
            // A copy passed with --data is the caller's. Smoke writes a fixture config, so it must not use that folder.
            if (askedData)
            {
                IgnoredDataDir = DataDir;
                Log("smoketest ignores --data and BONGHUB_DATA");
            }
            DataDir = Path.Combine(Path.GetTempPath(), "bonghub_smoke_" + Environment.ProcessId);
            Directory.CreateDirectory(DataDir);
        }
        if (OutDir != null) Directory.CreateDirectory(OutDir);
    }

    /// <summary>
    /// Call only after this process won the single-instance mutex.
    /// Tests use a temp folder and must not call this (they would fight the real app for the writer lock).
    /// </summary>
    public static void ClaimWriterOrReadOnly()
    {
        if (!IsDefaultData) return;
        // 3.4.8 takes the writer lock before its tray mutex, so a live tray means the stable app is already the writer.
        if (LegacyRunning())
        {
            EnterReadOnly("BongHub 3.4.8이 실행 중이라 실제 메모를 읽기 전용으로 열었어요.");
            return;
        }
        try
        {
            notesWriter = new Mutex(false, NotesWriterMutexName);
            try { notesWriterOwned = notesWriter.WaitOne(0); }
            catch (AbandonedMutexException) { notesWriterOwned = true; }
        }
        catch (Exception e)
        {
            notesWriterOwned = false;
            Log("notes writer mutex: " + e.Message);
        }
        if (!notesWriterOwned)
        {
            ReleaseWriter();
            EnterReadOnly("다른 BongHub가 메모를 저장 중이라 읽기 전용으로 열었어요.");
            return;
        }
        // An older 3.4.7 (no writer lock) may have appeared between the two checks.
        if (LegacyRunning())
        {
            ReleaseWriter();
            EnterReadOnly("BongHub 3.4.8이 실행 중이라 실제 메모를 읽기 전용으로 열었어요.");
        }
    }

    public static void ReleaseWriter()
    {
        if (notesWriter == null) return;
        try { if (notesWriterOwned) notesWriter.ReleaseMutex(); } catch { }
        notesWriterOwned = false;
        try { notesWriter.Dispose(); } catch { }
        notesWriter = null;
    }

    static void EnterReadOnly(string reason)
    {
        bool changed = !ReadOnly || ReadOnlyReason != reason;
        ReadOnly = true;
        ReadOnlyReason = reason;
        if (changed) ReadOnlyChanged?.Invoke();
    }

    /// <summary>
    /// True when a shared notes/config write must not proceed.
    /// Re-checks the legacy tray so a 3.4.7 build that does not know the writer mutex
    /// cannot start later and save over us. Once read-only, this process stays read-only.
    /// </summary>
    public static bool DenyWrite()
    {
        if (ReadOnly) return true;
        if (!IsDefaultData || !LegacyRunning()) return false;
        ReleaseWriter();
        EnterReadOnly("BongHub가 나중에 실행돼서 저장을 멈췄어요. 메모가 겹치지 않게 읽기 전용으로 바꿨어요.");
        return true;
    }

    /// <summary>True while the stable tray instance (mutex Local\BongHub_Tray) is alive.</summary>
    public static bool LegacyRunning()
    {
        try { using var m = Mutex.OpenExisting(@"Local\BongHub_Tray"); return true; }
        catch { return false; }
    }

    public static readonly System.Text.StringBuilder Marks = new();
    static readonly System.Text.StringBuilder MemLog = new();
    public static bool MainWindowCreated { get; private set; }
    public static double? TrayVisibleMs { get; private set; }
    public static double? FirstFrameMs { get; private set; }
    public static int? NotesAtFirstFrame { get; private set; }
    public static int? RenderTierAtFirstFrame { get; private set; }
    public static long? HeapMbAtFirstFrame { get; private set; }
    /// <summary>Process render preference recorded for the baseline log. "software" avoids the D3D9On12 device.</summary>
    public static string RenderModeLabel { get; set; } = "";
    public static string? HwndRenderModeAtFirstFrame { get; private set; }
    public static bool IdleTrimDone { get; private set; }
    public static int? StartupNotesOpened { get; private set; }
    public static bool SettingsHidden { get; private set; }
    /// <summary>Startup phase timestamp (ms since process start).</summary>
    public static void Mark(string what) { try { Marks.Append($"{what}={(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} "); } catch { } }
    /// <summary>Working set and private bytes at a startup phase. Written into startup.txt. Does not trim.</summary>
    public static void MarkMemory(string phase, int noteEditors = -1)
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            long ws = p.WorkingSet64 / 1048576;
            long priv = p.PrivateMemorySize64 / 1048576;
            string extra = noteEditors >= 0 ? $" editors={noteEditors}" : "";
            MemLog.Append($"mem {phase} ws_mb={ws} private_mb={priv}{extra}\r\n");
            Mark($"mem_{phase}");
            if (OutDir != null) WriteStartup();
        }
        catch { }
    }
    public static void NoteMainWindowCreated() { MainWindowCreated = true; }
    /// <summary>Call the moment the tray icon is visible. Writes startup.txt when --out is set.</summary>
    public static void NoteTrayVisible()
    {
        try
        {
            TrayVisibleMs = ElapsedMs();
            Mark("tray_visible");
            WriteStartup();
        }
        catch { }
    }
    public static void NoteFirstFrame(int notesOpen, int renderTier, long heapMb, string hwndRenderMode)
    {
        try
        {
            FirstFrameMs = ElapsedMs();
            NotesAtFirstFrame = notesOpen;
            RenderTierAtFirstFrame = renderTier;
            HeapMbAtFirstFrame = heapMb;
            HwndRenderModeAtFirstFrame = hwndRenderMode;
            WriteStartup();
        }
        catch { }
    }
    /// <summary>Startup trim finished. The baseline samples memory about 20s later; this only marks that the trim ran.</summary>
    public static void NoteIdleTrim()
    {
        try { IdleTrimDone = true; Mark("idle_trim"); WriteStartup(); }
        catch { }
    }
    public static void NoteStartupNotes(int opened)
    {
        try { StartupNotesOpened = opened; Mark("startup_notes"); WriteStartup(); }
        catch { }
    }
    public static void NoteSettingsHidden()
    {
        try { SettingsHidden = true; Mark("settings_hidden"); WriteStartup(); }
        catch { }
    }
    static double ElapsedMs() => (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
    /// <summary>Rewrites startup.txt so the tray line and the first-frame lines stay in one file.</summary>
    public static void WriteStartup()
    {
        if (OutDir == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append($"main_window_created={(MainWindowCreated ? 1 : 0)}\r\n");
        if (RenderModeLabel.Length > 0) sb.Append($"render_mode={RenderModeLabel}\r\n");
        if (TrayVisibleMs != null) sb.Append($"tray_visible_ms={TrayVisibleMs:0}\r\n");
        if (FirstFrameMs != null)
        {
            sb.Append($"first_frame_ms={FirstFrameMs:0}\r\n");
            sb.Append($"notes_open_at_first_frame={NotesAtFirstFrame}\r\n");
            sb.Append($"readonly={ReadOnly}\r\n");
            sb.Append($"render_tier={RenderTierAtFirstFrame}\r\n");
            sb.Append($"hwnd_render_mode={HwndRenderModeAtFirstFrame}\r\n");
            sb.Append($"managed_heap_mb={HeapMbAtFirstFrame}\r\n");
        }
        if (StartupNotesOpened != null) sb.Append($"startup_notes_opened={StartupNotesOpened}\r\n");
        if (SettingsHidden) sb.Append("settings_hidden=1\r\n");
        if (IdleTrimDone) sb.Append("idle_trim=1\r\n");
        sb.Append(MemLog);
        sb.Append($"phases: {Marks}\r\n");
        File.WriteAllText(Path.Combine(OutDir, "startup.txt"), sb.ToString());
    }

    public static void Log(string text)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\r\n"); } catch { }
    }
}
