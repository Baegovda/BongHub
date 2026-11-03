using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BongHub;

public partial class MainWindow : Window
{
    public static readonly (string Key, string Label, string Glyph, string Hex)[] Catalog =
    {
        ("presets", "모니터 프로필", "\uE7F4", "4C8AFE"),
        ("notes", "스티커 메모", "\uE70B", "F5B83D"),
        ("kakao", "카톡 광고 제거", "\uE8BD", "FFCC00"),
        ("powertoys", "PowerToys", "\uE945", "AF52DE"),
        ("general", "일반", "\uE713", "8E8E93"),
        ("about", "정보", "\uE946", "5856D6"),
    };
    public static readonly string[] DefaultTabOrder = { "presets", "notes", "kakao", "powertoys", "general", "about" };
    public IReadOnlyList<(string Key, string Label, string Glyph, string Hex)> Pages { get; private set; } = Catalog;
    public string CurrentPage { get; private set; } = "";
    public bool EverShown { get; private set; }
    public TextBlock? TitleBlock { get; private set; }
    public TextBlock? FirstRowTitle { get; private set; }
    public TextBlock? FirstSecondary { get; private set; }
    public double LastNavigateMs { get; private set; }
    public Slider? NoteFontSlider { get; private set; }
    public bool StartupSwitchEnabled { get; private set; }
    public bool AboutMentionsVersion { get; private set; }
    public bool AboutMentionsHotkey { get; private set; }
    public int FolderLaunches { get; private set; }
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
        SourceInitialized += (_, _) =>
        {
            var src = HwndSource.FromHwnd(Win32.Handle(this));
            src?.AddHook(OnDisplayMessage);
        };
        ProfileHotkey.Fired = id => Dispatcher.BeginInvoke(() => RequestApplyProfile(id));
        SizeChanged += (_, e) =>
        {
            if (CurrentPage != "presets") return;
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 12 && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 12) return;
            Dispatcher.BeginInvoke(() => { if (CurrentPage == "presets") Render("presets", false); });
        };
        ApplyTabOrder(ConfigStore.TabOrder);
        RebuildNav();
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
        AppEnv.Mark("presets_begin");
        Navigate("presets");
        AppEnv.Mark("presets_end");
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

    int IndexOfPage(string key)
    {
        for (int i = 0; i < Pages.Count; i++) if (Pages[i].Key == key) return i;
        return -1;
    }

    public void ApplyTabOrder(IEnumerable<string>? saved)
    {
        var keys = new List<string>();
        if (saved != null)
            foreach (var k in saved)
                if (Catalog.Any(p => p.Key == k) && !keys.Contains(k)) keys.Add(k);
        foreach (var k in DefaultTabOrder)
            if (!keys.Contains(k)) keys.Add(k);
        Pages = keys.Select(k => Catalog.First(p => p.Key == k)).ToList();
    }

    public void RebuildNav()
    {
        Nav.Children.Clear();
        navRows.Clear();
        int i = 0;
        foreach (var p in Pages) Nav.Children.Add(NavRow(p, i++));
        if (CurrentPage.Length > 0) Navigate(CurrentPage);
    }

    /// <summary>Reorder one tab and splice <c>General.TabOrder</c>. Other config bytes stay.</summary>
    public bool MoveTab(string key, int to)
    {
        var keys = Pages.Select(p => p.Key).ToList();
        int from = keys.IndexOf(key);
        if (from < 0 || to < 0 || to >= keys.Count || from == to) return false;
        keys.RemoveAt(from);
        keys.Insert(to, key);
        var arr = new JsonArray();
        foreach (var k in keys) arr.Add(JsonValue.Create(k));
        if (!ConfigStore.SetSectionValue("General", "TabOrder", arr)) return false;
        ConfigStore.TabOrder.Clear();
        ConfigStore.TabOrder.AddRange(keys);
        string stay = CurrentPage;
        ApplyTabOrder(keys);
        Nav.Children.Clear();
        navRows.Clear();
        int i = 0;
        foreach (var p in Pages) Nav.Children.Add(NavRow(p, i++));
        if (stay.Length > 0) { CurrentPage = ""; Navigate(stay); }
        return true;
    }

    /// <summary>Opens the data folder. Tests and <c>--off</c> record the request and do not start Explorer.</summary>
    public bool RequestOpenDataFolder()
    {
        if (AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest) return false;
        try
        {
            Directory.CreateDirectory(AppEnv.DataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", AppEnv.DataDir) { UseShellExecute = true });
            FolderLaunches++;
            return true;
        }
        catch (Exception e) { AppEnv.Log("open folder: " + e.Message); return false; }
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
        int idx = IndexOfPage(key); if (idx < 0) return;
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
            case "windows": PagePresets(); break;
            case "notes": PageNotes(); break;
            case "kakao": PageKakao(); break;
            case "desktop": PagePresets(); break;
            case "powertoys": PagePowerToys(); break;
            case "general": PageGeneral(); break;
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
    Border Row(StackPanel group, string? glyph, string? hex, string title, string? sub, UIElement? trailing, Action? click = null, bool wrap = false, ImageSource? icon = null)
    {
        bool lead = icon != null || glyph != null;
        if (group.Children.Count > 0)
            group.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Separator"), Margin = new Thickness(lead ? 58 : 16, 0, 0, 0) });
        var g = new Grid { MinHeight = 50 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon != null) g.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Child = new Image { Source = icon, Width = 30, Height = 30, Stretch = Stretch.Uniform } });
        else if (glyph != null) g.Children.Add(new Border { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tile(glyph, hex ?? "8E8E93", 30, 8, 15) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(lead ? 14 : 16, 9, 10, 9) };
        var tt = new TextBlock { Text = title, FontSize = 14.5, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
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

    public string DisplayAction { get; private set; } = "";
    public int DisplayBlocks { get; private set; }
    public string ActivePreset { get; private set; } = "";
    public string LastPresetToast { get; private set; } = "";
    public WindowPick? LastPick { get; private set; }
    public List<MonitorProfile> Profiles { get; } = new();
    public string SelectedProfileId { get; private set; } = "";
    int selectedMonitorNumber = 1;
    string selectedAppId = "";
    bool profilesReady;

    /// <summary>Crosshair result. Real top-level windows are read only after the window gate opens.</summary>
    public void RequestCaptureWindow() => RequestCaptureAt(40, 48);

    public void RequestCaptureAt(int x, int y)
    {
        if (!WindowGate.RealAllowed)
        {
            LastPick = WindowPicker.From(new TrackedWindow { Process = "notepad.exe", Title = "메모장", Class = "Notepad", X = x, Y = y, W = 640, H = 480 });
            return;
        }
        try
        {
            var hit = WindowPicker.Choose(LiveWindowSurface.ReadProbes(), x, y);
            if (hit != null) LastPick = WindowPicker.From(hit);
        }
        catch (Exception ex) { DisplayAction = ex.Message; }
    }
    int displayBusy;

    /// <summary>Test hook: the layout the user is already in, without touching a monitor.</summary>
    public void RememberPreset(string name) => ActivePreset = name ?? "";

    /// <summary>Explicit click. Tests, <c>--off</c>, and CI return here before any display command runs.</summary>
    public void RequestApplyPreset(string name)
    {
        if (PresetApplyPolicy.IsCurrent(ActivePreset, name))
        {
            LastPresetToast = PresetApplyPolicy.Toast(name);
            return;
        }
        if (!DisplayGate.RealAllowed)
        {
            DisplayBlocks++;
            DisplayAction = "blocked";
            return;
        }
        var snap = ConfigStore.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        if (snap == null) { DisplayAction = "missing"; return; }
        if (Interlocked.CompareExchange(ref displayBusy, 1, 0) != 0) { DisplayAction = "busy"; return; }
        var plan = PresetCodec.Clone(snap);
        var profile = Profiles.FirstOrDefault(p => string.Equals(p.Name, plan.Name, StringComparison.Ordinal));
        if (profile != null && !holdUndo) ProfileUndo.Remember(profile);
        holdUndo = false;
        DisplayAction = "applying";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { ApplyPresetNow(plan); PlaceProfile(profile); ActivePreset = plan.Name; DisplayAction = "applied " + plan.Name; }
            catch (Exception ex) { DisplayAction = ex.Message; }
            finally { Interlocked.Exchange(ref displayBusy, 0); }
        });
    }

    /// <summary>Apply a profile that is not one of the 3.4.x presets. Tests return before any display call.</summary>
    public void RequestApplyProfile(string id)
    {
        EnsureProfiles();
        var profile = Profiles.FirstOrDefault(p => p.Id == id);
        if (profile == null) { DisplayAction = "missing"; return; }
        if (ConfigStore.Presets.Any(p => string.Equals(p.Name, profile.Name, StringComparison.Ordinal)))
        {
            RequestApplyPreset(profile.Name);
            return;
        }
        if (PresetApplyPolicy.IsCurrent(ActivePreset, profile.Name))
        {
            LastPresetToast = PresetApplyPolicy.Toast(profile.Name);
            return;
        }
        if (!DisplayGate.RealAllowed)
        {
            DisplayBlocks++;
            DisplayAction = "blocked";
            return;
        }
        if (Interlocked.CompareExchange(ref displayBusy, 1, 0) != 0) { DisplayAction = "busy"; return; }
        var plan = ProfileDoc.ToPreset(profile);
        if (!holdUndo) ProfileUndo.Remember(profile);
        holdUndo = false;
        DisplayAction = "applying";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { ApplyPresetNow(plan); PlaceProfile(profile); ActivePreset = plan.Name; DisplayAction = "applied " + plan.Name; }
            catch (Exception ex) { DisplayAction = ex.Message; }
            finally { Interlocked.Exchange(ref displayBusy, 0); }
        });
    }

    bool holdUndo;

    public void UndoLastApply()
    {
        if (ProfileUndo.Last == null) return;
        if (!DisplayGate.RealAllowed)
        {
            DisplayBlocks++;
            DisplayAction = "blocked";
            return;
        }
        var shot = ProfileUndo.Take();
        if (shot == null) return;
        int at = Profiles.FindIndex(p => p.Id == shot.Id);
        if (at >= 0) Profiles[at] = shot;
        else Profiles.Add(shot);
        SelectedProfileId = shot.Id;
        ActivePreset = "";
        SaveProfiles();
        holdUndo = true;
        RequestApplyProfile(shot.Id);
    }

    void PlaceProfile(MonitorProfile? profile)
    {
        if (profile == null) return;
        if (WindowGate.RealAllowed)
        {
            using var live = new LiveWindowSurface();
            var open = live.List();
            ProfileDoc.Place(profile, live);
            foreach (var path in ProfileDoc.MissingLaunches(profile, open))
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { AppEnv.Log("launch: " + ex.Message); }
            }
        }
        if (DesktopGate.RealAllowed && profile.Icons.Count > 0)
        {
            using var icons = new ExplorerIconSurface();
            ProfileDoc.RestoreIcons(profile.Icons, icons);
        }
    }

    public IReadOnlyList<WindowPick> ListCaptureCandidates()
    {
        if (!WindowGate.RealAllowed)
            return new[] { WindowPicker.From(new TrackedWindow { Process = "notepad.exe", Title = "메모장", Class = "Notepad", X = 40, Y = 48, W = 640, H = 480 }) };
        using var live = new LiveWindowSurface();
        return live.List().Select(WindowPicker.From).ToList();
    }

    void EnsureProfiles()
    {
        if (profilesReady) return;
        profilesReady = true;
        string? json = null;
        try
        {
            string path = System.IO.Path.Combine(AppEnv.DataDir, "profiles.json");
            if (File.Exists(path)) json = File.ReadAllText(path);
        }
        catch { }
        Profiles.Clear();
        Profiles.AddRange(ProfileDoc.Migrate(ConfigStore.Presets, ConfigStore.WindowRules, json, ScreenMap.Current()));
        if (Profiles.All(p => p.Id != SelectedProfileId))
            SelectedProfileId = Profiles.FirstOrDefault()?.Id ?? "";
    }

    void SaveProfiles()
    {
        if (AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest || AppEnv.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(AppEnv.DataDir);
            File.WriteAllText(System.IO.Path.Combine(AppEnv.DataDir, "profiles.json"), ProfileDoc.ToJson(Profiles));
        }
        catch { }
    }

    void TouchProfile(Action edit)
    {
        edit();
        SaveProfiles();
        Render("presets", false);
    }

    void ApplyPresetNow(MonitorPreset plan)
    {
        new RealDisplay().Apply(PresetPlan.Commands(plan));
        var sleep = PresetExtras.Sleep;
        var signature = PresetExtras.Signature;
        var stamp = PresetExtras.StampFile;
        var workStamp = PresetExtras.WorkStampFile;
        var mutex = PresetExtras.WorkMutexName;
        var findDesk = PresetExtras.FindDesk;
        var restoreDesk = PresetExtras.RestoreDesk;
        var findWork = PresetExtras.FindWork;
        var launch = PresetExtras.LaunchWork;
        var missing = PresetExtras.WorkLaunchMissing;
        try
        {
            PresetExtras.Sleep = ms => { if (ms > 0) Thread.Sleep(ms); };
            PresetExtras.Signature = () =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (var m in Win32.Monitors()) sb.Append(m.W).Append('x').Append(m.H).Append('@').Append(m.X).Append(',').Append(m.Y).Append(m.Primary ? "*" : "").Append(';');
                return sb.ToString();
            };
            PresetExtras.StampFile = System.IO.Path.Combine(AppEnv.DataDir, "linked_desk.stamp");
            PresetExtras.WorkStampFile = System.IO.Path.Combine(AppEnv.DataDir, "linked_work.stamp");
            PresetExtras.WorkMutexName = @"Local\BongHub_LinkedWork";
            PresetExtras.FindDesk = id =>
            {
                var hit = LayoutStore.Default.Load().FirstOrDefault(l => l.Id == id);
                return hit == null ? null : new LinkedDesk { Id = hit.Id, Name = hit.Name };
            };
            PresetExtras.RestoreDesk = link =>
            {
                if (!DesktopGate.RealAllowed) return new DeskLinkResult { Ok = false, Error = "테스트 모드" };
                var hit = LayoutStore.Default.Load().FirstOrDefault(l => l.Id == link.Id);
                if (hit == null) return new DeskLinkResult { Ok = false, Error = "배치 없음" };
                using var icons = new ExplorerIconSurface();
                var r = IconMatch.Apply(icons, hit.Items);
                return new DeskLinkResult { Ok = r.Ok, Moved = r.Moved, Error = r.Error };
            };
            PresetExtras.FindWork = id =>
            {
                var ws = ConfigStore.Workspaces.FirstOrDefault(w => w.Id == id || w.Name == id);
                return ws == null ? null : new LinkedWork { Id = ws.Id.Length > 0 ? ws.Id : ws.Name, Name = ws.Name };
            };
            PresetExtras.WorkLaunchMissing = () => false;
            PresetExtras.LaunchWork = (w, _) =>
            {
                if (!WindowGate.RealAllowed) return "창 배치 실패: 테스트 모드";
                var ws = ConfigStore.Workspaces.FirstOrDefault(x => x.Id == w.Id || x.Name == w.Name);
                if (ws == null) return "창 배치 실패: 없음";
                using var live = new LiveWindowSurface();
                var moves = WorkspacePlan.Plan(live.List(), ws.Apps, out int _);
                foreach (var m in moves)
                {
                    if (m.State == "min") live.Minimize(m.WindowId);
                    else if (m.State == "max") { live.TryWorkArea("", m.WindowId, out var area); live.Maximize(m.WindowId, area); }
                    else live.Place(m.WindowId, m.X, m.Y, m.W, m.H);
                }
                return "'" + w.Name + "' · 배치 " + moves.Count + "개";
            };
            PresetExtras.Run(plan, _ => { });
        }
        finally
        {
            PresetExtras.Sleep = sleep;
            PresetExtras.Signature = signature;
            PresetExtras.StampFile = stamp;
            PresetExtras.WorkStampFile = workStamp;
            PresetExtras.WorkMutexName = mutex;
            PresetExtras.FindDesk = findDesk;
            PresetExtras.RestoreDesk = restoreDesk;
            PresetExtras.FindWork = findWork;
            PresetExtras.LaunchWork = launch;
            PresetExtras.WorkLaunchMissing = missing;
        }
    }

    // ---------- pages ----------
    IntPtr OnDisplayMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_DISPLAYCHANGE && DisplayGate.RealAllowed)
        {
            shownLive = "";
            if (CurrentPage == "presets") Dispatcher.BeginInvoke(() => { if (CurrentPage == "presets") Render("presets", false); });
        }
        return IntPtr.Zero;
    }

    void PagePresets()
    {
        EnsureProfiles();
        AdoptLiveGeometry();
        Header("모니터 프로필");
        var g = Group();
        string[] hexes = { "4C8AFE", "FF9500", "FF2D55", "34C759", "AF52DE" };
        int i = 0;
        if (Profiles.Count == 0)
            Row(g, "\uE7F4", "8E8E93", "프로필 없음", "추가하면 모니터 배치를 그릴 수 있어요.", null);
        foreach (var p in Profiles)
        {
            var item = p;
            var prim = item.Monitors.FirstOrDefault(m => m.Primary) ?? item.Monitors.FirstOrDefault();
            string sub = prim == null ? "모니터 없음" : (prim.Primary ? "주 모니터 · " : "") + prim.W + "×" + prim.H + " · 창 " + item.Apps.Count;
            var apply = new Button { Content = "지금 적용", Padding = new Thickness(12, 6, 12, 6) };
            apply.Click += (_, _) => RequestApplyProfile(item.Id);
            var trail = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(item.Hotkey)) trail.Children.Add(Keycap(item.Hotkey));
            apply.Margin = new Thickness(item.Hotkey.Length > 0 ? 8 : 0, 0, 0, 0);
            trail.Children.Add(apply);
            if (item.Id == SelectedProfileId && ProfileUndo.Last != null)
            {
                var undo = VectorButton("undo", "마지막 적용 되돌리기", UndoLastApply);
                undo.Margin = new Thickness(6, 0, 0, 0);
                trail.Children.Add(undo);
            }
            trail.Children.Add(ProfileMenu(item));
            var row = Row(g, "\uE7F4", hexes[i++ % hexes.Length], item.Name.Length > 0 ? item.Name : "(이름 없음)", sub, trail, () => SelectProfile(item.Id));
            if (item.Id == SelectedProfileId) row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0x8A, 0xFE));
        }
        var cmds = new StackPanel { Orientation = Orientation.Horizontal };
        cmds.Children.Add(Small("추가", () => { var n = ProfileDoc.Add(Profiles); SelectedProfileId = n.Id; SaveProfiles(); ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey); Render("presets", false); }));
        Row(g, null, null, "프로필", null, cmds);
        var selected = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId);
        if (selected != null) BuildProfileEditor(selected);
        Footer("모니터와 창, 아이콘은 지금 적용을 눌렀을 때만 바뀌어요. 시험·화면 밖·CI에서는 디스플레이, 실제 창, 탐색기를 열지 않아요. 모니터를 바꾼 뒤 Windows가 최소화된 창을 다시 그리는 경우가 있어요. 작업 표시줄을 맞추려고 탐색기를 끝내지는 않아요.");
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
        var custom = new Button { Content = "맞춤 색", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !ro };
        custom.Click += (_, _) => PickDefaultColor(custom);
        Row(g, "\uE771", "FF2D55", "기본 색", NoteColors.Name(dc) + " · 새 메모는 이 색을 피해 무작위로 골라요", custom);

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
        var w = new Window
        {
            Title = "맞춤 색", Width = 460, Height = 96, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = !AppEnv.Off,
            WindowStartupLocation = AppEnv.Off ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
        };
        if (AppEnv.Off) { w.Left = AppEnv.OffsetX + 260; w.Top = AppEnv.OffsetY + 260; }
        string result = current;
        var bar = new CustomColorBar { Margin = new Thickness(14, 18, 14, 12) };
        bar.Load(current);
        bar.Commit += key => { result = key; w.Close(); };
        w.Content = bar;
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

    public string WindowAction { get; private set; } = "";
    public int WindowBlocks { get; private set; }
    bool BlockWindows()
    {
        if (WindowGate.RealAllowed) return false;
        WindowBlocks++;
        WindowAction = "blocked";
        return true;
    }
    public void RequestApplyRules()
    {
        if (BlockWindows()) return;
        try
        {
            using var live = new LiveWindowSurface();
            int n = RuleEngine.Apply(live, ConfigStore.WindowRules);
            WindowAction = "rules " + n;
        }
        catch (Exception ex) { WindowAction = ex.Message; }
    }
    public void RequestApplyWorkspace(WorkspaceSnapshot? ws)
    {
        if (BlockWindows()) return;
        if (ws == null || ws.Apps.Count == 0) { WindowAction = "empty"; return; }
        try
        {
            using var live = new LiveWindowSurface();
            var moves = WorkspacePlan.Plan(live.List(), ws.Apps, out int missing);
            foreach (var m in moves)
            {
                if (m.State == "min") live.Minimize(m.WindowId);
                else if (m.State == "max") { live.TryWorkArea("", m.WindowId, out var area); live.Maximize(m.WindowId, area); }
                else live.Place(m.WindowId, m.X, m.Y, m.W, m.H);
            }
            WindowAction = "workspace " + moves.Count + " missing " + missing;
        }
        catch (Exception ex) { WindowAction = ex.Message; }
    }


    public string CloudAction { get; private set; } = "";
    public int CloudBlocks { get; private set; }
    public void RequestCloudSync()
    {
        if (!CloudGate.RealAllowed)
        {
            CloudBlocks++;
            CloudAction = "blocked";
            return;
        }
        try
        {
            var transport = new GitCloudTransport(ConfigStore.CloudRepo, ConfigStore.CloudBranch, Environment.MachineName);
            var ws = new CloudWorkspace
            {
                ConfigFile = AppEnv.ConfigPath,
                NotesDir = AppEnv.NotesDir,
                DeskDir = System.IO.Path.Combine(AppEnv.DataDir, "desktop"),
                Device = Environment.MachineName,
                AppVersion = AppEnv.Version,
                UtcNow = DateTime.UtcNow,
                LocalNow = DateTime.Now
            };
            ProfileCloud.Attach(ws, System.IO.Path.Combine(AppEnv.DataDir, "profiles.json"));
            CloudAction = CloudEngine.Run(ws, transport, writer: !AppEnv.ReadOnly);
            if (CloudAction.Length == 0) CloudAction = "synced";
        }
        catch (Exception ex) { CloudAction = ex.Message; }
    }
    public string KakaoAction { get; private set; } = "";
    public int KakaoBlocks { get; private set; }
    public void RequestSyncKakao()
    {
        if (!KakaoGate.RealAllowed)
        {
            KakaoBlocks++;
            KakaoAction = "blocked";
            return;
        }
        try
        {
            KakaoNative.Sync(ConfigStore.Kakao());
            KakaoAction = ConfigStore.KakaoAdBlock ? "sync" : "restore";
        }
        catch (Exception ex) { KakaoAction = ex.Message; }
    }
    void KakaoSwitch(StackPanel group, string key, string title, string sub, bool on, string hex)
    {
        Row(group, "\uE8BD", hex, title, sub, Switch(on, v =>
        {
            if (!ConfigStore.SetKakaoFlag(key, v)) return;
            if (key != "AutoApply") RequestSyncKakao();
        }, !AppEnv.ReadOnly));
    }
    void PageKakao()
    {
        Footer("광고 숨김을 켠 일반 실행에서만 도우미가 시작해요. 시험·화면 밖·CI에서는 카톡 창을 찾거나 숨기거나 도우미를 실행하지 않아요.");
        var g = Group();
        KakaoSwitch(g, "AdBlock", "광고 숨김", "켜야 도우미가 시작해요.", ConfigStore.KakaoAdBlock, "FFCC00");
        KakaoSwitch(g, "Compact", "목록 간격 줄이기", "끄면 인자는 /nocompact 예요.", ConfigStore.KakaoCompact, "5AC8FA");
        KakaoSwitch(g, "Dark", "다크 오버레이", "켜면 인자는 /dark 예요.", ConfigStore.KakaoDark, "8E8E93");
        KakaoSwitch(g, "AutoApply", "시작할 때 적용", "광고 숨김이 켜져 있을 때만 일반 실행에서 시작해요.", ConfigStore.KakaoAutoApply, "34C759");
    }

    public string DesktopAction { get; private set; } = "";
    public int DesktopBlocks { get; private set; }
    bool BlockDesktop()
    {
        if (DesktopGate.RealAllowed) return false;
        DesktopBlocks++;
        DesktopAction = "blocked";
        return true;
    }
    public void RequestSaveDesktop()
    {
        if (BlockDesktop()) return;
        try
        {
            using var icons = new ExplorerIconSurface();
            string key = ScreenMap.Key(ScreenMap.Current());
            var layout = IconLibrary.SaveNew(LayoutStore.Default, icons, "빠른 저장 " + DateTime.Now.ToString("M/d HH:mm"), key, out string err);
            DesktopAction = layout == null ? err : "saved " + layout.Items.Count;
        }
        catch (Exception ex) { DesktopAction = ex.Message; }
    }
    public void RequestRestoreDesktop()
    {
        if (BlockDesktop()) return;
        try
        {
            using var icons = new ExplorerIconSurface();
            string key = ScreenMap.Key(ScreenMap.Current());
            var result = IconLibrary.Restore(LayoutStore.Default, icons, key);
            DesktopAction = result.Ok ? result.Summary() : result.Error;
        }
        catch (Exception ex) { DesktopAction = ex.Message; }
    }

    void PagePowerToys()
    {
        Footer("켠 값을 저장해도 후킹, 전역 단축키, 밝기, 모니터는 바꾸지 않아요. 테스트에서는 그 호출이 거부돼요. 코드 기본은 위험한 항목이 꺼져 있어요.");
        var g = Group();
        if (ConfigStore.PowerToys.Count == 0)
            Row(g, "\uE945", "AF52DE", "PowerToys 설정 없음", "코드 기본은 위험한 항목이 꺼져 있어요.", null);
        else
            foreach (var toy in ConfigStore.PowerToys)
            {
                string key = toy.Name;
                bool owned = key is "Zones" or "Grab" or "Palette" or "Paste" or "Peek";
                string sub = (toy.Enabled ? "켜짐" : "꺼짐") + (toy.Hotkey.Length > 0 ? " · " + toy.Hotkey : "") + (owned ? " · 값만 저장" : "");
                Row(g, "\uE945", "AF52DE", toy.Name, sub, owned ? Switch(toy.Enabled, v => ConfigStore.SetPtEnabled(key, v), !AppEnv.ReadOnly) : null);
            }
    }

    void PageGeneral()
    {
        Header("자리를 비우면");
        var idle = Group();
        Row(idle, "\uE823", "FF9500", "자동 전환", "입력 없으면 고른 모니터 프리셋 이름만 저장해요. 전환은 하지 않아요.", Switch(ConfigStore.IdleEnabled, v =>
        {
            if (!ConfigStore.SetSectionValue("General", "IdleEnabled", JsonValue.Create(v))) return;
            ConfigStore.SetIdle(v, ConfigStore.IdleMinutes, ConfigStore.IdlePreset);
        }, !AppEnv.ReadOnly));
        var minutes = new Slider { Minimum = 1, Maximum = 180, Width = 140, IsEnabled = !AppEnv.ReadOnly, IsSnapToTickEnabled = true, TickFrequency = 1, Value = ConfigStore.IdleMinutes };
        var minuteLabel = Value(((int)minutes.Value).ToString());
        minutes.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(minutes.Value);
            minuteLabel.Text = v.ToString();
            if (v == ConfigStore.IdleMinutes) return;
            if (!ConfigStore.SetSectionValue("General", "IdleMinutes", JsonValue.Create(v))) return;
            ConfigStore.SetIdle(ConfigStore.IdleEnabled, v, ConfigStore.IdlePreset);
        };
        var minuteTrail = new StackPanel { Orientation = Orientation.Horizontal };
        minuteTrail.Children.Add(minutes); minuteTrail.Children.Add(minuteLabel);
        Row(idle, "\uE823", "8E8E93", "대기 분", "1–180", minuteTrail);
        string preset = ConfigStore.IdlePreset.Length > 0 ? ConfigStore.IdlePreset : (ConfigStore.Presets.FirstOrDefault()?.Name ?? "");
        Row(idle, "\uE7F4", "4C8AFE", "실행할 프리셋", preset.Length > 0 ? preset : "없음", null);

        Header("전환 화면");
        var ov = Group();
        Row(ov, "\uE7F4", "5856D6", "로딩 화면", "모든 모니터 프리셋. 지금은 띄우지 않아요.", Switch(ConfigStore.OverlayEnabled, v =>
        {
            if (!ConfigStore.SetSectionValue("General", "OverlayEnabled", JsonValue.Create(v))) return;
            ConfigStore.SetOverlay(v, ConfigStore.OverlayOpacity);
        }, !AppEnv.ReadOnly));
        var op = new Slider { Minimum = 20, Maximum = 100, Width = 140, IsEnabled = !AppEnv.ReadOnly, Value = ConfigStore.OverlayOpacity };
        var opLabel = Value(ConfigStore.OverlayOpacity.ToString());
        op.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(op.Value);
            opLabel.Text = v.ToString();
            if (v == ConfigStore.OverlayOpacity) return;
            if (!ConfigStore.SetSectionValue("General", "OverlayOpacity", JsonValue.Create(v))) return;
            ConfigStore.SetOverlay(ConfigStore.OverlayEnabled, v);
        };
        var opTrail = new StackPanel { Orientation = Orientation.Horizontal };
        opTrail.Children.Add(op); opTrail.Children.Add(opLabel);
        Row(ov, "\uE7F4", "8E8E93", "배경 어둡기", "20–100. 지금 화면은 어둡히지 않아요.", opTrail);

        Header("시작");
        var start = Group();
        var startup = Switch(false, _ => { }, enabled: false);
        StartupSwitchEnabled = startup.IsEnabled;
        Row(start, "\uE7E8", "34C759", "Windows 시작 시 실행", "바로가기는 보여 주기만 하고 만들거나 지우지 않아요.", startup);
        var open = new Button { Content = "폴더 열기", Padding = new Thickness(12, 6, 12, 6), IsEnabled = !AppEnv.ReadOnly };
        open.Click += (_, _) => RequestOpenDataFolder();
        Row(start, "\uE8B7", "5AC8FA", "설정 폴더", AppEnv.DataDir, open);
        Row(start, "\uE9E9", "8E8E93", "애니메이션 줄이기", "이 앱만의 설정이에요. 공유 config.json에는 없어요.", Switch(AppSettings.ReduceMotion, v => AppSettings.SetReduceMotion(v)));

        Header("클라우드");
        var cloud = Group();
        Row(cloud, "\uE753", "4C8AFE", "동기화", ConfigStore.CloudEnabled ? "켜짐 · 저장권자만" : "꺼짐", Switch(ConfigStore.CloudEnabled, v =>
        {
            if (!ConfigStore.SetCloudEnabled(v)) return;
        }, !AppEnv.ReadOnly));
        Row(cloud, "\uE753", "8E8E93", "저장소", ConfigStore.CloudRepo.Length > 0 ? ConfigStore.CloudRepo : "없음", null);
        Row(cloud, "\uE768", "34C759", "지금 동기화", "일반 실행의 저장권자만 git을 실행해요. 시험에서는 막혀요.", null, RequestCloudSync);
    }

    void PageAbout()
    {
        var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 18) };
        var icon = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(22), Background = (Brush)FindResource("AccentGradient"), HorizontalAlignment = HorizontalAlignment.Center };
        icon.Child = new UniformGrid4();
        hero.Children.Add(icon);
        hero.Children.Add(new TextBlock { Text = "BongHub", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 2) });
        var ver = new TextBlock { Text = $"버전 {AppEnv.Version} · .NET 10 WPF", Foreground = (Brush)FindResource("Secondary"), HorizontalAlignment = HorizontalAlignment.Center };
        FirstSecondary ??= ver;
        hero.Children.Add(ver);
        PageHost.Children.Add(hero);
        AboutMentionsVersion = ver.Text.Contains(AppEnv.Version);
        var g = Group();
        Row(g, null, null, "데이터", "config·notes·layouts는 예전과 같은 파일이에요.", null);
        Row(g, null, null, "구성요소", "WPF, H.NotifyIcon. WinForms 없음.", null);
        string hotkeys = "새 메모 " + (ConfigStore.Notes.Hotkey.Length > 0 ? ConfigStore.Notes.Hotkey : "없음");
        foreach (var p in ConfigStore.Presets.Take(4))
            if (p.Hotkey.Length > 0) hotkeys += " · " + p.Name + " " + p.Hotkey;
        AboutMentionsHotkey = hotkeys.Contains("메모");
        Row(g, null, null, "단축키", hotkeys, null);
        Row(g, null, null, "변경", "1.0.1. 사이드바 이름을 BongHub로 맞췄어요.", null);
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
