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
        Row(top, "\uE710", "F5B83D", "새 스티커 메모", ConfigStore.Notes.Hotkey.Length > 0 ? $"3.4.8 단축키: {ConfigStore.Notes.Hotkey}" : null, newBtn);

        Header("모양");
        var g = Group();
        Row(g, "\uE7B3", "FF9500", "시작할 때 열려 있던 메모 열기", null, Switch(ConfigStore.Notes.OpenOnStartup, v => { ConfigStore.Notes.OpenOnStartup = v; ConfigStore.SetNotesValue("OpenOnStartup", JsonValue.Create(v)); }, !ro));
        Row(g, "\uE790", "AF52DE", "그라데이션 배경", null, Switch(ConfigStore.Notes.Gradient, v => { ConfigStore.Notes.Gradient = v; ConfigStore.SetNotesValue("Gradient", JsonValue.Create(v)); foreach (var w in NoteManager.Windows) w.SetColorRefresh(); }, !ro));
        float[] sizes = { 10, 11, 13 };
        int si = Array.IndexOf(sizes, ConfigStore.Notes.FontSize); if (si < 0) si = 1;
        var seg = new Segmented(new[] { "작게", "보통", "크게" }, si) { IsEnabled = !ro };
        seg.Changed += k => { ConfigStore.Notes.FontSize = sizes[k]; ConfigStore.SetNotesValue("FontSize", JsonValue.Create(sizes[k])); foreach (var w in NoteManager.Windows) w.ApplyFontSize(); };
        Row(g, "\uE8D2", "4C8AFE", "글자 크기", null, seg);
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
        Row(g, "\uE771", "FF2D55", "기본 색", "새 메모는 이 색을 피해 무작위로 골라요 (3.4.8과 같음)", sw);

        Header($"메모 {NoteStore.All.Count}개");
        var list = Group();
        foreach (var n in NoteStore.All)
        {
            var t = (n.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (t.Length == 0) t = "(빈 메모)"; if (t.Length > 40) t = t[..40] + "…";
            var dot = new Ellipse { Width = 22, Height = 22, Fill = new LinearGradientBrush(NoteColors.Swatch(n.Color), NoteColors.SwatchB(n.Color), 45) };
            var trailing = new StackPanel { Orientation = Orientation.Horizontal };
            if (NoteManager.IsOpen(n.Id)) trailing.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x34, 0xC7, 0x59)), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = "열림", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xE3, 0x9A)) } });
            trailing.Children.Add(Chevron());
            var id = n.Id;
            var r = Row(list, null, null, t, $"{n.Modified:yyyy.MM.dd HH:mm} · {NoteColors.Name(n.Color)}" + (n.TopMost ? " · 항상 위" : ""), trailing, () => NoteManager.Open(id, activate: true));
            ((Grid)r.Child).Children.Insert(0, new Border { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = dot });
            ((StackPanel)((Grid)r.Child).Children.OfType<StackPanel>().First()).Margin = new Thickness(50, 9, 10, 9);
        }
        if (NoteStore.All.Count == 0) Row(list, null, null, "아직 메모가 없어요", null, null);

        Header("데이터");
        var d = Group();
        Row(d, "\uE8B7", "8E8E93", "데이터 폴더", AppEnv.DataDir, null);
        if (ro) Row(d, "\uE72E", "FF9500", "읽기 전용", AppEnv.ReadOnlyReason, null);
        Footer("3.4.8과 같은 notes\\<id>.json / .rtf 형식으로 저장해요. 손상되거나 빈 메모 파일은 건너뛰지 않고 안전하게 열어요.");
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
