using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BongHub;

public partial class StickyNoteWindow : Window
{
    public NoteMeta Meta { get; }
    public bool Ready { get; private set; }
    public bool TextDirty { get; private set; }
    public int RtfWrites, MetaWrites, ImageGuardHits;
    public string LoadStatus = "ok";          // ok | empty | corrupt | missing
    /// <summary>Longest edge of a picture bitmap actually shown. 0 when the note has no picture.</summary>
    public int DisplayImageMaxEdge { get; private set; }
    public long DisplayImagePixels { get; private set; }
    public bool LargeWarningVisible => LargeWarn.Visibility == Visibility.Visible;
    public string ShownColorKey { get; private set; } = "";
    public string LastLink { get; private set; } = "";
    public bool LinkLaunched { get; private set; }
    public byte[]? LastCopiedPng { get; private set; }
    public bool IsBlankLive => string.IsNullOrWhiteSpace(PlainText()) && DocImageCount() == 0 && originalPicts.Count == 0;
    readonly DispatcherTimer textTimer, metaTimer;
    readonly List<RtfPict.Pict> originalPicts = [];
    readonly List<Image> pictViews = [];
    Image? selectedImage;
    bool loading, metaDirty, corruptBackedUp, closing, discarding, sizing;
    int sourcePictCount;
    RichTextBox? editor;
    public bool IsEditing => editor != null;
    FlowDocument Document
    {
        get
        {
            if (editor != null) return editor.Document;
            Viewer.Document ??= new FlowDocument();
            return Viewer.Document;
        }
        set
        {
            if (editor != null) editor.Document = value;
            else Viewer.Document = value;
        }
    }
    /// <summary>The editing control. Created on first edit so idle notes do not each keep a RichTextBox.</summary>
    public RichTextBox Editor => EnsureEditor();
    int dragHandle = -1;
    Point dragStart;
    double dragW, dragH, dragAspect;

    public StickyNoteWindow(NoteMeta meta)
    {
        Meta = meta;
        InitializeComponent();
        var surfA = NoteColors.SurfA(meta.Color);
        WindowFx.Attach(this, Root, wantBackdrop: false, solid: surfA);
        textTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(900), DispatcherPriority.Background, (_, _) => FlushText(), Dispatcher) { IsEnabled = false };
        metaTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => FlushMeta(), Dispatcher) { IsEnabled = false };
        Topmost = meta.TopMost;
        if (AppEnv.Off)
        {
            double s = Win32.SystemScale; if (s < 0.5) s = 1;
            Left = (meta.X + AppEnv.OffsetX) / s;
            Top = (meta.Y + AppEnv.OffsetY) / s;
        }
        ShownColorKey = meta.Color;
        ApplyColor();
        ApplyOpacity();
        ApplyZoom();
        ApplyReadOnly();
        LoadDocument();

        SourceInitialized += (_, _) =>
        {
            Background = MakeBrush();          // after WindowFx's solid fill
            HwndSource.FromHwnd(Win32.Handle(this))?.AddHook(Hook);
            PlacePhysical();
        };
        Loaded += (_, _) => PlacePhysical();
        WindowFx.Of(this).AfterFirstFrame = () => { PlacePhysical(); Ready = true; Win32.AllowActivate(Win32.Handle(this)); };

        EditorHost.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers != ModifierKeys.Control) return; e.Handled = true; Meta.Zoom = (float)Math.Clamp(Math.Round(Meta.Zoom + (e.Delta > 0 ? 0.1 : -0.1), 1), 0.6, 3.0); ApplyZoom(); MarkMeta(); };
        EditorHost.PreviewMouseLeftButtonDown += (_, _) => { if (editor == null && !AppEnv.ReadOnly) EnsureEditor().Focus(); };
        MouseEnter += (_, _) => Fx.Fade(Bar, 1, 160);
        MouseLeave += (_, _) => { if (!Palette.IsOpen) Fx.Fade(Bar, 0, 260); };

        NewBtn.Click += (_, _) => NoteManager.NewNote();
        FontBtn.Click += (_, _) => ToggleFormatBar();
        ColorBtn.Click += (_, _) => { BuildSwatches(); Palette.IsOpen = true; };
        ListBtn.Click += (_, _) => ShowList();
        PinBtn.Click += (_, _) => SetTopMost(!Meta.TopMost);
        CloseBtn.Click += (_, _) => CloseNote();
        BoldBtn.Click += (_, _) => EditingCommands.ToggleBold.Execute(null, Editor);
        ItalicBtn.Click += (_, _) => EditingCommands.ToggleItalic.Execute(null, Editor);
        UnderBtn.Click += (_, _) => EditingCommands.ToggleUnderline.Execute(null, Editor);
        StrikeBtn.Click += (_, _) => ToggleStrike();
        BulletBtn.Click += (_, _) => EditingCommands.ToggleBullets.Execute(null, Editor);
        SizeBtn.Click += (_, _) => ShowHeadings(SizeBtn);
        SmallerBtn.Click += (_, _) => GrowFont(-1);
        LargerBtn.Click += (_, _) => GrowFont(1);
        ImageBtn.Click += (_, _) => PickImage();
        CustomApply.Click += (_, _) => { if (NoteColors.TryCustom(CustomA.Text.Trim(), CustomB.Text.Trim(), out var key)) { Palette.IsOpen = false; SetColor(key); } };
        Viewer.AllowDrop = true;
        Viewer.PreviewDragOver += (_, e) => { if (CanDrop(e.Data)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        Viewer.Drop += (_, e) => { if (!AppEnv.ReadOnly) EnsureEditor(); if (AcceptDrop(e.Data)) e.Handled = true; };
        PreviewKeyDown += (_, e) => { if (HandleShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        Closing += (_, e) => { if (!App.ShuttingDown && !closing) { e.Cancel = true; CloseNote(); } };
        UpdatePin();
    }

    // ---------- placement (3.4.7 stores physical pixels) ----------
    void PlacePhysical()
    {
        int x = Meta.X, y = Meta.Y, w = Math.Max(60, Meta.W), h = Math.Max(60, Meta.H);
        if (!AppEnv.Off)
        {
            var areas = Win32.WorkAreas();
            if (areas.Length > 0)
            {
                var c = NoteLayout.Clamp(new NoteLayout.Box(x, y, w, h), areas, 38);
                x = c.X; y = c.Y; w = Math.Max(60, c.W); h = Math.Max(60, c.H);
            }
            else if (!Win32.OnAnyMonitor(x, y, w, h)) { var a = Win32.WorkAreaAtCursor(); x = a.Left + 60; y = a.Top + 60; }
        }
        int px = x + AppEnv.OffsetX, py = y + AppEnv.OffsetY;
        double dpi = Win32.Scale(this);
        if (dpi < 0.5) dpi = 1;
        Width = w / dpi;
        Height = h / dpi;
        // Record the physical target before WPF applies Left/Top, so a monitor pull is undone exactly.
        Win32.RememberOffScreen(Win32.Handle(this), px, py);
        Win32.Place(this, px, py, w, h);
    }
    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        // Runs before WPF's own handler (HwndSource invokes the newest hook first) and leaves handled false.
        if (msg == Win32.WM_GETMINMAXINFO) Win32.AllowTallWindow(lp);
        Win32.OffScreenHook(h, msg, wp, lp, ref handled);
        if (msg == Win32.WM_ENTERSIZEMOVE) { sizing = true; textTimer.Stop(); metaTimer.Stop(); }
        else if (msg == Win32.WM_EXITSIZEMOVE) { sizing = false; CaptureRect(); FlushMeta(); if (TextDirty) textTimer.Start(); }
        else if (msg == Win32.WM_MOUSEACTIVATE) Win32.AllowActivate(h);
        else if (msg == Win32.WM_DISPLAYCHANGE && !AppEnv.Off) Dispatcher.BeginInvoke(ClampToScreens);
        else if ((msg == Win32.WM_MOVE || msg == Win32.WM_SIZE) && Ready && !sizing) { if (CaptureRect()) MarkMeta(); }
        return IntPtr.Zero;
    }
    public void ClampToScreens()
    {
        if (AppEnv.Off) return;
        var areas = Win32.WorkAreas();
        if (areas.Length == 0) return;
        var c = NoteLayout.Clamp(new NoteLayout.Box(Meta.X, Meta.Y, Math.Max(60, Meta.W), Math.Max(60, Meta.H)), areas, 38);
        if (c.X == Meta.X && c.Y == Meta.Y && c.W == Meta.W && c.H == Meta.H) return;
        Meta.X = c.X; Meta.Y = c.Y; Meta.W = c.W; Meta.H = c.H;
        Win32.Place(this, c.X + AppEnv.OffsetX, c.Y + AppEnv.OffsetY, c.W, c.H);
        metaDirty = true; FlushMeta();
    }
    public void EnableDuringModal()
    {
        var h = Win32.Handle(this);
        if (h != IntPtr.Zero) Win32.EnableWindow(h, true);
    }
    /// <summary>Copies the real window rect into Meta; true when it changed.</summary>
    bool CaptureRect()
    {
        if (!Ready) return false;
        var r = Win32.Rect(this);
        int x = r.Left - AppEnv.OffsetX, y = r.Top - AppEnv.OffsetY;
        if (x == Meta.X && y == Meta.Y && r.Width == Meta.W && r.Height == Meta.H) return false;
        Meta.X = x; Meta.Y = y; Meta.W = r.Width; Meta.H = r.Height; metaDirty = true; return true;
    }

    RichTextBox EnsureEditor()
    {
        if (editor != null) return editor;
        var doc = Viewer.Document ?? new FlowDocument();
        Viewer.Document = new FlowDocument();
        var box = new RichTextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF6)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF6)),
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xB4, 0xFF)),
            SelectionOpacity = 0.4,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            AllowDrop = true,
            IsUndoEnabled = true,
            IsReadOnly = AppEnv.ReadOnly,
            Document = doc,
        };
        box.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        var paraStyle = new Style(typeof(Paragraph));
        paraStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 2)));
        box.Resources.Add(typeof(Paragraph), paraStyle);
        SpellCheck.SetIsEnabled(box, false);
        box.TextChanged += (_, _) => { UpdateHint(); if (loading) return; TextDirty = true; if (sizing) return; textTimer.Stop(); textTimer.Start(); };
        box.PreviewDragOver += (_, e) => { if (CanDrop(e.Data)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        box.Drop += (_, e) => { if (AcceptDrop(e.Data)) e.Handled = true; };
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { if (CopyMixed()) e.Handled = true; }));
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { if (PasteMixed()) e.Handled = true; }));
        box.PreviewMouseLeftButtonDown += OnEditorDown;
        box.PreviewMouseMove += OnEditorMove;
        box.PreviewMouseLeftButtonUp += OnEditorUp;
        box.PreviewMouseRightButtonUp += OnEditorRight;
        box.PreviewMouseDoubleClick += OnEditorDouble;
        box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReleaseEditorIfIdle));
        editor = box;
        EditorHost.Children.Insert(0, box);
        Viewer.Visibility = Visibility.Collapsed;
        ApplyZoom();
        return box;
    }

    void ReleaseEditorIfIdle()
    {
        if (editor == null || dragHandle >= 0 || Palette.IsOpen) return;
        if (editor.IsKeyboardFocusWithin || FormatBar.IsKeyboardFocusWithin || EditorHost.IsKeyboardFocusWithin) return;
        var doc = editor.Document;
        editor.Document = new FlowDocument();
        EditorHost.Children.Remove(editor);
        editor = null;
        Viewer.Document = doc;
        Viewer.Visibility = Visibility.Visible;
        ApplyZoom();
    }

    // ---------- document ----------
    void LoadDocument()
    {
        loading = true;
        try
        {
            var path = NoteStore.RtfPath(Meta.Id);
            var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (bytes == null) LoadStatus = "missing";
            else if (bytes.Length == 0) LoadStatus = "empty";
            if (bytes is { Length: > 0 })
            {
                sourcePictCount = CountPict(bytes);
                var picts = sourcePictCount > 0 ? RtfPict.Parse(bytes) : [];
                bool proxy = picts.Count > 0 && picts.Count == sourcePictCount;
                try
                {
                    var doc = LoadFlow(proxy ? RtfPict.Placeholder(bytes) : bytes);
                    if (proxy && CollectImages(doc).Count != picts.Count)
                        doc = LoadFlow(bytes);   // placeholder did not round-trip; decode the file, then shrink
                    Document = doc;
                    if (proxy) ApplyProxies(CollectImages(doc), picts);
                }
                catch (Exception e) { LoadStatus = "corrupt"; AppEnv.Log($"rtf load failed {Meta.Id}: {e.Message}"); SetPlain(Meta.Text); }
                // parser "succeeded" but salvaged nothing while meta still has text → treat as damaged, show meta text
                if (LoadStatus == "ok" && DocImageCount() == 0 && PlainText().Trim().Length == 0 && !string.IsNullOrWhiteSpace(Meta.Text)) { LoadStatus = "corrupt"; SetPlain(Meta.Text); originalPicts.Clear(); }
                else if (LoadStatus == "ok" && !(bytes.Length > 5 && bytes[0] == (byte)'{' && bytes[1] == (byte)'\\' && bytes[2] == (byte)'r')) { LoadStatus = "corrupt"; SetPlain(Meta.Text); originalPicts.Clear(); }
            }
            else SetPlain(Meta.Text);
            StyleDocument();
        }
        finally { loading = false; if (editor != null) editor.IsUndoEnabled = true; }
        UpdateHint();
    }
    static FlowDocument LoadFlow(byte[] data)
    {
        var doc = new FlowDocument();
        using var ms = new MemoryStream(data);
        new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.Rtf);
        return doc;
    }
    void ApplyProxies(List<Image> images, List<RtfPict.Pict> picts)
    {
        originalPicts.Clear();
        originalPicts.AddRange(picts);
        pictViews.Clear();
        double dpi = Win32.SystemScale;
        int maxEdge = 0;
        long pixels = 0;
        int n = Math.Min(images.Count, picts.Count);
        for (int i = 0; i < n; i++)
        {
            var p = picts[i];
            // Already decoded near the display size. HighQuality keeps a second scaled copy and is slow in the software rasterizer.
            RenderOptions.SetBitmapScalingMode(images[i], BitmapScalingMode.Linear);
            int edge = RtfPict.TargetEdge(p.GoalW, p.GoalH, dpi, Meta.Zoom, p.SourceW, p.SourceH);
            try
            {
                var bmp = RtfPict.DecodeScaled(p.Bytes, p.SourceW, p.SourceH, edge);
                if (bmp != null)
                {
                    images[i].Source = bmp;
                    maxEdge = Math.Max(maxEdge, Math.Max(bmp.PixelWidth, bmp.PixelHeight));
                    pixels += (long)bmp.PixelWidth * bmp.PixelHeight;
                    p.Bytes = [];
                }
            }
            catch (Exception e) { AppEnv.Log($"image decode {Meta.Id}: {e.Message}"); }
            if (p.GoalW > 0) images[i].Width = p.GoalW / 15.0;
            if (p.GoalH > 0) images[i].Height = p.GoalH / 15.0;
            images[i].Stretch = Stretch.Fill;
            pictViews.Add(images[i]);
        }
        DisplayImageMaxEdge = maxEdge;
        DisplayImagePixels = pixels;
    }
    /// <summary>True when every picture in the document is scaled linearly (no cached high-quality copy).</summary>
    public bool DisplayImagesUseLinearScaling()
    {
        var images = CollectImages(Document);
        return images.Count > 0 && images.All(im => RenderOptions.GetBitmapScalingMode(im) == BitmapScalingMode.Linear);
    }
    static List<Image> CollectImages(FlowDocument doc)
    {
        var list = new List<Image>();
        void Walk(DependencyObject node)
        {
            if (node is Image img) list.Add(img);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(doc);
        return list;
    }
    void SetPlain(string text)
    {
        var doc = new FlowDocument();
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n')) doc.Blocks.Add(new Paragraph(new Run(line)));
        Document = doc;
    }
    void StyleDocument()
    {
        var d = Document;
        d.PagePadding = new Thickness(4, 2, 4, 6);
        d.FontFamily = (FontFamily)FindResource("UiFont");
        d.Foreground = new SolidColorBrush(NoteColors.Text);
        d.FontSize = PointsToDip(ClampPt(ConfigStore.Notes.FontSize));
        d.LineHeight = double.NaN;
    }
    static int CountPict(byte[] b) { int n = 0; var s = Encoding.ASCII.GetString(b); for (int i = s.IndexOf("\\pict", StringComparison.Ordinal); i >= 0; i = s.IndexOf("\\pict", i + 5, StringComparison.Ordinal)) n++; return n; }
    int DocImageCount() => CountImages(Document.Blocks);
    static int CountImages(IEnumerable<Block> blocks)
    {
        int n = 0;
        foreach (var b in blocks)
        {
            if (b is Paragraph p) n += p.Inlines.Count(i => i is InlineUIContainer { Child: Image });
            else if (b is BlockUIContainer { Child: Image }) n++;
            else if (b is List l) n += l.ListItems.Sum(li => CountImages(li.Blocks));
            else if (b is Section s) n += CountImages(s.Blocks);
        }
        return n;
    }
    public string PlainText()
    {
        return new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');
    }
    void UpdateHint() => Hint.Visibility = Document.Blocks.Count <= 1 && PlainText().Length == 0 && DocImageCount() == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---------- saving (debounced: one write per gesture) ----------
    void MarkMeta() { metaDirty = true; metaTimer.Stop(); metaTimer.Start(); }
    public void FlushText()
    {
        textTimer.Stop();
        if (!TextDirty) return;
        TextDirty = false;
        var range = new TextRange(Document.ContentStart, Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        byte[] wpfRtf = ms.ToArray();
        byte[]? rtf = wpfRtf;
        if (originalPicts.Count > 0)
        {
            // The in-memory bitmaps are display-sized. Put the original pict groups back so the file stays full resolution.
            // Goal edits are already in those groups; the blip hex is not rewritten.
            var merged = RtfPict.Splice(RtfPict.Latin1.GetString(wpfRtf), originalPicts);
            if (merged != null) rtf = RtfPict.Latin1.GetBytes(merged);
        }
        int emitted = rtf == null ? 0 : CountPict(rtf);
        if (originalPicts.Count > 0 && emitted != originalPicts.Count && DocImageCount() == originalPicts.Count && CountPict(wpfRtf) == 0)
        {
            // WPF kept the images on screen but did not write \pict. Insert the groups we already built.
            var injected = InjectPicts(wpfRtf, originalPicts);
            if (injected != null) { rtf = injected; emitted = CountPict(rtf); }
        }
        byte[] onDisk = [];
        try { if (File.Exists(NoteStore.RtfPath(Meta.Id))) onDisk = File.ReadAllBytes(NoteStore.RtfPath(Meta.Id)); } catch { onDisk = []; }
        var chosen = NoteRtf.Choose(onDisk.Length > 0 ? onDisk : (rtf ?? []), rtf ?? [], DocImageCount(), originalPicts.Count, out var dropWarn);
        Meta.Text = PlainText();
        if (dropWarn != null)
        {
            ImageGuardHits++;
            AppEnv.Log($"image guard: kept existing rtf for {Meta.Id} {dropWarn}");
            metaDirty = true; FlushMeta();
            return;
        }
        rtf = chosen;
        if (LoadStatus == "corrupt" && !corruptBackedUp && !AppEnv.DenyWrite())
        {
            try { File.Copy(NoteStore.RtfPath(Meta.Id), NoteStore.RtfPath(Meta.Id) + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
            corruptBackedUp = true;
        }
        if (NoteStore.SaveRtf(Meta, rtf!)) { RtfWrites++; sourcePictCount = originalPicts.Count > 0 ? originalPicts.Count : emitted; WarnIfLarge(); }
        metaDirty = true; FlushMeta();
    }
    static byte[]? InjectPicts(byte[] wpf, List<RtfPict.Pict> picts)
    {
        var s = RtfPict.Latin1.GetString(wpf);
        int end = s.LastIndexOf('}');
        if (end < 0) return null;
        var sb = new StringBuilder(s.Length + 64);
        sb.Append(s, 0, end);
        foreach (var p in picts) sb.Append(p.Group);
        sb.Append(s, end, s.Length - end);
        return RtfPict.Latin1.GetBytes(sb.ToString());
    }
    void WarnIfLarge()
    {
        try
        {
            var len = new FileInfo(NoteStore.RtfPath(Meta.Id)).Length;
            if (NoteImages.OverWarn(len)) ShowLargeWarning(len);
        }
        catch { }
    }
    public void ShowLargeWarning(long bytes)
    {
        int mb = (int)(bytes / 1024 / 1024);
        LargeWarn.Text = "이 메모가 " + mb + "MB로 커졌어요. 이미지가 많으면 저장과 클라우드 동기화가 느려질 수 있어요.";
        LargeWarn.Visibility = Visibility.Visible;
    }
    /// <summary>Drops WPF pictures without updating the saved groups, so the next flush has to keep the file.</summary>
    public void ForcePictMismatchForTest()
    {
        if (originalPicts.Count == 0) return;
        loading = true;
        try
        {
            Document.Blocks.Clear();
            Document.Blocks.Add(new Paragraph(new Run("떨어짐")));
        }
        finally { loading = false; }
        TextDirty = true;
    }
    public void FlushMeta()
    {
        metaTimer.Stop();
        if (!metaDirty) return;
        metaDirty = false;
        if (NoteStore.SaveMeta(Meta)) MetaWrites++;
    }
    public void FlushAll() { FlushText(); CaptureRect(); FlushMeta(); }

    // ---------- appearance ----------
    Brush MakeBrush()
    {
        if (!NoteColors.Grad) return new SolidColorBrush(NoteColors.Body(Meta.Color));
        var b = new LinearGradientBrush(NoteColors.SurfA(Meta.Color), NoteColors.SurfB(Meta.Color), new Point(0, 0), new Point(1, 1)); b.Freeze(); return b;
    }
    void ApplyColor()
    {
        Background = MakeBrush();
        TitleText.Text = NoteColors.Name(Meta.Color);
        var h = Win32.Handle(this);
        if (h != IntPtr.Zero) { Dwm.BorderColor(h, NoteColors.Mix(NoteColors.SurfA(Meta.Color), Colors.White, 0.12)); var src = HwndSource.FromHwnd(h); if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = NoteColors.SurfA(Meta.Color); }
    }
    public void SetColor(string key)
    {
        if (!NoteColors.Valid(key) || key == Meta.Color) return;
        var old = Background; Meta.Color = key; ShownColorKey = key; ApplyColor();
        if (Fx.Enabled && old is not null) { Root.Opacity = 0.85; Fx.Fade(Root, 1, 220); }
        metaDirty = true; FlushMeta();
    }
    /// <summary>Hover preview. Does not write the note. Pass null to restore the saved color.</summary>
    public void PreviewColor(string? key)
    {
        string show = string.IsNullOrEmpty(key) ? Meta.Color : key;
        if (!NoteColors.Valid(show)) return;
        ShownColorKey = show;
        var saved = Meta.Color;
        Meta.Color = show;
        ApplyColor();
        Meta.Color = saved;
        if (string.IsNullOrEmpty(key)) ShownColorKey = saved;
    }
    public void SetColorRefresh() => ApplyColor();
    public static double ClampPt(double pt) => Math.Clamp(pt, 7, 48);
    public static double PointsToDip(double pt) => ClampPt(pt) * 96.0 / 72.0;
    public void ApplyFontSize() { loading = true; try { Document.FontSize = PointsToDip(ConfigStore.Notes.FontSize); } finally { loading = false; } }
    public void ApplyHeading(double pt)
    {
        if (AppEnv.ReadOnly) return;
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, PointsToDip(pt));
        TextDirty = true; textTimer.Stop(); textTimer.Start();
    }
    public void GrowFont(double delta)
    {
        if (AppEnv.ReadOnly) return;
        double cur = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double d ? d * 72.0 / 96.0 : ConfigStore.Notes.FontSize;
        ApplyHeading(ClampPt(cur + delta));
    }
    public bool HandleShortcut(Key key, ModifierKeys mods)
    {
        bool ctrl = (mods & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (mods & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!ctrl) return false;
        if (shift && key == Key.L) { if (!AppEnv.ReadOnly) EditingCommands.ToggleBullets.Execute(null, Editor); return true; }
        switch (key)
        {
            case Key.B: if (!AppEnv.ReadOnly) EditingCommands.ToggleBold.Execute(null, Editor); return true;
            case Key.I: if (!AppEnv.ReadOnly) EditingCommands.ToggleItalic.Execute(null, Editor); return true;
            case Key.U: if (!AppEnv.ReadOnly) EditingCommands.ToggleUnderline.Execute(null, Editor); return true;
            case Key.T: if (!AppEnv.ReadOnly) ToggleStrike(); return true;
            case Key.N: NoteManager.NewNote(); return true;
            case Key.D: AskDelete(); return true;
            case Key.W: CloseNote(); return true;
            case Key.OemPlus:
            case Key.Add: GrowFont(1); return true;
            case Key.OemMinus:
            case Key.Subtract: GrowFont(-1); return true;
            case Key.V: return PasteMixed() || PasteImage();
            case Key.C: return CopyMixed() || CopySelectedImage();
            default: return false;
        }
    }
    /// <summary>Same window opacity as 3.4.8 NoteForm.ApplyOpacity (30–100, floor 0.3).</summary>
    public void ApplyOpacity()
    {
        int v = ConfigStore.Notes.Opacity;
        if (v < 30 || v > 100) v = 100;
        double o = Math.Max(0.3, Math.Min(1.0, v / 100.0));
        if (Math.Abs(Opacity - o) > 0.001) Opacity = o;
    }
    public void ApplyReadOnly() { if (editor != null) editor.IsReadOnly = AppEnv.ReadOnly; }
    void ApplyZoom()
    {
        var t = Math.Abs(Meta.Zoom - 1) < 0.01 ? Transform.Identity : new ScaleTransform(Meta.Zoom, Meta.Zoom);
        Viewer.LayoutTransform = t;
        if (editor != null) editor.LayoutTransform = t;
    }
    public void SetTopMost(bool on) { Meta.TopMost = on; Topmost = on; UpdatePin(); metaDirty = true; FlushMeta(); }
    void UpdatePin() { PinBtn.Content = Meta.TopMost ? "\uE840" : "\uE718"; PinBtn.Foreground = Meta.TopMost ? (Brush)FindResource("Accent") : Brushes.White; }
    void ToggleFormatBar()
    {
        if (FormatBar.Visibility != Visibility.Visible)
        {
            FormatBar.Visibility = Visibility.Visible;
            Fx.Fade(FormatBar, 1, 180); Fx.MoveY(FormatShift, 0, 260);
        }
        else
        {
            var a = Fx.Anim(0, 140); a.Completed += (_, _) => { if (FormatBar.Opacity < 0.05) FormatBar.Visibility = Visibility.Collapsed; };
            FormatBar.BeginAnimation(OpacityProperty, a); Fx.MoveY(FormatShift, 8, 160);
        }
    }
    void ToggleStrike()
    {
        var sel = Editor.Selection; var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        bool on = cur is TextDecorationCollection c && c.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }
    void BuildSwatches()
    {
        Swatches.Children.Clear();
        foreach (var k in NoteColors.PaletteKeys)
        {
            var dot = new Ellipse { Width = 22, Height = 22, Fill = new LinearGradientBrush(NoteColors.Swatch(k), NoteColors.SwatchB(k), 45) };
            var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(1), Child = dot, Cursor = Cursors.Hand, ToolTip = NoteColors.Name(k), Background = Brushes.Transparent,
                BorderThickness = new Thickness(2), BorderBrush = k == Meta.Color ? Brushes.White : Brushes.Transparent };
            var key = k;
            ring.MouseEnter += (_, _) => PreviewColor(key);
            ring.MouseLeave += (_, _) => { if (Palette.IsOpen) PreviewColor(null); };
            ring.MouseLeftButtonUp += (_, _) => { Palette.IsOpen = false; SetColor(key); };
            ring.MouseEnter += (_, _) => { dot.RenderTransformOrigin = new Point(.5, .5); var s = new ScaleTransform(1, 1); dot.RenderTransform = s; s.BeginAnimation(ScaleTransform.ScaleXProperty, Fx.Anim(1.15, 160, Fx.Spring)); s.BeginAnimation(ScaleTransform.ScaleYProperty, Fx.Anim(1.15, 160, Fx.Spring)); };
            ring.MouseLeave += (_, _) => dot.RenderTransform = Transform.Identity;
            Swatches.Children.Add(ring);
        }
    }
    void ShowList()
    {
        var m = new ContextMenu { PlacementTarget = ListBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var n in NoteStore.All.Take(15))
        {
            var t = (n.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (t.Length == 0) t = "(빈 메모)"; if (t.Length > 28) t = t[..28] + "…";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Fill = new LinearGradientBrush(NoteColors.Swatch(n.Color), NoteColors.SwatchB(n.Color), 45) });
            row.Children.Add(new TextBlock { Text = t, FontWeight = NoteManager.IsOpen(n.Id) ? FontWeights.SemiBold : FontWeights.Normal });
            var item = new MenuItem { Header = row }; var id = n.Id;
            item.Click += (_, _) => NoteManager.Open(id, activate: true);
            m.Items.Add(item);
        }
        m.Items.Add(new Separator());
        var del = new MenuItem { Header = "이 메모 삭제" }; del.Click += (_, _) => AskDelete(); m.Items.Add(del);
        var all = new MenuItem { Header = "모든 메모 보기" }; all.Click += (_, _) => App.Instance.ShowSettings("notes"); m.Items.Add(all);
        m.IsOpen = true;
    }

    void ShowHeadings(FrameworkElement at)
    {
        var menu = new ContextMenu { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, pt) in new[] { ("텍스트  ·  11pt", 11d), ("H1  제목1  ·  24pt", 24d), ("H2  제목2  ·  20pt", 20d), ("H3  제목3  ·  16pt", 16d), ("H4  제목4  ·  13pt", 13d) })
        {
            double size = pt;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { ApplyHeading(size); Editor.Focus(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void AskDelete()
    {
        if (AppEnv.ReadOnly) return;
        if (!IsBlankLive && !ConfirmDelete()) return;
        NoteManager.Delete(Meta.Id, confirmed: true);
    }

    static bool ConfirmDelete()
    {
        var w = new Window
        {
            Title = "스티커 메모", Width = 360, Height = 168, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = !AppEnv.Off, WindowStartupLocation = WindowStartupLocation.Manual,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
        };
        if (AppEnv.Off) { w.Left = AppEnv.OffsetX + 220; w.Top = AppEnv.OffsetY + 220; }
        else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        bool yes = false;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "이 메모를 삭제할까요?", Foreground = Brushes.White, FontSize = 15, Margin = new Thickness(0, 8, 0, 16) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Content = "취소", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        var ok = new Button { Content = "삭제", Padding = new Thickness(14, 6, 14, 6), IsDefault = true };
        no.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { yes = true; w.Close(); };
        row.Children.Add(no); row.Children.Add(ok); root.Children.Add(row);
        w.Content = root;
        w.ShowDialog();
        return yes;
    }

    public void DiscardClose()
    {
        if (closing) return;
        discarding = true; closing = true;
        textTimer.Stop(); metaTimer.Stop();
        NoteManager.Forget(this);
        Close();
    }

    // ---------- images: bytes stay in the pict group; only goals change ----------
    public bool InsertImageFile(string path)
    {
        if (AppEnv.ReadOnly) return false;
        int fit = Math.Max(80, (int)(EditorHost.ActualWidth > 1 ? EditorHost.ActualWidth - 24 : 260));
        var built = NoteImages.FromFile(path, fit);
        return built != null && AddPicture(built);
    }

    public bool InsertBitmap(BitmapSource source)
    {
        if (AppEnv.ReadOnly || source == null) return false;
        int fit = Math.Max(80, (int)(EditorHost.ActualWidth > 1 ? EditorHost.ActualWidth - 24 : 260));
        var built = NoteImages.FromBitmap(source, fit);
        return built != null && AddPicture(built);
    }

    bool AddPicture(NoteImages.Built built)
    {
        var bmp = NoteImages.Display(built.Bytes, built.PxW, built.PxH, built.GoalW, built.GoalH, Win32.SystemScale, Meta.Zoom);
        if (bmp == null) return false;
        var image = new Image { Source = bmp, Stretch = Stretch.Fill, Width = built.GoalW / 15.0, Height = built.GoalH / 15.0 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        var at = Editor.CaretPosition;
        TextPointer caret;
        if (at?.Paragraph != null) caret = at;
        else
        {
            var para = Document.Blocks.FirstBlock as Paragraph;
            if (para == null) { para = new Paragraph(); Document.Blocks.Add(para); }
            caret = para.ContentEnd;
        }
        new InlineUIContainer(image, caret);
        int index = CollectImages(Document).IndexOf(image);
        if (index < 0) index = originalPicts.Count;
        var pict = new RtfPict.Pict { Group = built.Group, GoalW = built.GoalW, GoalH = built.GoalH, SourceW = built.PxW, SourceH = built.PxH };
        if (index >= originalPicts.Count) { originalPicts.Add(pict); pictViews.Add(image); }
        else { originalPicts.Insert(index, pict); pictViews.Insert(index, image); }
        sourcePictCount = originalPicts.Count;
        TextDirty = true; textTimer.Stop(); textTimer.Start();
        UpdateHint();
        return true;
    }

    public bool SetImageScale(int index, double factor)
    {
        if (index < 0 || index >= originalPicts.Count) return false;
        var p = originalPicts[index];
        int sw = p.SourceW > 0 ? p.SourceW : Math.Max(1, (int)Math.Round((pictViews.ElementAtOrDefault(index)?.Width ?? 1)));
        int sh = p.SourceH > 0 ? p.SourceH : Math.Max(1, (int)Math.Round((pictViews.ElementAtOrDefault(index)?.Height ?? 1)));
        var (gw, gh) = NoteImages.ScaleGoals(sw, sh, factor);
        return ApplyGoals(index, gw, gh);
    }

    public bool FitImageWidth(int index)
    {
        if (index < 0 || index >= originalPicts.Count) return false;
        double maxDip = Math.Max(40, (EditorHost.ActualWidth > 1 ? EditorHost.ActualWidth : 280) - 28);
        var p = originalPicts[index];
        var (gw, gh) = NoteImages.FitGoals(p.GoalW, p.GoalH, maxDip);
        return ApplyGoals(index, gw, gh);
    }

    bool ApplyGoals(int index, int goalW, int goalH)
    {
        var p = originalPicts[index];
        p.Group = RtfPict.WithGoals(p.Group, goalW, goalH);
        p.GoalW = goalW; p.GoalH = goalH;
        if (index < pictViews.Count)
        {
            pictViews[index].Width = goalW / 15.0;
            pictViews[index].Height = goalH / 15.0;
        }
        TextDirty = true; textTimer.Stop(); textTimer.Start();
        return true;
    }

    public bool MoveImage(int from, int to)
    {
        if (from < 0 || from >= originalPicts.Count || to < 0 || to >= originalPicts.Count || from == to) return false;
        var pict = originalPicts[from];
        Image? view = from < pictViews.Count ? pictViews[from] : null;
        originalPicts.RemoveAt(from);
        if (view != null) pictViews.RemoveAt(from);
        originalPicts.Insert(to, pict);
        Image? neu = view;
        if (view != null)
        {
            neu = new Image { Source = view.Source, Width = view.Width, Height = view.Height, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(neu, BitmapScalingMode.Linear);
            if (view.Parent is InlineUIContainer box && box.Parent is Paragraph para) para.Inlines.Remove(box);
            view.Source = null;
            var images = CollectImages(Document);
            int dest = Math.Clamp(to, 0, images.Count);
            TextPointer where = Document.ContentEnd;
            if (dest < images.Count && images[dest].Parent is Inline inline && inline.ElementStart is { } start) where = start;
            new InlineUIContainer(neu, where);
            pictViews.Insert(Math.Min(to, pictViews.Count), neu);
        }
        if (selectedImage == view) selectedImage = neu;
        TextDirty = true; textTimer.Stop(); textTimer.Start();
        return true;
    }

    public bool DeleteImage(int index)
    {
        if (AppEnv.ReadOnly || index < 0 || index >= originalPicts.Count) return false;
        if (index < pictViews.Count)
        {
            var view = pictViews[index];
            if (view.Parent is InlineUIContainer box && box.Parent is Paragraph para) para.Inlines.Remove(box);
            pictViews.RemoveAt(index);
        }
        originalPicts.RemoveAt(index);
        sourcePictCount = originalPicts.Count;
        if (selectedImage != null && !pictViews.Contains(selectedImage)) selectedImage = null;
        TextDirty = true; textTimer.Stop(); textTimer.Start();
        UpdateHint();
        return true;
    }

    public byte[]? CopyImage(int index)
    {
        if (index < 0 || index >= originalPicts.Count) return null;
        var bytes = RtfPict.Parse(Encoding.ASCII.GetBytes(originalPicts[index].Group)).FirstOrDefault()?.Bytes;
        if (bytes == null || bytes.Length < 8) return null;
        LastCopiedPng = bytes;
        try
        {
            var bmp = RtfPict.DecodeScaled(bytes, 0, 0, 4096);
            if (bmp != null) Clipboard.SetImage(bmp);
        }
        catch (Exception e) { AppEnv.Log("copy image: " + e.Message); }
        return bytes;
    }

    public bool CopyMixed()
    {
        var pieces = ExportPieces();
        if (!pieces.Any(p => p.Png is { Length: > 0 })) return false;
        string pack = NoteClip.Pack(pieces);
        string plain = NoteClip.Plain(pieces);
        NoteClipSession.Publish(pack, plain, NoteClip.ToRtf(pieces), pieces.FirstOrDefault(p => p.Png is { Length: > 0 })?.Png);
        return true;
    }

    public bool PasteMixed()
    {
        if (AppEnv.ReadOnly) return false;
        string? pack = NoteClipSession.LastPack;
        if (NoteClipSession.AllowsSystem)
        {
            try
            {
                if (Clipboard.ContainsData(NoteClip.Format))
                    pack = Clipboard.GetData(NoteClip.Format) as string ?? pack;
            }
            catch (Exception e) { AppEnv.Log("paste note: " + e.Message); }
        }
        var pieces = NoteClip.Unpack(pack);
        if (pieces.Count == 0 || !pieces.Any(p => p.Png is { Length: > 0 } || p.Text.Length > 0)) return false;
        if (!pieces.Any(p => p.Png is { Length: > 0 })) return false;
        foreach (var p in pieces)
        {
            if (p.Text.Length > 0)
            {
                Editor.CaretPosition.InsertTextInRun(p.Text);
                TextDirty = true;
            }
            if (p.Png is { Length: > 0 }) InsertPng(p.Png);
        }
        textTimer.Stop(); textTimer.Start();
        return true;
    }

    void InsertPng(byte[] png)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            InsertBitmap(bmp);
        }
        catch (Exception e) { AppEnv.Log("paste png: " + e.Message); }
    }

    List<NoteClipPiece> ExportPieces()
    {
        var pieces = new List<NoteClipPiece>();
        var sb = new StringBuilder();
        int image = 0;
        void Flush()
        {
            if (sb.Length == 0) return;
            pieces.Add(new NoteClipPiece { Text = sb.ToString() });
            sb.Clear();
        }
        void Walk(IEnumerable<Inline> inlines)
        {
            foreach (var i in inlines)
            {
                if (i is Run r) sb.Append(r.Text);
                else if (i is LineBreak) sb.Append('\n');
                else if (i is Span s) Walk(s.Inlines);
                else if (i is InlineUIContainer { Child: Image })
                {
                    Flush();
                    var png = PngAt(image++);
                    if (png != null) pieces.Add(new NoteClipPiece { Png = png });
                }
            }
        }
        foreach (var b in Document.Blocks)
            if (b is Paragraph p) { Walk(p.Inlines); sb.Append('\n'); }
        Flush();
        return pieces;
    }

    byte[]? PngAt(int index)
    {
        if (index < 0 || index >= originalPicts.Count) return null;
        return RtfPict.Parse(Encoding.ASCII.GetBytes(originalPicts[index].Group)).FirstOrDefault()?.Bytes;
    }

    bool CopySelectedImage()
    {
        int i = selectedImage == null ? -1 : pictViews.IndexOf(selectedImage);
        return i >= 0 && CopyImage(i) != null;
    }

    public ImageViewerWindow? OpenViewer(int index)
    {
        if (index < 0 || index >= originalPicts.Count) return null;
        var bytes = RtfPict.Parse(Encoding.ASCII.GetBytes(originalPicts[index].Group)).FirstOrDefault()?.Bytes;
        if (bytes == null) return null;
        var bmp = RtfPict.DecodeScaled(bytes, 0, 0, 4096);
        if (bmp == null) return null;
        return ImageViewerWindow.Open(bmp);
    }

    public bool TryOpenLink(string text, int index, bool launch)
    {
        if (!NoteLinks.TryHit(text, index, out var url)) return false;
        LastLink = url;
        LinkLaunched = false;
        if (launch && !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); LinkLaunched = true; }
            catch (Exception e) { AppEnv.Log("link: " + e.Message); }
        }
        return true;
    }

    public bool AcceptDrop(IDataObject data)
    {
        if (AppEnv.ReadOnly || data == null) return false;
        bool any = false;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var f in files) if (NoteImages.IsImagePath(f) && InsertImageFile(f)) any = true;
        if (!any && data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            any = InsertBitmap(bmp);
        return any;
    }

    static bool CanDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap);

    bool PasteImage()
    {
        if (AppEnv.ReadOnly) return false;
        try
        {
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null && InsertBitmap(img)) return true;
            }
            if (Clipboard.ContainsFileDropList())
            {
                bool any = false;
                foreach (string? f in Clipboard.GetFileDropList())
                    if (!string.IsNullOrEmpty(f) && NoteImages.IsImagePath(f) && InsertImageFile(f)) any = true;
                if (any) return true;
            }
        }
        catch (Exception e) { AppEnv.Log("paste image: " + e.Message); }
        return false;
    }

    void PickImage()
    {
        if (AppEnv.ReadOnly || AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|모든 파일|*.*", Multiselect = true };
        if (dlg.ShowDialog() == true)
            foreach (var f in dlg.FileNames) InsertImageFile(f);
    }

    void OnEditorDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(Editor);
        var img = HitImage(dragStart);
        if (img == null) { selectedImage = null; return; }
        selectedImage = img;
        dragHandle = HandleAt(e.GetPosition(img), img);
        dragW = img.Width; dragH = img.Height;
        dragAspect = dragH < 1 ? 1 : dragW / dragH;
        Editor.CaptureMouse();
        e.Handled = true;
    }

    void OnEditorMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Editor);
        if (selectedImage != null && dragHandle >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx = pos.X - dragStart.X, dy = pos.Y - dragStart.Y;
            bool free = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            double w = dragW, h = dragH;
            if (dragHandle is 0 or 3 or 7) w -= dx; else if (dragHandle is 1 or 2 or 5) w += dx;
            if (dragHandle is 0 or 1 or 4) h -= dy; else if (dragHandle is 2 or 3 or 6) h += dy;
            if (!free) { if (dragHandle is 4 or 6) w = h * dragAspect; else h = w / Math.Max(0.05, dragAspect); }
            double maxW = Math.Max(40, EditorHost.ActualWidth - 28);
            w = Math.Clamp(w, 24, maxW); h = Math.Max(16, h);
            if (!free) h = w / Math.Max(0.05, dragAspect);
            selectedImage.Width = w; selectedImage.Height = h;
            e.Handled = true;
            return;
        }
        var over = HitImage(pos);
        if (over != null) Editor.Cursor = Cursors.SizeAll;
        else if (LinkUnder(pos, out _)) Editor.Cursor = Cursors.Hand;
        else Editor.Cursor = Cursors.IBeam;
    }

    void OnEditorUp(object sender, MouseButtonEventArgs e)
    {
        if (Editor.IsMouseCaptured) Editor.ReleaseMouseCapture();
        if (dragHandle >= 0 && selectedImage != null)
        {
            int i = pictViews.IndexOf(selectedImage);
            if (i >= 0) ApplyGoals(i, (int)Math.Round(selectedImage.Width * 15), (int)Math.Round(selectedImage.Height * 15));
        }
        else if (selectedImage != null)
        {
            var pos = e.GetPosition(Editor);
            if (Math.Abs(pos.X - dragStart.X) + Math.Abs(pos.Y - dragStart.Y) < 4 && LinkUnder(pos, out int hit) && hit >= 0)
                TryOpenLink(LinkSlice(pos, out int index), index, launch: true);
        }
        else
        {
            var pos = e.GetPosition(Editor);
            if (Math.Abs(pos.X - dragStart.X) + Math.Abs(pos.Y - dragStart.Y) < 4 && LinkUnder(pos, out int hit) && hit >= 0)
                TryOpenLink(LinkSlice(pos, out int index), index, launch: true);
        }
        dragHandle = -1;
    }

    void OnEditorRight(object sender, MouseButtonEventArgs e)
    {
        var img = HitImage(e.GetPosition(Editor));
        if (img == null) return;
        selectedImage = img;
        int index = pictViews.IndexOf(img);
        if (index < 0) return;
        var menu = new ContextMenu { PlacementTarget = Editor };
        void Add(string label, Action act) { var item = new MenuItem { Header = label }; item.Click += (_, _) => act(); menu.Items.Add(item); }
        Add("이미지 복사", () => CopyImage(index));
        Add("이미지 보기 (원본)", () => OpenViewer(index));
        menu.Items.Add(new Separator());
        foreach (var pc in new[] { 25, 50, 75, 100 }) { int v = pc; Add(v + "% 크기", () => SetImageScale(index, v / 100.0)); }
        Add("메모 폭에 맞춤", () => FitImageWidth(index));
        Add("원본 크기", () => SetImageScale(index, 1));
        menu.Items.Add(new Separator());
        Add("삭제", () => DeleteImage(index));
        menu.IsOpen = true;
        e.Handled = true;
    }

    void OnEditorDouble(object sender, MouseButtonEventArgs e)
    {
        var img = HitImage(e.GetPosition(Editor));
        if (img == null) return;
        int index = pictViews.IndexOf(img);
        if (index >= 0) { OpenViewer(index); e.Handled = true; }
    }

    Image? HitImage(Point inEditor)
    {
        var hit = VisualTreeHelper.HitTest(Editor, inEditor)?.VisualHit;
        while (hit != null && hit is not Image) hit = VisualTreeHelper.GetParent(hit);
        return hit as Image;
    }

    static int HandleAt(Point inImage, Image img)
    {
        double s = 10, w = img.ActualWidth > 1 ? img.ActualWidth : img.Width, h = img.ActualHeight > 1 ? img.ActualHeight : img.Height;
        (double x, double y)[] pts = [(0, 0), (w, 0), (w, h), (0, h), (w / 2, 0), (w, h / 2), (w / 2, h), (0, h / 2)];
        for (int i = 0; i < pts.Length; i++)
            if (Math.Abs(inImage.X - pts[i].x) <= s && Math.Abs(inImage.Y - pts[i].y) <= s) return i;
        return -1;
    }

    bool LinkUnder(Point inEditor, out int index)
    {
        index = -1;
        var slice = LinkSlice(inEditor, out index);
        return index >= 0 && NoteLinks.TryHit(slice, index, out _);
    }

    string LinkSlice(Point inEditor, out int index)
    {
        index = -1;
        var p = Editor.GetPositionFromPoint(inEditor, true);
        if (p == null) return "";
        var start = p.GetPositionAtOffset(-120, LogicalDirection.Backward) ?? Document.ContentStart;
        var end = p.GetPositionAtOffset(160, LogicalDirection.Forward) ?? Document.ContentEnd;
        var before = new TextRange(start, p).Text;
        index = before.Length;
        return new TextRange(start, end).Text;
    }

    // ---------- lifecycle ----------
    /// <summary>User close: saves, marks Open=false (the note is kept, like 3.4.7), fades out.</summary>
    public void CloseNote()
    {
        if (closing) return; closing = true;
        if (!discarding) { FlushText(); CaptureRect(); Meta.Open = false; metaDirty = true; FlushMeta(); }
        NoteManager.Forget(this);
        Fx.Disappear(Root, () => Close());
    }
    protected override void OnClosed(EventArgs e) { textTimer.Stop(); metaTimer.Stop(); Win32.ForgetOffScreen(Win32.Handle(this)); NoteManager.Forget(this); base.OnClosed(e); }
}

/// <summary>Last note package. The system clipboard runs only outside test modes.</summary>
public static class NoteClipSession
{
    public static string? LastPack { get; private set; }
    public static int SystemCalls { get; private set; }
    public static bool AllowsSystem => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;

    public static void Publish(string pack, string plain, string rtf, byte[]? png)
    {
        LastPack = pack;
        if (!AllowsSystem) return;
        SystemCalls++;
        var data = new DataObject();
        data.SetData(NoteClip.Format, pack);
        data.SetData(DataFormats.UnicodeText, plain ?? "");
        data.SetData(DataFormats.Rtf, rtf ?? "");
        if (png is { Length: > 0 })
        {
            try
            {
                using var ms = new System.IO.MemoryStream(png);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                data.SetImage(bmp);
            }
            catch { }
        }
        Clipboard.SetDataObject(data, copy: true);
    }
}
