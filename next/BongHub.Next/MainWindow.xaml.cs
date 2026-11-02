using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BongHub.Next;

public partial class MainWindow : Window
{
    public static readonly (string Key, string Label, string Glyph, string Hex)[] Pages =
    {
        ("presets", "모니터 프리셋", "\uE7F4", "4C8AFE"),
        ("notes",   "스티커 메모", "\uE70B", "F5B83D"),
        ("general", "일반", "\uE713", "8E8E93"),
        ("more",    "더 보기", "\uE712", "AF52DE"),
        ("about",   "정보", "\uE946", "34C759"),
    };
    public string CurrentPage { get; private set; } = "";
    public bool EverShown { get; private set; }
    public TextBlock? TitleBlock { get; private set; }
    public TextBlock? FirstRowTitle { get; private set; }
    public TextBlock? FirstSecondary { get; private set; }
    public double LastNavigateMs { get; private set; }
    public Slider? NoteFontSlider { get; private set; }
    readonly HashSet<string> noteChecks = new(StringComparer.Ordinal);
    readonly Dictionary<string, Border> navRows = new();
    bool hiding;

    public MainWindow()
    {
        InitializeComponent();
        WindowFx.Attach(this, Root, wantBackdrop: true, solid: Color.FromRgb(0x0D, 0x10, 0x18));
        SourceInitialized += (_, _) =>
        {
            // on Mica the tint is translucent so the wallpaper glow comes through; otherwise fully opaque
            byte a = WindowFx.Of(this).Backdrop ? (byte)0xD0 : (byte)0xFF;
            TintA.Color = Color.FromArgb(a, 0x0D, 0x10, 0x18); TintB.Color = Color.FromArgb(a, 0x14, 0x12, 0x24);
            if (AppEnv.Off) { ShowInTaskbar = false; Win32.Place(this, AppEnv.OffsetX + 100, AppEnv.OffsetY + 100, (int)(Width * Win32.Scale(this)), (int)(Height * Win32.Scale(this))); }
        };
        if (AppEnv.Off) { ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual; Left = -30000; Top = -30000; }
        ContentRendered += (_, _) => EverShown = true;
        int i = 0;
        foreach (var p in Pages) Nav.Children.Add(NavRow(p, i++));
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseBtn.Click += (_, _) => Close();
        Closing += (_, e) =>
        {
            if (App.ShuttingDown) return;
            e.Cancel = true;                              // the settings window is never destroyed: close == hide
            if (hiding) return; hiding = true;
            Fx.Disappear(Root, () => { Hide(); hiding = false; });
        };
        NoteManager.Changed += () => { if (CurrentPage == "notes" && IsVisible) Dispatcher.BeginInvoke(() => Render("notes", animate: false)); };
        AppEnv.ReadOnlyChanged += () => Dispatcher.BeginInvoke(() =>
        {
            UpdateStatus();
            foreach (var w in NoteManager.Windows) w.ApplyReadOnly();
            if (CurrentPage == "notes" && IsVisible) Render("notes", animate: false);
        });
        UpdateStatus();
        Navigate("presets");
    }

    /// <summary>Decorative radial glows. Shown after the first frame is recorded so the software raster of the first paint skips them.</summary>
    public void ShowDecorations()
    {
        if (GlowA.Visibility == Visibility.Visible) return;
        GlowA.Visibility = Visibility.Visible;
        GlowB.Visibility = Visibility.Visible;
    }

    public void Reappear() => Fx.Appear(Root);

    // ---------- navigation ----------
    Border NavRow((string Key, string Label, string Glyph, string Hex) p, int index)
    {
        var tile = Tile(p.Glyph, p.Hex, 28, 8, 14);
        var row = new Border { Height = 40, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = index };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(tile);
        sp.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.Medium });
        row.Child = sp;
        row.MouseEnter += (_, _) => { if (CurrentPage != p.Key) row.Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)); };
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => Navigate(p.Key);
        navRows[p.Key] = row;
        return row;
    }
    public static Border Tile(string glyph, string hex, double size, double radius, double font)
    {
        var c = (Color)ColorConverter.ConvertFromString("#" + hex);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(NoteColors.Mix(c, Colors.White, 0.18), c, 90),
            Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("Icons"), FontSize = font, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    public void Navigate(string key)
    {
        var sw = Stopwatch.StartNew();
        int idx = Array.FindIndex(Pages, p => p.Key == key); if (idx < 0) return;
        bool animate = CurrentPage != "" && CurrentPage != key;
        if (animate) Fx.MoveY(NavSelT, idx * 42, 320);
        else { NavSelT.BeginAnimation(TranslateTransform.YProperty, null); NavSelT.Y = idx * 42; }
        foreach (var r in navRows.Values) r.Background = Brushes.Transparent;
        Render(key, animate);
        LastNavigateMs = sw.Elapsed.TotalMilliseconds;
    }

    void Render(string key, bool animate)
    {
        CurrentPage = key;
        PageHost.Children.Clear(); FirstRowTitle = null; FirstSecondary = null;
        var label = Pages.First(p => p.Key == key).Label;
        TitleBlock = new TextBlock { Text = label, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 34, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 0, 14) };
        PageHost.Children.Add(TitleBlock);
        switch (key)
        {
            case "presets": PagePresets(); break;
            case "notes": PageNotes(); break;
            case "general": PageGeneral(); break;
            case "more": PageMore(); break;
            default: PageAbout(); break;
        }
        if (animate) Fx.SlideIn(PageHost);
        if (!animate) Scroller.ScrollToTop();
    }

    // ---------- building blocks (iOS inset grouped list) ----------
    void Header(string text) => PageHost.Children.Add(new TextBlock { Text = text, Margin = new Thickness(16, 14, 0, 7), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Secondary") });
    void Footer(string text) { var t = new TextBlock { Text = text, Margin = new Thickness(16, 0, 16, 10), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Secondary") }; FirstSecondary ??= t; PageHost.Children.Add(t); }
    StackPanel Group()
    {
        var sp = new StackPanel();
        PageHost.Children.Add(new Border { Style = (Style)FindResource("Group"), Child = sp });
        return sp;
    }
    Border Row(StackPanel group, string? glyph, string? hex, string title, string? sub, UIElement? trailing, Action? click = null)
    {
        if (group.Children.Count > 0)
            group.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Separator"), Margin = new Thickness(glyph != null ? 58 : 16, 0, 0, 0) });
        var g = new Grid { MinHeight = 50 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph != null) g.Children.Add(new Border { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tile(glyph, hex ?? "8E8E93", 30, 8, 15) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(glyph != null ? 14 : 16, 9, 10, 9) };
        var tt = new TextBlock { Text = title, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis };
        texts.Children.Add(tt); FirstRowTitle ??= tt;
        if (sub != null) { var st = new TextBlock { Text = sub, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)FindResource("Secondary"), TextWrapping = TextWrapping.Wrap }; texts.Children.Add(st); FirstSecondary ??= st; }
        Grid.SetColumn(texts, 1); g.Children.Add(texts);
        if (trailing != null) { var b = new Border { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Child = trailing }; Grid.SetColumn(b, 2); g.Children.Add(b); }
        var row = new Border { Child = g, Background = Brushes.Transparent, CornerRadius = new CornerRadius(16) };
        if (click != null)
        {
            row.Cursor = Cursors.Hand;
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x0E, 255, 255, 255));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => click();
        }
        group.Children.Add(row);
        return row;
    }
    CheckBox Switch(bool on, Action<bool> changed, bool enabled = true)
    {
        var c = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = on, IsEnabled = enabled };
        c.Click += (_, _) => changed(c.IsChecked == true);
        return c;
    }
    TextBlock Value(string text) => new() { Text = text, FontSize = 14, Foreground = (Brush)FindResource("Secondary") };
    TextBlock Chevron() => new() { Text = "\uE76C", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = (Brush)FindResource("Tertiary") };
    static readonly SolidColorBrush KeycapBrush = CreateKeycapBrush();
    static SolidColorBrush CreateKeycapBrush() { var b = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)); b.Freeze(); return b; }
    // Inherit UiFont. A new FontFamily("Cascadia Mono, ...") probes the font collection on the first frame.
    Border Keycap(string text) => new() { CornerRadius = new CornerRadius(7), Background = KeycapBrush, Padding = new Thickness(9, 3, 9, 4), Child = new TextBlock { Text = text, FontSize = 12.5 } };

    // ---------- pages ----------
    void PagePresets()
    {
        Header("내 프리셋");
        var g = Group();
        string[] hexes = { "4C8AFE", "FF9500", "FF2D55", "34C759", "AF52DE" };
        int i = 0;
        foreach (var p in ConfigStore.Presets) Row(g, "\uE7F4", hexes[i++ % hexes.Length], p.Name, null, string.IsNullOrEmpty(p.Hotkey) ? null : Keycap(p.Hotkey));
        Footer("프리셋 적용(모니터·밝기·창 배치)은 아직 3.4.8에서 동작해요. Next는 설정을 읽어 보여주기만 하고 바꾸지 않아요.");
        Header("이전 상태");
        var m = Group();
        Row(m, "\uE8B7", "8E8E93", "설정 파일", AppEnv.ConfigPath, Value(ConfigStore.LoadError == "" ? "읽음" : "오류"));
        Row(m, "\uE70B", "F5B83D", "스티커 메모", $"{NoteStore.All.Count}개 불러옴" + (NoteStore.CorruptMeta > 0 ? $" · 손상 {NoteStore.CorruptMeta}개 건너뜀" : ""), null);
    }

    void PageNotes()
    {
        bool ro = AppEnv.ReadOnly;
        var top = Group();
        var newBtn = new Button { Content = "새 메모", Style = (Style)FindResource("AccentButton"), IsEnabled = !ro, Padding = new Thickness(18, 8, 18, 8) };
        newBtn.Click += (_, _) => NoteManager.NewNote();
        Row(top, "\uE710", "F5B83D", "새 스티커 메모", ConfigStore.Notes.Hotkey.Length > 0 ? $"단축키 {ConfigStore.Notes.Hotkey}" : "단축키 없음", newBtn);

        Header("모양");
        var g = Group();
        Row(g, "\uE7B3", "FF9500", "시작할 때 열려 있던 메모 열기", null, Switch(ConfigStore.Notes.OpenOnStartup, v => { ConfigStore.Notes.OpenOnStartup = v; ConfigStore.SetNotesValue("OpenOnStartup", JsonValue.Create(v)); }, !ro));
        Row(g, "\uE790", "AF52DE", "그라데이션 배경", null, Switch(ConfigStore.Notes.Gradient, v => { ConfigStore.Notes.Gradient = v; ConfigStore.SetNotesValue("Gradient", JsonValue.Create(v)); foreach (var w in NoteManager.Windows) w.SetColorRefresh(); }, !ro));
        double font = StickyNoteWindow.ClampPt(ConfigStore.Notes.FontSize);
        var fontLabel = Value(((int)Math.Round(font)).ToString());
        fontLabel.Margin = new Thickness(8, 0, 0, 0);
        fontLabel.MinWidth = 24;
        var fontSlider = new Slider { Minimum = 7, Maximum = 48, Width = 148, IsEnabled = !ro, IsMoveToPointEnabled = true, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        NoteFontSlider = fontSlider;
        fontSlider.Value = font;
        int fontGuard = 0;
        fontSlider.ValueChanged += (_, _) =>
        {
            if (fontGuard > 0) return;
            int v = (int)Math.Round(fontSlider.Value);
            fontLabel.Text = v.ToString();
            if (Math.Abs(v - ConfigStore.Notes.FontSize) < 0.01) return;
            float prev = ConfigStore.Notes.FontSize;
            ConfigStore.Notes.FontSize = v;
            if (!ConfigStore.SetNotesValue("FontSize", JsonValue.Create(v)))
            {
                ConfigStore.Notes.FontSize = prev;
                fontGuard++;
                fontSlider.Value = StickyNoteWindow.ClampPt(prev);
                fontGuard--;
                fontLabel.Text = ((int)Math.Round(StickyNoteWindow.ClampPt(prev))).ToString();
                return;
            }
            foreach (var w in NoteManager.Windows) w.ApplyFontSize();
        };
        var fontTrail = new StackPanel { Orientation = Orientation.Horizontal };
        fontTrail.Children.Add(fontSlider);
        fontTrail.Children.Add(fontLabel);
        Row(g, "\uE8D2", "4C8AFE", "글자 크기", "7–48pt. 열린 메모에 바로 적용돼요.", fontTrail);
        var hkBox = new TextBox { Text = ConfigStore.Notes.Hotkey, Width = 120, IsReadOnly = true, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        hkBox.PreviewKeyDown += (sender, e) =>
        {
            e.Handled = true;
            if (ro || e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System) return;
            if (e.Key is Key.Escape or Key.Tab) return;
            string text = FormatHotkey(e.Key, Keyboard.Modifiers);
            // `out _` here would bind the lambda parameter if it were named `_` (object), not a discard.
            if (text.Length == 0 || !NoteHotkey.TryParse(text, out uint _, out uint _)) return;
            string prev = ConfigStore.Notes.Hotkey;
            ConfigStore.Notes.Hotkey = text;
            if (!ConfigStore.SetNotesValue("Hotkey", JsonValue.Create(text))) { ConfigStore.Notes.Hotkey = prev; return; }
            hkBox.Text = text;
            NoteHotkey.Apply(text, App.Instance.Tray);
        };
        var hkClear = new Button { Content = "지우기", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !ro };
        hkClear.Click += (_, _) =>
        {
            ConfigStore.Notes.Hotkey = "";
            if (!ConfigStore.SetNotesValue("Hotkey", JsonValue.Create(""))) { ConfigStore.Load(); return; }
            hkBox.Text = "";
            NoteHotkey.Apply("", App.Instance.Tray);
        };
        var hkTrail = new StackPanel { Orientation = Orientation.Horizontal };
        hkTrail.Children.Add(hkBox);
        hkTrail.Children.Add(hkClear);
        Row(g, "\uE765", "FF9500", "새 메모 단축키", "저장하는 즉시 다시 등록해요. 비우면 등록하지 않아요.", hkTrail);
        int op = ConfigStore.Notes.Opacity;
        if (op < 30 || op > 100) op = 100;
        int shown = Math.Clamp(op, 40, 100);
        var opLabel = Value(shown + "%");
        opLabel.Margin = new Thickness(10, 0, 0, 0);
        opLabel.MinWidth = 40;
        var slider = new Slider { Minimum = 40, Maximum = 100, Width = 148, IsEnabled = !ro, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.Value = shown;   // before the handler, so opening the page does not rewrite a clamped value
        int sliderGuard = 0;
        slider.ValueChanged += (_, _) =>
        {
            if (sliderGuard > 0) return;
            int v = (int)Math.Round(slider.Value);
            opLabel.Text = v + "%";
            if (v == ConfigStore.Notes.Opacity) return;
            int prev = ConfigStore.Notes.Opacity;
            ConfigStore.Notes.Opacity = v;
            if (!ConfigStore.SetNotesValue("Opacity", JsonValue.Create(v)))
            {
                ConfigStore.Notes.Opacity = prev;
                int back = prev < 30 || prev > 100 ? 100 : prev;
                back = Math.Clamp(back, 40, 100);
                sliderGuard++;
                slider.Value = back;
                sliderGuard--;
                opLabel.Text = back + "%";
                return;
            }
            foreach (var w in NoteManager.Windows) w.ApplyOpacity();
        };
        var opTrail = new StackPanel { Orientation = Orientation.Horizontal };
        opTrail.Children.Add(slider);
        opTrail.Children.Add(opLabel);
        Row(g, "\uE81E", "5AC8FA", "불투명도", "메모 창 전체예요. 40~100%.", opTrail);
        var dc = ConfigStore.Notes.DefaultColor;
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        sw.Children.Add(new Ellipse { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), Fill = new LinearGradientBrush(NoteColors.Swatch(dc), NoteColors.SwatchB(dc), 45) });
        sw.Children.Add(Value(NoteColors.Name(dc)));
        var custom = new Button { Content = "맞춤 색", Padding = new Thickness(10, 4, 10, 4), IsEnabled = !ro };
        custom.Click += (_, _) => PickDefaultColor(custom);
        var colorTrail = new StackPanel { Orientation = Orientation.Horizontal };
        colorTrail.Children.Add(sw);
        colorTrail.Children.Add(custom);
        Row(g, "\uE771", "FF2D55", "기본 색", "새 메모는 이 색을 피해 무작위로 골라요 (3.4.9와 같음)", colorTrail);

        Header($"메모 {NoteStore.All.Count}개");
        var cmds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) };
        cmds.Children.Add(NoteCmd("모두 보이기", () => NoteManager.ShowAll(), enabled: true));
        cmds.Children.Add(NoteCmd("모두 숨기기", () => NoteManager.HideAll(), enabled: true));
        cmds.Children.Add(NoteCmd("텍스트 복사", CopyAllNotes, enabled: true));
        cmds.Children.Add(NoteCmd("삭제", DeleteChecked, enabled: !ro));
        cmds.Children.Add(NoteCmd("선택 색", () => ColorChecked(cmds), enabled: !ro));
        PageHost.Children.Add(cmds);
        var list = Group();
        foreach (var n in NoteStore.All)
        {
            var t = (n.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (t.Length == 0) t = "(빈 메모)"; if (t.Length > 40) t = t[..40] + "…";
            var dot = new Ellipse { Width = 22, Height = 22, Fill = new LinearGradientBrush(NoteColors.Swatch(n.Color), NoteColors.SwatchB(n.Color), 45) };
            var trailing = new StackPanel { Orientation = Orientation.Horizontal };
            if (NoteManager.IsOpen(n.Id)) trailing.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x34, 0xC7, 0x59)), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = "열림", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xE3, 0x9A)) } });
            trailing.Children.Add(Chevron());
            var id = n.Id;
            var check = new CheckBox { IsChecked = noteChecks.Contains(id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            check.Checked += (_, _) => noteChecks.Add(id);
            check.Unchecked += (_, _) => noteChecks.Remove(id);
            trailing.Children.Insert(0, check);
            var r = Row(list, null, null, t, $"{n.Modified:yyyy.MM.dd HH:mm} · {NoteColors.Name(n.Color)}" + (n.TopMost ? " · 항상 위" : ""), trailing, () => NoteManager.Open(id, activate: true));
            ((Grid)r.Child).Children.Insert(0, new Border { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = dot });
            ((StackPanel)((Grid)r.Child).Children.OfType<StackPanel>().First()).Margin = new Thickness(50, 9, 10, 9);
        }
        if (NoteStore.All.Count == 0) Row(list, null, null, "아직 메모가 없어요", null, null);

        Header("데이터");
        var d = Group();
        Row(d, "\uE8B7", "8E8E93", "데이터 폴더", AppEnv.DataDir, null);
        if (ro) Row(d, "\uE72E", "FF9500", "읽기 전용", AppEnv.ReadOnlyReason, null);
        Footer("3.4.9와 같은 notes\\<id>.json / .rtf 형식으로 저장해요. 손상되거나 빈 메모 파일은 건너뛰지 않고 안전하게 열어요.");
        Footer("메모 안: Ctrl+B 굵게 · Ctrl+I 기울임 · Ctrl+U 밑줄 · Ctrl+T 취소선 · Ctrl+Shift+L 글머리 · Ctrl++/− 글자 크기 · Ctrl+N 새 메모 · Ctrl+D 삭제 · Ctrl+W 닫기");
    }

    Button NoteCmd(string label, Action act, bool enabled)
    {
        var b = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsEnabled = enabled };
        b.Click += (_, _) => act();
        return b;
    }

    public bool HasNotesCommand(string label) => WalkButtons(PageHost).Any(b => b.Content as string == label);

    static IEnumerable<Button> WalkButtons(DependencyObject node)
    {
        if (node is Button button) yield return button;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var b in WalkButtons(child)) yield return b;
    }

    void CopyAllNotes()
    {
        string text = NoteManager.AllText();
        try { if (text.Length > 0) Clipboard.SetText(text); } catch (Exception e) { AppEnv.Log("copy notes: " + e.Message); }
    }

    void DeleteChecked()
    {
        var ids = noteChecks.ToList();
        if (ids.Count == 0) return;
        bool blank = ids.All(id => NoteStore.Find(id) is { } m && (NoteManager.Windows.FirstOrDefault(w => w.Meta.Id == id)?.IsBlankLive ?? NoteStore.IsBlank(m)));
        if (!blank && !ConfirmNotes(ids.Count)) return;
        foreach (var id in ids) NoteManager.Delete(id, confirmed: true);
        noteChecks.Clear();
    }

    void ColorChecked(DependencyObject owner)
    {
        var ids = noteChecks.ToList();
        if (ids.Count == 0 || owner is not UIElement el) return;
        var menu = new ContextMenu { PlacementTarget = el, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var key in NoteColors.PaletteKeys)
        {
            var k = key;
            var item = new MenuItem { Header = NoteColors.Name(k) };
            item.Click += (_, _) =>
            {
                foreach (var id in ids)
                {
                    var w = NoteManager.Windows.FirstOrDefault(x => x.Meta.Id == id);
                    if (w != null) w.SetColor(k);
                    else if (NoteStore.Find(id) is { } m && m.Color != k) { m.Color = k; NoteStore.SaveMeta(m); }
                }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    static bool ConfirmNotes(int count)
    {
        if (AppEnv.Smoke) return false;
        var w = new Window
        {
            Title = "스티커 메모", Width = 380, Height = 168, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = !AppEnv.Off, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            WindowStartupLocation = AppEnv.Off ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
        };
        if (AppEnv.Off) { w.Left = AppEnv.OffsetX + 240; w.Top = AppEnv.OffsetY + 240; }
        bool yes = false;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = count + "개 메모를 삭제할까요?", Foreground = Brushes.White, FontSize = 15, Margin = new Thickness(0, 8, 0, 16) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Content = "취소", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        var ok = new Button { Content = "삭제", Padding = new Thickness(14, 6, 14, 6) };
        no.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { yes = true; w.Close(); };
        row.Children.Add(no); row.Children.Add(ok); root.Children.Add(row);
        w.Content = root;
        w.ShowDialog();
        return yes;
    }

    void PickDefaultColor(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var key in NoteColors.PaletteKeys)
        {
            var k = key;
            var item = new MenuItem { Header = NoteColors.Name(k) };
            item.Click += (_, _) => SaveDefaultColor(k);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var ask = new MenuItem { Header = "맞춤…" };
        ask.Click += (_, _) => SaveDefaultColor(PromptCustom(ConfigStore.Notes.DefaultColor));
        menu.Items.Add(ask);
        menu.IsOpen = true;
    }

    static string PromptCustom(string current)
    {
        NoteColors.Raw(current, out var a, out var b);
        string hex(Color c) => c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        var w = new Window
        {
            Title = "맞춤 색", Width = 320, Height = 160, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = !AppEnv.Off, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            WindowStartupLocation = AppEnv.Off ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
        };
        if (AppEnv.Off) { w.Left = AppEnv.OffsetX + 260; w.Top = AppEnv.OffsetY + 260; }
        string result = current;
        var aBox = new TextBox { Text = hex(a), Width = 90, MaxLength = 6, Margin = new Thickness(0, 0, 8, 0) };
        var bBox = new TextBox { Text = hex(b), Width = 90, MaxLength = 6 };
        var ok = new Button { Content = "적용", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => { if (NoteColors.TryCustom(aBox.Text.Trim(), bBox.Text.Trim(), out var k)) result = k; w.Close(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        row.Children.Add(aBox); row.Children.Add(bBox); row.Children.Add(ok);
        w.Content = row;
        w.ShowDialog();
        return result;
    }

    static void SaveDefaultColor(string key)
    {
        if (!NoteColors.Valid(key) || AppEnv.ReadOnly) return;
        string prev = ConfigStore.Notes.DefaultColor;
        ConfigStore.Notes.DefaultColor = key;
        if (!ConfigStore.SetNotesValue("DefaultColor", JsonValue.Create(key))) ConfigStore.Notes.DefaultColor = prev;
    }

    static string FormatHotkey(Key key, ModifierKeys mods)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return "";
        var parts = new List<string>();
        if ((mods & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");
        if ((mods & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        parts.Add(KeyToken(key));
        return string.Join("+", parts);
    }

    static string KeyToken(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (int)(key - Key.NumPad0);
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Escape => "Escape",
            Key.Enter => "Enter",
            Key.OemPlus => "Oemplus",
            Key.OemMinus => "OemMinus",
            _ => key.ToString(),
        };
    }

    void PageGeneral()
    {
        var g = Group();
        Row(g, "\uE7E8", "34C759", "Windows 시작 시 실행", "3.4.8의 시작프로그램 바로가기를 건드리지 않도록 Next에서는 아직 꺼 두었어요.", Switch(false, _ => { }, enabled: false));
        Row(g, "\uE9E9", "5AC8FA", "애니메이션 줄이기", "창과 화면 전환 움직임을 최소화해요.", Switch(NextSettings.ReduceMotion, v => NextSettings.SetReduceMotion(v)));
        Header("상태");
        var s = Group();
        var proc = Process.GetCurrentProcess();
        Row(s, "\uE950", "8E8E93", "메모리", null, Value($"{proc.PrivateMemorySize64 / 1048576} MB"));
        Row(s, "\uE7BA", AppEnv.ReadOnly ? "FF9500" : "34C759", "3.4.8 동시 실행", null, Value(AppEnv.ReadOnly ? "읽기 전용" : AppEnv.LegacyRunning() ? "실행 중" : "꺼져 있음"));
        Row(s, "\uE946", "4C8AFE", "버전", null, Value("Next " + AppEnv.Version));
    }

    void PageMore()
    {
        Footer("아래 기능은 아직 안정 버전(3.4.8)에서 동작해요. Next로 하나씩 옮기는 중이에요.");
        var g = Group();
        Row(g, "\uE8BD", "FFCC00", "카카오톡 알림 규칙", "3.4.8에서 사용", Chevron());
        Row(g, "\uE7F4", "5856D6", "모니터 · 밝기 전환", "3.4.8에서 사용", Chevron());
        Row(g, "\uE80F", "34C759", "바탕화면 아이콘 정리", "3.4.8에서 사용", Chevron());
        Row(g, "\uE753", "5AC8FA", "클라우드 동기화", "3.4.8에서 사용", Chevron());
    }

    void PageAbout()
    {
        var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 18) };
        var icon = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(22), Background = (Brush)FindResource("AccentGradient"), HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.35, Direction = 270, Color = Color.FromRgb(0x4C, 0x8A, 0xFE) } };
        var grid = new UniformGrid4();
        icon.Child = grid;
        hero.Children.Add(icon);
        hero.Children.Add(new TextBlock { Text = "BongHub Next", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 2) });
        var ver = new TextBlock { Text = $"버전 {AppEnv.Version} · .NET 10 WPF", Foreground = (Brush)FindResource("Secondary"), HorizontalAlignment = HorizontalAlignment.Center };
        FirstSecondary ??= ver;
        hero.Children.Add(ver);
        PageHost.Children.Add(hero);
        var g = Group();
        Row(g, null, null, "안정 버전", null, Value("3.4.8 (그대로 사용)"));
        Row(g, null, null, "단일 실행", null, Value(App.MutexName.Replace("Local\\", "")));
        Row(g, null, null, "데이터", null, Value(AppEnv.IsDefaultData ? "3.4.8과 공유" : "테스트 복사본"));
    }

    sealed class UniformGrid4 : Grid
    {
        public UniformGrid4()
        {
            Margin = new Thickness(20);
            ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition());
            RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition());
            byte[] a = { 0xFF, 0xCC, 0xCC, 0x80 };
            for (int i = 0; i < 4; i++) { var b = new Border { Margin = new Thickness(2.5), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(a[i], 255, 255, 255)) }; SetRow(b, i / 2); SetColumn(b, i % 2); Children.Add(b); }
        }
    }

    public void UpdateStatus()
    {
        if (AppEnv.ReadOnly) { StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x95, 0x00)); StatusText.Text = "읽기 전용 · 저장 안 함"; }
        else if (!AppEnv.IsDefaultData) { StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0xC8, 0xFA)); StatusText.Text = "테스트 데이터 사용 중"; }
        else StatusText.Text = $"메모 {NoteStore.All.Count}개 · 준비됨";
    }
}
