using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace BongHub;

/// <summary>--notetest --data &lt;copy&gt;: persistence round trip against a COPY of the 3.4.7 data.</summary>
public static class NoteTest
{
    public static async void Run(App app)
    {
        var t = new TestKit();
        try
        {
            if (AppEnv.IsDefaultData) { t.Check("refuses real data folder", false, "pass --data <copy>"); }
            else { AppEnv.Log("notetest start"); await Body(t); }
        }
        catch (Exception e) { t.Check("no exception", false, e.ToString()); }
        t.Save("notetest.txt");
        app.RequestShutdown(t.Fails > 0 ? 1 : 0);
    }

    static Dictionary<string, string> Hashes() => Directory.GetFiles(AppEnv.NotesDir).ToDictionary(f => Path.GetFileName(f)!, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    static int Pict(string path) { if (!File.Exists(path)) return 0; var s = Encoding.ASCII.GetString(File.ReadAllBytes(path)); return Regex.Matches(s, @"\\pict").Count; }

    static async Task Body(TestKit t)
    {
        var dir = AppEnv.NotesDir;
        var jsonFiles = Directory.GetFiles(dir, "*.json").Where(f => !Path.GetFileName(f).StartsWith('_')).ToArray();
        t.Check("loads every note", NoteStore.All.Count == jsonFiles.Length, $"{NoteStore.All.Count}/{jsonFiles.Length}, corrupt meta {NoteStore.CorruptMeta}");
        int fieldOk = 0;
        foreach (var f in jsonFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f, Encoding.UTF8)); var j = doc.RootElement;
            var m = NoteStore.Find(j.GetProperty("Id").GetString()!);
            if (m != null && m.X == j.GetProperty("X").GetInt32() && m.Y == j.GetProperty("Y").GetInt32() && m.W == j.GetProperty("W").GetInt32() && m.H == j.GetProperty("H").GetInt32()
                && m.TopMost == j.GetProperty("TopMost").GetBoolean() && m.Open == j.GetProperty("Open").GetBoolean() && (m.Color == j.GetProperty("Color").GetString() || !NoteColors.Valid(j.GetProperty("Color").GetString()))) fieldOk++;
        }
        t.Check("fields match the 3.4.7 files", fieldOk == jsonFiles.Length, $"{fieldOk}/{jsonFiles.Length}");
        t.Check("sorted by Modified desc", NoteStore.All.Zip(NoteStore.All.Skip(1)).All(p => p.First.Modified >= p.Second.Modified));

        // ---------- open everything, touch nothing ----------
        var before = Hashes();
        var wasClosed = NoteStore.All.Where(m => !m.Open).Select(m => m.Id + ".json").ToHashSet();   // opening a closed note sets Open=true (same as 3.4.7)
        var wins = NoteStore.All.ToList().Select(m => (m, w: NoteManager.Open(m.Id, activate: false)!)).ToList();
        await TestKit.Until(() => wins.All(x => x.w.Ready), 15000);
        t.Check("every note window rendered", wins.All(x => x.w.Ready), $"{wins.Count(x => x.w.Ready)}/{wins.Count}");
        int placed = 0; var bad = new List<string>();
        foreach (var (m, w) in wins)
        {
            var r = Win32.Rect(w);
            bool onScreen = Win32.OnAnyMonitor(r.Left, r.Top, r.Width, r.Height);
            int wantW = StickyNoteWindow.ClampNoteSpan(m.W, StickyNoteWindow.NoteMinWidth, StickyNoteWindow.NoteMaxWidth);
            int wantH = StickyNoteWindow.ClampNoteSpan(m.H, StickyNoteWindow.NoteMinHeight, StickyNoteWindow.NoteMaxHeight);
            if (!onScreen && r.Left - AppEnv.OffsetX == m.X && r.Top - AppEnv.OffsetY == m.Y && r.Width == wantW && r.Height == wantH) placed++; else bad.Add($"{m.Id}: want {m.X},{m.Y} {wantW}x{wantH} (stored {m.W}x{m.H}) got {r.Left - AppEnv.OffsetX},{r.Top - AppEnv.OffsetY} {r.Width}x{r.Height}{(onScreen ? " ON SCREEN" : "")}");
        }
        t.Check("exact position/size restore (physical px)", placed == wins.Count, bad.Count == 0 ? $"{placed}/{wins.Count}" : string.Join("; ", bad));
        foreach (var (m, w) in wins) t.Info($"note {m.Id}: load={w.LoadStatus}, pict={Pict(NoteStore.RtfPath(m.Id))}, color={m.Color}, text={m.Text.Length} chars");
        var empties = wins.Where(x => new FileInfo(NoteStore.RtfPath(x.m.Id)) is { Exists: true, Length: 0 }).ToList();
        t.Check("0-byte RTF opens without crash (meta text used)", empties.All(x => x.w.LoadStatus == "empty"), $"{empties.Count} empty file(s)");
        await TestKit.Delay(2200);   // longer than both debounce windows
        var after = Hashes();
        var changed = before.Where(kv => !after.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => kv.Key).ToList();
        var unexpected = changed.Where(k => !wasClosed.Contains(k)).ToList();
        t.Check("untouched notes are not rewritten", unexpected.Count == 0 && NoteStore.Writes == changed.Count, $"writes={NoteStore.Writes}, changed={changed.Count} (closed→opened meta: {wasClosed.Count})" + (unexpected.Count > 0 ? " unexpected: " + string.Join(",", unexpected) : ""));
        var legacySize = wins.Where(x => x.m.W < StickyNoteWindow.NoteMinWidth || x.m.H < StickyNoteWindow.NoteMinHeight || x.m.W > StickyNoteWindow.NoteMaxWidth || x.m.H > StickyNoteWindow.NoteMaxHeight).ToList();
        bool legacyShown = legacySize.All(x =>
        {
            var r = Win32.Rect(x.w);
            var raw = File.ReadAllText(NoteStore.MetaPath(x.m.Id), Encoding.UTF8);
            int wantW = StickyNoteWindow.ClampNoteSpan(x.m.W, StickyNoteWindow.NoteMinWidth, StickyNoteWindow.NoteMaxWidth);
            int wantH = StickyNoteWindow.ClampNoteSpan(x.m.H, StickyNoteWindow.NoteMinHeight, StickyNoteWindow.NoteMaxHeight);
            return r.Width == wantW && r.Height == wantH && raw.Contains($"\"W\":{x.m.W}") && raw.Contains($"\"H\":{x.m.H}");
        });
        t.Check("legacy out-of-range size is shown clamped and left in the file", legacyShown, legacySize.Count == 0 ? "none in this copy" : string.Join("; ", legacySize.Select(x =>
        {
            var r = Win32.Rect(x.w);
            var raw = File.ReadAllText(NoteStore.MetaPath(x.m.Id), Encoding.UTF8);
            int wantW = StickyNoteWindow.ClampNoteSpan(x.m.W, StickyNoteWindow.NoteMinWidth, StickyNoteWindow.NoteMaxWidth);
            int wantH = StickyNoteWindow.ClampNoteSpan(x.m.H, StickyNoteWindow.NoteMinHeight, StickyNoteWindow.NoteMaxHeight);
            bool fileKept = raw.Contains($"\"W\":{x.m.W}") && raw.Contains($"\"H\":{x.m.H}");
            return $"{x.m.Id} stored {x.m.W}x{x.m.H} want {wantW}x{wantH} hwnd {r.Width}x{r.Height} fileKept={fileKept} dpi={Win32.Scale(x.w):0.###}";
        })));

        // ---------- image note: edit text, images must survive ----------
        var img = wins.OrderByDescending(x => Pict(NoteStore.RtfPath(x.m.Id))).First();
        int pictBefore = Pict(NoteStore.RtfPath(img.m.Id));
        if (pictBefore > 0)
        {
            t.Check("display images are screen-sized", img.w.DisplayImageMaxEdge > 0 && img.w.DisplayImageMaxEdge <= RtfPict.MaxEdge,
                $"max edge {img.w.DisplayImageMaxEdge}, pixels {img.w.DisplayImagePixels}");
            t.Check("display images scale linearly", img.w.DisplayImagesUseLinearScaling());
            long payloadBefore = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(img.m.Id)));
            var rtfHashBefore = after[img.m.Id + ".rtf"];
            img.w.Editor.Document.Blocks.Add(new Paragraph(new Run("이미지 보존 테스트")));
            await TestKit.Delay(1400);
            int pictAfter = Pict(NoteStore.RtfPath(img.m.Id));
            var rtfHashAfter = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(NoteStore.RtfPath(img.m.Id))));
            bool kept = pictAfter >= pictBefore;
            t.Check("images survive an edit", kept, $"\\pict {pictBefore} → {pictAfter}; " + (img.w.ImageGuardHits > 0 ? "WPF could not re-encode images → image guard kept original .rtf, text saved to meta only" : rtfHashAfter != rtfHashBefore ? "rtf rewritten with images" : "rtf unchanged"));
            long payloadAfter = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(img.m.Id)));
            t.Check("original pict bytes kept", payloadAfter == payloadBefore, $"{payloadBefore} → {payloadAfter}");
            t.Info($"image note: guard hits {img.w.ImageGuardHits}, rtf writes {img.w.RtfWrites}, meta writes {img.w.MetaWrites}");
            img.w.CloseNote(); await TestKit.Delay(300);
            NoteStore.Load();
            var im = NoteStore.Find(img.m.Id)!;
            t.Check("image note meta text updated", im.Text.Contains("이미지 보존 테스트"));
        }
        else t.Info("no note with images in this copy");

        // ---------- corrupt / damaged RTF ----------
        var cases = new (string name, byte[] bytes)[] {
            ("garbage RTF header", Encoding.ASCII.GetBytes("{\\rtf1 {{{ \\bin999999 ")),
            ("not RTF at all", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0xFF, 0x13, 0x37, 0x00, 0x10 }) };
        foreach (var (name, bytes) in cases)
        {
            var cm = NoteStore.Create("g:mint", 300, 300, 300, 260);
            File.WriteAllBytes(NoteStore.RtfPath(cm.Id), bytes);
            cm.Text = "손상된 메모 백업 텍스트"; NoteStore.SaveMeta(cm);
            var cw = NoteManager.Open(cm.Id, activate: false)!;
            await TestKit.Until(() => cw.Ready, 5000);
            t.Check($"damaged RTF ({name}) opens with meta text", cw.LoadStatus == "corrupt" && cw.PlainText().Contains("손상된 메모"), $"status={cw.LoadStatus}, text='{cw.PlainText()}'");
            cw.Editor.Document.Blocks.Add(new Paragraph(new Run("복구 후 편집")));
            await TestKit.Delay(1300);
            t.Check($"damaged RTF ({name}) backed up before overwrite", Directory.GetFiles(dir, cm.Id + ".rtf.corrupt-*").Length == 1);
            cw.CloseNote(); await TestKit.Delay(200);
        }

        // ---------- full edit round trip on a new note ----------
        int writes0 = NoteStore.Writes;
        var nwq = NoteManager.NewNote();
        if (nwq == null) { t.Check("new note created", false, "NewNote returned null"); return; }
        var nw = nwq;
        t.Check("new note created", nw.Meta.Id.Length == 12 && File.Exists(NoteStore.MetaPath(nw.Meta.Id)) && File.Exists(NoteStore.RtfPath(nw.Meta.Id)), nw.Meta.Id);
        await TestKit.Until(() => nw.Ready, 5000);
        var id = nw.Meta.Id;
        // typing burst: 12 edits 40ms apart → exactly one RTF write after the burst ends
        nw.Editor.Document.Blocks.Clear();
        var para = new Paragraph(); nw.Editor.Document.Blocks.Add(para);
        string typed = "안녕 BongHub 한글 저장 테스트";
        foreach (var chunk in typed.Chunk(3)) { para.Inlines.Add(new Run(new string(chunk))); await TestKit.Delay(40); }
        para.Inlines.Add(new Bold(new Run(" 굵게")));
        await TestKit.Delay(1400);
        t.Check("typing burst → one RTF write", nw.RtfWrites == 1, $"rtf writes {nw.RtfWrites}, meta writes {nw.MetaWrites}");
        // drag gesture: many WM_MOVE, then WM_EXITSIZEMOVE → one meta write, immediately
        int mw0 = nw.MetaWrites;
        for (int i = 0; i < 8; i++) { Win32.Place(nw, AppEnv.OffsetX + 400 + i * 7, AppEnv.OffsetY + 260 + i * 3, 360, 300); await TestKit.Delay(16); }
        Win32.SendMessage(Win32.Handle(nw), Win32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        t.Check("drag end saves position at once", nw.MetaWrites == mw0 + 1 && nw.Meta.X == 449 && nw.Meta.Y == 281 && nw.Meta.W == 360 && nw.Meta.H == 300, $"meta writes +{nw.MetaWrites - mw0}, {nw.Meta.X},{nw.Meta.Y} {nw.Meta.W}x{nw.Meta.H}");
        int keepX = nw.Meta.X, keepY = nw.Meta.Y, keepW = nw.Meta.W, keepH = nw.Meta.H;
        bool iconTook = nw.CaptureSizeForTest(-32000, -32000, 40826, 34, iconic: true, zoomed: false);
        bool zoomTook = nw.CaptureSizeForTest(0, 0, 3840, 2160, iconic: false, zoomed: true);
        bool collapsedTook = nw.CaptureSizeForTest(keepX, keepY, 40826, 34, iconic: false, zoomed: false);
        t.Check("open note ignores minimized, maximized, and collapsed rects", !iconTook && !zoomTook && !collapsedTook && nw.Meta.X == keepX && nw.Meta.Y == keepY && nw.Meta.W == keepW && nw.Meta.H == keepH);
        await TestKit.Delay(900);
        t.Check("no extra write after drag", nw.MetaWrites == mw0 + 1, $"+{nw.MetaWrites - mw0}");
        nw.SetColor("g:ocean"); nw.SetTopMost(true);
        nw.CloseNote(); await TestKit.Delay(300);

        NoteStore.Load();
        var back = NoteStore.Find(id);
        t.Check("round trip: meta", back != null && back.Color == "g:ocean" && back.TopMost && !back.Open && back.X == 449 && back.Y == 281 && back.W == 360 && back.H == 300 && back.Text.Contains("한글 저장"),
            back == null ? "missing" : $"{back.Color} top={back.TopMost} open={back.Open} {back.X},{back.Y} {back.W}x{back.H} '{back.Text}'");
        var raw = File.ReadAllText(NoteStore.MetaPath(id), Encoding.UTF8);
        t.Check("JSON uses JavaScriptSerializer date", Regex.IsMatch(raw, "\"Modified\":\"\\\\/Date\\(\\d+\\)\\\\/\""), raw.Length > 200 ? raw[..200] : raw);
        var order = new[] { "Id", "Color", "X", "Y", "W", "H", "TopMost", "Open", "Zoom", "Text", "Modified" };
        var idx = order.Select(k => raw.IndexOf("\"" + k + "\":", StringComparison.Ordinal)).ToArray();
        t.Check("JSON field order matches 3.4.7", idx.All(i => i >= 0) && idx.Zip(idx.Skip(1)).All(p => p.First < p.Second));
        t.Check("JSON is UTF-8 without BOM", File.ReadAllBytes(NoteStore.MetaPath(id))[0] == (byte)'{');
        var rtf = File.ReadAllText(NoteStore.RtfPath(id), Encoding.ASCII);
        var fd = new FlowDocument(); using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(rtf))) new TextRange(fd.ContentStart, fd.ContentEnd).Load(ms, DataFormats.Rtf);
        var txt = new TextRange(fd.ContentStart, fd.ContentEnd).Text;
        t.Check("round trip: RTF text + bold", txt.Contains("한글 저장 테스트") && txt.Contains("굵게") && rtf.Contains("\\b"), $"'{txt.Trim()}'");
        t.Info($"total writes in edit phase: {NoteStore.Writes - writes0}");

        // ---------- read-only guard (as when 3.4.7 is running) ----------
        var hb = Hashes();
        AppEnv.ReadOnly = true;
        var any = NoteStore.All.First(); int blocked0 = NoteStore.BlockedWrites;
        bool wrote = NoteStore.SaveMeta(any);
        var nn = NoteManager.NewNote();
        AppEnv.ReadOnly = false;
        var ha = Hashes();
        t.Check("read-only blocks every write", !wrote && nn == null && NoteStore.BlockedWrites == blocked0 + 1 && hb.Count == ha.Count && hb.All(kv => ha[kv.Key] == kv.Value));

        await Phase1(t);

        foreach (var w in NoteManager.Windows.ToList()) w.Close();
    }

    /// <summary>Phase 1. These checks always run (they do not depend on data/notes having pictures). With the 7-note copy the earlier body adds 26, and this method adds the rest.</summary>
    static async Task Phase1(TestKit t)
    {
        var host = NoteManager.NewNote();
        t.Check("phase1 note", host != null);
        if (host == null) return;
        await TestKit.Until(() => host.Ready, 5000);
        host.Editor.Document.Blocks.Clear();
        var clickPara = new Paragraph(new Run("클릭"));
        host.Editor.Document.Blocks.Add(clickPara);
        var clickRun = clickPara.Inlines.FirstInline as Run;
        t.Check("click on a run does not throw", clickRun != null && host.ClickFrom(clickRun) && host.ClickFrom(clickPara));
        t.Check("noactivate cleared after show", (Win32.ExStyle(Win32.Handle(host)) & Win32.WS_EX_NOACTIVATE) == 0);
        Win32.EnableWindow(Win32.Handle(host), false);
        host.EnableDuringModal();
        t.Check("modal re-enables the note", Win32.IsWindowEnabled(Win32.Handle(host)));
        host.ShowLargeWarning(9L * 1024 * 1024);
        t.Check("large-note banner", host.LargeWarningVisible);

        ConfigStore.Notes.FontSize = 7;
        host.ApplyFontSize();
        t.Check("font 7pt", Math.Abs(host.Editor.Document.FontSize - StickyNoteWindow.PointsToDip(7)) < 0.2, host.Editor.Document.FontSize.ToString("0.00"));
        ConfigStore.Notes.FontSize = 48;
        host.ApplyFontSize();
        t.Check("font 48pt", Math.Abs(host.Editor.Document.FontSize - StickyNoteWindow.PointsToDip(48)) < 0.2);
        ConfigStore.Notes.FontSize = 99;
        host.ApplyFontSize();
        t.Check("font clamps at 48", Math.Abs(host.Editor.Document.FontSize - StickyNoteWindow.PointsToDip(48)) < 0.2);
        ConfigStore.Notes.FontSize = 11;
        host.ApplyFontSize();

        host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
        foreach (var pt in new[] { 24d, 20d, 16d, 13d, 11d })
        {
            host.ApplyHeading(pt);
            var sz = host.Editor.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
            t.Check($"heading {pt}pt", sz is double d && Math.Abs(d - StickyNoteWindow.PointsToDip(pt)) < 0.3, sz?.ToString() ?? "null");
        }
        host.GrowFont(100);
        var grown = host.Editor.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
        t.Check("grow clamps at 48pt", grown is double g && Math.Abs(g - StickyNoteWindow.PointsToDip(48)) < 0.3);
        ConfigStore.Notes.FontSize = 13;
        host.ApplyFontSize();
        host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
        host.GrowFont(4);
        var bumped = host.Editor.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
        host.ResetFontSize();
        var reset = host.Editor.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
        t.Check("reset font size uses the note default", bumped is double up && up > StickyNoteWindow.PointsToDip(13) + 1 && reset is double back && Math.Abs(back - StickyNoteWindow.PointsToDip(13)) < 0.4);
        ConfigStore.Notes.FontSize = 11;
        host.ApplyFontSize();

        int writes = host.MetaWrites;
        string saved = host.Meta.Color;
        host.PreviewColor("g:night");
        t.Check("hover preview does not save", host.ShownColorKey == "g:night" && host.Meta.Color == saved && host.MetaWrites == writes);
        host.PreviewColor(null);
        t.Check("hover leave restores", host.ShownColorKey == saved && host.Meta.Color == saved);
        host.SetColor("c:112233:AABBCC");
        t.Check("custom color saves", host.Meta.Color == "c:112233:AABBCC" && NoteColors.Valid("c:112233:AABBCC"));

        t.Check("hotkey parse Ctrl+Alt+N", NoteHotkey.TryParse("Ctrl+Alt+N", out uint mods, out uint vk) && mods == (NoteHotkey.Ctrl | NoteHotkey.Alt) && vk == 0x4E);
        t.Check("hotkey parse empty", !NoteHotkey.TryParse("", out _, out _) && !NoteHotkey.TryParse("Ctrl+Alt", out _, out _));
        t.Check("hotkey parse Win+Shift+V", NoteHotkey.TryParse("Win+Shift+V", out uint wm, out uint wvk) && (wm & NoteHotkey.Win) != 0 && (wm & NoteHotkey.Shift) != 0 && wvk == 0x56);
        t.Check("hotkey normalize", NoteHotkey.Normalize("alt+ctrl+n") == "Ctrl+Alt+N");
        int notesBefore = NoteStore.All.Count;
        NoteHotkey.DispatchNewNote();
        t.Check("simulated WM_HOTKEY opens a note", NoteStore.All.Count == notesBefore + 1);
        NoteHotkey.Apply("Ctrl+Alt+M");
        t.Check("test apply does not call RegisterHotKey", NoteHotkey.NativeRegistrations == 0 && NoteHotkey.HookInstalls == 0 && !NoteHotkey.Registered, $"reg {NoteHotkey.NativeRegistrations}");
        bool hkWrote = ConfigStore.SetNotesValue("Hotkey", System.Text.Json.Nodes.JsonValue.Create("Ctrl+Alt+M"));
        ConfigStore.Load();
        t.Check("hotkey round trip", hkWrote && ConfigStore.Notes.Hotkey == "Ctrl+Alt+M");
        ConfigStore.Notes.Hotkey = "Ctrl+Alt+N";
        ConfigStore.SetNotesValue("Hotkey", System.Text.Json.Nodes.JsonValue.Create("Ctrl+Alt+N"));
        bool colorWrote = ConfigStore.SetNotesValue("DefaultColor", System.Text.Json.Nodes.JsonValue.Create("c:010203:AABBCC"));
        ConfigStore.Load();
        t.Check("custom default color round trip", colorWrote && ConfigStore.Notes.DefaultColor == "c:010203:AABBCC" && NoteColors.Valid(ConfigStore.Notes.DefaultColor));
        ConfigStore.Notes.FontSize = 11;

        var blank = NoteStore.Create("g:mist", 120, 120, 200, 160);
        t.Check("empty delete removes files", NoteManager.Delete(blank.Id, confirmed: false) && !File.Exists(NoteStore.MetaPath(blank.Id)) && !File.Exists(NoteStore.RtfPath(blank.Id)));
        var kept = NoteStore.Create("g:sand", 140, 140, 220, 180);
        kept.Text = "지우지 마"; NoteStore.SaveMeta(kept);
        t.Check("non-empty delete waits for confirm", !NoteManager.Delete(kept.Id, confirmed: false) && File.Exists(NoteStore.MetaPath(kept.Id)));
        t.Check("confirmed delete removes the note", NoteManager.Delete(kept.Id, confirmed: true) && NoteStore.Find(kept.Id) == null);
        var blocked = NoteStore.Create("yellow", 160, 160, 200, 160);
        blocked.Text = "잠금"; NoteStore.SaveMeta(blocked);
        int blocked0 = NoteStore.BlockedWrites;
        AppEnv.ReadOnly = true;
        t.Check("read-only blocks delete", !NoteManager.Delete(blocked.Id, confirmed: true) && File.Exists(NoteStore.MetaPath(blocked.Id)) && NoteStore.BlockedWrites == blocked0 + 1);
        AppEnv.ReadOnly = false;
        NoteManager.Delete(blocked.Id, confirmed: true);

        var closed = NoteStore.Create("g:slate", 180, 180, 200, 160);
        closed.Open = false; NoteStore.SaveMeta(closed);
        NoteManager.ShowAll();
        t.Check("show all opens a closed note", NoteManager.IsOpen(closed.Id));
        NoteManager.HideAll();
        NoteStore.Load();
        var closedBack = NoteStore.Find(closed.Id);
        t.Check("hide all keeps the file and marks it closed", closedBack != null && !closedBack.Open && !NoteManager.IsOpen(closed.Id) && File.Exists(NoteStore.RtfPath(closed.Id)));
        host = NoteManager.NewNote();
        if (host != null)
        {
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("모두 복사")));
            host.FlushText();
        }
        t.Check("all text joins notes", NoteManager.AllText().Contains("모두 복사"));

        string group = RtfPict.BuildGroup(RtfPict.TinyPngBytes, jpeg: false, pxW: 1, pxH: 1, goalW: 150, goalH: 150);
        string resized = RtfPict.WithGoals(group, 300, 300);
        t.Check("goal edit keeps blip bytes", RtfPict.BlipHex(group) == RtfPict.BlipHex(resized) && resized.Contains(@"\picwgoal300") && resized.Contains(@"\pichgoal300"));
        var (gw, gh) = NoteImages.ScaleGoals(100, 50, 0.5);
        t.Check("50% goals are half the 96dpi twips", gw == 750 && gh == 375, $"{gw}x{gh}");
        t.Check("8MB warning threshold", NoteImages.OverWarn(NoteImages.WarnBytes + 1) && !NoteImages.OverWarn(NoteImages.WarnBytes));
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var gifBuilt = NoteImages.FromEncoded(gif, 120);
        t.Check("gif bytes become a png", gifBuilt != null && !gifBuilt.Jpeg && gifBuilt.Bytes.Length > 8 && gifBuilt.Bytes[0] == 0x89 && gifBuilt.Bytes[1] == (byte)'P' && gifBuilt.Group.Contains("\\pngblip"));
        t.Check("link hit", NoteLinks.TryHit("see https://example.com/a now", 6, out var url) && url == "https://example.com/a");
        t.Check("link miss", !NoteLinks.TryHit("see https://example.com/a now", 0, out _));
        t.Check("www link gains https", NoteLinks.TryHit("www.example.com", 0, out var www) && www == "https://www.example.com");

        var areas = new[] { new NoteLayout.Box(0, 0, 1920, 1040), new NoteLayout.Box(1920, 0, 2560, 1400) };
        var c1 = NoteLayout.Clamp(new NoteLayout.Box(9000, 300, 300, 300), areas, 32);
        var c2 = NoteLayout.Clamp(new NoteLayout.Box(100, 100, 300, 300), areas, 32);
        var c3 = NoteLayout.Clamp(new NoteLayout.Box(-500, 2000, 5000, 5000), areas, 32);
        t.Check("clamp far note into nearest area", areas[1].Contains(c1), c1.ToString());
        t.Check("clamp leaves a visible note", c2.X == 100 && c2.Y == 100 && c2.W == 300 && c2.H == 300);
        t.Check("clamp shrinks an oversized note", areas.Any(a => a.Contains(c3)), c3.ToString());
        var restore = new StickyNoteWindow.NoteSpan(442, 757, 647, 873);
        t.Check("minimized capture keeps the restore size", !StickyNoteWindow.TryNoteSize(restore, -32000, -32000, 40826, 34, iconic: true, zoomed: false, out _));
        t.Check("maximized capture keeps the restore size", !StickyNoteWindow.TryNoteSize(restore, -8, -8, 3856, 2112, iconic: false, zoomed: true, out _));
        t.Check("collapsed capture keeps the restore size", !StickyNoteWindow.TryNoteSize(restore, 3840, -16, 40826, 34, false, false, out _) && !StickyNoteWindow.TryNoteSize(restore, 0, 0, 3840, 60, false, false, out _) && !StickyNoteWindow.TryNoteSize(restore, 5759, -564, 960, 60, false, false, out _));
        bool movedSize = StickyNoteWindow.TryNoteSize(restore, 500, 400, 360, 300, false, false, out var nextSize);
        t.Check("normal move saves the restore size", movedSize && nextSize == new StickyNoteWindow.NoteSpan(500, 400, 360, 300));
        t.Check("showing the clamp of a legacy size is not a new save", !StickyNoteWindow.TryNoteSize(new StickyNoteWindow.NoteSpan(0, 0, 3840, 60), 0, 0, 3840, 90, false, false, out _) && !StickyNoteWindow.TryNoteSize(new StickyNoteWindow.NoteSpan(10, 20, 40826, 34), 10, 20, 30000, 90, false, false, out _));
        t.Check("span clamp is the note minimum and maximum", StickyNoteWindow.ClampNoteSpan(34, StickyNoteWindow.NoteMinHeight, StickyNoteWindow.NoteMaxHeight) == 90 && StickyNoteWindow.ClampNoteSpan(60, StickyNoteWindow.NoteMinHeight, StickyNoteWindow.NoteMaxHeight) == 90 && StickyNoteWindow.ClampNoteSpan(40826, StickyNoteWindow.NoteMinWidth, StickyNoteWindow.NoteMaxWidth) == 30000 && StickyNoteWindow.ClampNoteSpan(200, StickyNoteWindow.NoteMinWidth, StickyNoteWindow.NoteMaxWidth) == 200);
        var pictOrig = Encoding.ASCII.GetBytes("{\\rtf1\\ansi{\\pict\\pngblip ab}{\\pict\\pngblip cd}}");
        var pictNone = Encoding.ASCII.GetBytes("{\\rtf1\\ansi deleted}");
        var pictOne = Encoding.ASCII.GetBytes("{\\rtf1\\ansi{\\pict\\pngblip ab}}");
        var blockedSave = NoteRtf.Choose(pictOrig, pictNone, 2, 5, out var blockedWarn);
        t.Check("guard blocks images still on screen", blockedWarn != null && blockedWarn.Contains("screen=2") && blockedWarn.Contains("groups=5") && blockedWarn.Contains("picts=0") && blockedSave.AsSpan().SequenceEqual(pictOrig));
        var deletedSave = NoteRtf.Choose(pictOrig, pictNone, 0, 5, out var deletedWarn);
        t.Check("guard allows a deleted image", deletedWarn == null && deletedSave.AsSpan().SequenceEqual(pictNone));
        var matchedSave = NoteRtf.Choose(pictOrig, pictOne, 1, 5, out var matchedWarn);
        t.Check("guard allows a save that matches the screen", matchedWarn == null && NoteRtf.Picts(matchedSave) == 1);
        var pile = NoteStore.Create("yellow", 40, 40, 200, 200);
        File.WriteAllBytes(NoteStore.RtfPath(pile.Id), Encoding.ASCII.GetBytes("{\\rtf1\\ansi pile}"));
        NoteStore.SavePendingRtf(pile, Encoding.ASCII.GetBytes("{\\rtf1\\ansi one}"));
        NoteStore.SavePendingRtf(pile, Encoding.ASCII.GetBytes("{\\rtf1\\ansi two}"));
        var pileFiles = NoteStore.PendingRtfs(pile.Id);
        t.Check("a note keeps one pending file", pileFiles.Length == 1 && File.ReadAllText(pileFiles[0], Encoding.ASCII).Contains("two"), string.Join(",", pileFiles.Select(Path.GetFileName)));
        var stale = NoteStore.Create("yellow", 48, 48, 200, 200);
        File.WriteAllBytes(NoteStore.RtfPath(stale.Id), Encoding.ASCII.GetBytes("{\\rtf1\\ansi stale}"));
        var oldPending = NoteStore.PendingRtfPath(stale.Id, "19990101010101000");
        var oldPending2 = NoteStore.PendingRtfPath(stale.Id, "19990101010101001");
        File.WriteAllBytes(oldPending, Encoding.ASCII.GetBytes("{\\rtf1\\ansi old1}"));
        File.WriteAllBytes(oldPending2, Encoding.ASCII.GetBytes("{\\rtf1\\ansi old2}"));
        File.SetLastWriteTimeUtc(oldPending, new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(oldPending2, new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(NoteStore.RtfPath(stale.Id), DateTime.UtcNow);
        NoteStore.PrunePending(stale.Id);
        t.Check("pending older than the rtf is removed", !File.Exists(oldPending) && !File.Exists(oldPending2) && NoteStore.PendingRtfs(stale.Id).Length == 0);

        var picNote = NoteManager.NewNote();
        t.Check("image note opened", picNote != null);
        if (picNote == null) return;
        await TestKit.Until(() => picNote.Ready, 5000);
        string pngPath = Path.Combine(AppEnv.NotesDir, "tiny-phase1.png");
        File.WriteAllBytes(pngPath, RtfPict.TinyPngBytes);
        t.Check("drop inserts a png", picNote.AcceptDrop(new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { pngPath })));
        picNote.FlushText();
        var pictBytes = File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id));
        string hex0 = RtfPict.BlipHex(RtfPict.Parse(pictBytes)[0].Group);
        t.Check("inserted blip is png and matches the file", pictBytes.Length > 20 && RtfPict.Parse(pictBytes).Count == 1 && hex0 == Convert.ToHexString(RtfPict.TinyPngBytes).ToLowerInvariant() && Encoding.ASCII.GetString(pictBytes).Contains(@"\pngblip"));
        t.Check("50% scale keeps blip bytes", picNote.SetImageScale(0, 0.5));
        picNote.FlushText();
        var scaled = File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id));
        var sp = RtfPict.Parse(scaled)[0];
        t.Check("50% writes picwgoal only", RtfPict.BlipHex(sp.Group) == hex0 && sp.GoalW == NoteImages.ScaleGoals(1, 1, 0.5).GoalW, $"goal {sp.GoalW}");
        t.Check("fit width keeps blip bytes", picNote.FitImageWidth(0));
        picNote.FlushText();
        t.Check("fit width payload", RtfPict.BlipHex(RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id)))[0].Group) == hex0);
        t.Check("second image inserts", picNote.InsertImageFile(pngPath));
        picNote.FlushText();
        t.Check("two picts", RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id))).Count == 2);
        t.Check("move keeps both payloads", picNote.MoveImage(0, 1));
        picNote.FlushText();
        var moved = RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id)));
        t.Check("moved pict bytes", moved.Count == 2 && RtfPict.BlipHex(moved[0].Group) == hex0 && RtfPict.BlipHex(moved[1].Group) == hex0);
        var copied = picNote.CopyImage(0);
        t.Check("copy returns the blip bytes", copied != null && copied.Length == RtfPict.TinyPngBytes.Length);
        t.Check("delete image leaves the other blip", picNote.DeleteImage(0));
        picNote.FlushText();
        var left = RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id)));
        t.Check("one pict remains", left.Count == 1 && RtfPict.BlipHex(left[0].Group) == hex0);
        picNote.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("그림 유지")));
        await TestKit.Delay(1200);
        var afterText = File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id));
        t.Check("text edit keeps the pict payload", RtfPict.PayloadBytes(afterText) == RtfPict.TinyPngBytes.Length && Encoding.ASCII.GetString(afterText).Contains(@"\pngblip"));

        long beforeDrop = RtfPict.PayloadBytes(afterText);
        picNote.ForcePictMismatchForTest();
        picNote.FlushText();
        long afterDrop = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id)));
        t.Check("pict drop keeps file bytes", afterDrop == beforeDrop, $"{beforeDrop} → {afterDrop} guard {picNote.ImageGuardHits}");
        t.Check("pict drop stays on the wpf editor", picNote.ImageGuardHits > 0 && picNote.PlainText().Contains("떨어짐"), picNote.PlainText());
        picNote.Editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("리치")));
        await TestKit.Delay(1200);
        picNote.FlushText();
        var afterEdit = File.ReadAllBytes(NoteStore.RtfPath(picNote.Meta.Id));
        t.Check("later edit keeps pngblip bytes", RtfPict.PayloadBytes(afterEdit) == beforeDrop && Encoding.ASCII.GetString(afterEdit).Contains(@"\pngblip"), $"payload {RtfPict.PayloadBytes(afterEdit)}");
        t.Check("editor shows the edit", picNote.PlainText().Contains("리치") || picNote.PlainText().Contains("떨어짐"), picNote.PlainText());
        t.Check("image guard keeps one pending file", NoteStore.PendingRtfs(picNote.Meta.Id).Length == 1, string.Join(",", NoteStore.PendingRtfs(picNote.Meta.Id).Select(Path.GetFileName)));
        var erased = NoteManager.NewNote();
        if (erased != null)
        {
            await TestKit.Until(() => erased.Ready, 5000);
            erased.InsertImageFile(pngPath);
            erased.FlushText();
            int erasedGuards = erased.ImageGuardHits;
            erased.RemoveImagesFromDocumentForTest();
            erased.FlushText();
            var erasedBytes = File.ReadAllBytes(NoteStore.RtfPath(erased.Meta.Id));
            t.Check("deleting the pictures updates the rtf", erased.ImageGuardHits == erasedGuards && NoteRtf.Picts(erasedBytes) == 0 && NoteStore.PendingRtfs(erased.Meta.Id).Length == 0 && !Encoding.ASCII.GetString(erasedBytes).Contains(@"\pngblip"), $"guard {erased.ImageGuardHits} picts {NoteRtf.Picts(erasedBytes)}");
            erased.InsertImageFile(pngPath);
            erased.FlushText();
            int restoredCount = RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(erased.Meta.Id))).Count;
            t.Check("a new picture does not restore a deleted one", erased.ImageGuardHits == erasedGuards && restoredCount == 1, $"picts {restoredCount} guard {erased.ImageGuardHits}");
        }
        else
        {
            t.Check("deleting the pictures updates the rtf", false, "no note");
            t.Check("a new picture does not restore a deleted one", false, "no note");
        }

        var guardNote = NoteManager.NewNote();
        t.Check("guard note opened", guardNote != null);
        if (guardNote != null)
        {
            await TestKit.Until(() => guardNote.Ready, 5000);
            guardNote.InsertImageFile(pngPath);
            guardNote.FlushText();
            int pictsBeforeLoose = RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(guardNote.Meta.Id))).Count;
            int guardsBeforeLoose = guardNote.ImageGuardHits;
            guardNote.InsertLooseImageForTest();
            guardNote.FlushText();
            var looseParsed = RtfPict.Parse(File.ReadAllBytes(NoteStore.RtfPath(guardNote.Meta.Id)));
            string tinyHex = Convert.ToHexString(RtfPict.TinyPngBytes).ToLowerInvariant();
            t.Check("loose image is stored as png", guardNote.ImageGuardHits == guardsBeforeLoose && looseParsed.Count == pictsBeforeLoose + 1 && looseParsed.All(p => p.Group.Contains("\\pngblip")) && looseParsed.Any(p => RtfPict.BlipHex(p.Group) == tinyHex), $"picts {looseParsed.Count} guard {guardNote.ImageGuardHits}");
            long richPayload = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(guardNote.Meta.Id)));
            guardNote.ForcePictMismatchForTest();
            NoteManager.FlushAll();
            long afterQuit = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(guardNote.Meta.Id)));
            string? pending = NoteStore.NewestPendingRtf(guardNote.Meta.Id);
            t.Check("quit flush keeps the rtf and writes a pending copy", guardNote.ImageGuardHits > guardsBeforeLoose && afterQuit == richPayload && pending != null && new FileInfo(pending).Length > 20, $"payload {richPayload} → {afterQuit} pending {pending ?? "none"}");
            string guardId = guardNote.Meta.Id;
            guardNote.CloseNote();
            await TestKit.Delay(300);
            var reopened = NoteManager.Open(guardId, activate: false);
            t.Check("pending rtf loads when it is newer", reopened != null);
            if (reopened != null)
            {
                await TestKit.Until(() => reopened.Ready, 5000);
                reopened.Editor.Document.Blocks.Add(new Paragraph(new Run("이어쓰기")));
                reopened.FlushText();
                long still = RtfPict.PayloadBytes(File.ReadAllBytes(NoteStore.RtfPath(guardId)));
                t.Check("screen-matching edit replaces the rtf and clears pending", reopened.PlainText().Contains("떨어짐") && reopened.PlainText().Contains("이어쓰기") && NoteStore.PendingRtfs(guardId).Length == 0 && still != richPayload, $"text '{reopened.PlainText()}' payload {still}/{richPayload} pending {NoteStore.PendingRtfs(guardId).Length}");
            }
            else t.Check("screen-matching edit replaces the rtf and clears pending", false, "no reopen");
        }
        else
        {
            t.Check("loose image is stored as png", false, "no note");
            t.Check("quit flush keeps the rtf and writes a pending copy", false, "no note");
            t.Check("pending rtf loads when it is newer", false, "no note");
            t.Check("screen-matching edit replaces the rtf and clears pending", false, "no note");
        }

        var viewNote = NoteManager.NewNote();
        ImageViewerWindow? viewer = null;
        if (viewNote != null)
        {
            await TestKit.Until(() => viewNote.Ready, 5000);
            viewNote.InsertImageFile(pngPath);
            viewer = viewNote.OpenViewer(0);
        }
        t.Check("viewer opens off-screen", viewer != null && Win32.Rect(viewer).Left < -10000, viewer == null ? "null" : Win32.Rect(viewer).Left.ToString());
        if (viewer != null)
        {
            viewer.SetZoom(2, false);
            t.Check("viewer zoom", Math.Abs(viewer.Zoom - 2) < 0.01);
            viewer.Close();
        }
        else t.Check("viewer zoom", false, "no viewer");

        if (host != null)
        {
            t.Check("toolbar stays visible", host.Bar.Opacity >= 0.99 && host.NewBtn.IsVisible && host.FontBtn.IsVisible && host.ColorBtn.IsVisible && host.ListBtn.IsVisible && host.PinBtn.IsVisible && host.CloseBtn.IsVisible);
            host.SetTitle("회의록");
            string titleRaw = File.ReadAllText(NoteStore.MetaPath(host.Meta.Id), Encoding.UTF8);
            var titleBack = NoteStore.Parse(titleRaw);
            var titleOld = NoteStore.Parse("{\"Id\":\"legacy\",\"Color\":\"yellow\",\"X\":0,\"Y\":0,\"W\":10,\"H\":10,\"TopMost\":false,\"Open\":true,\"Zoom\":1,\"Text\":\"본문\",\"Modified\":\"\\/Date(0)\\/\"}");
            var titleOrder = new[] { "Id", "Color", "X", "Y", "W", "H", "TopMost", "Open", "Zoom", "Text", "Modified" };
            var titleIdx = titleOrder.Select(k => titleRaw.IndexOf("\"" + k + "\":", StringComparison.Ordinal)).ToArray();
            t.Check("title persists and old meta ignores it", host.TitleHint.Text == "제목" && titleBack != null && titleBack.Title == "회의록" && titleRaw.Contains("\"Title\":\"회의록\"") && titleRaw.IndexOf("\"Modified\":", StringComparison.Ordinal) < titleRaw.IndexOf("\"Title\":", StringComparison.Ordinal) && titleIdx.All(i => i >= 0) && titleIdx.Zip(titleIdx.Skip(1)).All(p => p.First < p.Second) && titleOld != null && titleOld.Title.Length == 0);
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("본문시작")));
            host.Editor.CaretPosition = host.Editor.Document.ContentEnd;
            int titleCaret = host.ReadCaret();
            bool titleEntered = host.MoveToBody();
            t.Check("enter in the title moves to the body start", titleEntered && titleCaret > 0 && host.ReadCaret() == 0);
            host.Editor.Document.Blocks.Clear();
            for (int line = 0; line < 40; line++)
                host.Editor.Document.Blocks.Add(new Paragraph(new Run("줄 " + line + " 스크롤 유지")) { FontSize = 28 });
            host.UpdateLayout();
            host.Editor.UpdateLayout();
            var hostScroll = host.EditorScroll;
            if (hostScroll != null) hostScroll.ScrollToVerticalOffset(Math.Max(hostScroll.ScrollableHeight, 0));
            host.Editor.CaretPosition = host.Editor.Document.ContentEnd;
            double scrollY = host.ReadScroll();
            int caret = host.ReadCaret();
            host.ParkEditor();
            host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            double parked = host.ReadScroll();
            host.ResumeEditor();
            host.UpdateLayout();
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            t.Check("scroll and caret survive focus loss", scrollY > 8 && Math.Abs(parked - scrollY) < 3 && Math.Abs(host.ReadScroll() - scrollY) < 3 && host.ReadCaret() == caret, $"y={scrollY:0} parked={parked:0} back={host.ReadScroll():0} caret {caret}/{host.ReadCaret()}");
            string clipPng = Path.Combine(Path.GetTempPath(), "bonghub_note_clip_" + Environment.ProcessId + ".png");
            File.WriteAllBytes(clipPng, RtfPict.TinyPngBytes);
            host.Editor.Document.Blocks.Clear();
            var clipPara = new Paragraph(new Run("앞 "));
            host.Editor.Document.Blocks.Add(clipPara);
            host.Editor.CaretPosition = clipPara.ContentEnd;
            host.InsertImageFile(clipPng);
            host.Editor.CaretPosition = host.Editor.Document.ContentEnd;
            host.Editor.CaretPosition.InsertTextInRun(" 뒤");
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            int clipCalls = NoteClipSession.SystemCalls;
            bool mixedCopied = host.CopyMixed();
            var copiedPieces = NoteClip.Unpack(NoteClipSession.LastPack);
            try { File.Delete(clipPng); } catch { }
            t.Check("selection copy keeps text and image", mixedCopied && copiedPieces.Any(p => p.Text.Contains("앞")) && copiedPieces.Any(p => p.Png is { Length: > 0 }) && !NoteClip.Plain(copiedPieces).Contains("Image") && NoteClipSession.SystemCalls == clipCalls && (NoteClipSession.LastRtf ?? "").Contains("pngblip") && (NoteClipSession.LastHtml ?? "").Contains("앞") && NoteClipSession.LastHadBitmap);
        }
        else
        {
            t.Check("toolbar stays visible", false, "no note");
            t.Check("title persists and old meta ignores it", false, "no note");
            t.Check("enter in the title moves to the body start", false, "no note");
            t.Check("scroll and caret survive focus loss", false, "no note");
            t.Check("selection copy keeps text and image", false, "no note");
        }
        t.Check("link open does not launch during notetest", host != null && host.TryOpenLink("go https://example.com/x", 3, launch: true) && host.LastLink.StartsWith("https://") && !host.LinkLaunched);
        var wwwHit = NoteLinks.Find("go www.example.com, now");
        t.Check("www link trims the trailing comma", wwwHit.Count == 1 && wwwHit[0].Url == "https://www.example.com" && wwwHit[0].Length == "www.example.com".Length);
        bool linkReadable = true;
        string linkBad = "";
        foreach (var key in NoteColors.PaletteKeys)
        {
            var ink = NoteColors.LinkInk(key);
            double linkRatio = Math.Min(NoteColors.Contrast(ink, NoteColors.SurfA(key)), Math.Min(NoteColors.Contrast(ink, NoteColors.SurfB(key)), NoteColors.Contrast(ink, NoteColors.Body(key))));
            if (linkRatio < 4.5)
            {
                linkReadable = false;
                linkBad = key + " " + linkRatio.ToString("0.00");
                break;
            }
        }
        t.Check("link ink is readable on every note color", linkReadable, linkBad);
        bool inkOk = true;
        string inkBad = "";
        foreach (var key in NoteColors.PaletteKeys)
        {
            var fg = NoteColors.Ink(key);
            if (fg != NoteColors.DarkText && fg != NoteColors.Text)
            {
                inkOk = false;
                inkBad = key + " ink";
                break;
            }
            NoteColors.Raw(key, out var rawA, out var rawB);
            var samples = new[] { (NoteColors.SurfA(key), rawA), (NoteColors.SurfB(key), rawB) };
            foreach (var (surf, raw) in samples)
            {
                bool towardWhite = NoteColors.Lum(surf) + 1e-9 >= NoteColors.Lum(raw);
                bool hue = towardWhite
                    ? surf.R >= raw.R && surf.G >= raw.G && surf.B >= raw.B
                    : surf.R <= raw.R && surf.G <= raw.G && surf.B <= raw.B;
                if (!hue)
                {
                    inkOk = false;
                    inkBad = key + " hue";
                    break;
                }
            }
            if (!inkOk) break;
            double ratio = Math.Min(NoteColors.Contrast(fg, NoteColors.SurfA(key)), Math.Min(NoteColors.Contrast(fg, NoteColors.SurfB(key)), NoteColors.Contrast(fg, NoteColors.Body(key))));
            if (ratio < 7)
            {
                inkOk = false;
                inkBad = key + " " + ratio.ToString("0.00");
                break;
            }
        }
        NoteColors.Raw("g:lemon", out var lemonA, out var lemonB);
        NoteColors.Raw("yellow", out var yellowA, out var yellowB);
        NoteColors.Raw("g:mist", out var mistA, out var mistB);
        if (inkOk && (NoteColors.SurfA("g:lemon") != lemonA || NoteColors.SurfB("g:lemon") != lemonB || NoteColors.SurfA("yellow") != yellowA || NoteColors.SurfB("yellow") != yellowB))
        {
            inkOk = false;
            inkBad = "vivid";
        }
        if (inkOk && (NoteColors.Lum(NoteColors.SurfA("g:mist")) <= NoteColors.Lum(mistA) || NoteColors.Lum(NoteColors.SurfB("g:mist")) <= NoteColors.Lum(mistB)))
        {
            inkOk = false;
            inkBad = "mist";
        }
        NoteColors.Raw("g:blocks", out var blocksA, out _);
        NoteColors.Raw("g:lemon", out var lemonRaw, out _);
        NoteColors.Raw("yellow", out var yellowRaw, out _);
        static int Chroma(System.Windows.Media.Color c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        static int Mean(System.Windows.Media.Color c) => (c.R + c.G + c.B) / 3;
        t.Check("preset colors are softer", inkOk && Chroma(blocksA) < 150 && Mean(blocksA) < 200 && Chroma(lemonRaw) < 120 && Mean(lemonRaw) < 200 && Chroma(yellowRaw) < 120 && Mean(yellowRaw) < 210);
        t.Check("note ink clears 7 on light and dark colors", inkOk, inkBad);
        var legacy = NoteStore.Create("g:lemon", 120, 120, 280, 240);
        legacy.Color = "g:lemon";
        NoteStore.SaveMeta(legacy);
        string legacyRtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss\\fcharset0 Calibri;}}{\\colortbl ;\\red240\\green240\\blue246;\\red236\\green236\\blue240;\\red0\\green0\\blue255;}\\pard\\cf1\\f0\\fs22 base\\par\\cf2 old\\par\\cf3 blue\\par}";
        File.WriteAllBytes(NoteStore.RtfPath(legacy.Id), Encoding.ASCII.GetBytes(legacyRtf));
        var legacyNote = NoteManager.Open(legacy.Id, activate: false);
        t.Check("legacy note opened", legacyNote != null);
        if (legacyNote != null)
        {
            await TestKit.Until(() => legacyNote.Ready, 5000);
            var lemonInk = NoteColors.Ink("g:lemon");
            t.Check("legacy default ink follows the note", legacyNote.TextInheritsInk("base") && legacyNote.TextInheritsInk("old") && legacyNote.PlainText().Contains("base") && lemonInk != NoteColors.Text && NoteColors.Contrast(lemonInk, NoteColors.Body("g:lemon")) >= 7);
            t.Check("legacy custom color stays", legacyNote.TextKeepsColor("blue", 0, 0, 255));
            legacyNote.Editor.Document.Blocks.Add(new Paragraph(new Run(" added")));
            legacyNote.FlushText();
            var savedLegacy = File.ReadAllBytes(NoteStore.RtfPath(legacyNote.Meta.Id));
            string savedText = RtfPict.Latin1.GetString(savedLegacy);
            bool applied = NoteRtf.AppliesAutomaticInk(savedLegacy);
            t.Check("legacy default color is not written back", !applied && savedText.Contains("blue") && savedText.Contains("added") && savedText.Contains("\\red0\\green0\\blue255"), applied ? "automatic cf remains" : savedText.Length > 180 ? savedText[..180] : savedText);
        }
        else
        {
            t.Check("legacy default ink follows the note", false, "no note");
            t.Check("legacy custom color stays", false, "no note");
            t.Check("legacy default color is not written back", false, "no note");
        }
        if (host != null)
        {
            host.Editor.Document.Blocks.Clear();
            var linkPara = new Paragraph();
            host.Editor.Document.Blocks.Add(linkPara);
            host.Editor.CaretPosition = linkPara.ContentEnd;
            host.Editor.CaretPosition.InsertTextInRun("see https://bong.dev/a. and www.bong.dev");
            var shown = host.DisplayLinks();
            t.Check("typing paints http and www without eating punctuation", shown.Count == 2 && shown.Contains("https://bong.dev/a") && shown.Contains("www.bong.dev") && host.PlainText().Contains("https://bong.dev/a."));
            int beforeSpace = host.ReadCaret();
            host.Editor.CaretPosition.InsertTextInRun("!");
            t.Check("link styling keeps the caret on the line", host.ReadCaret() == beforeSpace + 1 && host.PlainText().EndsWith("!"));
            t.Check("link hover underlines and shows the tip", host.HoverFirstLink());
            t.Check("plain click does not open a link", !host.OpenDisplayedLink(false) && !host.LinkLaunched);
            t.Check("ctrl click opens the link without a process", host.OpenDisplayedLink(true) && host.LastLink == "https://bong.dev/a" && !host.LinkLaunched);
            var linkNote = NoteManager.NewNote();
            t.Check("link note opened", linkNote != null);
            if (linkNote != null)
            {
                await TestKit.Until(() => linkNote.Ready, 5000);
                linkNote.Editor.Document.Blocks.Clear();
                var fresh = new Paragraph(new Run("see https://bong.dev/a. and www.bong.dev"));
                linkNote.Editor.Document.Blocks.Add(fresh);
                linkNote.RefreshLinks();
                linkNote.FlushText();
                string linkRtf = Encoding.ASCII.GetString(File.ReadAllBytes(NoteStore.RtfPath(linkNote.Meta.Id)));
                t.Check("link color stays out of the rtf", !linkRtf.Contains("HYPERLINK", StringComparison.OrdinalIgnoreCase) && linkRtf.Contains("https://bong.dev/a") && linkRtf.Contains("www.bong.dev"));
            }
            else t.Check("link color stays out of the rtf", false, "no link note");
            host.SetColor("g:lemon");
            t.Check("link ink follows the note color", host.LinkInkMatchesNote());
        }
        else
        {
            t.Check("typing paints http and www without eating punctuation", false, "no note");
            t.Check("link styling keeps the caret on the line", false, "no note");
            t.Check("link hover underlines and shows the tip", false, "no note");
            t.Check("plain click does not open a link", false, "no note");
            t.Check("ctrl click opens the link without a process", false, "no note");
            t.Check("link color stays out of the rtf", false, "no note");
            t.Check("link ink follows the note color", false, "no note");
        }
        t.Check("shortcut ctrl+t is handled", host != null && host.HandleShortcut(System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control));
        if (host != null)
        {
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("안녕복사")));
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            NoteClipSession.RichThrows = 0;
            bool textCopied = host.CopySelection(false);
            t.Check("text-only copy reaches the clipboard", textCopied && NoteClipSession.LastPlain.Contains("안녕복사"));
            NoteClipSession.RichThrows = 40;
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            bool fellBack = host.CopySelection(false);
            t.Check("copy keeps plain text when rich clipboard data throws", fellBack && NoteClipSession.LastPlain.Contains("안녕복사") && NoteClipSession.LastError != null);
            NoteClipSession.RichThrows = 0;
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("자를글")));
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            bool cut = host.HandleShortcut(System.Windows.Input.Key.X, System.Windows.Input.ModifierKeys.Control);
            t.Check("ctrl+x copies then clears the selection", cut && NoteClipSession.LastPlain.Contains("자를글") && host.PlainText().Length == 0);
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("인서트")));
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            bool insert = host.HandleShortcut(System.Windows.Input.Key.Insert, System.Windows.Input.ModifierKeys.Control);
            t.Check("ctrl+insert copies", insert && NoteClipSession.LastPlain.Contains("인서트"));
            host.TitleBox.Text = "제목만";
            host.TitleBox.SelectAll();
            bool titleCopied = host.CopyTitleText(false);
            t.Check("title copy is plain text", titleCopied && NoteClipSession.LastPlain == "제목만" && string.IsNullOrEmpty(NoteClipSession.LastXaml));
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, host.Editor);
            t.Check("editor context menu can copy", host.Editor.ContextMenu != null && host.Editor.ContextMenu.Items.Count >= 4 && NoteClipSession.LastPlain.Contains("인서트"));
            var pngOnly = new DataObject();
            pngOnly.SetData("PNG", RtfPict.TinyPngBytes);
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph());
            host.Editor.CaretPosition = host.Editor.Document.ContentEnd;
            t.Check("png stream paste inserts a picture", host.PasteObject(pngOnly) && host.DocImageCount() >= 1);
            var unicode = new DataObject(DataFormats.UnicodeText, "붙여넣은글");
            t.Check("unicode paste inserts text", host.PasteObject(unicode) && host.PlainText().Contains("붙여넣은글"));
            var html = new DataObject();
            html.SetData(DataFormats.Html, "Version:0.9\r\n<html><body><!--StartFragment-->브라우저 <b>글</b><!--EndFragment--></body></html>");
            t.Check("html paste keeps the words", host.PasteObject(html) && host.PlainText().Contains("브라우저") && host.PlainText().Contains("글"));
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("우리꺼플러그인에 항상")));
            host.Editor.Document.Blocks.Add(new Paragraph());
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("다음")));
            var empty = (Paragraph)host.Editor.Document.Blocks.ElementAt(1);
            host.Editor.CaretPosition = empty.ContentStart;
            int emptyPara = host.CaretParagraph;
            host.ParkEditor();
            host.ResumeEditor();
            t.Check("empty line caret stays on that paragraph", emptyPara == 1 && host.CaretParagraph == 1 && host.CaretOffsetInParagraph == 0);
            var other = NoteManager.NewNote();
            if (other != null)
            {
                await TestKit.Until(() => other.Ready, 5000);
                other.Editor.Document.Blocks.Clear();
                other.Editor.Document.Blocks.Add(new Paragraph(new Run("다른메모")));
                other.Editor.CaretPosition = other.Editor.Document.ContentStart;
                int otherPara = other.CaretParagraph;
                host.ParkEditor();
                other.ChromeActivateLikeTest();
                host.ResumeEditor();
                t.Check("switching notes keeps both carets", host.CaretParagraph == 1 && other.CaretParagraph == otherPara);
            }
            else t.Check("switching notes keeps both carets", false, "no second note");
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph(new Run("내부")));
            host.Editor.Selection.Select(host.Editor.Document.ContentStart, host.Editor.Document.ContentEnd);
            host.CopySelection(false);
            uint owned = NoteClipSession.LastSequence;
            var foreign = new DataObject(DataFormats.UnicodeText, "외부앱");
            NoteClipSession.Fake = foreign;
            NoteClipSession.SequenceIsFake = true;
            NoteClipSession.SequenceNow = owned + 1;
            host.Editor.Selection.Select(host.Editor.Document.ContentEnd, host.Editor.Document.ContentEnd);
            bool foreignPaste = host.PasteMixed();
            t.Check("paste reads the other app clipboard, not the last internal copy", foreignPaste && host.PlainText().Contains("외부앱") && !host.PlainText().EndsWith("내부내부"));
            var same = new DataObject();
            same.SetData(NoteClip.Format, NoteClip.Pack(new[] { new NoteClipPiece { Text = "같은순번" } }));
            same.SetData(DataFormats.UnicodeText, "무시될글");
            NoteClipSession.Fake = same;
            NoteClipSession.SequenceNow = owned;
            host.Editor.Document.Blocks.Clear();
            host.Editor.Document.Blocks.Add(new Paragraph());
            host.Editor.CaretPosition = host.Editor.Document.ContentEnd;
            bool ownPaste = host.PasteMixed();
            t.Check("paste uses BongHub.Note only when the clipboard sequence matches", ownPaste && host.PlainText().Contains("같은순번") && !host.PlainText().Contains("무시될글"));
            NoteClipSession.Fake = null;
            int keepW = host.Meta.W, keepH = host.Meta.H, heldWrites = host.MetaWrites;
            host.SetTopologyHold(true);
            bool ignored = !host.CaptureSizeForTest(host.Meta.X, host.Meta.Y, host.Meta.W, 30, false, false);
            host.FinishTopology(new[] { new NoteLayout.Box(-4800, 5, 3840, 36) }, true);
            t.Check("topology hold does not persist a thin note", ignored && !host.TopologyHold && host.Meta.W == keepW && host.Meta.H == keepH && host.MetaWrites == heldWrites);
            string noteStory = NoteLayout.TopologyStory();
            t.Check("note rehome keeps the saved size across a monitor move", noteStory.Length == 0, noteStory);
        }
    }
}
