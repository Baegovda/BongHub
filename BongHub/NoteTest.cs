using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

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
        app.RequestShutdown();
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
            if (!onScreen && r.Left - AppEnv.OffsetX == m.X && r.Top - AppEnv.OffsetY == m.Y && r.Width == m.W && r.Height == m.H) placed++; else bad.Add($"{m.Id}: want {m.X},{m.Y} {m.W}x{m.H} got {r.Left - AppEnv.OffsetX},{r.Top - AppEnv.OffsetY} {r.Width}x{r.Height}{(onScreen ? " ON SCREEN" : "")}");
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

        t.Check("link open does not launch during notetest", host != null && host.TryOpenLink("go https://example.com/x", 3, launch: true) && host.LastLink.StartsWith("https://") && !host.LinkLaunched);
        t.Check("shortcut ctrl+t is handled", host != null && host.HandleShortcut(System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control));
    }
}
