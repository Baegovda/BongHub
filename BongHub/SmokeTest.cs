using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BongHub;

/// <summary>--smoketest: settings window + sticky note, rendered off-screen and never activated.</summary>
public static class SmokeTest
{
    static TrayService? tray;
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    static readonly Color DarkUnder = Color.FromRgb(0x1C, 0x1C, 0x1E);   // stand-in for dark Mica
    static readonly Color LightUnder = Color.FromRgb(0xD8, 0xD8, 0xD8);  // worst case: very light wallpaper bleeding through Mica

    public static async void Run(App app)
    {
        var t = new TestKit();
        try { await Body(app, t); }
        catch (Exception e) { t.Check("no exception", false, e.ToString()); }
        t.Save("smoketest.txt");
        if (t.Fails > 0) Environment.ExitCode = 1;
        if (tray != null) tray.OnExit();
        else app.RequestShutdown();
    }

    static void ContrastCheck(TestKit t, Window w, FrameworkElement? el, string name, double need)
    {
        if (el == null) { t.Check($"contrast {name}", false, "element missing"); return; }
        var d = TestKit.Contrast(w, el, DarkUnder);
        var l = TestKit.Contrast(w, el, LightUnder);
        double worst = Math.Min(d.ratio, l.ratio);
        t.Check($"contrast {name} ≥ {need}", worst >= need, $"dark-bg {d.ratio:0.0}:1 ({d.fg} on {d.bg}), light-bg {l.ratio:0.0}:1");
    }

    static async Task Body(App app, TestKit t)
    {
        // ---------- settings window ----------
        var sw = Stopwatch.StartNew();
        double renderedAt = -1; Color compBg = Colors.Magenta; double firstFrameMeanLum = -1, firstFrameWhite = -1;
        var w = app.EnsureMain();
        w.ContentRendered += (_, _) =>
        {
            renderedAt = sw.Elapsed.TotalMilliseconds;
            var src = HwndSource.FromHwnd(Win32.Handle(w));
            compBg = src?.CompositionTarget?.BackgroundColor ?? Colors.Magenta;
        };
        app.ShowSettings(null);
        var st = WindowFx.Of(w);
        t.Check("settings first frame", await TestKit.Until(() => st.Revealed, 8000), $"{renderedAt:0} ms to ContentRendered");
        t.Info($"settings: backdrop(Mica)={st.Backdrop}, cloaked at creation={st.CloakedAtCreate}, cloaked after reveal={st.CloakedAfterReveal}");
        t.Check("DWM cloak engaged before first paint", st.CloakedAtCreate > 0, $"DWMWA_CLOAKED={st.CloakedAtCreate}");
        t.Check("uncloaked after first frame", st.CloakedAfterReveal == 0, $"DWMWA_CLOAKED={st.CloakedAfterReveal}");
        var settingsMode = HwndSource.FromHwnd(Win32.Handle(w))?.CompositionTarget?.RenderMode;
        t.Check("settings window renders in software", settingsMode == RenderMode.SoftwareOnly, settingsMode?.ToString() ?? "no target");
        double compLum = NoteColors.Lum(Color.FromRgb(compBg.R, compBg.G, compBg.B));
        t.Check("composition background not white", compBg.A == 0 || compLum < 0.05, $"#{compBg.A:X2}{compBg.R:X2}{compBg.G:X2}{compBg.B:X2} (transparent = Mica shows through)");
        await TestKit.Until(() => w.Root.Opacity >= 0.999, 2000);
        t.Check("appear animation finishes", w.Root.Opacity >= 0.999, $"opacity {w.Root.Opacity:0.00}");
        var frame = TestKit.Render(w, DarkUnder);
        firstFrameWhite = TestKit.WhitePercent(frame); firstFrameMeanLum = TestKit.MeanLum(frame);
        double dom = TestKit.DominantLum(frame);
        t.Check("settings frame not white", firstFrameWhite < 1 && firstFrameMeanLum < 0.12 && dom < 0.05, $"pure-white {firstFrameWhite:0.00}%, mean luminance {firstFrameMeanLum:0.000}, background luminance {dom:0.000}");
        t.Check("settings did not take focus", TestKit.NotForeground(w));
        t.Check("theme font applied", w.FontFamily.Source.StartsWith("Segoe UI Variable"), w.FontFamily.Source);
        t.Check("theme foreground applied", w.Foreground is SolidColorBrush b && NoteColors.Lum(b.Color) > 0.8, (w.Foreground as SolidColorBrush)?.Color.ToString() ?? "?");
        TestKit.SavePng(w, DarkUnder, "settings_presets.png");
        ContrastCheck(t, w, w.TitleBlock, "page title", 7);
        ContrastCheck(t, w, w.FirstRowTitle, "row title", 7);
        ContrastCheck(t, w, w.FirstSecondary, "secondary text", 4.5);
        ContrastCheck(t, w, w.AppTitle, "sidebar title", 7);

        foreach (var page in new[] { "notes", "kakao", "general", "powertoys", "about" })
        {
            w.Navigate(page);
            t.Check($"tab {page} switch is non-blocking (<60ms)", w.LastNavigateMs < 60, $"{w.LastNavigateMs:0.0} ms");
            await TestKit.Delay(420);
            t.Check($"tab {page} slide-in settles", w.PageHost.Opacity >= 0.999, $"opacity {w.PageHost.Opacity:0.00}");
            if (page == "notes")
            {
                var slider = w.NoteFontSlider;
                t.Check("note font size is 7–48", slider != null && slider.Minimum == 7 && slider.Maximum == 48, slider == null ? "no slider" : $"{slider.Minimum}–{slider.Maximum}");
                t.Check("notes page show/hide/copy/delete", w.HasNotesCommand("모두 보이기") && w.HasNotesCommand("모두 숨기기") && w.HasNotesCommand("텍스트 복사") && w.HasNotesCommand("삭제"));
            }
            TestKit.SavePng(w, DarkUnder, $"settings_{page}.png");
            ContrastCheck(t, w, w.TitleBlock, $"{page} title", 7);
            ContrastCheck(t, w, w.FirstSecondary, $"{page} secondary", 4.5);
        }
        TestKit.SavePng(w, LightUnder, "settings_about_lightbackdrop.png");

        // ---------- Alt+F4 then reopen (the old crash) ----------
        var h = Win32.Handle(w);
        Win32.SendMessage(h, 0x0010 /*WM_CLOSE, what Alt+F4 sends*/, IntPtr.Zero, IntPtr.Zero);
        await TestKit.Until(() => !w.IsVisible, 2000);
        t.Check("Alt+F4 hides (window kept alive)", !w.IsVisible && IsWindow(h));
        Exception? reopenErr = null;
        try { app.ShowSettings("presets"); } catch (Exception e) { reopenErr = e; }
        await TestKit.Delay(400);
        t.Check("reopen from tray after Alt+F4", reopenErr == null && w.IsVisible && w.Root.Opacity > 0.99, reopenErr?.Message ?? $"visible={w.IsVisible}");
        t.Check("reopen did not take focus", TestKit.NotForeground(w));
        t.Check("six settings tabs", w.Pages.Count == 6 && w.Pages.Any(p => p.Label == "모니터 프로필") && w.Pages.Any(p => p.Label == "카톡 광고 제거") && !w.Pages.Any(p => p.Key == "windows") && !w.Pages.Any(p => p.Key == "desktop"));
        t.Check("startup switch is display only", w.StartupSwitchEnabled == false);
        t.Check("about shows version and hotkeys", w.AboutMentionsVersion && w.AboutMentionsHotkey);
        t.Check("open folder stays inside tests", w.RequestOpenDataFolder() == false && w.FolderLaunches == 0);
        t.Check("smoketest keeps its own temp folder", AppEnv.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) && AppEnv.DataDir.Contains("bonghub_smoke_"));
        string fixture = "{\r\n  \"Version\": 2,\r\n  \"Presets\": [{ \"Name\": \"작업\", \"Hotkey\": \"Ctrl+Alt+1\", \"Marker\": \"PRESET_KEEP\" }],\r\n  \"Pt\": { \"Zones\": { \"Enabled\": false, \"Marker\": \"PT_KEEP\" } },\r\n  \"Desk\": { \"HotkeySave\": \"Ctrl+Alt+S\", \"Marker\": \"DESK_KEEP\" },\r\n  \"General\": { \"OverlayOpacity\": 70 }\r\n}\r\n";
        // --data is never the write target. When that folder has a real config, the byte diff uses a temp copy of it.
        string? srcPath = string.IsNullOrEmpty(AppEnv.IgnoredDataDir) ? null : Path.Combine(AppEnv.IgnoredDataDir, "config.json");
        byte[]? srcBefore = srcPath != null && File.Exists(srcPath) ? File.ReadAllBytes(srcPath) : null;
        if (srcBefore != null) File.WriteAllBytes(AppEnv.ConfigPath, srcBefore);
        else File.WriteAllText(AppEnv.ConfigPath, fixture);
        string beforeText = File.ReadAllText(AppEnv.ConfigPath);
        ConfigStore.Load();
        w.ApplyTabOrder(ConfigStore.TabOrder);
        string moveKey = w.Pages.Count > 1 ? w.Pages[0].Key : "";
        bool moved = moveKey.Length > 0 && w.MoveTab(moveKey, w.Pages.Count - 1);
        string patched = File.ReadAllText(AppEnv.ConfigPath);
        NotesConfigPatch.ChangedSpan(beforeText, patched, out string removed, out string inserted);
        bool sourceSame = srcBefore == null || File.ReadAllBytes(srcPath!).AsSpan().SequenceEqual(srcBefore);
        bool markers = srcBefore != null || (patched.Contains("PRESET_KEEP") && patched.Contains("PT_KEEP") && patched.Contains("DESK_KEEP"));
        bool shapeKept = beforeText.Count(c => c == '\n') == patched.Count(c => c == '\n') && beforeText.Count(c => c == '\r') == patched.Count(c => c == '\r');
        t.Check("tab order changes only TabOrder bytes", moved && sourceSame && markers && shapeKept && NotesConfigPatch.TabOrderSpanOnly(removed, inserted),
            srcBefore == null ? $"fixture removed={removed.Length} inserted={inserted.Length}" : $"real copy {srcBefore.Length} bytes untouched, removed={removed.Length} inserted={inserted.Length}");
        // Contrast measures the built-in palette at full opacity. A copied config (Opacity 80) blends the glyphs.
        File.WriteAllText(AppEnv.ConfigPath, fixture);
        ConfigStore.Load();

        t.Check("screen description puts the primary first", ScreenMap.Describe("1920x1080@0,0;2560x1440@1920,0*") == "모니터 2대 · 2560×1440(주) + 1920×1080");
        var icons = new MemoryIconSurface();
        icons.Icons.Add(new DeskIcon { Name = "Readme", X = 10, Y = 10 });
        icons.Icons.Add(new DeskIcon { Name = "readme", X = 20, Y = 20 });
        icons.Icons.Add(new DeskIcon { Name = "Other", X = 30, Y = 30 });
        var iconTarget = new List<DeskIcon> { new() { Name = "README", X = 11, Y = 12 }, new() { Name = "ReadMe", X = 21, Y = 22 }, new() { Name = "없는 아이콘", X = 1, Y = 1 } };
        var applied = IconMatch.Apply(icons, iconTarget);
        t.Check("duplicate icon names match in order", applied.Ok && applied.Moved == 2 && applied.Missing == 1 && applied.Extra == 1 && icons.Icons[0].X == 11 && icons.Icons[1].X == 21 && icons.Icons[2].X == 30);
        icons.AutoArrange = true;
        int movesBefore = icons.Moves;
        var refused = IconMatch.Apply(icons, iconTarget);
        t.Check("auto-arrange refuses without moving", !refused.Ok && refused.Error.Contains("자동 정렬") && icons.Moves == movesBefore);
        var when = DateTimeOffset.FromUnixTimeMilliseconds(1793585924140).UtcDateTime;
        string layoutJson = LayoutStore.Serialize(new List<DeskLayoutFile> { new() { Id = "abc", Name = "배치", Key = "1920x1080@0,0*", Desc = "모니터 1대 · 1920×1080(주)", Auto = true, Saved = when, Items = new() { new DeskIcon { Name = "휴지통", X = 14, Y = 2 } } } });
        var layoutBack = LayoutStore.Parse(layoutJson);
        t.Check("layout round trip keeps the ajax date", layoutJson.Contains("\\/Date(1793585924140)\\/") && layoutJson.Contains("휴지통") && layoutBack.Count == 1 && new DateTimeOffset(layoutBack[0].Saved).ToUnixTimeMilliseconds() == 1793585924140);
        var merged = LayoutMerge.Merge(
            new[] { new DeskLayoutFile { Id = "A", Saved = DateTimeOffset.FromUnixTimeMilliseconds(100).UtcDateTime, Items = new() }, new DeskLayoutFile { Id = "C", Saved = DateTimeOffset.FromUnixTimeMilliseconds(50).UtcDateTime, Items = new() } },
            new[] { new DeskLayoutFile { Id = "A", Saved = DateTimeOffset.FromUnixTimeMilliseconds(200).UtcDateTime, Items = new() }, new DeskLayoutFile { Id = "B", Saved = DateTimeOffset.FromUnixTimeMilliseconds(10).UtcDateTime, Items = new() } },
            new[] { "A", "B" });
        t.Check("layout merge keeps the newer snapshot", merged.Count == 2 && merged[0].Id == "A" && merged[1].Id == "C");
        w.RequestSaveDesktop();
        w.RequestRestoreDesktop();
        bool desktopThrew = false;
        try { using var blocked = new ExplorerIconSurface(); } catch (InvalidOperationException) { desktopThrew = true; }
        t.Check("desktop actions stay off the real list view", desktopThrew && w.DesktopBlocks == 2 && w.DesktopAction == "blocked" && ExplorerIconSurface.NativeCalls == 0);

        var wins = new FakeWindowSurface();
        wins.Windows.Add(new TrackedWindow { Id = 1, Process = "Discord", Title = "Discord", Class = "Chrome_WidgetWin_1", X = 0, Y = 0, W = 400, H = 300 });
        wins.Windows.Add(new TrackedWindow { Id = 2, Process = "notepad", Title = "메모장", X = 5, Y = 5, W = 200, H = 200 });
        var rules = new List<WindowRule>
        {
            new() { Name = "첫 규칙", Process = "Discord", Action = "pos", X = 40, Y = 50, W = 800, H = 600 },
            new() { Name = "나중 규칙", Process = "Discord", Action = "min" },
            new() { Name = "메모장", Process = "notepad.exe", Action = "pos", X = 12, Y = 16, W = 640, H = 480 }
        };
        int touched = RuleEngine.Apply(wins, rules);
        t.Check("first matching window rule wins", touched == 2 && wins.Windows[0].X == 40 && wins.Windows[0].Y == 50 && wins.Windows[0].W == 800 && !wins.Windows[0].Min && wins.Windows[1].X == 12 && !wins.Ops.Contains("min 1"));
        var keep = new KeepWatch();
        keep.SetRules(new[] { new WindowRule { Enabled = true, Keep = true, Process = "Discord", Action = "pos", X = 40, Y = 50, W = 800, H = 600 } });
        int ops = wins.Ops.Count;
        keep.Check(wins, 1);
        t.Check("keep watch leaves an in-place window", keep.Active && keep.Applied == 0 && wins.Ops.Count == ops && LiveWindowSurface.NativeCalls == 0);
        var planned = WorkspacePlan.Plan(
            new[] { new TrackedWindow { Id = 7, Process = "notepad", Title = "메모장" }, new TrackedWindow { Id = 8, Process = "notepad", Title = "다른" } },
            new[] { new WorkspaceItem { Process = "notepad.exe", Title = "메모장", X = 10, Y = 20, W = 640, H = 480 }, new WorkspaceItem { Process = "missing", Title = "없음" } },
            out int workspaceMissing);
        t.Check("workspace plan does not launch a missing app", planned.Count == 1 && planned[0].WindowId == 7 && planned[0].X == 10 && workspaceMissing == 1);
        w.RequestApplyRules();
        w.RequestApplyWorkspace(null);
        bool windowThrew = false;
        try { using var blockedWindows = new LiveWindowSurface(); } catch (InvalidOperationException) { windowThrew = true; }
        t.Check("window actions stay off real top-level windows", windowThrew && w.WindowBlocks == 2 && w.WindowAction == "blocked" && LiveWindowSurface.NativeCalls == 0);

        var cols = ZoneMath.Rects(new ZoneLayout { Kind = "cols", A = 2, Spacing = 0 }, new PxRect(0, 0, 1920, 1080));
        t.Check("zone columns split the work area", cols.Count == 2 && cols[0].W == 960 && cols[1].Right == 1920);
        t.Check("palette calculator", PaletteMath.Calc("12*3+4") == "40" && PaletteMath.Calc("hello") == null && PaletteMath.Search("7*6", Array.Empty<(string, string)>())[0] == "= 42");
        string md = PasteText.HtmlToMarkdown("Version:0.9\r\n<html><body><!--StartFragment--><h2>제목</h2><p>이건 <b>굵게</b></p><!--EndFragment--></body></html>") ?? "";
        t.Check("paste html becomes markdown", md.Contains("## 제목") && md.Contains("**굵게**"));
        t.Check("resize fit keeps the ratio", ImageFit.Target(new ImageFit.Px(2400, 1600), 1366, 768, "fit", true) == new ImageFit.Px(1152, 768));
        bool ptThrew = false;
        try { PtSystem.Touch(); } catch (InvalidOperationException) { ptThrew = true; }
        t.Check("powertoys effects stay off hooks and hardware", ptThrew && PtSystem.NativeCalls == 0 && !PtDefaults.Zones && !PtDefaults.Grab && !PtDefaults.Paste && PtDefaults.Resizer && !GrabMath.CanGrab(false, false, true, false, false));

        var workPlan = new MonitorPreset { Name = "작업", Primary = "SAM73D3", Layout = "work_layout.cfg" };
        workPlan.Enable.Add("SAM747A");
        workPlan.Enable.Add("SAM73D3");
        var awayPlan = new MonitorPreset { Name = "자리비움", Primary = "GSM76F5" };
        awayPlan.Disable.Add("SAM73D3");
        var fakeDisplay = new FakeDisplay();
        fakeDisplay.Apply(PresetPlan.Commands(workPlan));
        fakeDisplay.Apply(PresetPlan.Commands(awayPlan));
        t.Check("fake display records the preset plan", string.Join("|", fakeDisplay.Log) == "enable SAM747A|enable SAM73D3|load work_layout.cfg|primary SAM73D3|primary GSM76F5|disable SAM73D3" && DisplaySystem.NativeCalls == 0 && DisplaySystem.MmtStarts == 0);
        var brightPlan = new MonitorPreset { Name = "작업" };
        brightPlan.Brightness.Add(new BrightTarget { Monitor = "", Value = 60 });
        brightPlan.Brightness.Add(new BrightTarget { Monitor = "SAM73D3", Value = 30 });
        brightPlan.Brightness.Add(new BrightTarget { Monitor = "GSM76F5", Value = -1 });
        var targets = PresetExtras.Targets(brightPlan, new[] { "SAM73D3", "SAM747A", "GSM76F5" });
        t.Check("brightness target prefers one monitor", targets["SAM73D3"] == 30 && targets["SAM747A"] == 60 && !targets.ContainsKey("GSM76F5") && PresetExtras.BrightSummary(brightPlan).Contains("SAM73D3 30%"));
        int displayBlocks = w.DisplayBlocks;
        int workRuns = PresetExtras.WorkRuns, deskRestores = PresetExtras.DeskRestores;
        w.RequestApplyPreset("작업");
        w.RequestApplyPreset("자리비움");
        bool displayThrew = false, brightThrew = false;
        try { new RealDisplay().Apply(new[] { "enable SAM73D3" }); } catch (InvalidOperationException) { displayThrew = true; }
        try { new RealBright().Set("SAM73D3", 30, out _); } catch (InvalidOperationException) { brightThrew = true; }
        t.Check("display apis stay off for 작업 and 자리비움", displayThrew && brightThrew && w.DisplayBlocks == displayBlocks + 2 && w.DisplayAction == "blocked" && DisplaySystem.NativeCalls == 0 && DisplaySystem.MmtStarts == 0 && DisplaySystem.ChangeDisplaySettingsExCalls == 0 && DisplaySystem.SetDisplayConfigCalls == 0 && DisplaySystem.BrightnessCalls == 0 && DisplaySystem.DdcCalls == 0 && PresetExtras.WorkRuns == workRuns && PresetExtras.DeskRestores == deskRestores);

        var talk = new List<KakaoWindow>
        {
            new() { Id = 1, Class = "EVA_Window_Dblclk", Title = "카카오톡", Process = "KakaoTalk.exe", X = 0, Y = 0, W = 400, H = 700 },
            new() { Id = 2, ParentId = 1, Class = "EVA_ChildWindow", Title = "", Process = "KakaoTalk.exe", X = 0, Y = 620, W = 400, H = 80 },
            new() { Id = 3, ParentId = 1, Title = "OnlineMainView", Process = "KakaoTalk.exe", X = 0, Y = 38, W = 400, H = 660 },
            new() { Id = 4, ParentId = 3, Title = "ChatRoomListView", Process = "KakaoTalk.exe", X = 0, Y = 30, W = 400, H = 600 },
            new() { Id = 5, ParentId = 4, Class = "EVA_Window_Dblclk", Title = "", Process = "KakaoTalk.exe", X = 8, Y = 56, W = 360, H = 28 },
            new() { Id = 6, ParentId = 4, Title = "ChatRoomListCtrl", Process = "KakaoTalk.exe", X = 8, Y = 143, W = 360, H = 400 },
            new() { Id = 7, Class = "EVA_Window_Dblclk", Title = "친구", Process = "KakaoTalk.exe", X = 20, Y = 20, W = 300, H = 400 },
            new() { Id = 8, ParentId = 7, Title = "BannerAd", Process = "KakaoTalk.exe", X = 20, Y = 300, W = 300, H = 80 },
            new() { Id = 9, Class = "Notepad", Title = "AdView", Process = "notepad.exe", X = 0, Y = 0, W = 100, H = 100 }
        };
        var kakaoCmds = KakaoPlan.Commands(talk, compact: true);
        var surface = talk.ToDictionary(x => x.Id);
        foreach (var cmd in kakaoCmds)
        {
            var bits = cmd.Split(' ');
            if (bits[0] == "hide") surface[int.Parse(bits[1])].Visible = false;
            else if (bits[0] == "place") { var row = surface[int.Parse(bits[1])]; row.X = int.Parse(bits[2]); row.Y = int.Parse(bits[3]); row.W = int.Parse(bits[4]); row.H = int.Parse(bits[5]); }
        }
        t.Check("kakao plan hides the ad on a fake window", !surface[2].Visible && !surface[8].Visible && surface[9].Visible && surface[3].Y == 30 && surface[6].Y == 129 && surface[6].H == 414 && surface[5].Y == 48 && KakaoSystem.WindowCalls == 0 && KakaoSystem.ProcessStarts == 0);
        var host = new FakeKakaoHost();
        KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = true, Dark = false });
        KakaoSync.Apply(host, new KakaoSettings { AdBlock = true, Compact = false, Dark = true });
        int running = host.Running;
        string darkArgs = host.LastArgs ?? "";
        KakaoSync.Apply(host, new KakaoSettings { AdBlock = false });
        t.Check("kakao helper args stay on the fake", running == 1 && darkArgs == "/nocompact /dark" && host.Running == 0 && host.Log.Contains("restore") && KakaoArgs.Format(new KakaoSettings { AdBlock = true, Compact = true, Dark = false }) == "");
        int kakaoBlocks = w.KakaoBlocks;
        w.RequestSyncKakao();
        bool kakaoThrew = false;
        try { KakaoNative.Start(""); } catch (InvalidOperationException) { kakaoThrew = true; }
        t.Check("kakao stays off real windows", kakaoThrew && w.KakaoBlocks == kakaoBlocks + 1 && w.KakaoAction == "blocked" && KakaoSystem.NativeCalls == 0 && KakaoSystem.ProcessStarts == 0 && KakaoSystem.WindowCalls == 0 && KakaoSystem.ProcessQueries == 0);

        string cloudRoot = Path.Combine(Path.GetTempPath(), "bonghub_smoke_cloud_" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(cloudRoot);
            string cfg = Path.Combine(cloudRoot, "config.json");
            File.WriteAllText(cfg, "{\"General\":{\"IdleMinutes\":17}}");
            File.SetLastWriteTimeUtc(cfg, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var mem = new MemoryCloud();
            string synced = CloudEngine.Run(new CloudWorkspace
            {
                ConfigFile = cfg,
                NotesDir = Path.Combine(cloudRoot, "notes"),
                DeskDir = Path.Combine(cloudRoot, "desktop"),
                Device = "PC-A",
                AppVersion = AppEnv.Version,
                UtcNow = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc),
                LocalNow = new DateTime(2026, 1, 1, 9, 5, 0)
            }, mem, writer: true);
            string committed = mem.Snapshot().TryGetValue("data/config.json", out var committedCfg) ? System.Text.Encoding.UTF8.GetString(committedCfg.Bytes) : "";
            t.Check("cloud sync uses an in-memory remote", synced.Length == 0 && mem.Commits.Count == 1 && mem.Commits[0].StartsWith("sync: PC-A") && committed.Contains("17") && CloudSystem.GitStarts == 0 && CloudSystem.NetworkCalls == 0);
            var blockedSchema = new MemoryCloud();
            blockedSchema.Seed(new Dictionary<string, CloudFile> { ["data/meta.json"] = new CloudFile { Bytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":2,\"minApp\":\"9.9.9\"}"), MtimeUtc = DateTime.UtcNow } });
            File.WriteAllText(cfg, "LOCAL");
            string schema = CloudEngine.Run(new CloudWorkspace
            {
                ConfigFile = cfg,
                NotesDir = Path.Combine(cloudRoot, "notes"),
                DeskDir = Path.Combine(cloudRoot, "desktop"),
                Device = "PC-A",
                AppVersion = AppEnv.Version,
                UtcNow = DateTime.UtcNow,
                LocalNow = DateTime.Now
            }, blockedSchema, writer: true);
            t.Check("newer cloud schema stops the sync", schema.StartsWith("업데이트 필요") && File.ReadAllText(cfg) == "LOCAL");
        }
        finally { try { Directory.Delete(cloudRoot, true); } catch { } }
        int cloudBlocks = w.CloudBlocks;
        w.RequestCloudSync();
        bool cloudThrew = false;
        try { _ = new GitCloudTransport("https://github.com/Baegovda/BongHub.git", "main", "test"); } catch (InvalidOperationException) { cloudThrew = true; }
        t.Check("cloud stays off GitHub", cloudThrew && w.CloudBlocks == cloudBlocks + 1 && w.CloudAction == "blocked" && CloudSystem.NativeCalls == 0 && CloudSystem.GitStarts == 0 && CloudSystem.GhStarts == 0 && CloudSystem.NetworkCalls == 0);

        var clip = NoteClip.Unpack(NoteClip.Pack(new[] { new NoteClipPiece { Text = "안녕" }, new NoteClipPiece { Png = new byte[] { 1, 2, 3, 4 } } }));
        NoteClipSession.Publish(NoteClip.Pack(clip), NoteClip.Plain(clip), NoteClip.ToRtf(clip), clip[1].Png);
        t.Check("note copy keeps text and the picture off the system clipboard", clip.Count == 2 && NoteClip.Plain(clip) == "안녕" && !NoteClip.Plain(clip).Contains("Image") && NoteClipSession.SystemCalls == 0 && NoteClip.ToRtf(clip).Contains("\\pngblip"));
        int presetBlocks = w.DisplayBlocks;
        int presetNatives = DisplaySystem.NativeCalls;
        w.RememberPreset("작업");
        w.RequestApplyPreset("작업");
        t.Check("preset hotkey does not reapply the active layout", w.LastPresetToast == "이미 작업 상태입니다!" && w.DisplayBlocks == presetBlocks && DisplaySystem.NativeCalls == presetNatives);
        var snapped = ProfileLayout.Snap(new ProfileBox(13, 17, 200, 100), new ProfileBox(0, 0, 800, 600), 8);
        w.RequestCaptureWindow();
        bool captureThrew = false;
        try { using var livePick = new LiveWindowSurface(); } catch (InvalidOperationException) { captureThrew = true; }
        t.Check("profile snap and capture stay off real windows", snapped.X == 16 && snapped.Y == 16 && snapped.Right <= 800 && w.LastPick != null && w.LastPick.Title == "메모장" && w.LastPick.W == 640 && captureThrew && LiveWindowSurface.NativeCalls == 0);
        var beside = ProfileSnap.SnapMonitor(new ProfileBox(1900, 30, 1920, 1080), new[] { new ProfileBox(0, 0, 1920, 1080) }, 48);
        var half = ProfileSnap.PreviewAt(10, 540, new ProfileBox(0, 0, 1920, 1080));
        var quarter = ProfileSnap.PreviewAt(8, 8, new ProfileBox(0, 0, 1920, 1080));
        var maximized = ProfileSnap.PreviewAt(960, 8, new ProfileBox(0, 0, 1920, 1080));
        var resized = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "e", 50, 0, 120);
        var clamped = ProfileSnap.Resize(new ProfileBox(100, 100, 400, 300), new ProfileBox(0, 0, 1920, 1080), "e", 5000, 0, 120);
        t.Check("profile monitors snap together and windows snap to half, quarter, and max", beside.X == 1920 && beside.Y == 0 && half.Kind == "left" && half.Box.W == 960 && half.Box.H == 1080 && quarter.Kind == "tl" && quarter.Box.W == 960 && quarter.Box.H == 540 && maximized.Kind == "top" && maximized.Box.W == 1920 && resized.W == 450 && clamped.Right == 1920);
        var crossed = ProfileSnap.Transfer(new ProfileBox(1800, 100, 400, 300), new[] { new ProfileBox(0, 0, 1920, 1080), new ProfileBox(1920, 0, 1920, 1080) });
        var edged = ProfileSnap.SnapWindow(new ProfileBox(200, 40, 100, 80), new ProfileBox(0, 0, 800, 600), new[] { new ProfileBox(0, 0, 208, 200) }, 8);
        t.Check("profile window crosses monitors and snaps to a neighbor", crossed.Index == 1 && crossed.Local.X == -120 && crossed.Local.Y == 100 && edged.X == 208 && edged.Y == 40);
        var migrated = ProfileDoc.Migrate(
            new[] { new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1", Primary = "SAM1", DeskLayout = "L1", Workspace = "W1", Enable = new() { "SAM1" }, Brightness = new() { new BrightTarget { Monitor = "SAM1", Value = 40 } } } },
            new[] { new WindowRule { Name = "Discord", Process = "discord.exe", Title = "친구", X = 10, Y = 20, W = 300, H = 200 } },
            "{\"profiles\":[{\"name\":\"작업\",\"icons\":\"L1\",\"windows\":[{\"process\":\"notepad.exe\",\"title\":\"메모장\",\"class\":\"Notepad\",\"x\":16,\"y\":16,\"w\":640,\"h\":480}]}]}");
        var again = ProfileDoc.Parse(ProfileDoc.ToJson(migrated));
        bool primaryOff = !ProfileDoc.SetEnabled(migrated[0], migrated[0].Monitors.First(m => m.Primary).Number, false);
        var added = ProfileDoc.Add(migrated);
        ProfileDoc.Rename(added, "집");
        var copy = ProfileDoc.Duplicate(migrated, added.Id);
        bool dropped = copy != null && ProfileDoc.Delete(migrated, copy.Id);
        t.Check("profile migration and edits keep the 3.4.x fields", migrated[0].Hotkey == "Ctrl+Alt+1" && migrated[0].Workspace == "W1" && migrated[0].Apps.Any(a => a.Process == "notepad.exe" && a.Class == "Notepad") && migrated[0].Apps.Any(a => a.Process == "discord.exe") && migrated[0].Monitors[0].Id == "SAM1" && migrated[0].Monitors[0].Primary && again[0].Workspace == "W1" && again[0].Brightness[0].Value == 40 && primaryOff && added.Name == "집" && dropped && migrated.All(p => p.Id != copy!.Id));
        var iconSurface = new MemoryIconSurface();
        iconSurface.Icons.Add(new DeskIcon { Name = "휴지통", X = 1, Y = 1 });
        var storedIcons = ProfileDoc.CaptureIcons(iconSurface.Read());
        storedIcons[0].X = 40; storedIcons[0].Y = 50;
        var iconResult = ProfileDoc.RestoreIcons(storedIcons, iconSurface);
        bool explorerBlocked = false;
        try { using var realIcons = new ExplorerIconSurface(); } catch (InvalidOperationException) { explorerBlocked = true; }
        t.Check("profile icon layout restores through a fake desktop", iconResult.Ok && iconSurface.Icons[0].X == 40 && iconSurface.Icons[0].Y == 50 && explorerBlocked && ExplorerIconSurface.NativeCalls == 0);
        string legacy = "{\"profiles\":[{\"name\":\"작업\",\"icons\":\"L1\",\"windows\":["
            + "{\"process\":\"a.exe\",\"title\":\"A\",\"class\":\"A\",\"x\":36,\"y\":28,\"w\":96,\"h\":64},"
            + "{\"process\":\"b.exe\",\"title\":\"B\",\"class\":\"B\",\"x\":140,\"y\":28,\"w\":96,\"h\":64},"
            + "{\"process\":\"c.exe\",\"title\":\"C\",\"class\":\"C\",\"x\":244,\"y\":28,\"w\":96,\"h\":64},"
            + "{\"process\":\"d.exe\",\"title\":\"D\",\"class\":\"D\",\"x\":36,\"y\":100,\"w\":96,\"h\":64},"
            + "{\"process\":\"e.exe\",\"title\":\"E\",\"class\":\"E\",\"x\":140,\"y\":100,\"w\":96,\"h\":64},"
            + "{\"process\":\"f.exe\",\"title\":\"F\",\"class\":\"F\",\"x\":244,\"y\":100,\"w\":96,\"h\":64}]}]}";
        var lifted = ProfileDoc.Migrate(new[] { new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1" } }, Array.Empty<WindowRule>(), legacy);
        var liftedApps = lifted[0].Apps.OrderBy(a => a.Title).ToList();
        t.Check("1.1.0 canvas windows become monitor pixels", liftedApps.Count == 6 && liftedApps.All(a => a.W == 576 && a.H == 384) && liftedApps[0].X == 96 && liftedApps[1].X == 720 && liftedApps[5].Y == 540 && liftedApps.Select(a => a.X).Distinct().Count() == 3 && lifted[0].Monitors[0].W == 1920);
        var screens = new List<MonitorSpec>
        {
            new(3840, 2160, 0, 0, true),
            new(1920, 1080, 3840, 0, false),
            new(1920, 1080, 3840, 1080, false),
            new(3840, 2160, -3840, 0, false),
            new(960, 640, 5760, 0, false)
        };
        var seeded = ProfileDoc.Migrate(new[] { new MonitorPreset { Name = "작업", Hotkey = "Ctrl+Alt+1", Primary = "SAM1", Enable = new() { "SAM1" } } }, Array.Empty<WindowRule>(), null, screens);
        var seedPrimary = seeded[0].Monitors.First(m => m.Primary);
        string seedSummary = seedPrimary.W + "×" + seedPrimary.H;
        t.Check("profile monitors follow the screen list", seeded[0].Monitors.Count == 5 && seedPrimary.W == 3840 && seedPrimary.H == 2160 && seedPrimary.X == 0 && seedPrimary.Id == "SAM1" && seeded[0].Monitors.Any(m => m.W == 960 && m.H == 640) && seedSummary == "3840×2160" && ScreenMap.Current().Count == 0);
        var numbered = new MonitorProfile { Name = "작업" };
        numbered.Monitors.Add(new ProfileMonitor { Id = "old-a", Number = 1, X = 0, Y = 0, W = 1920, H = 1080 });
        numbered.Monitors.Add(new ProfileMonitor { Id = "old-b", Number = 1, X = 1920, Y = 0, W = 3840, H = 2160 });
        numbered.Apps.Add(new ProfileApp { Id = "w", Process = "notepad.exe", Title = "메모", Monitor = 1, X = 10, Y = 10, W = 400, H = 300 });
        var live = new List<ScreenInfo>
        {
            new() { Number = ScreenBind.WindowsNumber(@"\\.\DISPLAY1"), Device = @"\\.\DISPLAY1", Path = @"\\?\DISPLAY#AAA", Edid = "DEL:1", W = 3840, H = 2160, X = 1920, Y = 0, Primary = true },
            new() { Number = ScreenBind.WindowsNumber(@"\\.\DISPLAY2"), Device = @"\\.\DISPLAY2", Path = @"\\?\DISPLAY#BBB", Edid = "SAM:2", W = 1920, H = 1080, X = 0, Y = 0 }
        };
        ScreenBind.Adopt(numbered, live);
        var wide = numbered.Monitors.First(m => m.W == 3840);
        var desk = numbered.Monitors.First(m => m.W == 1920);
        t.Check("windows numbers follow display names and resolution", ScreenBind.WindowsNumber(@"\\.\DISPLAY12") == 12 && wide.Number == 1 && desk.Number == 2 && wide.Path.Contains("AAA") && numbered.Apps[0].Monitor == desk.Number);
        var bound = new MonitorProfile();
        bound.Monitors.Add(new ProfileMonitor { Number = 2, X = 0, Y = 0, W = 800, H = 600, Path = @"\\?\DISPLAY#GONE", Edid = "OLD" });
        bound.Apps.Add(new ProfileApp { Title = "남은 창", Monitor = 2, W = 200, H = 200 });
        var now = new List<ScreenInfo> { new() { Number = 4, Device = @"\\.\DISPLAY4", Path = @"\\?\DISPLAY#BBB", Edid = "SAM:2", W = 1920, H = 1080, X = 10, Y = 10 } };
        int pathScore = ScreenBind.Score(new ProfileMonitor { Path = @"\\?\DISPLAY#BBB", W = 100, H = 100, X = 9000, Y = 0 }, now[0]);
        int placeScore = ScreenBind.Score(new ProfileMonitor { W = 1920, H = 1080, X = 10, Y = 10 }, now[0]);
        ScreenBind.Adopt(bound, now);
        bool wasMismatch = ScreenBind.Mismatch(bound, now);
        ScreenBind.MatchCurrent(bound, now);
        bool iconThrew = false;
        try { AppIcon.ReadReal("notepad.exe"); } catch (InvalidOperationException) { iconThrew = true; }
        bool screenThrew = false;
        try { ScreenCatalog.ReadReal(); } catch (InvalidOperationException) { screenThrew = true; }
        t.Check("disconnected monitor stays and matching uses identity", pathScore > placeScore && bound.Apps.Count == 1 && bound.Apps[0].Monitor == 4 && wasMismatch && !ScreenBind.Mismatch(bound, now) && bound.Monitors.Any(m => m.Connected && m.Number == 4 && m.Path.Contains("BBB")) && iconThrew && AppIcon.NativeCalls == 0 && screenThrew && ScreenCatalog.NativeCalls == 0);

        // ---------- sticky note ----------
        var meta = NoteStore.Create("g:aurora", 200, 200, 330, 330);
        meta.Color = "g:lemon";   // one of the lightest gradients: hardest case for white text
        var nsw = Stopwatch.StartNew();
        var note = NoteManager.Open(meta.Id, activate: false)!;
        var ns = WindowFx.Of(note);
        t.Check("note first frame", await TestKit.Until(() => note.Ready, 6000), $"{nsw.Elapsed.TotalMilliseconds:0} ms");
        t.Check("note cloaked before first paint", ns.CloakedAtCreate > 0, $"{ns.CloakedAtCreate}");
        t.Check("note did not take focus (ShowActivated=false)", TestKit.NotForeground(note) && !note.ShowActivated);
        var r = Win32.Rect(note);
        bool onScreen = Win32.OnAnyMonitor(r.Left, r.Top, r.Width, r.Height);
        t.Check("note placed by physical pixels", !onScreen && r.Left == 200 + AppEnv.OffsetX && r.Top == 200 + AppEnv.OffsetY && r.Width == 330 && r.Height == 330, $"onScreen={onScreen} {r.Left},{r.Top} {r.Width}x{r.Height}");
        var noteMode = HwndSource.FromHwnd(Win32.Handle(note))?.CompositionTarget?.RenderMode;
        t.Check("note window renders in software", noteMode == RenderMode.SoftwareOnly, noteMode?.ToString() ?? "no target");
        note.Editor.Document.Blocks.Clear();
        note.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("오늘 할 일")) { FontWeight = FontWeights.Bold, FontSize = 18 });
        note.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("· 디자인 확인하기  · BongHub 테스트")));
        await TestKit.Delay(450);
        var nf = TestKit.Render(note, DarkUnder);
        t.Check("note frame not white", TestKit.WhitePercent(nf) < 1 && TestKit.DominantLum(nf) < 0.1, $"pure-white {TestKit.WhitePercent(nf):0.00}%, background luminance {TestKit.DominantLum(nf):0.000}");
        foreach (var key in new[] { "g:lemon", "yellow", "g:mist", "g:blocks", "g:mint", "charcoal" })
        {
            note.SetColor(key); await TestKit.Delay(260);
            var c = TestKit.Contrast(note, note.Editor, DarkUnder);
            t.Check($"note text contrast [{key}] ≥ 7", c.ratio >= 7, $"{c.ratio:0.0}:1 ({c.fg} on {c.bg})");
        }
        note.SetColor("g:aurora"); await TestKit.Delay(260);
        t.Check("note toolbar stays visible", note.Bar.Opacity >= 0.99 && note.NewBtn.IsVisible && note.FontBtn.IsVisible && note.ColorBtn.IsVisible && note.ListBtn.IsVisible && note.PinBtn.IsVisible && note.CloseBtn.IsVisible);
        note.Bar.BeginAnimation(UIElement.OpacityProperty, null); note.Bar.Opacity = 1;
        TestKit.SavePng(note, DarkUnder, "note_aurora.png");
        note.SetColor("g:peach"); await TestKit.Delay(260); TestKit.SavePng(note, DarkUnder, "note_peach.png");
        note.SetTitle("회의");
        string titleRaw = File.ReadAllText(NoteStore.MetaPath(note.Meta.Id));
        var titleBack = NoteStore.Parse(titleRaw);
        var titleOld = NoteStore.Parse("{\"Id\":\"legacy\",\"Color\":\"g:aurora\",\"X\":0,\"Y\":0,\"W\":10,\"H\":10,\"TopMost\":false,\"Open\":true,\"Zoom\":1,\"Text\":\"\",\"Modified\":\"\\/Date(0)\\/\"}");
        int titleAt = titleRaw.IndexOf("\"Title\":", StringComparison.Ordinal);
        int modifiedAt = titleRaw.IndexOf("\"Modified\":", StringComparison.Ordinal);
        t.Check("note title persists without a color name", note.TitleHint.Text == "제목" && titleBack != null && titleBack.Title == "회의" && titleAt > modifiedAt && titleOld != null && titleOld.Title.Length == 0 && !titleRaw.Contains("오로라"));
        note.UpdateLayout();
        double bodyPx = note.Editor.Document.FontSize;
        double titlePx = note.TitleBox.FontSize;
        var titleBrush = note.TitleBox.Foreground as SolidColorBrush;
        var caretBrush = note.TitleBox.CaretBrush as SolidColorBrush;
        var editorCaret = note.Editor.CaretBrush as SolidColorBrush;
        double titleX = note.TitleBox.TranslatePoint(new Point(note.TitleBox.Padding.Left, 0), note).X;
        double bodyX = note.Editor.TranslatePoint(new Point(note.Editor.Document.PagePadding.Left, 0), note).X;
        double titleBottom = note.TitleBox.TranslatePoint(new Point(0, note.TitleBox.ActualHeight), note).Y;
        double bodyTop = note.Editor.TranslatePoint(new Point(0, note.Editor.Document.PagePadding.Top), note).Y;
        double barBottom = note.Bar.TranslatePoint(new Point(0, note.Bar.ActualHeight), note).Y;
        double titleTop = note.TitleBox.TranslatePoint(new Point(0, 0), note).Y;
        double gap = bodyTop - titleBottom;
        bool ink = titleBrush != null && caretBrush != null && titleBrush.Color == caretBrush.Color && editorCaret != null && editorCaret.Color == NoteColors.Text && NoteColors.Lum(titleBrush.Color) + 0.001 >= NoteColors.Lum(NoteColors.Text);
        t.Check("note title uses the ink, sits under the toolbar, and leaves a short gap",
            ink && note.TitleBox.FontWeight == FontWeights.Bold && titlePx / bodyPx > 1.55 && titlePx / bodyPx < 1.66
            && Math.Abs(titleX - bodyX) < 2 && gap >= 8 && gap <= 18 && titleTop + 1 >= barBottom && titleTop - barBottom <= 12,
            $"ink={titleBrush?.Color} caret={caretBrush?.Color} editor={editorCaret?.Color} size {titlePx:0.0}/{bodyPx:0.0} align {titleX:0.0}/{bodyX:0.0} gap {gap:0.0} above {titleTop - barBottom:0.0}");
        note.SetTitle("");
        var hintBrush = note.TitleHint.Foreground as SolidColorBrush;
        t.Check("note title placeholder is dimmed", note.TitleHint.Visibility == Visibility.Visible && note.TitleHint.Opacity <= 0.45 && note.TitleHint.Opacity >= 0.25 && hintBrush != null && titleBrush != null && hintBrush.Color == titleBrush.Color);
        note.SetTitle("회의");
        foreach (var key in new[] { "g:lemon", "yellow", "g:mist", "g:blocks", "g:mint", "charcoal" })
        {
            note.SetColor(key); await TestKit.Delay(260);
            var c = TestKit.Contrast(note, note.TitleBox, DarkUnder);
            t.Check($"note title contrast [{key}] ≥ 7", c.ratio >= 7, $"{c.ratio:0.0}:1 ({c.fg} on {c.bg})");
        }
        note.SetColor("g:aurora");
        note.Editor.Document.Blocks.Clear();
        for (int line = 0; line < 40; line++)
            note.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("줄 " + line + " 스크롤 유지")) { FontSize = 28 });
        note.UpdateLayout();
        note.Editor.UpdateLayout();
        var noteScroll = note.EditorScroll;
        if (noteScroll != null) noteScroll.ScrollToVerticalOffset(Math.Max(noteScroll.ScrollableHeight, 0));
        note.Editor.CaretPosition = note.Editor.Document.ContentEnd;
        double scrollY = note.ReadScroll();
        int caret = note.ReadCaret();
        note.ParkEditor();
        note.UpdateLayout();
        await note.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        double parked = note.ReadScroll();
        note.ResumeEditor();
        note.UpdateLayout();
        await note.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        t.Check("note scroll and caret survive focus loss", scrollY > 8 && Math.Abs(parked - scrollY) < 3 && Math.Abs(note.ReadScroll() - scrollY) < 3 && note.ReadCaret() == caret, $"y={scrollY:0} parked={parked:0} back={note.ReadScroll():0} caret {caret}/{note.ReadCaret()}");
        string clipPng = Path.Combine(Path.GetTempPath(), "bonghub_smoke_clip_" + Environment.ProcessId + ".png");
        File.WriteAllBytes(clipPng, RtfPict.TinyPngBytes);
        note.Editor.Document.Blocks.Clear();
        var clipPara = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("앞 "));
        note.Editor.Document.Blocks.Add(clipPara);
        note.Editor.CaretPosition = clipPara.ContentEnd;
        note.InsertImageFile(clipPng);
        note.Editor.CaretPosition = note.Editor.Document.ContentEnd;
        note.Editor.CaretPosition.InsertTextInRun(" 뒤");
        note.Editor.Selection.Select(note.Editor.Document.ContentStart, note.Editor.Document.ContentEnd);
        int clipCalls = NoteClipSession.SystemCalls;
        bool copied = note.CopyMixed();
        var copiedPieces = NoteClip.Unpack(NoteClipSession.LastPack);
        try { File.Delete(clipPng); } catch { }
        t.Check("selection copy keeps text and image off the clipboard", copied && copiedPieces.Any(p => p.Text.Contains("앞")) && copiedPieces.Any(p => p.Png is { Length: > 0 }) && !NoteClip.Plain(copiedPieces).Contains("Image") && NoteClipSession.SystemCalls == clipCalls && (NoteClipSession.LastRtf ?? "").Contains("pngblip") && (NoteClipSession.LastHtml ?? "").Contains("앞") && NoteClipSession.LastHadBitmap);

        await TrayChecks(app, t, w);

        // timings
        var p = Process.GetCurrentProcess(); p.Refresh();
        t.Info($"memory: working set {p.WorkingSet64 / 1048576} MB, private {p.PrivateMemorySize64 / 1048576} MB");
        t.Info($"settings ContentRendered {renderedAt:0} ms after test start; process uptime {(DateTime.Now - p.StartTime).TotalMilliseconds:0} ms");
        t.Info("hidden-desktop limitation: DWM does not compose a hidden desktop, so what DWM itself shows in the very first frame cannot be captured here; the checks above verify the cloak/background mechanism and WPF's own first render.");
    }

    /// <summary>Dark menu, left click, 종료, and balloon chrome. No shell icon, so nothing is added to the notification area.</summary>
    static async Task TrayChecks(App app, TestKit t, MainWindow w)
    {
        tray = new TrayService(app, registerShell: false);
        var style = Application.Current.FindResource(typeof(ContextMenu)) as Style;
        t.Check("tray menu uses the dark ContextMenu style", ReferenceEquals(tray.Menu.Style, style));
        bool entries = tray.Menu.Items.Count == 6
            && tray.Menu.Items[0] is MenuItem open && (open.Header as string) == "BongHub 열기"
            && tray.Menu.Items[1] is MenuItem newer && (newer.Header as string) == "새 스티커 메모"
            && tray.Menu.Items[2] is MenuItem show && (show.Header as string) == "스티커 메모 모두 보이기"
            && tray.Menu.Items[3] is MenuItem hide && (hide.Header as string) == "스티커 메모 모두 숨기기"
            && tray.Menu.Items[4] is Separator
            && tray.Menu.Items[5] is MenuItem quit && (quit.Header as string) == "종료";
        t.Check("tray menu entries", entries, "열기, 새 스티커 메모, 모두 보이기, 모두 숨기기, separator, 종료");
        var (chrome, sample) = tray.CreateChromeVisual();
        var shot = TestKit.RenderElement(chrome, DarkUnder);
        t.Check("tray menu chrome is dark", TestKit.WhitePercent(shot) < 1 && TestKit.DominantLum(shot) < 0.08, $"pure-white {TestKit.WhitePercent(shot):0.00}%, background luminance {TestKit.DominantLum(shot):0.000}");
        var mc = TestKit.ContrastElement(chrome, sample, DarkUnder);
        t.Check("tray menu text contrast ≥ 7", mc.ratio >= 7, $"{mc.ratio:0.0}:1 ({mc.fg} on {mc.bg})");

        Win32.SendMessage(Win32.Handle(w), 0x0010, IntPtr.Zero, IntPtr.Zero);
        await TestKit.Until(() => !w.IsVisible, 2000);
        t.Check("tray menu 열기 opens settings", tray.RaiseHeader("BongHub 열기") && await TestKit.Until(() => w.IsVisible, 2000));
        await TestKit.Delay(350);
        t.Check("tray menu 열기 did not take focus", TestKit.NotForeground(w));

        Win32.SendMessage(Win32.Handle(w), 0x0010, IntPtr.Zero, IntPtr.Zero);
        await TestKit.Until(() => !w.IsVisible, 2000);
        tray.OnLeftClick();
        t.Check("tray left click opens settings", await TestKit.Until(() => w.IsVisible, 2000));
        await TestKit.Delay(350);
        t.Check("tray left click did not take focus", TestKit.NotForeground(w));
        t.Check("tray 종료 requests shutdown", tray.ExitRequestsShutdown);

        tray.ShowBalloon("기준선");
        t.Check("balloon text", tray.LastBalloonTitle == TrayService.BalloonTitle && tray.LastBalloonMessage == "기준선" && tray.LastBalloonVisual != null);
        var balloon = TestKit.RenderElement(tray.LastBalloonVisual!, DarkUnder);
        t.Check("balloon chrome is dark", TestKit.WhitePercent(balloon) < 1 && TestKit.DominantLum(balloon) < 0.08, $"pure-white {TestKit.WhitePercent(balloon):0.00}%, background luminance {TestKit.DominantLum(balloon):0.000}");
        var bc = TestKit.ContrastElement(tray.LastBalloonVisual!, tray.LastBalloonSample!, DarkUnder);
        t.Check("balloon text contrast ≥ 7", bc.ratio >= 7, $"{bc.ratio:0.0}:1 ({bc.fg} on {bc.bg})");
        t.Check("smoketest does not register a shell tray icon", !tray.ShellIconCreated && tray.NativeBalloonCalls == 0);
        t.Check("smoketest does not register a hotkey or a hook", NoteHotkey.NativeRegistrations == 0 && NoteHotkey.HookInstalls == 0 && !NoteHotkey.Registered);
        NoteHotkey.ReportFailure(tray, "Ctrl+Alt+N");
        t.Check("hotkey conflict balloon", tray.LastBalloonMessage != null && tray.LastBalloonMessage.Contains("단축키 등록 실패") && tray.LastBalloonMessage.Contains("Ctrl+Alt+N") && tray.NativeBalloonCalls == 0);

        int beforeHide = NoteManager.Windows.Count;
        tray.RaiseHeader("스티커 메모 모두 숨기기");
        t.Check("tray hide-all closes notes", beforeHide > 0 && await TestKit.Until(() => NoteManager.Windows.Count == 0, 4000), $"open {NoteManager.Windows.Count}");
        tray.RaiseHeader("스티커 메모 모두 보이기");
        t.Check("tray show-all opens notes", await TestKit.Until(() => NoteManager.Windows.Count >= 1, 4000), $"open {NoteManager.Windows.Count}");
        t.Check("tray note commands still installed no hook", NoteHotkey.HookInstalls == 0 && NoteHotkey.NativeRegistrations == 0);

        var bare = new HashSet<string>();
        foreach (var page in new[] { "notes", "general", "powertoys", "kakao", "presets" })
        {
            w.Navigate(page);
            w.UpdateLayout();
            var live = NoteManager.Windows.FirstOrDefault();
            if (live != null) { _ = live.Editor; live.ShowPalette(); live.CustomColor.OpenPicker(); }
            foreach (var item in ThemeAudit.Bare(tray.Menu)) bare.Add(item);
            live?.ClosePalette();
        }
        var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var viewer = ImageViewerWindow.Open(bmp);
        await TestKit.Delay(120);
        foreach (var item in ThemeAudit.Bare(tray.Menu)) bare.Add(item);
        t.Check("every control uses a themed style", bare.Count == 0, bare.Count == 0 ? "" : string.Join(", ", bare.Take(12)));
        viewer.Close();
    }
}
