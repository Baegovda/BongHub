using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace BongHub.Next;

/// <summary>--notetest --data &lt;copy&gt;: persistence round trip against a COPY of the 3.4.7 data.</summary>
public static class NoteTest
{
    public static async void Run(App app)
    {
        var t = new TestKit();
        try
        {
            if (AppEnv.IsDefaultData) { t.Check("refuses real data folder", false, "pass --data <copy>"); }
            else await Body(t);
        }
        catch (Exception e) { t.Check("no exception", false, e.ToString()); }
        t.Save("next_notetest.txt");
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
            if (r.Left - AppEnv.OffsetX == m.X && r.Top - AppEnv.OffsetY == m.Y && r.Width == m.W && r.Height == m.H) placed++; else bad.Add($"{m.Id}: want {m.X},{m.Y} {m.W}x{m.H} got {r.Left - AppEnv.OffsetX},{r.Top - AppEnv.OffsetY} {r.Width}x{r.Height}");
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
        string typed = "안녕 BongHub Next 한글 저장 테스트";
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

        foreach (var w in NoteManager.Windows.ToList()) w.Close();
    }
}
