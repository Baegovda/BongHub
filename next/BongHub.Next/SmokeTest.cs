using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace BongHub.Next;

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
        t.Save("next_smoketest.txt");
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

        foreach (var page in new[] { "notes", "general", "more", "about" })
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
        t.Check("note placed by physical pixels", r.Left == 200 + AppEnv.OffsetX && r.Top == 200 + AppEnv.OffsetY && r.Width == 330 && r.Height == 330, $"{r.Left},{r.Top} {r.Width}x{r.Height}");
        var noteMode = HwndSource.FromHwnd(Win32.Handle(note))?.CompositionTarget?.RenderMode;
        t.Check("note window renders in software", noteMode == RenderMode.SoftwareOnly, noteMode?.ToString() ?? "no target");
        note.Editor.Document.Blocks.Clear();
        note.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("오늘 할 일")) { FontWeight = FontWeights.Bold, FontSize = 18 });
        note.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("· 디자인 확인하기  · BongHub Next 테스트")));
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
        note.Bar.BeginAnimation(UIElement.OpacityProperty, null); note.Bar.Opacity = 1;   // show the hover bar for the screenshot
        TestKit.SavePng(note, DarkUnder, "note_aurora.png");
        note.SetColor("g:peach"); await TestKit.Delay(260); TestKit.SavePng(note, DarkUnder, "note_peach.png");

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
    }
}
