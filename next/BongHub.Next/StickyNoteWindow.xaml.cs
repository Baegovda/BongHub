using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BongHub.Next;

public partial class StickyNoteWindow : Window
{
    public NoteMeta Meta { get; }
    public bool Ready { get; private set; }
    public bool TextDirty { get; private set; }
    public int RtfWrites, MetaWrites, ImageGuardHits;
    public string LoadStatus = "ok";          // ok | empty | corrupt | missing
    readonly DispatcherTimer textTimer, metaTimer;
    bool loading, metaDirty, corruptBackedUp, closing;
    int sourcePictCount;

    public StickyNoteWindow(NoteMeta meta)
    {
        Meta = meta;
        InitializeComponent();
        var surfA = NoteColors.SurfA(meta.Color);
        WindowFx.Attach(this, Root, wantBackdrop: false, solid: surfA);
        textTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(900), DispatcherPriority.Background, (_, _) => FlushText(), Dispatcher) { IsEnabled = false };
        metaTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => FlushMeta(), Dispatcher) { IsEnabled = false };
        Topmost = meta.TopMost;
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
        WindowFx.Of(this).AfterFirstFrame = () => Ready = true;

        Editor.TextChanged += (_, _) => { UpdateHint(); if (loading) return; TextDirty = true; textTimer.Stop(); textTimer.Start(); };
        Editor.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers != ModifierKeys.Control) return; e.Handled = true; Meta.Zoom = (float)Math.Clamp(Math.Round(Meta.Zoom + (e.Delta > 0 ? 0.1 : -0.1), 1), 0.6, 3.0); ApplyZoom(); MarkMeta(); };
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
        Closing += (_, e) => { if (!App.ShuttingDown && !closing) { e.Cancel = true; CloseNote(); } };
        UpdatePin();
    }

    // ---------- placement (3.4.7 stores physical pixels) ----------
    void PlacePhysical()
    {
        int x = Meta.X, y = Meta.Y, w = Math.Max(60, Meta.W), h = Math.Max(60, Meta.H);
        if (!AppEnv.Off && !Win32.OnAnyMonitor(x, y, w, h)) { var a = Win32.WorkAreaAtCursor(); x = a.Left + 60; y = a.Top + 60; }
        Win32.Place(this, x + AppEnv.OffsetX, y + AppEnv.OffsetY, w, h);
    }
    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == Win32.WM_EXITSIZEMOVE) { CaptureRect(); FlushMeta(); }
        else if ((msg == Win32.WM_MOVE || msg == Win32.WM_SIZE) && Ready) { if (CaptureRect()) MarkMeta(); }
        return IntPtr.Zero;
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
                try
                {
                    var doc = new FlowDocument();
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    using var ms = new MemoryStream(bytes);
                    range.Load(ms, DataFormats.Rtf);
                    Editor.Document = doc;
                }
                catch (Exception e) { LoadStatus = "corrupt"; AppEnv.Log($"rtf load failed {Meta.Id}: {e.Message}"); SetPlain(Meta.Text); }
                // parser "succeeded" but salvaged nothing while meta still has text → treat as damaged, show meta text
                if (LoadStatus == "ok" && DocImageCount() == 0 && PlainText().Trim().Length == 0 && !string.IsNullOrWhiteSpace(Meta.Text)) { LoadStatus = "corrupt"; SetPlain(Meta.Text); }
                else if (LoadStatus == "ok" && !(bytes.Length > 5 && bytes[0] == (byte)'{' && bytes[1] == (byte)'\\' && bytes[2] == (byte)'r')) { LoadStatus = "corrupt"; SetPlain(Meta.Text); }
            }
            else SetPlain(Meta.Text);
            StyleDocument();
        }
        finally { loading = false; }
        UpdateHint();
    }
    void SetPlain(string text)
    {
        var doc = new FlowDocument();
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n')) doc.Blocks.Add(new Paragraph(new Run(line)));
        Editor.Document = doc;
    }
    void StyleDocument()
    {
        var d = Editor.Document;
        d.PagePadding = new Thickness(4, 2, 4, 6);
        d.FontFamily = (FontFamily)FindResource("UiFont");
        d.Foreground = new SolidColorBrush(NoteColors.Text);
        d.FontSize = ConfigStore.Notes.FontSize * 96.0 / 72.0;
        d.LineHeight = double.NaN;
    }
    static int CountPict(byte[] b) { int n = 0; var s = Encoding.ASCII.GetString(b); for (int i = s.IndexOf("\\pict", StringComparison.Ordinal); i >= 0; i = s.IndexOf("\\pict", i + 5, StringComparison.Ordinal)) n++; return n; }
    int DocImageCount() => CountImages(Editor.Document.Blocks);
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
    public string PlainText() => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.TrimEnd('\r', '\n');
    void UpdateHint() => Hint.Visibility = Editor.Document.Blocks.Count <= 1 && PlainText().Length == 0 && DocImageCount() == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---------- saving (debounced: one write per gesture) ----------
    void MarkMeta() { metaDirty = true; metaTimer.Stop(); metaTimer.Start(); }
    public void FlushText()
    {
        textTimer.Stop();
        if (!TextDirty) return;
        TextDirty = false;
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        var rtf = ms.ToArray();
        int wantImages = Math.Max(sourcePictCount, DocImageCount());
        Meta.Text = PlainText();
        if (CountPict(rtf) < wantImages)
        {
            // WPF could not re-encode an embedded image: keep the existing .rtf (images stay safe), save text in meta only
            ImageGuardHits++; AppEnv.Log($"image guard: kept existing rtf for {Meta.Id}");
        }
        else
        {
            if (LoadStatus == "corrupt" && !corruptBackedUp && !AppEnv.DenyWrite())
            {
                try { File.Copy(NoteStore.RtfPath(Meta.Id), NoteStore.RtfPath(Meta.Id) + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
                corruptBackedUp = true;
            }
            if (NoteStore.SaveRtf(Meta, rtf)) RtfWrites++;
        }
        metaDirty = true; FlushMeta();
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
        var old = Background; Meta.Color = key; ApplyColor();
        if (Fx.Enabled && old is not null) { Root.Opacity = 0.85; Fx.Fade(Root, 1, 220); }
        metaDirty = true; FlushMeta();
    }
    public void SetColorRefresh() => ApplyColor();
    public void ApplyFontSize() { loading = true; try { Editor.Document.FontSize = ConfigStore.Notes.FontSize * 96.0 / 72.0; } finally { loading = false; } }
    /// <summary>Same window opacity as 3.4.8 NoteForm.ApplyOpacity (30–100, floor 0.3).</summary>
    public void ApplyOpacity()
    {
        int v = ConfigStore.Notes.Opacity;
        if (v < 30 || v > 100) v = 100;
        double o = Math.Max(0.3, Math.Min(1.0, v / 100.0));
        if (Math.Abs(Opacity - o) > 0.001) Opacity = o;
    }
    public void ApplyReadOnly() => Editor.IsReadOnly = AppEnv.ReadOnly;
    void ApplyZoom() => Editor.LayoutTransform = Math.Abs(Meta.Zoom - 1) < 0.01 ? Transform.Identity : new ScaleTransform(Meta.Zoom, Meta.Zoom);
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
        var all = new MenuItem { Header = "모든 메모 보기" }; all.Click += (_, _) => App.Instance.ShowSettings("notes"); m.Items.Add(all);
        m.IsOpen = true;
    }

    // ---------- lifecycle ----------
    /// <summary>User close: saves, marks Open=false (the note is kept, like 3.4.7), fades out.</summary>
    public void CloseNote()
    {
        if (closing) return; closing = true;
        FlushText(); CaptureRect(); Meta.Open = false; metaDirty = true; FlushMeta();
        NoteManager.Forget(this);
        Fx.Disappear(Root, () => Close());
    }
    protected override void OnClosed(EventArgs e) { textTimer.Stop(); metaTimer.Stop(); NoteManager.Forget(this); base.OnClosed(e); }
}
