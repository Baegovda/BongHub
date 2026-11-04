using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public const double NoteMinWidth = 120;
    public const double NoteMinHeight = 90;
    public const double NoteMaxWidth = 30000;
    public const double NoteMaxHeight = 30000;
    /// <summary>Physical pixels the window shows, and the range a new save may write. A file may still hold an older out-of-range size until the user moves the note.</summary>
    public static int ClampNoteSpan(int value, double min, double max) => (int)Math.Clamp(value, min, max);
    public readonly record struct NoteSpan(int X, int Y, int W, int H);
    /// <summary>
    /// Next stored rectangle. Minimized, maximized, and a collapsed or out-of-range live rect keep <paramref name="stored"/>.
    /// Showing the clamp of a legacy size is not a user resize, so that file value stays until the note actually moves.
    /// </summary>
    public static bool TryNoteSize(NoteSpan stored, int liveX, int liveY, int liveW, int liveH, bool iconic, bool zoomed, out NoteSpan next)
    {
        next = stored;
        if (iconic || zoomed) return false;
        if (liveW < NoteMinWidth || liveH < NoteMinHeight || liveW > NoteMaxWidth || liveH > NoteMaxHeight) return false;
        int w = ClampNoteSpan(liveW, NoteMinWidth, NoteMaxWidth);
        int h = ClampNoteSpan(liveH, NoteMinHeight, NoteMaxHeight);
        if (liveX == stored.X && liveY == stored.Y && w == stored.W && h == stored.H) return false;
        if (liveX == stored.X && liveY == stored.Y
            && w == ClampNoteSpan(stored.W, NoteMinWidth, NoteMaxWidth)
            && h == ClampNoteSpan(stored.H, NoteMinHeight, NoteMaxHeight))
            return false;
        next = new NoteSpan(liveX, liveY, w, h);
        return true;
    }

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
    bool loading, metaDirty, corruptBackedUp, closing, discarding, sizing, stylingLinks;
    Brush linkBrush = Brushes.Transparent;
    Brush titleInkBrush = Brushes.White;
    Run? hoveredLink;
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
        // XAML min/max are DIPs. Notes store physical pixels, so the limits have to be divided by DPI
        // before the HWND is created. Otherwise 90 DIP becomes 113 px at 125% and the file's 90 px floor is lost.
        ApplyPhysicalLimits(Win32.SystemScale);
        DpiChanged += (_, e) => { ApplyPhysicalLimits(e.NewDpi.DpiScaleX); BeginTopology(); };
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
        EditorHost.PreviewMouseLeftButtonDown += (_, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl)
            {
                if (editor == null && !AppEnv.ReadOnly) EnsureEditor();
                if (editor != null && LinkUnder(e.GetPosition(editor), out int at) && at >= 0)
                {
                    TryOpenLink(LinkSlice(e.GetPosition(editor), out int index), index, launch: true);
                    e.Handled = true;
                    return;
                }
            }
            if (editor == null && !AppEnv.ReadOnly) EnsureEditor().Focus();
        };
        ApplyTitle();
        TitleBox.TextChanged += (_, _) => { OnTitleEdited(); SizeTitle(); };
        TitleBox.GotKeyboardFocus += (_, _) => SizeTitle();
        TitleBox.LostKeyboardFocus += (_, _) => SizeTitle();
        Caption.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (CaptionPress(e.GetPosition(this), e.ClickCount) != "drag") return;
            e.Handled = true;
        };
        TitleBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            MoveToBody();
        };
        TitleBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopyTitleText(false); e.Handled = true; }));
        TitleBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => { CopyTitleText(true); e.Handled = true; }));
        TitleBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { PasteTitleClipboard(); e.Handled = true; }));
        DataObject.AddPastingHandler(TitleBox, OnTitlePasting);
        TitleBox.ContextMenu = EditMenu(TitleBox);
        Viewer.Loaded += (_, _) => WatchScroll(Viewer);
        Deactivated += (_, _) => HoldView();
        Activated += (_, _) => { if (!textClick) Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RestoreView); };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            var source = e.OriginalSource as DependencyObject;
            textClick = source != null && IsUnder(EditorHost, source);
        };

        NewBtn.Click += (_, _) => NoteManager.NewNote();
        FontBtn.Click += (_, _) => ToggleFormatBar();
        ColorBtn.Click += (_, _) => ShowPalette();
        CustomColor.Preview += key => PreviewColor(key);
        CustomColor.Commit += key => { Palette.IsOpen = false; SetColor(key); };
        ListBtn.Click += (_, _) => ShowList();
        PinBtn.Click += (_, _) => SetTopMost(!Meta.TopMost);
        CloseBtn.Click += (_, _) => CloseNote();
        MoreBtn.Click += (_, _) => ShowHiddenTools();
        SizeChanged += (_, _) => FitCaption();
        BoldBtn.Click += (_, _) => EditingCommands.ToggleBold.Execute(null, Editor);
        ItalicBtn.Click += (_, _) => EditingCommands.ToggleItalic.Execute(null, Editor);
        UnderBtn.Click += (_, _) => EditingCommands.ToggleUnderline.Execute(null, Editor);
        StrikeBtn.Click += (_, _) => ToggleStrike();
        BulletBtn.Click += (_, _) => EditingCommands.ToggleBullets.Execute(null, Editor);
        SizeBtn.Click += (_, _) => ShowHeadings(SizeBtn);
        SmallerBtn.Click += (_, _) => GrowFont(-1);
        LargerBtn.Click += (_, _) => GrowFont(1);
        ResetSizeBtn.Click += (_, _) => ResetFontSize();
        ImageBtn.Click += (_, _) => PickImage();
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
        int x = Meta.X, y = Meta.Y;
        int w = ClampNoteSpan(Meta.W, NoteMinWidth, NoteMaxWidth);
        int h = ClampNoteSpan(Meta.H, NoteMinHeight, NoteMaxHeight);
        if (!AppEnv.Off)
        {
            var areas = Win32.WorkAreas().Where(a => a.W >= NoteMinWidth && a.H >= NoteMinHeight).ToArray();
            if (areas.Length > 0)
            {
                var c = NoteLayout.Clamp(new NoteLayout.Box(x, y, w, h), areas, 38);
                x = c.X; y = c.Y;
                w = ClampNoteSpan(c.W, NoteMinWidth, NoteMaxWidth);
                h = ClampNoteSpan(c.H, NoteMinHeight, NoteMaxHeight);
            }
            else if (!Win32.OnAnyMonitor(x, y, w, h)) { var a = Win32.WorkAreaAtCursor(); x = a.Left + 60; y = a.Top + 60; }
        }
        int px = x + AppEnv.OffsetX, py = y + AppEnv.OffsetY;
        double dpi = Win32.Scale(this);
        if (dpi < 0.5) dpi = 1;
        ApplyPhysicalLimits(dpi);
        Width = w / dpi;
        Height = h / dpi;
        // Record the physical target before WPF applies Left/Top, so a monitor pull is undone exactly.
        Win32.RememberOffScreen(Win32.Handle(this), px, py);
        Win32.Place(this, px, py, w, h);
    }
    /// <summary>
    /// WPF min/max are DIPs and <c>WM_GETMINMAXINFO</c> ceilings them into physical pixels.
    /// Keep that ceiling on the stored physical limit (90 px, not 90 DIP).
    /// </summary>
    void ApplyPhysicalLimits(double dpi)
    {
        if (dpi < 0.5) dpi = 1;
        MinWidth = DipForPhysicalLimit(NoteMinWidth, dpi);
        MinHeight = DipForPhysicalLimit(NoteMinHeight, dpi);
        MaxWidth = NoteMaxWidth / dpi;
        MaxHeight = NoteMaxHeight / dpi;
    }
    /// <summary>One physical pixel under the limit, so a DIP ceiling still lands on that pixel.</summary>
    static double DipForPhysicalLimit(double physical, double dpi) => Math.Max(0, (physical - 0.49) / dpi);
    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        // Runs before WPF's own handler (HwndSource invokes the newest hook first) and leaves handled false.
        if (msg == Win32.WM_GETMINMAXINFO) Win32.AllowTallWindow(lp);
        Win32.OffScreenHook(h, msg, wp, lp, ref handled);
        if (msg == Win32.WM_ENTERSIZEMOVE) { sizing = true; textTimer.Stop(); metaTimer.Stop(); }
        else if (msg == Win32.WM_EXITSIZEMOVE) { sizing = false; CaptureRect(); FlushMeta(); if (TextDirty) textTimer.Start(); }
        else if (msg == Win32.WM_MOUSEACTIVATE) Win32.AllowActivate(h);
        else if (msg == Win32.WM_DISPLAYCHANGE && !AppEnv.Off) BeginTopology();
        else if ((msg == Win32.WM_MOVE || msg == Win32.WM_SIZE) && Ready && !sizing && !TopologyHold)
        {
            // SIZE_MINIMIZED / SIZE_MAXIMIZED carry the iconic or zoomed rect, not the restore size.
            if (msg == Win32.WM_SIZE && (wp == (IntPtr)Win32.SIZE_MINIMIZED || wp == (IntPtr)Win32.SIZE_MAXIMIZED))
                return IntPtr.Zero;
            if (CaptureRect()) MarkMeta();
        }
        return IntPtr.Zero;
    }
    public bool TopologyHold { get; private set; }
    public string AnchorId { get; private set; } = "";
    DispatcherTimer? topologyTimer;
    int topologyWaits;
    List<ProfileMonitor> anchorBefore = new();

    public void SetTopologyHold(bool hold) => TopologyHold = hold;

    void BeginTopology()
    {
        if (AppEnv.Off) return;
        if (NoteManager.SwitchPin)
        {
            TopologyHold = true;
            if (NoteManager.TryPin(this, out var pin))
                Win32.Place(this, pin.X + AppEnv.OffsetX, pin.Y + AppEnv.OffsetY, pin.W, pin.H);
            return;
        }
        TopologyHold = true;
        topologyWaits = 0;
        if (anchorBefore.Count == 0) anchorBefore = RememberAnchors();
        topologyTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) => FinishTopology(Win32.WorkAreas(), false), Dispatcher);
        topologyTimer.Stop();
        topologyTimer.Start();
    }

    static List<ProfileMonitor> RememberAnchors()
    {
        var list = new List<ProfileMonitor>();
        try
        {
            foreach (var box in Win32.WorkAreas())
                list.Add(new ProfileMonitor { X = box.X, Y = box.Y, W = box.W, H = box.H, Connected = true, Enabled = true });
        }
        catch { }
        return list;
    }

    /// <summary>Re-reads the desk after it settles. A thin or empty work area does not change the saved size.</summary>
    public void FinishTopology(NoteLayout.Box[] areas, bool forceTransient)
    {
        topologyTimer?.Stop();
        if (NoteManager.SwitchPin && NoteManager.TryPin(this, out var pin))
        {
            TopologyHold = true;
            if (!AppEnv.Off) Win32.Place(this, pin.X + AppEnv.OffsetX, pin.Y + AppEnv.OffsetY, pin.W, pin.H);
            return;
        }
        bool transient = forceTransient || NoteLayout.IsTransient(areas);
        if (transient && !forceTransient && topologyWaits < 8 && !AppEnv.Off)
        {
            topologyWaits++;
            TopologyHold = true;
            topologyTimer?.Start();
            return;
        }
        if (transient)
        {
            TopologyHold = false;
            return;
        }
        var saved = new NoteLayout.Box(Meta.X, Meta.Y, Meta.W, Meta.H);
        var next = NoteLayout.Rehome(saved, AnchorId, anchorBefore, anchorBefore, false);
        TopologyHold = false;
        anchorBefore = RememberAnchors();
        if (next.W != saved.W || next.H != saved.H) next = saved;
        if (!AppEnv.Off) Win32.Place(this, next.X + AppEnv.OffsetX, next.Y + AppEnv.OffsetY, next.W, next.H);
        if (next.X == Meta.X && next.Y == Meta.Y) return;
        Meta.X = next.X;
        Meta.Y = next.Y;
        metaDirty = true;
        FlushMeta();
    }

    public void ClampToScreens()
    {
        if (AppEnv.Off) return;
        var hwnd = Win32.Handle(this);
        if (hwnd != IntPtr.Zero && (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd) || WindowState != WindowState.Normal))
            return;
        var areas = Win32.WorkAreas().Where(a => a.W >= NoteMinWidth && a.H >= NoteMinHeight).ToArray();
        if (areas.Length == 0) return;
        int w = ClampNoteSpan(Meta.W, NoteMinWidth, NoteMaxWidth);
        int h = ClampNoteSpan(Meta.H, NoteMinHeight, NoteMaxHeight);
        var c = NoteLayout.Clamp(new NoteLayout.Box(Meta.X, Meta.Y, w, h), areas, 38);
        w = ClampNoteSpan(c.W, NoteMinWidth, NoteMaxWidth);
        h = ClampNoteSpan(c.H, NoteMinHeight, NoteMaxHeight);
        bool moved = c.X != Meta.X || c.Y != Meta.Y;
        if (!moved && w == Meta.W && h == Meta.H) return;
        // The display clamp of a legacy size is not a resize. Leave that file alone until the note moves.
        if (!moved && w == ClampNoteSpan(Meta.W, NoteMinWidth, NoteMaxWidth) && h == ClampNoteSpan(Meta.H, NoteMinHeight, NoteMaxHeight))
            return;
        Meta.X = c.X; Meta.Y = c.Y; Meta.W = w; Meta.H = h;
        Win32.Place(this, c.X + AppEnv.OffsetX, c.Y + AppEnv.OffsetY, w, h);
        metaDirty = true; FlushMeta();
    }
    public void EnableDuringModal()
    {
        var h = Win32.Handle(this);
        if (h != IntPtr.Zero) Win32.EnableWindow(h, true);
    }
    /// <summary>Copies the restored window rect into Meta. Minimized, maximized, and collapsed rects do not count.</summary>
    bool CaptureRect()
    {
        if (!Ready) return false;
        var hwnd = Win32.Handle(this);
        if (hwnd == IntPtr.Zero) return false;
        var r = Win32.Rect(this);
        bool iconic = Win32.IsIconic(hwnd) || WindowState == WindowState.Minimized;
        bool zoomed = Win32.IsZoomed(hwnd) || WindowState == WindowState.Maximized;
        return CaptureSizeForTest(r.Left - AppEnv.OffsetX, r.Top - AppEnv.OffsetY, r.Width, r.Height, iconic, zoomed);
    }
    /// <summary>Same decision as <see cref="CaptureRect"/>, with the rectangle supplied by the caller. Tests use this so they never minimize a real window.</summary>
    public bool CaptureSizeForTest(int x, int y, int w, int h, bool iconic, bool zoomed)
    {
        if (TopologyHold) return false;
        if (!TryNoteSize(new NoteSpan(Meta.X, Meta.Y, Meta.W, Meta.H), x, y, w, h, iconic, zoomed, out var next))
            return false;
        Meta.X = next.X; Meta.Y = next.Y; Meta.W = next.W; Meta.H = next.H;
        metaDirty = true;
        return true;
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
            Foreground = InkBrush(NoteColors.Text),
            CaretBrush = InkBrush(NoteColors.Text),
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
        box.TextChanged += (_, _) =>
        {
            if (stylingLinks) return;
            UpdateHint();
            if (loading) return;
            TextDirty = true;
            RefreshLinks();
            if (pendingEnter is { } plan)
            {
                pendingEnter = null;
                var born = box.CaretPosition?.Paragraph;
                if (born != null)
                {
                    blockLock = true;
                    try { Stamp(born, plan.Next, plan.Exit); }
                    finally { blockLock = false; }
                }
            }
            else if (!blockLock) SyncBlock(box.CaretPosition?.Paragraph);
            if (sizing) return;
            textTimer.Stop();
            textTimer.Start();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (SlashOpen)
            {
                if (e.Key == Key.Down) { slashIndex = Math.Min(slashHits.Count - 1, slashIndex + 1); PaintSlash(); e.Handled = true; return; }
                if (e.Key == Key.Up) { slashIndex = Math.Max(0, slashIndex - 1); PaintSlash(); e.Handled = true; return; }
                if (e.Key == Key.Enter || e.Key == Key.Tab) { e.Handled = true; CommitSlash(); return; }
                if (e.Key == Key.Escape) { e.Handled = true; CloseSlash(); return; }
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                var para = box.CaretPosition?.Paragraph;
                if (para != null) pendingEnter = NoteBlocks.OnEnter(KindOf(para), ParagraphText(para));
            }
        };
        box.PreviewDragOver += (_, e) => { if (CanDrop(e.Data)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        box.Drop += (_, e) => { if (AcceptDrop(e.Data)) e.Handled = true; };
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopySelection(false); e.Handled = true; }));
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => { CopySelection(true); e.Handled = true; }));
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { PasteClipboard(); e.Handled = true; }));
        box.ContextMenu = EditMenu(box);
        box.PreviewMouseLeftButtonDown += OnEditorDown;
        box.PreviewMouseMove += OnEditorMove;
        box.PreviewMouseLeftButtonUp += OnEditorUp;
        box.PreviewMouseRightButtonUp += OnEditorRight;
        box.PreviewMouseDoubleClick += OnEditorDouble;
        box.SelectionChanged += (_, _) =>
        {
            if (restoringView || freezeCaret || !box.IsKeyboardFocusWithin) return;
            HeldCaret = ReadCaret();
            HeldSpot = CaptureSpot(box);
        };
        box.PreviewLostKeyboardFocus += (_, _) => HoldView();
        box.LayoutUpdated += (_, _) => { if (freezeCaret) ApplyHeld(); };
        box.Loaded += (_, _) => WatchScroll(box);
        editor = box;
        EditorHost.Children.Insert(0, box);
        Viewer.Visibility = Visibility.Collapsed;
        ApplyZoom();
        ApplyInk();
        RestoreView();
        return box;
    }

    void HoldView()
    {
        if (!freezeCaret) RememberView();
        freezeCaret = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RestoreView);
    }

    public double HeldScroll { get; private set; }
    /// <summary>Caret as a character count from the start of the text, not a symbol offset.</summary>
    public int HeldCaret { get; private set; }
    public readonly record struct CaretSpot(int Paragraph, int Offset, LogicalDirection Direction);
    public CaretSpot HeldSpot { get; private set; }
    bool textClick;
    bool restoringView, freezeCaret, idleQueued;

    void WatchScroll(FrameworkElement host)
    {
        var sv = FindScroll(host);
        if (sv == null || Equals(sv.Tag, "watched")) return;
        sv.Tag = "watched";
        sv.ScrollChanged += (_, e) =>
        {
            if (restoringView || e.VerticalChange == 0) return;
            if (!host.IsKeyboardFocusWithin && sv.VerticalOffset + 1 < HeldScroll) return;
            HeldScroll = sv.VerticalOffset;
        };
    }

    public ScrollViewer? EditorScroll
    {
        get
        {
            var host = editor != null ? (FrameworkElement)editor : Viewer;
            host.ApplyTemplate();
            return FindScroll(host);
        }
    }

    public double ReadScroll()
    {
        UpdateLayout();
        return EditorScroll?.VerticalOffset ?? HeldScroll;
    }

    public int ReadCaret()
    {
        if (editor == null) return HeldCaret;
        try { return CharsUntil(editor.Document, editor.CaretPosition); }
        catch { return HeldCaret; }
    }

    /// <summary>Characters in text runs before <paramref name="caret"/>. Element edges are not counted, so the number survives a focus change.</summary>
    static int CharsUntil(FlowDocument doc, TextPointer caret)
    {
        int n = 0;
        var pos = doc.ContentStart;
        while (pos != null && pos.CompareTo(caret) < 0 && pos.CompareTo(doc.ContentEnd) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = pos.GetTextRunLength(LogicalDirection.Forward);
                var next = pos.GetPositionAtOffset(len, LogicalDirection.Forward);
                if (next == null || next.CompareTo(caret) > 0)
                {
                    n += new TextRange(pos, caret).Text.Length;
                    break;
                }
                n += len;
                pos = next;
            }
            else
            {
                var next = pos.GetNextContextPosition(LogicalDirection.Forward);
                if (next == null || next.CompareTo(pos) == 0) break;
                pos = next;
            }
        }
        return n;
    }

    static TextPointer PointerAtChars(FlowDocument doc, int chars)
    {
        if (chars <= 0) return doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? doc.ContentStart;
        int left = chars;
        var pos = doc.ContentStart;
        while (pos != null && pos.CompareTo(doc.ContentEnd) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = pos.GetTextRunLength(LogicalDirection.Forward);
                if (left <= len) return pos.GetPositionAtOffset(left, LogicalDirection.Forward) ?? doc.ContentEnd;
                left -= len;
                var next = pos.GetPositionAtOffset(len, LogicalDirection.Forward);
                if (next == null || next.CompareTo(pos) == 0) break;
                pos = next;
            }
            else
            {
                var next = pos.GetNextContextPosition(LogicalDirection.Forward);
                if (next == null || next.CompareTo(pos) == 0) break;
                pos = next;
            }
        }
        return doc.ContentEnd;
    }

    public void RememberView()
    {
        var sv = EditorScroll;
        if (sv != null && !(freezeCaret && sv.VerticalOffset + 1 < HeldScroll)) HeldScroll = sv.VerticalOffset;
        if (editor != null && !freezeCaret)
        {
            HeldCaret = ReadCaret();
            HeldSpot = CaptureSpot(editor);
        }
    }

    public int CaretParagraph => editor == null ? HeldSpot.Paragraph : CaptureSpot(editor).Paragraph;
    public int CaretOffsetInParagraph => editor == null ? HeldSpot.Offset : CaptureSpot(editor).Offset;

    static CaretSpot CaptureSpot(RichTextBox box)
    {
        var caret = box.CaretPosition;
        var para = caret.Paragraph;
        if (para == null) return new CaretSpot(0, 0, LogicalDirection.Forward);
        int index = 0;
        foreach (var block in box.Document.Blocks)
        {
            if (ReferenceEquals(block, para)) break;
            index++;
        }
        int offset = new TextRange(para.ContentStart, caret).Text.Length;
        return new CaretSpot(index, offset, caret.LogicalDirection);
    }

    static void ApplySpot(RichTextBox box, CaretSpot spot)
    {
        var blocks = box.Document.Blocks.ToList();
        if (blocks.Count == 0) return;
        int index = Math.Clamp(spot.Paragraph, 0, blocks.Count - 1);
        var para = blocks[index] as Paragraph;
        if (para == null)
        {
            var paragraphs = blocks.OfType<Paragraph>().ToList();
            if (paragraphs.Count == 0) return;
            para = paragraphs[Math.Clamp(spot.Paragraph, 0, paragraphs.Count - 1)];
        }
        int Len(TextPointer pointer) => new TextRange(para.ContentStart, pointer).Text.Length;
        TextPointer At(int offset, LogicalDirection dir)
        {
            if (offset <= 0) return para.ContentStart;
            return para.ContentStart.GetPositionAtOffset(offset, dir) ?? para.ContentEnd;
        }
        var forward = At(spot.Offset, LogicalDirection.Forward);
        var backward = At(spot.Offset, LogicalDirection.Backward);
        var chosen = Len(backward) == spot.Offset ? backward : Len(forward) == spot.Offset ? forward : para.ContentEnd;
        box.CaretPosition = chosen;
    }

    static bool SpotIsEmpty(RichTextBox box, CaretSpot spot)
    {
        var blocks = box.Document.Blocks.ToList();
        if (spot.Paragraph < 0 || spot.Paragraph >= blocks.Count) return false;
        return blocks[spot.Paragraph] is Paragraph para && new TextRange(para.ContentStart, para.ContentEnd).Text.Length == 0;
    }

    public void ChromeActivateLikeTest()
    {
        textClick = false;
        RestoreView();
    }

    /// <summary>Same walk as a left click whose <c>OriginalSource</c> is <paramref name="source"/>.</summary>
    public bool ClickFrom(DependencyObject source)
    {
        textClick = IsUnder(EditorHost, source);
        return textClick;
    }

    static bool IsUnder(DependencyObject root, DependencyObject? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, root)) return true;
            node = NextParent(node);
        }
        return false;
    }

    /// <summary><see cref="VisualTreeHelper.GetParent"/> throws unless the node is a <see cref="Visual"/> or <see cref="System.Windows.Media.Media3D.Visual3D"/>. A click's source is often a <see cref="Run"/> or <see cref="Paragraph"/>.</summary>
    static DependencyObject? NextParent(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    void ApplyHeld()
    {
        if (restoringView) return;
        restoringView = true;
        try
        {
            var host = editor != null ? (FrameworkElement)editor : Viewer;
            host.ApplyTemplate();
            var sv = FindScroll(host);
            if (sv != null && Math.Abs(sv.VerticalOffset - HeldScroll) > 0.5) sv.ScrollToVerticalOffset(HeldScroll);
            if (editor != null)
            {
                bool emptyLine = SpotIsEmpty(editor, HeldSpot);
                ApplySpot(editor, HeldSpot);
                if (!emptyLine && ReadCaret() != HeldCaret)
                    editor.CaretPosition = PointerAtChars(editor.Document, HeldCaret);
            }
        }
        catch { }
        finally { restoringView = false; }
    }

    public void RestoreView()
    {
        ApplyHeld();
        if (idleQueued) return;
        idleQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            idleQueued = false;
            ApplyHeld();
            var host = editor != null ? (FrameworkElement)editor : Viewer;
            host.UpdateLayout();
            ApplyHeld();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => freezeCaret = false);
        });
    }

    /// <summary>Focus left the note. The document stays put; only the scroll and caret are put back.</summary>
    public void ParkEditor()
    {
        RememberView();
        freezeCaret = true;
        if (editor != null)
        {
            restoringView = true;
            try
            {
                var start = editor.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? editor.Document.ContentStart;
                if (editor.CaretPosition.CompareTo(start) != 0) editor.CaretPosition = start;
            }
            catch { }
            finally { restoringView = false; }
        }
        RestoreView();
    }

    public void ResumeEditor()
    {
        EnsureEditor();
        RestoreView();
    }

    static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScroll(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    // ---------- document ----------
    void LoadDocument()
    {
        loading = true;
        try
        {
            NoteStore.PrunePending(Meta.Id);
            var path = NoteStore.RtfPath(Meta.Id);
            string? pendingPath = NoteStore.NewestPendingRtf(Meta.Id);
            bool pendingWins = pendingPath != null && (!File.Exists(path) || File.GetLastWriteTimeUtc(pendingPath) >= File.GetLastWriteTimeUtc(path));
            var loadPath = pendingWins ? pendingPath! : path;
            var bytes = File.Exists(loadPath) ? File.ReadAllBytes(loadPath) : null;
            if (bytes == null) LoadStatus = "missing";
            else if (bytes.Length == 0) LoadStatus = "empty";
            if (bytes is { Length: > 0 })
            {
                int pictCount = CountPict(bytes);
                var picts = pictCount > 0 ? RtfPict.Parse(bytes) : [];
                bool proxy = picts.Count > 0 && picts.Count == pictCount;
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
        d.FontSize = PointsToDip(ClampPt(ConfigStore.Notes.FontSize));
        d.LineHeight = double.NaN;
        ReleaseAutomaticInk(d);
        ApplyInk();
    }
    /// <summary>Stored #F0F0F6 / #ECECF0 / the current automatic ink become inherited, so the note color picks the brush. The document brush itself stays.</summary>
    static void ReleaseAutomaticInk(FlowDocument doc)
    {
        void Walk(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                if (child is TextElement el && el.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush brush && NoteColors.IsAutomaticInk(brush.Color))
                    el.ClearValue(TextElement.ForegroundProperty);
                Walk(child);
            }
        }
        Walk(doc);
    }
    static SolidColorBrush InkBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    /// <summary>Title uses the same opaque brush as the note body.</summary>
    void ApplyInk()
    {
        var body = InkBrush(NoteColors.Ink(Meta.Color));
        titleInkBrush = body;
        double size = Math.Round(PointsToDip(ClampPt(ConfigStore.Notes.FontSize)) * 1.6, 2);
        TitleBox.Opacity = 1;
        TitleBox.Foreground = body;
        TitleBox.CaretBrush = body;
        TitleBox.FontSize = size;
        TitleHint.Foreground = body;
        TitleHint.FontSize = size;
        TitleClip.Foreground = body;
        TitleClip.FontSize = size;
        TitleClip.FontFamily = TitleBox.FontFamily;
        foreach (var button in new[] { NewBtn, FontBtn, ColorBtn, ListBtn, CloseBtn, MoreBtn }) button.Foreground = body;
        if (editor != null)
        {
            editor.Foreground = body;
            editor.CaretBrush = body;
        }
        SizeTitle();
        linkBrush = InkBrush(NoteColors.LinkInk(Meta.Color));
        UpdatePin();
        var doc = editor != null ? editor.Document : Viewer.Document;
        var ink = NoteColors.Ink(Meta.Color);
        if (doc != null && (doc.Foreground as SolidColorBrush)?.Color != ink)
        {
            bool prev = loading;
            loading = true;
            try { doc.Foreground = body; }
            finally { loading = prev; }
        }
        RefreshLinks();
    }
    static int CountPict(byte[] b) { int n = 0; var s = Encoding.ASCII.GetString(b); for (int i = s.IndexOf("\\pict", StringComparison.Ordinal); i >= 0; i = s.IndexOf("\\pict", i + 5, StringComparison.Ordinal)) n++; return n; }
    public int DocImageCount() => CountImages(Document.Blocks);
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
        RememberView();
        TextDirty = false;
        bool wasLoading = loading;
        loading = true;
        try { AdoptScreenImages(); }
        finally { loading = wasLoading; }
        byte[] wpfRtf = SaveRtfWithoutLinkChrome();
        byte[]? rtf = wpfRtf;
        if (originalPicts.Count > 0)
        {
            // The in-memory bitmaps are display-sized. Put the original pict groups back so the file stays full resolution.
            // Goal edits are already in those groups; the blip hex is not rewritten.
            var merged = RtfPict.Splice(RtfPict.Latin1.GetString(wpfRtf), originalPicts);
            if (merged != null) rtf = RtfPict.Latin1.GetBytes(merged);
        }
        int emitted = rtf == null ? 0 : CountPict(rtf);
        if (!DropPictsForTest && originalPicts.Count > 0 && emitted != originalPicts.Count && DocImageCount() == originalPicts.Count && CountPict(wpfRtf) == 0)
        {
            // WPF kept the images on screen but did not write \pict. Insert the groups we already built.
            var injected = InjectPicts(wpfRtf, originalPicts);
            if (injected != null) rtf = injected;
        }
        if (rtf != null) rtf = NoteRtf.StripAutomaticInk(rtf);
        if (DropPictsForTest && rtf != null) rtf = RtfPict.RemoveGroups(rtf);
        byte[] onDisk = [];
        try { if (File.Exists(NoteStore.RtfPath(Meta.Id))) onDisk = File.ReadAllBytes(NoteStore.RtfPath(Meta.Id)); } catch { onDisk = []; }
        int held = originalPicts.Count;
        var chosen = NoteRtf.Choose(onDisk.Length > 0 ? onDisk : (rtf ?? []), rtf ?? [], DocImageCount(), held, out var dropWarn);
        Meta.Text = PlainText();
        if (dropWarn != null)
        {
            ImageGuardHits++;
            bool pending = NoteStore.SavePendingRtf(Meta, rtf ?? []);
            AppEnv.Log($"image guard: kept existing rtf for {Meta.Id} {dropWarn}" + (pending ? " pending saved" : " pending FAILED"));
            metaDirty = true; FlushMeta();
            RestoreView();
            return;
        }
        rtf = chosen;
        if (LoadStatus == "corrupt" && !corruptBackedUp && !AppEnv.DenyWrite())
        {
            try { File.Copy(NoteStore.RtfPath(Meta.Id), NoteStore.RtfPath(Meta.Id) + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
            corruptBackedUp = true;
        }
        if (NoteStore.SaveRtf(Meta, rtf!))
        {
            RtfWrites++;
            NoteStore.ClearPendingRtf(Meta.Id);
            WarnIfLarge();
        }
        metaDirty = true; FlushMeta();
        RestoreView();
    }
    /// <summary>
    /// A picture on screen with no PNG/JPEG group (GIF, metafile, or a paste WPF will not write)
    /// becomes a PNG group so the save can include it. Original PNG/JPEG groups stay.
    /// </summary>
    void AdoptScreenImages()
    {
        var images = CollectImages(Document);
        if (images.Count == 0)
        {
            originalPicts.Clear();
            pictViews.Clear();
            return;
        }
        int fit = Math.Max(80, (int)(EditorHost.ActualWidth > 1 ? EditorHost.ActualWidth - 24 : 260));
        var nextPicts = new List<RtfPict.Pict>();
        var nextViews = new List<Image>();
        foreach (var image in images)
        {
            int known = pictViews.IndexOf(image);
            RtfPict.Pict? pict = known >= 0 && known < originalPicts.Count ? originalPicts[known] : null;
            var source = image.Source as BitmapSource ?? RenderImage(image);
            bool tiny = source != null && source.PixelWidth <= 1 && source.PixelHeight <= 1;
            if (pict != null && (RtfPict.IsRaster(pict.Group) || tiny || source == null))
            {
                nextPicts.Add(pict);
                nextViews.Add(image);
                continue;
            }
            var built = source == null ? null : NoteImages.FromBitmap(source, fit);
            if (built == null)
            {
                if (pict != null) { nextPicts.Add(pict); nextViews.Add(image); }
                else AppEnv.Log($"image png: could not encode a screen image for {Meta.Id}");
                continue;
            }
            AppEnv.Log(pict == null ? $"image png: stored an untracked screen image for {Meta.Id}" : $"image png: converted a non-raster pict for {Meta.Id}");
            var shown = NoteImages.Display(built.Bytes, built.PxW, built.PxH, built.GoalW, built.GoalH, Win32.SystemScale, Meta.Zoom);
            if (shown != null)
            {
                image.Source = shown;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
                if (double.IsNaN(image.Width) || image.Width < 1) image.Width = built.GoalW / 15.0;
                if (double.IsNaN(image.Height) || image.Height < 1) image.Height = built.GoalH / 15.0;
            }
            nextPicts.Add(new RtfPict.Pict { Group = built.Group, GoalW = built.GoalW, GoalH = built.GoalH, SourceW = built.PxW, SourceH = built.PxH });
            nextViews.Add(image);
        }
        if (nextViews.Count != images.Count)
        {
            // Keep groups for pictures still on screen. A deleted picture must not stay in the count.
            var live = new HashSet<Image>(images);
            for (int i = pictViews.Count - 1; i >= 0; i--)
            {
                if (live.Contains(pictViews[i])) continue;
                pictViews.RemoveAt(i);
                if (i < originalPicts.Count) originalPicts.RemoveAt(i);
            }
            while (originalPicts.Count > pictViews.Count) originalPicts.RemoveAt(originalPicts.Count - 1);
            return;
        }
        originalPicts.Clear();
        originalPicts.AddRange(nextPicts);
        pictViews.Clear();
        pictViews.AddRange(nextViews);
    }
    static BitmapSource? RenderImage(Image image)
    {
        double w = image.ActualWidth > 1 ? image.ActualWidth : image.Width;
        double h = image.ActualHeight > 1 ? image.ActualHeight : image.Height;
        if (w < 1 || h < 1 || double.IsNaN(w) || double.IsNaN(h)) return null;
        int pw = Math.Max(1, (int)Math.Ceiling(w)), ph = Math.Max(1, (int)Math.Ceiling(h));
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(image);
        rtb.Freeze();
        return rtb;
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
    /// <summary>An on-screen picture with no saved group, the shape of a GIF or metafile WPF will not write back.</summary>
    public void InsertLooseImageForTest()
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(RtfPict.TinyPngBytes);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        var image = new Image { Source = bmp, Width = 16, Height = 16, Stretch = Stretch.Fill };
        var para = Document.Blocks.OfType<Paragraph>().FirstOrDefault();
        if (para == null) { para = new Paragraph(); Document.Blocks.Add(para); }
        new InlineUIContainer(image, para.ContentEnd);
        TextDirty = true;
    }
    public bool TextInheritsInk(string sample)
    {
        var run = EnumRuns(Document).FirstOrDefault(r => (r.Text ?? "").Contains(sample));
        if (run == null) return false;
        if (run.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue) return false;
        var ink = NoteColors.Ink(Meta.Color);
        return (run.Foreground as SolidColorBrush)?.Color == ink;
    }
    public bool TextKeepsColor(string sample, byte r, byte g, byte b)
    {
        var run = EnumRuns(Document).FirstOrDefault(x => (x.Text ?? "").Contains(sample));
        var color = (run?.Foreground as SolidColorBrush)?.Color;
        return color.HasValue && color.Value.R == r && color.Value.G == g && color.Value.B == b;
    }
    /// <summary>Pictures stay on screen, but the next flush writes an RTF with the picts removed. That is a real loss the guard must refuse.</summary>
    public bool DropPictsForTest { get; set; }
    public void ForcePictMismatchForTest()
    {
        if (originalPicts.Count == 0 && DocImageCount() == 0) return;
        var para = Document.Blocks.OfType<Paragraph>().FirstOrDefault();
        if (para == null) { para = new Paragraph(); Document.Blocks.Add(para); }
        para.Inlines.Add(new Run("떨어짐"));
        DropPictsForTest = true;
        TextDirty = true;
    }
    /// <summary>Removes pictures from the document the way a backspace does, and leaves the tracked groups for the next save to drop.</summary>
    public void RemoveImagesFromDocumentForTest()
    {
        loading = true;
        try
        {
            foreach (var image in CollectImages(Document).ToList())
            {
                if (image.Parent is InlineUIContainer box && box.Parent is Paragraph para)
                    para.Inlines.Remove(box);
            }
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
    bool suppressTitle;
    void ApplyTitle()
    {
        suppressTitle = true;
        TitleBox.Text = Meta.Title ?? "";
        TitleHint.Visibility = TitleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        suppressTitle = false;
        SizeTitle();
    }
    void OnTitleEdited()
    {
        TitleHint.Visibility = TitleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (suppressTitle) return;
        string next = (TitleBox.Text ?? "").Replace("\r", "").Replace("\n", " ");
        if (next.Length > 80) next = next[..80];
        if (next == Meta.Title) return;
        Meta.Title = next;
        MarkMeta();
    }
    public void SetTitle(string title)
    {
        Meta.Title = title ?? "";
        ApplyTitle();
        metaDirty = true;
        FlushMeta();
    }

    void ApplyColor()
    {
        Background = MakeBrush();
        ApplyInk();
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
    public void ApplyFontSize() { loading = true; try { Document.FontSize = PointsToDip(ConfigStore.Notes.FontSize); } finally { loading = false; } ApplyInk(); }
    /// <summary>Enter in the title. The caret goes to the first character of the body.</summary>
    public bool MoveToBody()
    {
        if (AppEnv.ReadOnly) return false;
        var ed = EnsureEditor();
        ed.CaretPosition = ed.Document.ContentStart;
        ed.Focus();
        return true;
    }
    /// <summary>Puts every run back on <see cref="ConfigStore.Notes.FontSize"/>.</summary>
    public void ResetFontSize()
    {
        if (AppEnv.ReadOnly) return;
        double dip = PointsToDip(ConfigStore.Notes.FontSize);
        var ed = Editor;
        ed.Document.FontSize = dip;
        new TextRange(ed.Document.ContentStart, ed.Document.ContentEnd).ApplyPropertyValue(TextElement.FontSizeProperty, dip);
        ed.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, dip);
        TextDirty = true;
        textTimer.Stop();
        textTimer.Start();
    }

    bool blockLock;
    EnterPlan? pendingEnter;
    Popup? slashPop;
    int slashIndex;
    string slashQuery = "";
    List<NoteCommand> slashHits = new();
    Paragraph? slashPara;
    public bool SlashOpen { get; private set; }

    public string Feed(string text)
    {
        var ed = EnsureEditor();
        blockLock = true;
        try
        {
            ed.Document.Blocks.Clear();
            var para = new Paragraph(new Run(text ?? ""));
            ed.Document.Blocks.Add(para);
            ed.CaretPosition = para.ContentEnd;
        }
        finally { blockLock = false; }
        SyncBlock(ed.CaretPosition?.Paragraph);
        return BlockNow();
    }

    public string BlockNow()
    {
        var para = FirstParagraph();
        return para == null ? "" : Describe(para);
    }

    public bool CommitSlash()
    {
        if (!SlashOpen || slashHits.Count == 0 || AppEnv.ReadOnly) return false;
        var command = slashHits[Math.Clamp(slashIndex, 0, slashHits.Count - 1)];
        var para = slashPara ?? editor?.CaretPosition?.Paragraph;
        CloseSlash();
        if (para == null) return false;
        blockLock = true;
        try
        {
            string seed = command.Kind switch
            {
                NoteBlockKind.Todo => "☐ ",
                NoteBlockKind.Quote => "│ ",
                NoteBlockKind.Divider => "———",
                _ => ""
            };
            SetParagraphText(para, seed);
            Stamp(para, command.Kind, exit: false);
        }
        finally { blockLock = false; }
        TextDirty = true;
        return true;
    }

    /// <summary>Empty when the title and the tools both sit in the caption and inside <paramref name="width"/> physical pixels.</summary>
    public string CaptionFitReport(double width)
    {
        int metaW = Meta.W;
        double saved = Width;
        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
        if (scale < 0.5 || double.IsNaN(scale)) scale = 1;
        Width = width / scale;
        UpdateLayout();
        FitCaption();
        UpdateLayout();
        double titleW = TitleBox.ActualWidth;
        double barRight = Bar.TranslatePoint(new Point(Math.Max(0, Bar.ActualWidth), 0), this).X;
        double titleRight = TitleBox.TranslatePoint(new Point(Math.Max(0, TitleBox.ActualWidth), 0), this).X;
        double barLeft = Bar.TranslatePoint(new Point(0, 0), this).X;
        double titleTop = TitleBox.TranslatePoint(new Point(0, 0), this).Y;
        double titleBottom = TitleBox.TranslatePoint(new Point(0, TitleBox.ActualHeight), this).Y;
        double barTop = Bar.TranslatePoint(new Point(0, 0), this).Y;
        double barBottom = Bar.TranslatePoint(new Point(0, Bar.ActualHeight), this).Y;
        bool close = CloseBtn.Visibility == Visibility.Visible && CloseBtn.ActualWidth >= 16;
        bool room = titleW >= 32;
        bool inside = barRight <= ActualWidth + 1.5 && titleRight <= ActualWidth + 1.5;
        bool beside = titleRight <= barLeft + 2;
        bool band = titleTop >= barTop - 10 && titleBottom <= barBottom + 10;
        Width = saved;
        Meta.W = metaW;
        UpdateLayout();
        if (room && inside && beside && close && band) return "";
        return "w=" + ActualWidth.ToString("0") + " title=" + titleW.ToString("0") + " barR=" + barRight.ToString("0") + " titleR=" + titleRight.ToString("0") + " barL=" + barLeft.ToString("0");
    }

    /// <summary><c>title</c> when the point is on the title text, <c>drag</c> on the empty caption, <c>tools</c> on the buttons.</summary>
    public string CaptionHit(Point windowPoint)
    {
        if (TitleBox != null && BoundsOf(TitleBox).Contains(windowPoint)) return "title";
        if (Bar != null && BoundsOf(Bar).Contains(windowPoint)) return "tools";
        if (Caption != null && BoundsOf(Caption).Contains(windowPoint)) return "drag";
        return "outside";
    }

    /// <summary>True only when a real hit-test lands on the title box, not the empty caption.</summary>
    public bool TitleEditsAt(Point windowPoint)
    {
        bool titleHit = false;
        VisualTreeHelper.HitTest(this,
            potential => potential is UIElement element && !element.IsHitTestVisible
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            result =>
            {
                var hit = result.VisualHit as DependencyObject;
                while (hit != null)
                {
                    if (hit == TitleBox) { titleHit = true; return HitTestResultBehavior.Stop; }
                    hit = VisualTreeHelper.GetParent(hit) as DependencyObject;
                }
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(windowPoint));
        return titleHit;
    }

    /// <summary>A click on the empty caption drags. A double-click there does not focus the title.</summary>
    public string CaptionPress(Point windowPoint, int clicks)
    {
        string zone = CaptionHit(windowPoint);
        if (zone != "drag") return zone == "title" ? "edit" : zone;
        if (clicks > 1) return "drag";
        if (!AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest && Mouse.LeftButton == MouseButtonState.Pressed)
            try { DragMove(); } catch { }
        return "drag";
    }

    public double MeasureTitle(string? text)
    {
        string shown = string.IsNullOrEmpty(text) ? (TitleHint?.Text ?? "제목") : text!;
        double dpi = 1;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { dpi = 1; }
        if (dpi < 0.5 || double.IsNaN(dpi)) dpi = 1;
        double size = TitleBox.FontSize > 0 ? TitleBox.FontSize : 16;
        var formatted = new FormattedText(shown, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TitleBox.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, Brushes.Black, dpi);
        return Math.Ceiling(formatted.Width) + 8;
    }

    Rect BoundsOf(FrameworkElement el)
    {
        if (el == null || el.ActualWidth < 1 || el.ActualHeight < 1) return Rect.Empty;
        var origin = el.TranslatePoint(new Point(0, 0), this);
        return new Rect(origin, new Size(el.ActualWidth, el.ActualHeight));
    }

    void SizeTitle()
    {
        if (TitleBox == null || TitleSlot == null) return;
        string text = TitleBox.Text ?? "";
        bool empty = text.Length == 0;
        double textW = MeasureTitle(empty ? "" : text);
        double avail = TitleSlot.ActualWidth;
        if (double.IsNaN(avail) || avail < 8) avail = textW;
        double width = Math.Min(textW, Math.Max(8, avail));
        if (width < 28) width = Math.Min(28, avail);
        TitleBox.Width = width;
        TitleBox.MaxWidth = avail;
        TitleHint.Width = width;
        TitleHint.MaxWidth = avail;
        bool clip = !empty && textW > avail + 1;
        bool editing = TitleBox.IsKeyboardFocusWithin;
        TitleClip.Text = text;
        TitleClip.Width = width;
        TitleClip.MaxWidth = avail;
        TitleClip.FontSize = TitleBox.FontSize;
        TitleClip.FontFamily = TitleBox.FontFamily;
        // The textbox text is transparent while clipped so the ellipsis can replace it. Keep the
        // visible ink separate: a later sizing pass must not copy that transparent brush here.
        TitleClip.Foreground = titleInkBrush;
        TitleClip.Visibility = clip && !editing ? Visibility.Visible : Visibility.Collapsed;
        TitleBox.Foreground = clip && !editing ? Brushes.Transparent : titleInkBrush;
        TitleBox.Opacity = 1; // keep the text box hit-testable beneath the non-hit-testable ellipsis
    }

    void FitCaption()
    {
        var tools = new[] { NewBtn, FontBtn, ColorBtn, ListBtn, PinBtn, CloseBtn };
        foreach (var button in tools)
        {
            button.Visibility = Visibility.Visible;
            button.Width = 30;
            button.Height = 30;
        }
        MoreBtn.Visibility = Visibility.Collapsed;
        MoreBtn.Width = 30;
        MoreBtn.Height = 30;
        if (FormatTools != null) FormatTools.MaxWidth = Math.Max(80, ActualWidth - 20);
        double inner = Math.Max(0, ActualWidth - 20);
        if (inner < 1) { SizeTitle(); return; }
        const double titleMin = 36;
        double Used()
        {
            double w = 0;
            foreach (var button in tools)
                if (button.Visibility == Visibility.Visible) w += button.Width;
            if (MoreBtn.Visibility == Visibility.Visible) w += MoreBtn.Width;
            return w;
        }
        if (Used() + titleMin <= inner) { SizeTitle(); return; }
        foreach (var button in tools) { button.Width = 22; button.Height = 22; }
        MoreBtn.Width = 22;
        MoreBtn.Height = 22;
        if (Used() + titleMin <= inner) { SizeTitle(); return; }
        foreach (var button in new[] { NewBtn, FontBtn, ColorBtn, ListBtn, PinBtn })
        {
            button.Visibility = Visibility.Collapsed;
            MoreBtn.Visibility = Visibility.Visible;
            if (Used() + titleMin <= inner) { SizeTitle(); return; }
        }
        SizeTitle();
    }

    void ShowHiddenTools()
    {
        var menu = new ContextMenu { PlacementTarget = MoreBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(Button button, string label, Action act)
        {
            if (button.Visibility == Visibility.Visible) return;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        Add(NewBtn, "새 메모", () => NoteManager.NewNote());
        Add(FontBtn, "서식", ToggleFormatBar);
        Add(ColorBtn, "색", ShowPalette);
        Add(ListBtn, "메모 목록", ShowList);
        Add(PinBtn, "항상 위", () => SetTopMost(!Meta.TopMost));
        if (menu.Items.Count == 0) return;
        menu.IsOpen = true;
    }

    void SyncBlock(Paragraph? para)
    {
        if (para == null || blockLock || loading || AppEnv.ReadOnly) return;
        if (ParagraphHasImage(para)) { CloseSlash(); return; }
        string text = ParagraphText(para);
        if (NoteBlocks.TryShortcut(text, out var shortcut))
        {
            blockLock = true;
            try
            {
                string body = shortcut.Kind switch
                {
                    NoteBlockKind.Todo => "☐ " + shortcut.Rest,
                    NoteBlockKind.Quote => "│ " + shortcut.Rest,
                    NoteBlockKind.Divider => "———",
                    _ => shortcut.Rest
                };
                SetParagraphText(para, body);
                Stamp(para, shortcut.Kind, exit: false);
            }
            finally { blockLock = false; }
            CloseSlash();
            return;
        }
        if (NoteBlocks.TryInline(text, out var inline))
        {
            blockLock = true;
            try { PaintInline(para, text, inline); }
            finally { blockLock = false; }
        }
        if (NoteBlocks.TrySlash(text, out string query)) OpenSlash(para, query);
        else CloseSlash();
    }

    void Stamp(Paragraph para, NoteBlockKind kind, bool exit)
    {
        if (exit) kind = NoteBlockKind.Text;
        var ui = (FontFamily)FindResource("UiFont");
        var live = para;
        if (kind is NoteBlockKind.Text or NoteBlockKind.H1 or NoteBlockKind.H2 or NoteBlockKind.H3 or NoteBlockKind.Todo or NoteBlockKind.Quote or NoteBlockKind.Divider or NoteBlockKind.Code)
            live = LeaveList(para);
        switch (kind)
        {
            case NoteBlockKind.H1: StyleHeading(live, 24, ui); break;
            case NoteBlockKind.H2: StyleHeading(live, 20, ui); break;
            case NoteBlockKind.H3: StyleHeading(live, 16, ui); break;
            case NoteBlockKind.Bullet: live = EnsureList(live, numbered: false); break;
            case NoteBlockKind.Number: live = EnsureList(live, numbered: true); break;
            case NoteBlockKind.Code:
                live.ClearValue(TextElement.FontSizeProperty);
                live.ClearValue(TextElement.FontWeightProperty);
                live.FontFamily = new FontFamily("Consolas");
                break;
            default:
                live.ClearValue(TextElement.FontSizeProperty);
                live.ClearValue(TextElement.FontWeightProperty);
                live.FontFamily = ui;
                if (exit) SetParagraphText(live, "");
                break;
        }
        if (editor != null) editor.CaretPosition = live.ContentEnd;
    }

    static void StyleHeading(Paragraph para, double pt, FontFamily family)
    {
        para.FontFamily = family;
        para.FontWeight = FontWeights.Bold;
        para.FontSize = PointsToDip(pt);
    }

    Paragraph LeaveList(Paragraph para)
    {
        if (para.Parent is not ListItem item || item.Parent is not List list) return para;
        var fresh = new Paragraph();
        while (para.Inlines.FirstInline != null) fresh.Inlines.Add(para.Inlines.FirstInline);
        CopyBlockLook(para, fresh);
        if (list.Parent is not FlowDocument doc) return fresh;
        list.ListItems.Remove(item);
        if (list.ListItems.Count == 0)
        {
            doc.Blocks.InsertAfter(list, fresh);
            doc.Blocks.Remove(list);
        }
        else doc.Blocks.InsertAfter(list, fresh);
        return fresh;
    }

    Paragraph EnsureList(Paragraph para, bool numbered)
    {
        if (para.Parent is ListItem item && item.Parent is List existing)
        {
            existing.MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
            return para;
        }
        if (para.Parent is not FlowDocument doc) return para;
        var list = new List { MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0), Padding = new Thickness(12, 0, 0, 0) };
        var fresh = new Paragraph();
        while (para.Inlines.FirstInline != null) fresh.Inlines.Add(para.Inlines.FirstInline);
        list.ListItems.Add(new ListItem(fresh));
        doc.Blocks.InsertAfter(para, list);
        doc.Blocks.Remove(para);
        return fresh;
    }

    static void CopyBlockLook(Paragraph from, Paragraph to)
    {
        if (from.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue) to.FontSize = from.FontSize;
        if (from.ReadLocalValue(TextElement.FontWeightProperty) != DependencyProperty.UnsetValue) to.FontWeight = from.FontWeight;
        if (from.ReadLocalValue(TextElement.FontFamilyProperty) != DependencyProperty.UnsetValue) to.FontFamily = from.FontFamily;
    }

    void SetParagraphText(Paragraph para, string text)
    {
        para.Inlines.Clear();
        para.Inlines.Add(new Run(text ?? ""));
    }

    void PaintInline(Paragraph para, string text, NoteInline inline)
    {
        string before = text[..inline.Start];
        string after = text[(inline.Start + inline.Length)..];
        para.Inlines.Clear();
        if (before.Length > 0) para.Inlines.Add(new Run(before));
        var run = new Run(inline.Text);
        switch (inline.Style)
        {
            case "bold": run.FontWeight = FontWeights.Bold; break;
            case "italic": run.FontStyle = FontStyles.Italic; break;
            case "strike": run.TextDecorations = TextDecorations.Strikethrough; break;
            case "code": run.FontFamily = new FontFamily("Consolas"); break;
        }
        para.Inlines.Add(run);
        if (after.Length > 0) para.Inlines.Add(new Run(after));
        if (editor != null) editor.CaretPosition = run.ContentEnd;
    }

    void OpenSlash(Paragraph para, string query)
    {
        var hits = NoteBlocks.Match(query);
        if (hits.Count == 0) { CloseSlash(); return; }
        if (!string.Equals(query, slashQuery, StringComparison.Ordinal)) slashIndex = 0;
        slashQuery = query;
        slashHits = hits;
        slashPara = para;
        SlashOpen = true;
        slashPop ??= new Popup
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            PlacementTarget = EditorHost,
            StaysOpen = true,
            AllowsTransparency = true,
            IsHitTestVisible = false
        };
        PaintSlash();
        slashPop.IsOpen = true;
    }

    void PaintSlash()
    {
        if (slashPop == null) return;
        var stack = new StackPanel();
        for (int i = 0; i < slashHits.Count; i++)
        {
            var command = slashHits[i];
            bool on = i == slashIndex;
            stack.Children.Add(new Border
            {
                Background = on ? new SolidColorBrush(Color.FromArgb(0x33, 0x8F, 0xB4, 0xFF)) : Brushes.Transparent,
                Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock { Text = command.Title + "   " + command.Hint, Foreground = Brushes.White, FontSize = 13 }
            });
        }
        slashPop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Child = stack
        };
    }

    void CloseSlash()
    {
        SlashOpen = false;
        slashHits.Clear();
        slashPara = null;
        slashQuery = "";
        if (slashPop != null) slashPop.IsOpen = false;
    }

    Paragraph? FirstParagraph()
    {
        if (editor == null) return null;
        foreach (var block in editor.Document.Blocks)
        {
            if (block is Paragraph para) return para;
            if (block is List list)
                foreach (var item in list.ListItems)
                    foreach (var inner in item.Blocks)
                        if (inner is Paragraph ip) return ip;
        }
        return null;
    }

    static string ParagraphText(Paragraph para)
        => new TextRange(para.ContentStart, para.ContentEnd).Text.Replace("\r", "").Replace("\n", "");

    static bool ParagraphHasImage(Paragraph para)
        => para.Inlines.Any(inline => inline is InlineUIContainer { Child: Image });

    NoteBlockKind KindOf(Paragraph para)
    {
        string text = ParagraphText(para);
        bool list = para.Parent is ListItem;
        bool numbered = list && para.Parent is ListItem item && item.Parent is List host && host.MarkerStyle == TextMarkerStyle.Decimal;
        double font = para.FontSize;
        double pt = double.IsNaN(font) ? ConfigStore.Notes.FontSize : font * 72.0 / 96.0;
        string family = para.FontFamily?.Source ?? "";
        return NoteBlocks.Classify(text, list, numbered, pt, ConfigStore.Notes.FontSize, family.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
    }

    string Describe(Paragraph para)
    {
        string style = para.Inlines.FirstInline is Run run && run.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight weight && weight == FontWeights.Bold ? "|bold" : "";
        string menu = SlashOpen ? "|" + string.Join(",", slashHits.Select(hit => hit.Title)) : "";
        return KindOf(para) + "|" + ParagraphText(para) + style + menu;
    }

    public void ShowPalette() { BuildSwatches(); CustomColor.Load(Meta.Color); Palette.IsOpen = true; }
    public void ClosePalette() { Palette.IsOpen = false; CustomColor.ClosePicker(); }
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
            case Key.V: PasteClipboard(); return true;
            case Key.C: CopySelection(false); return true;
            case Key.X: CopySelection(true); return true;
            case Key.Insert: CopySelection(false); return true;
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
    void UpdatePin() { PinBtn.Content = Meta.TopMost ? "\uE840" : "\uE718"; PinBtn.Foreground = Meta.TopMost ? (Brush)FindResource("Accent") : InkBrush(NoteColors.Ink(Meta.Color)); }
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
        var all = new MenuItem { Header = "모든 메모 보기" }; all.Click += (_, _) => App.Instance.ShowSettings("notes", user: true); m.Items.Add(all);
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
        NoteClipSession.Publish(NoteClip.Pack(new[] { new NoteClipPiece { Png = bytes } }), "", "", bytes, null, null);
        return bytes;
    }

    public bool CopyMixed() => CopySelection(false);

    /// <summary>Ctrl+C, Ctrl+Insert, Ctrl+X, and the edit menu. Always leaves plain text when the selection has any.</summary>
    public bool CopySelection(bool cut)
    {
        try
        {
            if (TitleBox.IsKeyboardFocusWithin) return CopyTitleText(cut);
            return CopyEditor(cut);
        }
        catch (Exception ex)
        {
            AppEnv.Log("copy: " + ex);
            try
            {
                string plain = editor != null && !editor.Selection.IsEmpty
                    ? new TextRange(editor.Selection.Start, editor.Selection.End).Text
                    : TitleBox.SelectedText ?? "";
                if (plain.Length == 0) return false;
                return NoteClipSession.Publish(NoteClip.Pack(new[] { new NoteClipPiece { Text = plain } }), plain, plain, null, null, null);
            }
            catch (Exception ex2)
            {
                AppEnv.Log("copy fallback: " + ex2);
                return false;
            }
        }
    }

    public bool CopyTitleText(bool cut)
    {
        string all = TitleBox.Text ?? "";
        int start = Math.Clamp(TitleBox.SelectionStart, 0, all.Length);
        int len = Math.Clamp(TitleBox.SelectionLength, 0, all.Length - start);
        string plain = len > 0 ? all.Substring(start, len) : all;
        if (plain.Length == 0) return false;
        bool ok = NoteClipSession.Publish(NoteClip.Pack(new[] { new NoteClipPiece { Text = plain } }), plain, plain, null, null, null);
        if (ok && cut && !AppEnv.ReadOnly)
        {
            if (len > 0) TitleBox.Text = all.Remove(start, len);
            else TitleBox.Text = "";
            OnTitleEdited();
        }
        return ok;
    }

    bool CopyEditor(bool cut)
    {
        if (editor == null) return CopySelectedImage();
        if (editor.Selection.IsEmpty)
        {
            if (CopySelectedImage()) return true;
            string all = PlainText();
            if (all.Length == 0) return false;
            return NoteClipSession.Publish(NoteClip.Pack(new[] { new NoteClipPiece { Text = all } }), all, all, null, null, null);
        }
        var start = editor.Selection.Start;
        var end = editor.Selection.End;
        string plain = new TextRange(start, end).Text ?? "";
        List<NoteClipPiece> pieces;
        try { pieces = ExportPieces(start, end); }
        catch (Exception ex)
        {
            AppEnv.Log("copy pieces: " + ex);
            pieces = plain.Length > 0 ? new List<NoteClipPiece> { new() { Text = plain } } : new List<NoteClipPiece>();
        }
        if (pieces.Count == 0 && plain.Length == 0) return CopySelectedImage();
        if (plain.Length == 0) plain = NoteClip.Plain(pieces);
        string? xaml = null;
        try
        {
            using var ms = new MemoryStream();
            new TextRange(start, end).Save(ms, DataFormats.Xaml);
            xaml = Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex) { AppEnv.Log("copy xaml: " + ex); }
        var pngs = pieces.Where(p => p.Png is { Length: > 0 }).ToList();
        byte[]? png = pngs.Count == 1 ? pngs[0].Png : null;
        string rtf = "";
        string html = "";
        try { rtf = NoteClip.ToRtf(pieces); } catch (Exception ex) { AppEnv.Log("copy rtf: " + ex); }
        try { html = NoteClip.ToHtml(pieces); } catch (Exception ex) { AppEnv.Log("copy html: " + ex); }
        bool ok = NoteClipSession.Publish(NoteClip.Pack(pieces), plain, rtf, png, xaml, html);
        if (ok && cut && !AppEnv.ReadOnly)
        {
            editor.Selection.Text = "";
            TextDirty = true;
            textTimer.Stop();
            textTimer.Start();
        }
        return ok;
    }

    ContextMenu EditMenu(Control target)
    {
        var menu = new ContextMenu();
        if (Application.Current?.TryFindResource(typeof(ContextMenu)) is Style style) menu.Style = style;
        void Add(string label, string icon, string gesture, ICommand command)
        {
            var item = new MenuItem { Header = label, InputGestureText = gesture, Command = command, CommandTarget = target };
            if (Application.Current?.TryFindResource(typeof(MenuItem)) is Style itemStyle) item.Style = itemStyle;
            item.Icon = new TextBlock
            {
                Text = icon,
                FontFamily = (FontFamily)FindResource("Icons"),
                FontSize = 14,
                Width = 18,
                TextAlignment = TextAlignment.Center
            };
            menu.Items.Add(item);
        }
        Add("잘라내기", "\uE8C6", "Ctrl+X", ApplicationCommands.Cut);
        Add("복사", "\uE8C8", "Ctrl+C", ApplicationCommands.Copy);
        Add("붙여넣기", "\uE77F", "Ctrl+V", ApplicationCommands.Paste);
        menu.Items.Add(new Separator());
        Add("모두 선택", "\uE8B3", "Ctrl+A", ApplicationCommands.SelectAll);
        return menu;
    }

    void OnTitlePasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        string plain = TitlePlain(e.DataObject);
        if (plain.Length == 0) return;
        int start = TitleBox.SelectionStart;
        int len = TitleBox.SelectionLength;
        string all = TitleBox.Text ?? "";
        if (start < 0 || start > all.Length) start = all.Length;
        if (len < 0 || start + len > all.Length) len = 0;
        string next = (all.Remove(start, len).Insert(start, plain));
        next = next.Replace("\r", "").Replace("\n", " ");
        if (next.Length > 80) next = next[..80];
        TitleBox.Text = next;
        TitleBox.SelectionStart = Math.Min(start + plain.Length, TitleBox.Text.Length);
        OnTitleEdited();
    }

    public bool PasteTitleClipboard()
    {
        var data = NoteClipSession.TakeClipboard();
        string plain = data == null ? "" : TitlePlain(data);
        if (plain.Length == 0) return false;
        int start = Math.Clamp(TitleBox.SelectionStart, 0, (TitleBox.Text ?? "").Length);
        int len = Math.Clamp(TitleBox.SelectionLength, 0, (TitleBox.Text ?? "").Length - start);
        string all = TitleBox.Text ?? "";
        string next = all.Remove(start, len).Insert(start, plain);
        if (next.Length > 80) next = next[..80];
        TitleBox.Text = next;
        TitleBox.SelectionStart = Math.Min(start + plain.Length, TitleBox.Text.Length);
        OnTitleEdited();
        return true;
    }

    static string TitlePlain(IDataObject data)
    {
        string text = "";
        try
        {
            if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string ?? "";
            else if (data.GetDataPresent(DataFormats.Text)) text = data.GetData(DataFormats.Text) as string ?? "";
        }
        catch (Exception ex) { AppEnv.Log("title paste: " + ex.Message); }
        text = text.Replace("\r", "").Replace("\n", " ").Trim();
        return text.Length > 80 ? text[..80] : text;
    }

    public bool PasteClipboard()
    {
        if (AppEnv.ReadOnly) return false;
        if (TitleBox.IsKeyboardFocusWithin) return PasteTitleClipboard();
        var data = NoteClipSession.TakeClipboard();
        if (data != null && PasteObject(data)) return true;
        return PasteMixed() || PasteImage();
    }

    public bool PasteObject(IDataObject data)
    {
        if (AppEnv.ReadOnly || data == null) return false;
        var pieces = NotePaste.Read(data);
        if (pieces.Count == 0) return false;
        bool any = false;
        foreach (var piece in pieces)
        {
            if (piece.Text.Length > 0)
            {
                Editor.CaretPosition.InsertTextInRun(piece.Text);
                TextDirty = true;
                any = true;
            }
            if (piece.Png is { Length: > 0 } png && png.Length <= NotePaste.MaxBytes && InsertPng(png))
                any = true;
        }
        if (any) { textTimer.Stop(); textTimer.Start(); }
        return any;
    }

    public bool PasteMixed()
    {
        if (AppEnv.ReadOnly) return false;
        var data = NoteClipSession.CurrentClipboard();
        if (data == null) return false;
        try
        {
            uint now = NoteClipSession.ReadSequence();
            bool own = NoteClipSession.LastSequence != 0 && now == NoteClipSession.LastSequence && data.GetDataPresent(NoteClip.Format);
            if (own && data.GetData(NoteClip.Format) is string pack)
                return InsertPieces(NoteClip.Unpack(pack));
        }
        catch (Exception e) { AppEnv.Log("paste note: " + e.Message); }
        return PasteObject(data);
    }

    bool InsertPieces(List<NoteClipPiece> pieces)
    {
        if (pieces.Count == 0 || !pieces.Any(p => p.Png is { Length: > 0 } || p.Text.Length > 0)) return false;
        bool any = false;
        foreach (var p in pieces)
        {
            if (p.Text.Length > 0)
            {
                Editor.CaretPosition.InsertTextInRun(p.Text);
                TextDirty = true;
                any = true;
            }
            if (p.Png is { Length: > 0 } && InsertPng(p.Png)) any = true;
        }
        if (!any) return false;
        textTimer.Stop(); textTimer.Start();
        return true;
    }

    bool InsertPng(byte[] png)
    {
        try
        {
            int fit = Math.Max(80, (int)(EditorHost.ActualWidth > 1 ? EditorHost.ActualWidth - 24 : 260));
            var built = NoteImages.FromEncoded(png, fit);
            if (built != null && AddPicture(built)) return true;
        }
        catch (Exception e) { AppEnv.Log("paste png: " + e.Message); }
        return false;
    }

    List<NoteClipPiece> ExportPieces(TextPointer selStart, TextPointer selEnd)
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
        bool Overlaps(TextPointer a, TextPointer b) => a.CompareTo(selEnd) < 0 && b.CompareTo(selStart) > 0;
        string RunText(Run run)
        {
            var s = run.ContentStart.CompareTo(selStart) < 0 ? selStart : run.ContentStart;
            var e = run.ContentEnd.CompareTo(selEnd) > 0 ? selEnd : run.ContentEnd;
            if (s.CompareTo(e) >= 0) return "";
            return new TextRange(s, e).Text;
        }
        void Walk(IEnumerable<Inline> inlines, bool take)
        {
            foreach (var i in inlines)
            {
                if (i is Run r)
                {
                    if (take) sb.Append(RunText(r));
                }
                else if (i is LineBreak lb)
                {
                    if (take && Overlaps(lb.ElementStart, lb.ElementEnd)) sb.Append('\n');
                }
                else if (i is Span s) Walk(s.Inlines, take && Overlaps(s.ContentStart, s.ContentEnd));
                else if (i is InlineUIContainer { Child: Image } box)
                {
                    int idx = image++;
                    if (take && Overlaps(box.ElementStart, box.ElementEnd))
                    {
                        Flush();
                        var png = PngAt(idx);
                        if (png != null) pieces.Add(new NoteClipPiece { Png = png });
                    }
                }
            }
        }
        void WalkBlocks(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is Paragraph p)
                {
                    bool take = Overlaps(p.ContentStart, p.ContentEnd);
                    Walk(p.Inlines, take);
                    if (take) sb.Append('\n');
                }
                else if (b is List list)
                    foreach (var item in list.ListItems) WalkBlocks(item.Blocks);
                else if (b is Section s) WalkBlocks(s.Blocks);
                else if (b is BlockUIContainer { Child: Image } block)
                {
                    int idx = image++;
                    if (Overlaps(block.ElementStart, block.ElementEnd))
                    {
                        Flush();
                        var png = PngAt(idx);
                        if (png != null) pieces.Add(new NoteClipPiece { Png = png });
                    }
                }
            }
        }
        WalkBlocks(Document.Blocks);
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

    public void RefreshLinks()
    {
        if (stylingLinks) return;
        stylingLinks = true;
        try
        {
            if (linkBrush == null || linkBrush == Brushes.Transparent) linkBrush = InkBrush(NoteColors.LinkInk(Meta.Color));
            StyleBlocks(Document.Blocks);
        }
        finally { stylingLinks = false; }
    }

    public bool LinkInkMatchesNote()
    {
        var want = NoteColors.LinkInk(Meta.Color);
        bool any = false;
        foreach (var run in EnumRuns(Document))
        {
            if (!NoteLinks.GetDisplay(run)) continue;
            any = true;
            if ((run.Foreground as SolidColorBrush)?.Color != want) return false;
        }
        return any;
    }

    public IReadOnlyList<string> DisplayLinks()
    {
        var list = new List<string>();
        foreach (var run in EnumRuns(Document))
            if (NoteLinks.GetDisplay(run)) list.Add(run.Text);
        return list;
    }

    /// <summary>Ctrl opens the first displayed link. A plain click does not.</summary>
    public bool OpenDisplayedLink(bool ctrl)
    {
        var run = EnumRuns(Document).FirstOrDefault(NoteLinks.GetDisplay);
        if (run == null || !ctrl) return false;
        return TryOpenLink(run.Text, 0, launch: true);
    }

    public bool HoverFirstLink()
    {
        var run = EnumRuns(Document).FirstOrDefault(NoteLinks.GetDisplay);
        ShowLinkHover(run);
        return run != null && run.TextDecorations != null && run.TextDecorations.Count > 0 && Equals(editor?.ToolTip ?? Viewer.ToolTip, NoteLinks.Tip);
    }

    byte[] SaveRtfWithoutLinkChrome()
    {
        var saved = new List<(Run Run, object Foreground, object Decorations)>();
        bool prev = stylingLinks;
        stylingLinks = true;
        try
        {
            foreach (var run in EnumRuns(Document))
            {
                if (!NoteLinks.GetDisplay(run)) continue;
                saved.Add((run, run.ReadLocalValue(TextElement.ForegroundProperty), run.ReadLocalValue(Inline.TextDecorationsProperty)));
                run.ClearValue(TextElement.ForegroundProperty);
                run.ClearValue(Inline.TextDecorationsProperty);
            }
            ReleaseAutomaticInk(Document);
            using var ms = new MemoryStream();
            new TextRange(Document.ContentStart, Document.ContentEnd).Save(ms, DataFormats.Rtf);
            return ms.ToArray();
        }
        finally
        {
            foreach (var (run, foreground, decorations) in saved)
            {
                if (foreground != DependencyProperty.UnsetValue) run.SetValue(TextElement.ForegroundProperty, foreground);
                if (decorations != DependencyProperty.UnsetValue) run.SetValue(Inline.TextDecorationsProperty, decorations);
            }
            stylingLinks = prev;
        }
    }

    void StyleBlocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.ToList())
        {
            if (block is Paragraph paragraph) StyleInlines(paragraph.Inlines);
            else if (block is Section section) StyleBlocks(section.Blocks);
            else if (block is List list)
                foreach (var item in list.ListItems) StyleBlocks(item.Blocks);
        }
    }

    void StyleInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Span span) StyleInlines(span.Inlines);
            else if (inline is Run run) StyleRun(inlines, run);
        }
    }

    void StyleRun(InlineCollection inlines, Run run)
    {
        string text = run.Text ?? "";
        var hits = NoteLinks.Find(text);
        if (hits.Count == 0)
        {
            if (NoteLinks.GetDisplay(run)) ClearLink(run);
            return;
        }
        if (hits.Count == 1 && hits[0].Start == 0 && hits[0].Length == text.Length)
        {
            var have = (run.Foreground as SolidColorBrush)?.Color;
            var want = (linkBrush as SolidColorBrush)?.Color;
            if (!NoteLinks.GetDisplay(run) || have != want) PaintLink(run);
            return;
        }
        int? caretOff = null;
        if (editor != null && editor.CaretPosition.Parent == run)
            caretOff = new TextRange(run.ContentStart, editor.CaretPosition).Text.Length;
        var pieces = new List<Run>();
        int at = 0;
        foreach (var hit in hits)
        {
            if (hit.Start > at) pieces.Add(PlainPiece(run, text[at..hit.Start]));
            var link = PlainPiece(run, text.Substring(hit.Start, hit.Length));
            PaintLink(link);
            pieces.Add(link);
            at = hit.Start + hit.Length;
        }
        if (at < text.Length) pieces.Add(PlainPiece(run, text[at..]));
        foreach (var piece in pieces) inlines.InsertBefore(run, piece);
        inlines.Remove(run);
        if (hoveredLink == run) hoveredLink = pieces.FirstOrDefault(NoteLinks.GetDisplay);
        if (caretOff is int offset && editor != null) PlaceCaret(pieces, offset);
    }

    static Run PlainPiece(Run source, string text)
    {
        var piece = new Run(text);
        CopyLocal(source, piece, TextElement.FontWeightProperty);
        CopyLocal(source, piece, TextElement.FontStyleProperty);
        CopyLocal(source, piece, TextElement.FontSizeProperty);
        if (!NoteLinks.GetDisplay(source)) CopyLocal(source, piece, Inline.TextDecorationsProperty);
        return piece;
    }

    static void CopyLocal(Run source, Run target, DependencyProperty property)
    {
        var value = source.ReadLocalValue(property);
        if (value != DependencyProperty.UnsetValue) target.SetValue(property, value);
    }

    void PaintLink(Run run)
    {
        run.Foreground = linkBrush;
        NoteLinks.SetDisplay(run, true);
        if (!ReferenceEquals(run, hoveredLink)) run.TextDecorations = null;
    }

    static void ClearLink(Run run)
    {
        NoteLinks.SetDisplay(run, false);
        run.ClearValue(TextElement.ForegroundProperty);
        run.ClearValue(Inline.TextDecorationsProperty);
    }

    void PlaceCaret(IReadOnlyList<Run> pieces, int offset)
    {
        if (editor == null || pieces.Count == 0) return;
        int left = Math.Max(0, offset);
        foreach (var piece in pieces)
        {
            int n = piece.Text?.Length ?? 0;
            if (left < n)
            {
                var raw = piece.ContentStart.GetPositionAtOffset(left, LogicalDirection.Forward) ?? piece.ContentStart;
                var inserted = raw.GetInsertionPosition(LogicalDirection.Forward) ?? raw;
                if (inserted.CompareTo(piece.ContentStart) < 0 || inserted.CompareTo(piece.ContentEnd) > 0) inserted = raw;
                editor.CaretPosition = inserted;
                return;
            }
            if (left == n)
            {
                editor.CaretPosition = piece.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? piece.ContentEnd;
                return;
            }
            left -= n;
        }
        var last = pieces[^1];
        editor.CaretPosition = last.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? last.ContentEnd;
    }

    void ShowLinkHover(Run? run)
    {
        if (ReferenceEquals(hoveredLink, run)) return;
        if (hoveredLink != null && NoteLinks.GetDisplay(hoveredLink)) hoveredLink.TextDecorations = null;
        hoveredLink = run;
        object? tip = run == null ? null : NoteLinks.Tip;
        if (editor != null) editor.ToolTip = tip;
        Viewer.ToolTip = tip;
        if (run == null) return;
        var color = (linkBrush as SolidColorBrush)?.Color ?? NoteColors.LinkInk(Meta.Color);
        var line = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
        line.Freeze();
        var pen = new Pen(line, 1);
        pen.Freeze();
        var decoration = new TextDecoration { Location = TextDecorationLocation.Underline, Pen = pen, PenThicknessUnit = TextDecorationUnit.FontRecommended };
        var set = new TextDecorationCollection { decoration };
        run.TextDecorations = set;
    }

    Run? RunAt(Point pos)
    {
        if (editor == null) return null;
        var pointer = editor.GetPositionFromPoint(pos, true);
        return pointer?.Parent as Run;
    }

    static IEnumerable<Run> EnumRuns(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
            foreach (var run in EnumRuns(block))
                yield return run;
    }

    static IEnumerable<Run> EnumRuns(Block block)
    {
        if (block is Paragraph paragraph)
        {
            foreach (var inline in paragraph.Inlines)
                foreach (var run in EnumRuns(inline))
                    yield return run;
        }
        else if (block is Section section)
        {
            foreach (var child in section.Blocks)
                foreach (var run in EnumRuns(child))
                    yield return run;
        }
        else if (block is List list)
        {
            foreach (var item in list.ListItems)
                foreach (var child in item.Blocks)
                    foreach (var run in EnumRuns(child))
                        yield return run;
        }
    }

    static IEnumerable<Run> EnumRuns(Inline inline)
    {
        if (inline is Run run) yield return run;
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                foreach (var nested in EnumRuns(child))
                    yield return nested;
        }
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
        return PasteObject(data);
    }

    static bool CanDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text)
        || data.GetDataPresent(DataFormats.Html) || data.GetDataPresent(DataFormats.Rtf)
        || data.GetDataPresent("PNG") || data.GetDataPresent(NoteClip.Format);

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
        var link = RunAt(pos);
        bool onLink = link != null && NoteLinks.GetDisplay(link);
        ShowLinkHover(onLink ? link : null);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (over != null) Editor.Cursor = Cursors.SizeAll;
        else if (onLink && ctrl) Editor.Cursor = Cursors.Hand;
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
        Add("복사", () => { if (!CopyMixed()) CopyImage(index); });
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
        while (hit != null && hit is not Image) hit = NextParent(hit);
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
    public static string? LastRtf { get; private set; }
    public static string? LastXaml { get; private set; }
    public static string? LastHtml { get; private set; }
    public static string LastPlain { get; private set; } = "";
    public static bool LastHadBitmap { get; private set; }
    public static int SystemCalls { get; private set; }
    public static int SetAttempts { get; private set; }
    public static int RichThrows { get; set; }
    public static Exception? LastError { get; private set; }
    public static IDataObject? Fake { get; set; }
    public static DataObject? Memory { get; private set; }
    public static uint LastSequence { get; private set; }
    public static uint SequenceNow { get; set; }
    public static bool SequenceIsFake { get; set; }
    public static bool AllowsSystem => !AppEnv.Off && !AppEnv.Smoke && !AppEnv.NoteTest;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

    public static uint ReadSequence() => SequenceIsFake || !AllowsSystem ? SequenceNow : GetClipboardSequenceNumber();

    public static IDataObject? CurrentClipboard()
    {
        if (Fake != null) return Fake;
        if (!AllowsSystem) return null;
        for (int i = 0; i < 6; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (Exception ex)
            {
                LastError = ex;
                AppEnv.Log("clipboard read: " + ex.GetType().Name + " " + ex.Message);
                if (i + 1 < 6) Thread.Sleep(15 * (i + 1));
            }
        }
        return null;
    }

    public static bool Publish(string pack, string plain, string rtf, byte[]? png, string? xaml = null, string? html = null)
    {
        LastPack = pack;
        LastRtf = rtf;
        LastXaml = xaml;
        LastHtml = html;
        LastHadBitmap = png is { Length: > 0 };
        LastPlain = plain ?? "";
        if (Commit(Build(plain, rtf, html, xaml, pack, png, rich: true), rich: true)) return HasPayload(plain, png);
        AppEnv.Log("clipboard rich copy failed; writing plain text");
        return Commit(Build(plain, null, null, null, null, png, rich: false), rich: false) && HasPayload(plain, png);
    }

    static bool HasPayload(string? plain, byte[]? png) => !string.IsNullOrEmpty(plain) || png is { Length: > 0 };

    static DataObject Build(string? plain, string? rtf, string? html, string? xaml, string? pack, byte[]? png, bool rich)
    {
        var data = new DataObject();
        string text = plain ?? "";
        try { data.SetData(DataFormats.UnicodeText, text); } catch (Exception ex) { AppEnv.Log("clipboard text: " + ex.Message); }
        try { data.SetData(DataFormats.Text, text); } catch (Exception ex) { AppEnv.Log("clipboard ansi: " + ex.Message); }
        if (!rich) return AttachPng(data, png);
        try { if (!string.IsNullOrEmpty(pack)) data.SetData(NoteClip.Format, pack); } catch (Exception ex) { AppEnv.Log("clipboard pack: " + ex.Message); }
        try { if (!string.IsNullOrEmpty(rtf)) data.SetData(DataFormats.Rtf, rtf); } catch (Exception ex) { AppEnv.Log("clipboard rtf: " + ex.Message); }
        try { if (!string.IsNullOrEmpty(xaml)) data.SetData(DataFormats.Xaml, xaml); } catch (Exception ex) { AppEnv.Log("clipboard xaml: " + ex.Message); }
        try { if (!string.IsNullOrEmpty(html)) data.SetData(DataFormats.Html, html); } catch (Exception ex) { AppEnv.Log("clipboard html: " + ex.Message); }
        return AttachPng(data, png);
    }

    static DataObject AttachPng(DataObject data, byte[]? png)
    {
        if (png is not { Length: > 0 }) return data;
        try { data.SetData("PNG", new MemoryStream(png), false); } catch (Exception ex) { AppEnv.Log("clipboard png: " + ex.Message); }
        try
        {
            using var ms = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            data.SetImage(bmp);
        }
        catch (Exception ex) { AppEnv.Log("clipboard bitmap: " + ex.Message); }
        return data;
    }

    static bool Commit(DataObject data, bool rich)
    {
        for (int i = 0; i < 6; i++)
        {
            SetAttempts++;
            try
            {
                if (rich && RichThrows > 0)
                {
                    RichThrows--;
                    throw new System.Runtime.InteropServices.COMException("OpenClipboard", unchecked((int)0x800401D0));
                }
                Memory = data;
                if (!AllowsSystem)
                {
                    LastSequence = ++SequenceNow;
                    return true;
                }
                Clipboard.SetDataObject(data, copy: true);
                SystemCalls++;
                try { LastSequence = GetClipboardSequenceNumber(); } catch (Exception ex) { AppEnv.Log("clipboard sequence: " + ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                AppEnv.Log("clipboard: " + ex.GetType().Name + " " + ex.Message);
                if (AllowsSystem && i + 1 < 6) Thread.Sleep(15 * (i + 1));
            }
        }
        return false;
    }

    public static IDataObject? TakeClipboard()
    {
        var current = CurrentClipboard();
        if (current != null) return current;
        if (!AllowsSystem) return null;
        for (int i = 0; i < 6; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (Exception ex)
            {
                LastError = ex;
                AppEnv.Log("clipboard read: " + ex.GetType().Name + " " + ex.Message);
                if (i + 1 < 6) Thread.Sleep(15 * (i + 1));
            }
        }
        return Memory;
    }
}
