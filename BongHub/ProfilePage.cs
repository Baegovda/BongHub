using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BongHub;

public partial class MainWindow
{
    Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    public Canvas? ProfileStage { get; private set; }
    readonly Dictionary<int, Border> monitorFrames = new();
    double profileSceneScale = 0.1;
    public int HoveredMonitor { get; private set; }
    public string IconSaveNote { get; private set; } = "";
    public FrameworkElement? ProfileWindowList { get; private set; }
    public bool PreviewIcons { get; private set; }

    Button VectorButton(string kind, string tip, Action click, string? keep = null, double box = 26)
    {
        var b = new Button
        {
            Content = ProfileMarks.Icon(kind, box >= 24 ? 14 : 11),
            Style = (Style)FindResource("IconButton"),
            Width = box,
            Height = box,
            ToolTip = tip,
            Tag = keep ?? "path-icon",
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            FontFamily = (FontFamily)FindResource("UiFont")
        };
        b.Click += (_, _) => click();
        return b;
    }

    UIElement AppFace(string? process, string? path)
    {
        var face = AppIcon.Lookup(process, path);
        if (face != null)
            return new Image { Source = face, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        return ProfileMarks.Icon("app", 15);
    }

    string shownLive = "";
    public string ProfileLayoutNote { get; private set; } = "";
    public bool ProfileMismatchBanner { get; private set; }

    void SelectProfile(string id)
    {
        SelectedProfileId = id;
        var profile = Profiles.FirstOrDefault(p => p.Id == id);
        if (profile != null)
        {
            selectedMonitorNumber = (profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors.FirstOrDefault())?.Number ?? 1;
            if (profile.Apps.All(a => a.Id != selectedAppId)) selectedAppId = profile.Apps.FirstOrDefault()?.Id ?? "";
        }
        Render("presets", false);
    }

    /// <summary>Pulls Windows' current arrangement in when the display signature changes. An empty catalog (tests, the gate shut) does nothing.</summary>
    void AdoptLiveGeometry()
    {
        var live = ScreenCatalog.Current();
        string sig = ScreenBind.Signature(live);
        if (sig.Length == 0 || sig == shownLive) return;
        shownLive = sig;
        foreach (var profile in Profiles) ScreenBind.FollowLive(profile, live);
        SaveProfiles();
        ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey);
    }

    /// <summary>Off-screen smoke paints a known desk. The catalog source is cleared before the method returns, and nothing native runs.</summary>
    public void ShowLiveLayout(IReadOnlyList<ScreenInfo> live)
    {
        EnsureProfiles();
        if (live == null || live.Count == 0 || Profiles.Count == 0) return;
        ScreenCatalog.Source = () => live;
        shownLive = "";
        try
        {
            foreach (var profile in Profiles) ScreenBind.FollowLive(profile, live);
            shownLive = ScreenBind.Signature(live);
            var profile0 = Profiles[0];
            if (profile0.Apps.Count == 0)
            {
                var home = live.FirstOrDefault(s => s.Primary) ?? live[0];
                profile0.Apps.Add(new ProfileApp
                {
                    Id = "demo-code", Process = "Code", Path = @"C:\Apps\Code.exe", Title = "BongHub", Name = "BongHub",
                    Monitor = home.Number, X = 160, Y = 120, W = 1400, H = 900, State = "normal"
                });
                var side = live.FirstOrDefault(s => s.Number != home.Number) ?? home;
                profile0.Apps.Add(new ProfileApp
                {
                    Id = "demo-note", Process = "notepad", Path = @"C:\Windows\notepad.exe", Title = "메모장", Name = "메모장",
                    Monitor = side.Number, X = 80, Y = 80, W = 720, H = 480, State = "normal"
                });
            }
            if (profile0.Icons.Count == 0)
            {
                var left = live.OrderBy(s => s.X).First();
                profile0.Icons.Add(new ProfileIcon { Name = "휴지통", X = left.X + 40, Y = left.Y + 36, Show = true });
                profile0.Icons.Add(new ProfileIcon { Name = "BongHub", X = left.X + 40, Y = left.Y + 120, Show = true });
                var mid = live.First(s => s.Number == (live.FirstOrDefault(x => x.Primary) ?? live[0]).Number);
                profile0.Icons.Add(new ProfileIcon { Name = "문서", X = mid.X + 48, Y = mid.Y + 36, Show = false });
            }
            SelectedProfileId = profile0.Id;
            ProfileLayoutNote = string.Join(",", profile0.Monitors.Where(m => m.Connected).OrderBy(m => m.X).ThenBy(m => m.Y).Select(m => m.Number.ToString()));
            Render("presets", false);
        }
        finally { ScreenCatalog.Source = null; }
    }

    bool windowPickerOpen;
    string hotkeyMessage = "";

    void BuildProfileEditor(MonitorProfile profile)
    {
        ProfileMismatchBanner = false;
        var live = ScreenCatalog.Current();
        var note = new TextBlock
        {
            Text = ProfileDiff.Summary(profile, live),
            Margin = new Thickness(16, 0, 16, 8),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("Secondary"),
            TextWrapping = TextWrapping.Wrap
        };
        PageHost.Children.Add(note);
        if (ProfileApplyNote.Length > 0)
            PageHost.Children.Add(new TextBlock { Text = ProfileApplyNote, Margin = new Thickness(16, 0, 16, 8), FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), TextWrapping = TextWrapping.Wrap });
        DrawProfileCanvas(profile);
        DrawWindowStrip(profile);
        Header("선택");
        DrawDetail(profile);
        DrawIcons(profile);
    }

    void DrawIcons(MonitorProfile profile)
    {
        var icons = Group();
        int shown = profile.Icons.Count(i => i.Show);
        int hidden = profile.Icons.Count - shown;
        string when = profile.Icons.Count == 0
            ? "아직 이 프로필에 담긴 배치가 없어요."
            : "담긴 " + profile.Icons.Count + "개 · 보이는 " + shown + "개" + (hidden > 0 ? " · 숨김 " + hidden + "개" : "")
              + (profile.IconsSaved.Length == 0 ? "" : " · " + profile.IconsSaved);
        var save = Small("배치 저장", () => CaptureProfileIcons(profile));
        Row(icons, "\uE8A9", "34C759", "아이콘 배치", when, save, wrap: true);
        if (IconSaveNote.Length > 0)
            Row(icons, "\uE8A5", "34C759", IconSaveNote, "저장한 개수와 시각이에요. 점은 모니터 그림에 바로 나와요.", null, wrap: true);
        string key = ScreenMap.Key(profile.Monitors.Where(m => m.Connected).Select(m => new MonitorSpec(m.W, m.H, m.X, m.Y, m.Primary)));
        string auto = ScreenMap.Describe(key);
        if (DesktopGate.RealAllowed)
        {
            var found = LayoutStore.Default.Load().FirstOrDefault(a => a.Auto && a.Key == key);
            auto = found == null ? auto + " · 자동 저장은 아직 없어요." : auto + " · 자동 저장이 있어요.";
        }
        else auto += " · 화면 구성마다 자동 저장은 일반 실행에서 보여요.";
        Row(icons, "\uE8A5", "5AC8FA", "이 화면 구성", auto, null, wrap: true);
        Row(icons, "\uE8A5", "5AC8FA", "적용할 때 아이콘 되돌리기", "지금 적용은 보이는 아이콘만 옮겨요.", Switch(profile.RestoreIcons, v => { profile.RestoreIcons = v; SaveProfiles(); }, !AppEnv.ReadOnly), wrap: true);
        Row(icons, "\uE8A5", "34C759", "그림 위 아이콘 점", "켜면 모니터 그림에 작은 점으로 보여요.", Switch(PreviewIcons, v => { PreviewIcons = v; Render("presets", false); }, true), wrap: true);
        if (profile.Icons.Count == 0) return;
        var scroll = new ScrollViewer { MaxHeight = 132, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var wrap = new WrapPanel { Margin = new Thickness(8, 4, 8, 8) };
        foreach (var icon in profile.Icons)
        {
            var item = icon;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
                Padding = new Thickness(8, 4, 4, 4),
                Margin = new Thickness(0, 0, 6, 6)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = item.Name.Length > 0 ? item.Name : "아이콘",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = item.Show ? 1 : 0.4,
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(VectorButton(item.Show ? "eye" : "eyeoff", item.Show ? "미리보기에서 숨기기" : "미리보기에 보이기", () =>
            {
                item.Show = !item.Show;
                SaveProfiles();
                Render("presets", false);
            }));
            chip.Child = row;
            wrap.Children.Add(chip);
        }
        scroll.Content = wrap;
        icons.Children.Add(scroll);
        var picked = profile.Icons.Where(i => i.Show).Select(i => i.Name).ToList();
        Row(icons, "\uE8A5", "34C759", "고른 아이콘만 되돌리기", "눈 표시가 켜진 아이콘만 옮겨요.", Small("고른 것만", () => RestoreSelectedIcons(profile, picked)), wrap: true);
    }

    void DrawWindowStrip(MonitorProfile profile)
    {
        var scroll = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var stack = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };
        bool any = false;
        foreach (var mon in profile.Monitors.OrderBy(m => m.Number))
        {
            var apps = profile.Apps.Where(a => a.Monitor == mon.Number).ToList();
            if (apps.Count == 0) continue;
            any = true;
            var group = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
                Padding = new Thickness(8, 6, 6, 4),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var inner = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            head.Children.Add(new Border
            {
                MinWidth = 22,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource("Accent"),
                Padding = new Thickness(6, 0, 6, 0),
                Child = new TextBlock { Text = mon.Number.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            head.Children.Add(new TextBlock
            {
                Text = mon.W + "×" + mon.H + (mon.Connected ? "" : " · 연결 안 됨"),
                FontSize = 11.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Secondary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            inner.Children.Add(head);
            foreach (var app in apps)
            {
                var item = app;
                var row = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(AppFace(item.Process, item.Path));
                string name = item.Name.Length > 0 ? item.Name : (item.Title.Length > 0 ? item.Title : item.Process);
                var label = new TextBlock
                {
                    Text = (name.Length > 0 ? name : "창") + "  " + item.W + "×" + item.H,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                tools.Children.Add(VectorButton("pencil", "수치로 고치기", () => { selectedAppId = item.Id; selectedMonitorNumber = item.Monitor; Render("presets", false); }));
                tools.Children.Add(VectorButton("trash", "창 빼기", () => RemoveApp(profile, item), "keep:trash:" + item.Id));
                Grid.SetColumn(tools, 2);
                row.Children.Add(tools);
                row.MouseLeftButtonUp += (_, _) => { selectedAppId = item.Id; selectedMonitorNumber = item.Monitor; Render("presets", false); };
                if (item.Id == selectedAppId) row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0x8A, 0xFE));
                inner.Children.Add(row);
            }
            group.Child = inner;
            stack.Children.Add(group);
        }
        if (!any)
            stack.Children.Add(new TextBlock { Text = "담긴 창이 없어요.", Margin = new Thickness(8, 6, 8, 6), Foreground = (Brush)FindResource("Secondary"), FontSize = 12.5 });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 6) };
        bar.Children.Add(Small(windowPickerOpen ? "닫기" : "창 추가", () => { windowPickerOpen = !windowPickerOpen; Render("presets", false); }));
        stack.Children.Add(bar);
        if (windowPickerOpen) stack.Children.Add(PickerBody(profile));
        scroll.Content = stack;
        var list = new Border { Style = (Style)FindResource("Group"), Child = scroll, Margin = new Thickness(0, 8, 0, 0) };
        list.Tag = "keep:window-list";
        list.Focusable = true;
        ProfileWindowList = list;
        PageHost.Children.Add(list);
    }

    UIElement PickerBody(MonitorProfile profile)
    {
        var g = new StackPanel { Margin = new Thickness(4, 0, 4, 6) };
        Button running = null!;
        running = Small("목록", () => ShowRunningMenu(profile, running));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 0, 4) };
        line.Children.Add(running);
        line.Children.Add(Small("조준", () => BeginAim(profile)));
        g.Children.Add(line);
        var proc = PromptBox("notepad.exe");
        var title = PromptBox("메모장");
        var cls = PromptBox("Notepad");
        var fields = new WrapPanel();
        fields.Children.Add(Labeled("실행 파일", proc.Field));
        fields.Children.Add(Labeled("창 제목", title.Field));
        fields.Children.Add(Labeled("창 클래스", cls.Field));
        g.Children.Add(fields);
        g.Children.Add(Small("담기", () => AddPicked(profile, new WindowPick { Process = proc.Box.Text.Trim(), Title = title.Box.Text.Trim(), Class = cls.Box.Text.Trim(), W = 800, H = 600 })));
        return g;
    }

    StackPanel Labeled(string caption, UIElement field)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
        s.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = (Brush)FindResource("Secondary"), Margin = new Thickness(2, 0, 0, 3) });
        s.Children.Add(field);
        return s;
    }

    void DrawDetail(MonitorProfile profile)
    {
        var g = Group();
        var nameBox = new TextBox { Text = profile.Name, MinWidth = 160, FontSize = 15, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground };
        nameBox.LostFocus += (_, _) => { ProfileDoc.Rename(profile, nameBox.Text); SaveProfiles(); ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey); };
        FieldRow(g, "이름", Chip(nameBox));
        var hotkeyBox = new TextBox
        {
            Text = profile.Hotkey.Length > 0 ? profile.Hotkey : "키를 누르세요",
            MinWidth = 140,
            FontSize = 14,
            IsReadOnly = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Foreground,
            CaretBrush = Foreground
        };
        hotkeyBox.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (AppEnv.ReadOnly) return;
            string gesture;
            if (e.Key == Key.Escape) gesture = "escape";
            else if (e.Key == Key.Back) gesture = "back";
            else if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System) return;
            else gesture = FormatHotkey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
            if (gesture.Length == 0) return;
            var taken = Profiles.Where(p => p.Id != profile.Id).Select(p => p.Hotkey);
            var (value, error) = HotkeyEdit.Apply(profile.Hotkey, gesture, taken, ConfigStore.Notes.Hotkey);
            hotkeyMessage = error;
            if (error.Length == 0 && value != profile.Hotkey)
            {
                profile.Hotkey = value;
                SaveProfiles();
                ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey);
                if (ProfileHotkey.LastError.Length > 0) hotkeyMessage = ProfileHotkey.LastError;
            }
            hotkeyBox.Text = profile.Hotkey.Length > 0 ? profile.Hotkey : "키를 누르세요";
            Render("presets", false);
        };
        FieldRow(g, "단축키", Chip(hotkeyBox));
        string remembered = profile.SessionSaved.Length == 0 ? "아직 남긴 배치가 없어요." : "마지막 기록 " + profile.SessionSaved + " · 창 " + profile.Session.Count + "개";
        Row(g, "\uE8A5", "5AC8FA", "떠날 때 창 배치 기억", "다른 프로필로 바꾸기 직전에 열린 창을 남기고, 돌아오면 그 자리로 돌려요. 캔버스에 담은 창은 이 기록이 없을 때만 써요. " + remembered, Switch(profile.RememberWindows, v => { profile.RememberWindows = v; SaveProfiles(); }, !AppEnv.ReadOnly), wrap: true);
        Row(g, "\uE7F8", "8E8E93", "모니터 위치 잠금", "켜 두면 창을 옮기는 동안 모니터가 따라 움직이지 않아요.", Switch(AppSettings.MonitorLocked, v => { AppSettings.SetMonitorLocked(v); Render("presets", false); }, true), wrap: true);
        if (hotkeyMessage.Length > 0)
            Row(g, "\uE7BA", "FF9500", hotkeyMessage, "Esc는 취소, Backspace는 지워요.", null, wrap: true);
        var app = profile.Apps.FirstOrDefault(a => a.Id == selectedAppId);
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors.FirstOrDefault();
        if (app != null && mon != null) DrawAppDetail(g, profile, app, mon);
        else if (mon != null) DrawMonitorDetail(g, profile, mon);
    }

    void DrawAppDetail(StackPanel g, MonitorProfile profile, ProfileApp app, ProfileMonitor mon)
    {
        string label = app.Name.Length > 0 ? app.Name : app.Title;
        Row(g, "\uE737", "5AC8FA", label.Length > 0 ? label : "창", app.W + "×" + app.H + (app.Process.Length > 0 ? " · " + app.Process : ""), null, wrap: true, icon: AppIcon.Lookup(app.Process, app.Path));
        var x = NumBox(app.X.ToString());
        var y = NumBox(app.Y.ToString());
        var w = NumBox(app.W.ToString());
        var h = NumBox(app.H.ToString());
        liveX = x; liveY = y; liveW = w; liveH = h; liveAppId = app.Id;
        x.Tag = "keep:x:" + app.Id;
        y.Tag = "keep:y:" + app.Id;
        w.Tag = "keep:w:" + app.Id;
        h.Tag = "keep:h:" + app.Id;
        void Commit()
        {
            int nx = ParseNum(x.Text, app.X), ny = ParseNum(y.Text, app.Y), nw = ParseNum(w.Text, app.W), nh = ParseNum(h.Text, app.H);
            var box = ProfileSnap.Resize(new ProfileBox(mon.X + nx, mon.Y + ny, nw, nh), mon.Box, "", 0, 0, 120);
            app.X = box.X - mon.X;
            app.Y = box.Y - mon.Y;
            app.W = box.W;
            app.H = box.H;
            app.State = "normal";
            SaveProfiles();
            Render("presets", false);
        }
        x.LostFocus += (_, _) => Commit();
        y.LostFocus += (_, _) => Commit();
        w.LostFocus += (_, _) => Commit();
        h.LostFocus += (_, _) => Commit();
        var nums = new StackPanel { Orientation = Orientation.Horizontal };
        nums.Children.Add(Chip(x));
        nums.Children.Add(Chip(y));
        nums.Children.Add(Chip(w));
        nums.Children.Add(Chip(h));
        FieldRow(g, "X  Y  너비  높이", nums);
        Row(g, "\uE718", "FF9500", "항상 위", "적용할 때 이 창을 맨 위에 둬요.", Switch(app.TopMost, v => { app.TopMost = v; SaveProfiles(); }, !AppEnv.ReadOnly), wrap: true);
        Row(g, "\uE921", "8E8E93", "최소화", "적용할 때 최소화해요.", Switch(app.State == "min", v => { app.State = v ? "min" : "normal"; SaveProfiles(); Render("presets", false); }, !AppEnv.ReadOnly), wrap: true);
        Row(g, "\uE768", "34C759", "없으면 실행", "경로가 있고 창이 없을 때만 일반 실행에서 켜요.", Switch(app.Launch, v => { app.Launch = v; SaveProfiles(); }, !AppEnv.ReadOnly && app.Path.Length > 0), wrap: true);
    }

    void DrawMonitorDetail(StackPanel g, MonitorProfile profile, ProfileMonitor mon)
    {
        string orient = mon.Orientation == 0 ? "" : " · " + mon.Orientation + "°";
        string state = mon.Connected
            ? (mon.Enabled ? "적용하면 켜 둠" : "적용하면 이 화면을 꺼요") + " · " + (mon.Scale <= 0 ? 100 : mon.Scale) + "%" + orient
            : "연결 안 됨";
        UIElement? primary = mon.Connected ? Small(mon.Primary ? "주 모니터" : "주로 지정", () => { ProfileDoc.SetPrimary(profile, mon.Number); selectedMonitorNumber = mon.Number; SaveProfiles(); Render("presets", false); }) : null;
        Row(g, "\uE7F4", mon.Connected ? "4C8AFE" : "8E8E93", mon.Number + "번 · " + mon.W + "×" + mon.H, state, primary, wrap: true);
        if (!mon.Connected) return;
        Row(g, "\uE7F4", "34C759", "이 모니터 켜기", mon.Primary ? "주 모니터라 이 프로필에서도 항상 켜져 있어요." : "끄면 이 프로필을 적용할 때 이 화면을 꺼요. 창 배치와는 별개예요.", Switch(mon.Enabled, v =>
        {
            if (!ProfileDoc.SetEnabled(profile, mon.Number, v)) { Render("presets", false); return; }
            SaveProfiles();
            Render("presets", false);
        }, !AppEnv.ReadOnly && !mon.Primary), wrap: true);
        int bright = profile.Brightness.FirstOrDefault(b => string.Equals(b.Monitor, mon.Id, StringComparison.OrdinalIgnoreCase))?.Value ?? -1;
        bool brightOn = bright >= 0;
        Row(g, "\uE706", "FFCC00", "밝기", brightOn ? bright + "%" : "적용할 때 밝기는 그대로 둬요.", Switch(brightOn, v =>
        {
            SetBrightness(profile, mon.Id, v ? (bright >= 0 ? bright : 70) : -1);
            SaveProfiles();
            Render("presets", false);
        }, !AppEnv.ReadOnly), wrap: true);
        if (!brightOn) return;
        var label = Value(bright + "%");
        label.Margin = new Thickness(8, 0, 0, 0);
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 160, Value = bright, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        int guard = 0;
        slider.ValueChanged += (_, _) =>
        {
            if (guard > 0) return;
            int v = (int)Math.Round(slider.Value);
            label.Text = v + "%";
            SetBrightness(profile, mon.Id, v);
            SaveProfiles();
        };
        var trail = new StackPanel { Orientation = Orientation.Horizontal };
        trail.Children.Add(slider);
        trail.Children.Add(label);
        Row(g, "\uE706", "FFCC00", "밝기 값", "0–100. 지금 적용할 때만 바꿔요.", trail, wrap: true);
    }

    static void SetBrightness(MonitorProfile profile, string id, int value)
    {
        profile.Brightness.RemoveAll(b => string.Equals(b.Monitor, id, StringComparison.OrdinalIgnoreCase));
        if (value >= 0) profile.Brightness.Add(new BrightTarget { Monitor = id, Value = Math.Clamp(value, 0, 100) });
    }

    static int ParseNum(string text, int fallback) => int.TryParse((text ?? "").Trim(), out int n) ? n : fallback;

    TextBox NumBox(string text) => new()
    {
        Text = text,
        Width = 64,
        FontSize = 13,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Foreground,
        HorizontalContentAlignment = HorizontalAlignment.Right
    };

    Border Chip(UIElement child) => new()
    {
        CornerRadius = new CornerRadius(8),
        BorderBrush = (Brush)FindResource("Separator"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 4, 8, 4),
        Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
        Child = child,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    void FieldRow(StackPanel group, string label, UIElement editor)
        => Row(group, null, null, label, null, editor);

    (Border Field, TextBox Box) PromptBox(string placeholder)
    {
        var box = new TextBox { MinWidth = 140, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground, VerticalContentAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = placeholder, Opacity = 0.4, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Foreground = (Brush)FindResource("Secondary") };
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid { MinWidth = 140 };
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return (Chip(grid), box);
    }

    Button ProfileMenu(MonitorProfile item)
    {
        var b = new Button
        {
            Content = ProfileMarks.Icon("ellipsis", 15),
            Style = (Style)FindResource("IconButton"),
            Width = 28,
            Height = 28,
            ToolTip = "복제, 삭제, 가져오기, 보내기",
            Tag = "path-icon",
            Margin = new Thickness(6, 0, 0, 0),
            FontFamily = (FontFamily)FindResource("UiFont")
        };
        var menu = new ContextMenu { PlacementTarget = b };
        void Add(string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => click();
            menu.Items.Add(mi);
        }
        Add("복제", () => { var n = ProfileDoc.Duplicate(Profiles, item.Id); if (n != null) { SelectedProfileId = n.Id; SaveProfiles(); ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey); Render("presets", false); } });
        Add("삭제", () => { if (ProfileDoc.Delete(Profiles, item.Id)) { SelectedProfileId = Profiles.FirstOrDefault()?.Id ?? ""; selectedAppId = ""; SaveProfiles(); ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey); Render("presets", false); } });
        Add("보내기", () => ExportProfile(item));
        Add("가져오기", () => ImportProfile());
        b.Click += (_, _) => menu.IsOpen = true;
        return b;
    }

    void ExportProfile(MonitorProfile profile)
    {
        if (AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest) { DisplayAction = "blocked"; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = (profile.Name.Length > 0 ? profile.Name : "profile") + ".json" };
        if (dlg.ShowDialog() == true)
            try { File.WriteAllText(dlg.FileName, ProfileDoc.ExportOne(profile)); } catch (Exception ex) { DisplayAction = ex.Message; }
    }

    void ImportProfile()
    {
        if (AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest) { DisplayAction = "blocked"; return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var imported = ProfileDoc.ImportOne(File.ReadAllText(dlg.FileName));
            if (imported == null) { DisplayAction = "프로필을 읽지 못했어요."; return; }
            imported.Id = ProfileDoc.NewId();
            if (Profiles.Any(p => p.Name == imported.Name)) imported.Name += " 가져옴";
            Profiles.Add(imported);
            SelectedProfileId = imported.Id;
            SaveProfiles();
            ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey);
            Render("presets", false);
        }
        catch (Exception ex) { DisplayAction = ex.Message; }
    }

    void DrawProfileCanvas(MonitorProfile profile)
    {
        double availW = PageHost.ActualWidth;
        if (availW < 280)
        {
            double view = Scroller.ViewportWidth;
            availW = view > 320 ? view - 4 : 680;
        }
        double availH = Scroller.ViewportHeight > 240 ? Scroller.ViewportHeight : 560;
        var (cw, fitH) = ProfileChrome.Stage(availW, availH);
        const int pad = 16;
        var boxes = profile.Monitors.Select(m => m.Box).ToList();
        var scene = ProfileSnap.Fit(boxes, cw, fitH, pad);
        profileSceneScale = scene.Scale < 0.02 || double.IsNaN(scene.Scale) ? 0.02 : scene.Scale;
        int ch = ProfileChrome.PaintedHeight(scene.Bounds.H, scene.Scale, pad);
        var stage = new Canvas { Width = cw, Height = ch, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true, UseLayoutRounding = true, Background = Brushes.Transparent };
        ProfileStage = stage;
        var preview = new Border
        {
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x4C, 0x8A, 0xFE)),
            CornerRadius = new CornerRadius(12),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        var previewLabel = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 0, 0), IsHitTestVisible = false };
        preview.Child = previewLabel;
        var guideLine = new Line { Stroke = (Brush)FindResource("Accent"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var guideLine2 = new Line { Stroke = (Brush)FindResource("Accent"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        int Vx(double x) => scene.Bounds.X + (int)Math.Round((x - scene.OriginX) / scene.Scale);
        int Vy(double y) => scene.Bounds.Y + (int)Math.Round((y - scene.OriginY) / scene.Scale);
        void ShowGuide(SnapGuide guide)
        {
            if (!guide.Active) { guideLine.Visibility = Visibility.Collapsed; guideLine2.Visibility = Visibility.Collapsed; return; }
            var a = ProfileSnap.ToPoint(guide.X1, guide.Y1, scene);
            var b = ProfileSnap.ToPoint(guide.X2, guide.Y2, scene);
            guideLine.X1 = a.X; guideLine.Y1 = a.Y; guideLine.X2 = b.X; guideLine.Y2 = b.Y;
            guideLine.Visibility = Visibility.Visible;
            if (!guide.Corner) { guideLine2.Visibility = Visibility.Collapsed; return; }
            var c = ProfileSnap.ToPoint(guide.X3, guide.Y3, scene);
            var d = ProfileSnap.ToPoint(guide.X4, guide.Y4, scene);
            guideLine2.X1 = c.X; guideLine2.Y1 = c.Y; guideLine2.X2 = d.X; guideLine2.Y2 = d.Y;
            guideLine2.Visibility = Visibility.Visible;
        }
        ProfileMonitor? HostAt(int x, int y)
            => profile.Monitors.FirstOrDefault(m => m.Connected && x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H)
               ?? profile.Monitors.FirstOrDefault(m => x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H)
               ?? profile.Monitors.FirstOrDefault();
        monitorFrames.Clear();
        var chipSpots = new Dictionary<int, List<DipRect>>();
        var badges = new List<(FrameworkElement El, DipRect Box)>();
        foreach (var mon in profile.Monitors)
        {
            var box = ProfileSnap.ToCanvas(mon.Box, scene, 8, 8);
            bool selected = mon.Number == selectedMonitorNumber;
            var frame = new Border
            {
                Width = box.W,
                Height = box.H,
                CornerRadius = new CornerRadius(Math.Clamp(box.H / 5.0, 8, 16)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = (Brush)FindResource(mon.Connected ? "Accent" : "Separator"),
                Background = new SolidColorBrush(Color.FromArgb(mon.Connected ? (byte)36 : (byte)18, 0x4C, 0x8A, 0xFE)),
                Opacity = !mon.Connected ? 0.38 : mon.Enabled ? 1 : 0.55,
                Cursor = AppSettings.MonitorLocked ? Cursors.Hand : Cursors.SizeAll
            };
            Canvas.SetLeft(frame, box.X);
            Canvas.SetTop(frame, box.Y);
            string detail = ProfileChrome.Detail(box.W, box.H, mon.W, mon.H, mon.Scale, mon.Orientation, mon.Primary, mon.Connected);
            var badge = new StackPanel { IsHitTestVisible = false, Margin = new Thickness(8, 28, 8, 0) };
            if (detail.Length > 0)
                badge.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Opacity = 0.85,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Tag = "monitor-label"
                });
            frame.Child = badge;
            badges.Add((badge, new DipRect(box.X, box.Y, box.W, Math.Min(box.H, ProfileChrome.BadgeBand(box.W, box.H, detail.Length > 0)))));
            chipSpots[mon.Number] = new List<DipRect>();
            var start = mon.Box;
            var origin = new Point(box.X, box.Y);
            Point grab = default;
            frame.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is Button) return;
                selectedMonitorNumber = mon.Number;
                selectedAppId = "";
                if (AppSettings.MonitorLocked)
                {
                    Render("presets", false);
                    e.Handled = true;
                    return;
                }
                frame.CaptureMouse();
                grab = e.GetPosition(stage);
                origin = new Point(Canvas.GetLeft(frame), Canvas.GetTop(frame));
                e.Handled = true;
            };
            frame.MouseMove += (_, e) =>
            {
                if (!frame.IsMouseCaptured) return;
                var p = e.GetPosition(stage);
                Canvas.SetLeft(frame, origin.X + p.X - grab.X);
                Canvas.SetTop(frame, origin.Y + p.Y - grab.Y);
                int vdx = (int)Math.Round((p.X - grab.X) / scene.Scale);
                int vdy = (int)Math.Round((p.Y - grab.Y) / scene.Scale);
                var others = profile.Monitors.Where(m => m != mon).Select(m => m.Box).ToList();
                int reach = Math.Max(48, (int)Math.Round(16 / Math.Max(0.02, scene.Scale)));
                ShowGuide(ProfileSnap.SnapMonitorWithGuide(new ProfileBox(start.X + vdx, start.Y + vdy, start.W, start.H), others, reach).Guide);
            };
            frame.MouseLeftButtonUp += (_, e) =>
            {
                if (!frame.IsMouseCaptured) return;
                frame.ReleaseMouseCapture();
                var p = e.GetPosition(stage);
                int vdx = (int)Math.Round((p.X - grab.X) / scene.Scale);
                int vdy = (int)Math.Round((p.Y - grab.Y) / scene.Scale);
                var others = profile.Monitors.Where(m => m != mon).Select(m => m.Box).ToList();
                int reach = Math.Max(48, (int)Math.Round(16 / Math.Max(0.02, scene.Scale)));
                var snapped = ProfileSnap.SnapMonitor(new ProfileBox(start.X + vdx, start.Y + vdy, start.W, start.H), others, reach);
                mon.X = snapped.X;
                mon.Y = snapped.Y;
                SaveProfiles();
                Render("presets", false);
            };
            frame.Tag = "monitor-tile";
            monitorFrames[mon.Number] = frame;
            Canvas.SetZIndex(frame, 1);
            stage.Children.Add(frame);
        }
        int tileZ = 10;
        StackPanel? fallbackTools = null;
        bool drewSelected = false;
        foreach (var app in profile.Apps)
        {
            var home = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors[0];
            var homeView = ProfileSnap.ToCanvas(home.Box, scene, 8, 8);
            var chip = ProfileChrome.WindowChip(app.X, app.Y, app.W, app.H, home.W, home.H, homeView.X, homeView.Y, homeView.W, homeView.H);
            bool homeDetail = ProfileChrome.Detail(homeView.W, homeView.H, home.W, home.H, home.Scale, home.Orientation, home.Primary, home.Connected).Length > 0;
            var contentInset = ProfileChrome.ChipContent(chip, homeView.X, homeView.Y, homeView.W, ProfileChrome.BadgeBand(homeView.W, homeView.H, homeDetail));
            string shownName = app.Name.Length > 0 ? app.Name : (app.Title.Length > 0 ? app.Title : app.Process);
            string chipText = shownName.Length > 0 ? shownName : "창";
            bool countOnly = false;
            if (chip.W < 2 || chip.H < 2 || !contentInset.Show) continue;
            if (chipSpots.TryGetValue(home.Number, out var spots)) spots.Add(chip);
            bool selectedTile = app.Id == selectedAppId;
            var tile = new Border
            {
                Width = chip.W,
                Height = chip.H,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E)),
                BorderBrush = selectedTile ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
                BorderThickness = new Thickness(selectedTile ? 2 : 1),
                Cursor = Cursors.SizeAll,
                ToolTip = (app.Name.Length > 0 ? app.Name : app.Title) + "\n" + app.W + "×" + app.H,
                Tag = "window-chip"
            };
            Canvas.SetLeft(tile, chip.X);
            Canvas.SetTop(tile, chip.Y);
            Canvas.SetZIndex(tile, selectedTile ? 40 : 20);
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            if (!countOnly && contentInset.Icon >= 10)
            {
                var mark = AppFace(app.Process, app.Path);
                if (mark is FrameworkElement face)
                {
                    face.Width = contentInset.Icon;
                    face.Height = contentInset.Icon;
                    face.VerticalAlignment = VerticalAlignment.Center;
                }
                head.Children.Add(mark);
            }
            var nameBlock = new TextBlock
            {
                Text = chipText,
                Foreground = Brushes.White,
                FontSize = chip.W < 72 ? 11 : 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(nameBlock, 1);
            head.Children.Add(nameBlock);
            var body = new Grid { Margin = new Thickness(6 + contentInset.Left, 3 + contentInset.Top, 4, 2) };
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            lines.Children.Add(head);
            bool roomForSize = chip.H >= 48 && chip.W >= 72;
            if (roomForSize)
            {
                var size = new TextBlock
                {
                    Text = app.W + "×" + app.H,
                    Foreground = (Brush)FindResource("Secondary"),
                    FontSize = 11,
                    Margin = new Thickness(chip.W < 96 ? 0 : 20, 1, 2, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    IsHitTestVisible = false
                };
                lines.Children.Add(size);
            }
            body.Children.Add(lines);
            var chipTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = selectedTile ? Visibility.Visible : Visibility.Hidden, IsHitTestVisible = selectedTile, Tag = "chip-tools" };
            chipTools.Children.Add(VectorButton("pencil", "수치로 고치기", () => { selectedAppId = app.Id; selectedMonitorNumber = app.Monitor; Render("presets", false); }, "keep:edit:" + app.Id, 20));
            chipTools.Children.Add(VectorButton("trash", "창 빼기", () => RemoveApp(profile, app), "keep:trash:" + app.Id, 20));
            body.Children.Add(chipTools);
            tile.ClipToBounds = true;
            tile.Child = body;
            if (selectedTile) drewSelected = true;
            else fallbackTools ??= chipTools;
            tile.MouseEnter += (_, _) => { chipTools.Visibility = Visibility.Visible; chipTools.IsHitTestVisible = true; Canvas.SetZIndex(tile, 50); };
            tile.MouseLeave += (_, _) => { if (app.Id != selectedAppId) { chipTools.Visibility = Visibility.Hidden; chipTools.IsHitTestVisible = false; } };
            double chipX = chip.X, chipY = chip.Y;
            var appStart = new ProfileBox(home.X + app.X, home.Y + app.Y, app.W, app.H);
            string edge = "";
            string startedKind = "";
            bool leftZone = false;
            Point grab = default;
            tile.MouseMove += (_, e) =>
            {
                if (tile.IsMouseCaptured) return;
                var local = e.GetPosition(tile);
                tile.Cursor = CursorFor(EdgeAt(local, tile.ActualWidth, tile.ActualHeight));
            };
            tile.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is Button) return;
                selectedAppId = app.Id;
                selectedMonitorNumber = app.Monitor;
                Canvas.SetZIndex(tile, ++tileZ);
                tile.CaptureMouse();
                grab = e.GetPosition(tile);
                edge = EdgeAt(grab, tile.ActualWidth, tile.ActualHeight);
                int sx = Vx(chipX + grab.X), sy = Vy(chipY + grab.Y);
                var startHost = HostAt(sx, sy) ?? home;
                startedKind = ProfileSnap.PreviewAt(sx, sy, startHost.Box).Kind;
                leftZone = startedKind.Length == 0;
                tile.Cursor = CursorFor(edge);
                e.Handled = true;
            };
            tile.MouseMove += (_, e) =>
            {
                if (!tile.IsMouseCaptured) return;
                var p = e.GetPosition(stage);
                int px = Vx(p.X), py = Vy(p.Y);
                var host = HostAt(px, py);
                if (host == null) return;
                PaintMonitorHover(host.Number);
                int vdxLive = (int)Math.Round((p.X - (chipX + grab.X)) / scene.Scale);
                int vdyLive = (int)Math.Round((p.Y - (chipY + grab.Y)) / scene.Scale);
                bool movedFar = Math.Abs(p.X - (chipX + grab.X)) + Math.Abs(p.Y - (chipY + grab.Y)) > 6;
                if (edge.Length > 0)
                {
                    preview.Visibility = Visibility.Collapsed;
                    var resized = ProfileSnap.Resize(appStart, host.Box, edge, vdxLive, vdyLive, 120);
                    var hostView = ProfileSnap.ToCanvas(host.Box, scene, 8, 8);
                    var view = ProfileChrome.WindowChip(resized.X - host.X, resized.Y - host.Y, resized.W, resized.H, host.W, host.H, hostView.X, hostView.Y, hostView.W, hostView.H);
                    if (view.W >= 2 && view.H >= 2)
                    {
                        tile.Width = view.W;
                        tile.Height = view.H;
                        Canvas.SetLeft(tile, view.X);
                        Canvas.SetTop(tile, view.Y);
                    }
                    app.X = resized.X - host.X;
                    app.Y = resized.Y - host.Y;
                    app.W = resized.W;
                    app.H = resized.H;
                    SyncLiveFields(app);
                }
                else
                {
                    var hit = ProfileSnap.PreviewAt(px, py, host.Box);
                    if (startedKind.Length > 0 && hit.Kind != startedKind) leftZone = true;
                    bool arm = ProfileSnap.ArmZone(leftZone ? "" : startedKind, hit.Kind, movedFar);
                    if (arm)
                    {
                        var c = ProfileSnap.ToCanvas(hit.Box, scene);
                        preview.Width = c.W;
                        preview.Height = c.H;
                        Canvas.SetLeft(preview, c.X);
                        Canvas.SetTop(preview, c.Y);
                        previewLabel.Text = ZoneName(hit.Kind);
                        preview.Visibility = Visibility.Visible;
                    }
                    else preview.Visibility = Visibility.Collapsed;
                    Canvas.SetLeft(tile, p.X - grab.X);
                    Canvas.SetTop(tile, p.Y - grab.Y);
                }
            };
            tile.MouseLeftButtonUp += (_, e) =>
            {
                if (!tile.IsMouseCaptured) return;
                tile.ReleaseMouseCapture();
                var p = e.GetPosition(stage);
                int px = Vx(p.X), py = Vy(p.Y);
                var host = HostAt(px, py) ?? home;
                int vdx = (int)Math.Round((p.X - (chipX + grab.X)) / scene.Scale);
                int vdy = (int)Math.Round((p.Y - (chipY + grab.Y)) / scene.Scale);
                if (edge.Length > 0)
                {
                    var resized = ProfileSnap.Resize(appStart, host.Box, edge, vdx, vdy, 120);
                    app.Monitor = host.Number;
                    app.X = resized.X - host.X;
                    app.Y = resized.Y - host.Y;
                    app.W = resized.W;
                    app.H = resized.H;
                    app.State = "normal";
                }
                else
                {
                    var hit = ProfileSnap.PreviewAt(px, py, host.Box);
                    bool movedFar = Math.Abs(p.X - (chipX + grab.X)) + Math.Abs(p.Y - (chipY + grab.Y)) > 6;
                    if (startedKind.Length > 0 && hit.Kind != startedKind) leftZone = true;
                    bool arm = ProfileSnap.ArmZone(leftZone ? "" : startedKind, hit.Kind, movedFar);
                    if (arm && hit.Kind.Length > 0)
                    {
                        app.Monitor = host.Number;
                        app.State = hit.Kind;
                        app.X = hit.Box.X - host.X;
                        app.Y = hit.Box.Y - host.Y;
                        app.W = hit.Box.W;
                        app.H = hit.Box.H;
                    }
                    else
                    {
                        var moved = new ProfileBox(appStart.X + vdx, appStart.Y + vdy, appStart.W, appStart.H);
                        var (index, local) = ProfileSnap.Transfer(moved, profile.Monitors.Select(m => m.Box).ToList());
                        var landed = profile.Monitors[index];
                        var siblings = profile.Apps.Where(a => a != app && a.Monitor == landed.Number).Select(a => new ProfileBox(landed.X + a.X, landed.Y + a.Y, a.W, a.H)).ToList();
                        var snapped = ProfileSnap.SnapWindow(new ProfileBox(landed.X + local.X, landed.Y + local.Y, local.W, local.H), landed.Box, siblings, 1, ProfileSnap.EdgeSlop(scene.Scale));
                        app.Monitor = landed.Number;
                        app.X = snapped.X - landed.X;
                        app.Y = snapped.Y - landed.Y;
                        app.W = snapped.W;
                        app.H = snapped.H;
                        app.State = "normal";
                    }
                }
                SaveProfiles();
                Render("presets", false);
            };
            stage.Children.Add(tile);
        }
        if (!drewSelected && fallbackTools != null)
        {
            fallbackTools.Visibility = Visibility.Visible;
            fallbackTools.IsHitTestVisible = true;
        }
        if (PreviewIcons)
            foreach (var icon in profile.Icons)
            {
                var home = profile.Monitors.FirstOrDefault(m => icon.X >= m.X && icon.Y >= m.Y && icon.X < m.X + m.W && icon.Y < m.Y + m.H);
                if (home == null) continue;
                var homeView = ProfileSnap.ToCanvas(home.Box, scene, 8, 8);
                var dot = ProfileChrome.IconDot(icon.X, icon.Y, home.X, home.Y, home.W, home.H, homeView.X, homeView.Y, homeView.W, homeView.H);
                if (dot.W < 1) continue;
                var mark = new Ellipse
                {
                    Width = dot.W,
                    Height = dot.H,
                    Fill = new SolidColorBrush(Color.FromArgb(icon.Show ? (byte)0xB0 : (byte)0x55, 0x5A, 0xC8, 0x7A)),
                    IsHitTestVisible = false,
                    ToolTip = (icon.Name.Length > 0 ? icon.Name : "아이콘") + (icon.Show ? "" : " · 숨김"),
                    Tag = "icon-dot"
                };
                Canvas.SetLeft(mark, dot.X);
                Canvas.SetTop(mark, dot.Y);
                Canvas.SetZIndex(mark, 3);
                stage.Children.Add(mark);
            }
        foreach (var badge in badges)
        {
            bool covered = false;
            foreach (var spots in chipSpots.Values)
                if (spots.Any(s => s.Hits(badge.Box))) { covered = true; break; }
            badge.El.Opacity = covered ? 0.28 : 1;
        }
        foreach (var mon in profile.Monitors)
        {
            var box = ProfileSnap.ToCanvas(mon.Box, scene, 8, 8);
            var number = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1C, 0x1C, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                IsHitTestVisible = false,
                Tag = "monitor-number",
                Child = new TextBlock
                {
                    Text = mon.Number.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    IsHitTestVisible = false,
                    Tag = "monitor-label"
                }
            };
            Canvas.SetLeft(number, box.X + 6);
            Canvas.SetTop(number, box.Y + 6);
            Canvas.SetZIndex(number, 75);
            stage.Children.Add(number);
        }
        Canvas.SetZIndex(guideLine, 90);
        Canvas.SetZIndex(guideLine2, 90);
        Canvas.SetZIndex(preview, 5);
        stage.Children.Add(guideLine);
        stage.Children.Add(guideLine2);
        stage.Children.Add(preview);
        var card = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        var bar = new Grid { Height = 34, Margin = new Thickness(2, 0, 2, 4) };
        var lockPill = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 3, 10, 3),
            ToolTip = AppSettings.MonitorLocked ? "모니터 위치가 잠겨 있어요" : "모니터 위치를 잠글 수 있어요",
            Tag = "monitor-lock"
        };
        var lockRow = new StackPanel { Orientation = Orientation.Horizontal };
        lockRow.Children.Add(ProfileMarks.Icon(AppSettings.MonitorLocked ? "lock" : "unlock", 14));
        lockRow.Children.Add(new TextBlock
        {
            Text = "위치 잠금",
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            Foreground = (Brush)FindResource(AppSettings.MonitorLocked ? "Text" : "Secondary")
        });
        lockPill.Content = lockRow;
        lockPill.Click += (_, _) =>
        {
            AppSettings.SetMonitorLocked(!AppSettings.MonitorLocked);
            Render("presets", false);
        };
        bar.Children.Add(lockPill);
        DockPanel.SetDock(bar, Dock.Top);
        card.Children.Add(bar);
        stage.Margin = new Thickness(0);
        card.Children.Add(stage);
        PageHost.Children.Add(card);
    }

    static string ZoneName(string kind) => kind switch
    {
        "left" => "왼쪽 절반",
        "right" => "오른쪽 절반",
        "top" => "최대",
        "tl" => "왼쪽 위 1/4",
        "tr" => "오른쪽 위 1/4",
        "bl" => "왼쪽 아래 1/4",
        "br" => "오른쪽 아래 1/4",
        _ => ""
    };

    static Cursor CursorFor(string edge) => edge switch
    {
        "n" or "s" => Cursors.SizeNS,
        "e" or "w" => Cursors.SizeWE,
        "nw" or "se" => Cursors.SizeNWSE,
        "ne" or "sw" => Cursors.SizeNESW,
        _ => Cursors.SizeAll
    };

    static string EdgeAt(Point p, double w, double h)
    {
        const double band = 8;
        bool l = p.X <= band, r = p.X >= w - band, t = p.Y <= band, b = p.Y >= h - band;
        if (l && t) return "nw";
        if (r && t) return "ne";
        if (l && b) return "sw";
        if (r && b) return "se";
        if (l) return "w";
        if (r) return "e";
        if (t) return "n";
        if (b) return "s";
        return "";
    }

    void RemoveApp(MonitorProfile profile, ProfileApp app)
    {
        profile.Apps.Remove(app);
        if (selectedAppId == app.Id) selectedAppId = "";
        SaveProfiles();
        Render("presets", false);
    }

    void AddPicked(MonitorProfile profile, WindowPick pick)
    {
        if (pick.Process.Length == 0 && pick.Title.Length == 0 && pick.Class.Length == 0) return;
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.First();
        var app = new ProfileApp
        {
            Id = ProfileDoc.NewId(),
            Process = pick.Process,
            Path = pick.Path,
            Title = pick.Title,
            Class = pick.Class,
            Name = pick.Title.Length > 0 ? pick.Title : pick.Process,
            Monitor = mon.Number,
            X = 48,
            Y = 48,
            W = Math.Clamp(pick.W <= 0 ? 800 : pick.W, 200, Math.Max(200, mon.W)),
            H = Math.Clamp(pick.H <= 0 ? 600 : pick.H, 120, Math.Max(120, mon.H))
        };
        profile.Apps.Add(app);
        selectedAppId = app.Id;
        windowPickerOpen = false;
        SaveProfiles();
        Render("presets", false);
    }

    void BeginAim(MonitorProfile profile)
    {
        if (!WindowGate.RealAllowed)
        {
            RequestCaptureWindow();
            if (LastPick != null) AddPicked(profile, LastPick);
            return;
        }
        var aim = new AimOverlay();
        aim.Chosen += (x, y) =>
        {
            try
            {
                var hit = WindowPicker.Choose(LiveWindowSurface.ReadProbes(), x, y);
                if (hit == null) { hotkeyMessage = ""; DisplayAction = "그 자리에는 담을 창이 없어요."; return; }
                AddPicked(profile, WindowPicker.From(hit));
            }
            catch (Exception ex) { DisplayAction = ex.Message; }
        };
        aim.Show();
    }

    void ShowRunningMenu(MonitorProfile profile, FrameworkElement owner)
    {
        var menu = new ContextMenu { PlacementTarget = owner };
        foreach (var pick in ListCaptureCandidates())
        {
            var captured = pick;
            var item = new MenuItem { Header = (captured.Title.Length > 0 ? captured.Title : captured.Process) + " · " + captured.W + "×" + captured.H };
            var face = AppIcon.Lookup(captured.Process, captured.Path);
            if (face != null) item.Icon = new Image { Source = face, Width = 16, Height = 16 };
            item.Click += (_, _) => AddPicked(profile, captured);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "열린 창 없음", IsEnabled = false });
        menu.IsOpen = true;
    }

    public void RequestCaptureProfileIcons()
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        if (profile != null) CaptureProfileIcons(profile);
    }

    void CaptureProfileIcons(MonitorProfile profile)
    {
        if (DesktopGate.RealAllowed)
        {
            try
            {
                using var icons = new ExplorerIconSurface();
                StoreIcons(profile, icons.Read(), auto: true);
            }
            catch (Exception ex) { DesktopAction = ex.Message; IconSaveNote = ex.Message; Render("presets", false); }
            return;
        }
        if (IconPreview.Source != null)
        {
            StoreIcons(profile, IconPreview.Source(), auto: false);
            return;
        }
        DesktopBlocks++;
        DesktopAction = "blocked";
        IconSaveNote = "바탕화면을 읽지 않았어요.";
        Render("presets", false);
    }

    void StoreIcons(MonitorProfile profile, IReadOnlyList<DeskIcon> current, bool auto)
    {
        profile.Icons = ProfileDoc.CaptureIcons(current);
        foreach (var icon in profile.Icons) icon.Show = true;
        profile.IconsSaved = DateTime.Now.ToString("M월 d일 HH:mm");
        PreviewIcons = true;
        IconSaveNote = profile.Icons.Count + "개를 저장했어요 · " + profile.IconsSaved;
        if (profile.Icons.Count > 0) profile.DeskLayout = "";
        if (auto)
        {
            var key = ScreenMap.Key(profile.Monitors.Where(m => m.Connected).Select(m => new MonitorSpec(m.W, m.H, m.X, m.Y, m.Primary)));
            try { IconLibrary.SaveAuto(LayoutStore.Default, current, key); } catch { }
        }
        SaveProfiles();
        Render("presets", false);
    }

    TextBox? liveX, liveY, liveW, liveH;
    string liveAppId = "";

    void SyncLiveFields(ProfileApp app)
    {
        if (liveAppId != app.Id) return;
        if (liveX != null) liveX.Text = app.X.ToString();
        if (liveY != null) liveY.Text = app.Y.ToString();
        if (liveW != null) liveW.Text = app.W.ToString();
        if (liveH != null) liveH.Text = app.H.ToString();
    }

    void PaintMonitorHover(int number)
    {
        HoveredMonitor = number;
        foreach (var pair in monitorFrames)
        {
            bool hot = pair.Key == number;
            pair.Value.BorderThickness = new Thickness(hot ? 3 : pair.Key == selectedMonitorNumber ? 2 : 1);
            pair.Value.Tag = hot ? "monitor-hot" : "monitor-tile";
            if (hot) pair.Value.Background = new SolidColorBrush(Color.FromArgb(0x77, 0x4C, 0x8A, 0xFE));
        }
    }

    public bool NudgeMonitor(int number, int dx, int dy)
    {
        if (AppSettings.MonitorLocked) return false;
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        var mon = profile?.Monitors.FirstOrDefault(m => m.Number == number);
        if (profile == null || mon == null) return false;
        double scale = profileSceneScale < 0.02 ? 0.02 : profileSceneScale;
        int vdx = (int)Math.Round(dx / scale);
        int vdy = (int)Math.Round(dy / scale);
        int reach = Math.Max(48, (int)Math.Round(16 / scale));
        var others = profile.Monitors.Where(m => m != mon).Select(m => m.Box).ToList();
        var snapped = ProfileSnap.SnapMonitor(new ProfileBox(mon.X + vdx, mon.Y + vdy, mon.W, mon.H), others, reach);
        mon.X = snapped.X;
        mon.Y = snapped.Y;
        SaveProfiles();
        Render("presets", false);
        return true;
    }

    public bool ResizeChipLive(string appId, string edge, int dx, int dy)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        var app = profile?.Apps.FirstOrDefault(a => a.Id == appId);
        var mon = profile?.Monitors.FirstOrDefault(m => m.Number == app?.Monitor);
        if (profile == null || app == null || mon == null) return false;
        selectedAppId = app.Id;
        var resized = ProfileSnap.Resize(new ProfileBox(mon.X + app.X, mon.Y + app.Y, app.W, app.H), mon.Box, edge, dx, dy, 120);
        app.X = resized.X - mon.X;
        app.Y = resized.Y - mon.Y;
        app.W = resized.W;
        app.H = resized.H;
        Render("presets", false);
        SyncLiveFields(app);
        return true;
    }

    public void HoverChip(int monitorNumber) => PaintMonitorHover(monitorNumber);

    public bool RemoveShownApp()
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        var app = profile?.Apps.FirstOrDefault();
        if (profile == null || app == null) return false;
        RemoveApp(profile, app);
        return true;
    }

    void RestoreSelectedIcons(MonitorProfile profile, IReadOnlyList<string> names)
    {
        if (!DesktopGate.RealAllowed)
        {
            DesktopBlocks++;
            DesktopAction = "blocked";
            return;
        }
        try
        {
            using var icons = new ExplorerIconSurface();
            var result = ProfileDoc.RestoreIcons(profile.Icons, icons, names);
            DesktopAction = result.Summary();
        }
        catch (Exception ex) { DesktopAction = ex.Message; }
    }
}

/// <summary>Full-screen crosshair. Created only after the window gate is open, so tests never construct it.</summary>
sealed class AimOverlay : Window
{
    public event Action<int, int>? Chosen;
    public AimOverlay()
    {
        ThemeMark.SetOn(this, true);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = Math.Max(1, SystemParameters.VirtualScreenWidth);
        Height = Math.Max(1, SystemParameters.VirtualScreenHeight);
        Content = new TextBlock
        {
            Text = "창 위에서 클릭 · Esc 취소",
            Foreground = Brushes.White,
            FontSize = 14,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        MouseLeftButtonDown += (_, _) =>
        {
            var p = Win32.Cursor();
            Chosen?.Invoke(p.X, p.Y);
            Close();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}

/// <summary>Vector symbols for the profile page. The icon font is not used, so a missing Segoe glyph cannot draw a blank box.</summary>
static class ProfileMarks
{
    static readonly Dictionary<string, Geometry> Cache = new();

    public static Viewbox Icon(string kind, double size)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = GeometryOf(kind),
            Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA)),
            Stretch = Stretch.Uniform
        };
        return new Viewbox { Width = size, Height = size, Child = path, IsHitTestVisible = false };
    }

    static Geometry GeometryOf(string kind)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        Geometry geo = kind switch
        {
            "ellipsis" => Dots(),
            "app" => WindowGlyph(),
            "eye" or "eyeoff" => Eye(kind == "eyeoff"),
            _ => Geometry.Parse(kind switch
            {
                "trash" => "M6,1.4 H10 L10.7,3.1 H14.2 V4.6 H1.8 V3.1 H5.3 Z M3.5,6 H12.5 L11.7,14.6 H4.3 Z",
                "pencil" => "M11.4,1.6 L14.4,4.6 L5.4,13.6 L2,14.2 L2.6,10.8 Z",
                "lock" => "M5.1,7.2 H10.9 V4.7 C10.9,3.1 9.6,2 8,2 C6.4,2 5.1,3.1 5.1,4.7 Z M3.2,7 H12.8 V14.6 H3.2 Z",
                "unlock" => "M5.1,7.2 H10.9 V4.2 H12.4 V2.6 H9.4 V4.7 C9.4,3.1 8.2,2 6.8,2 C5.2,2 4,3.2 4,4.8 V7.2 H3.2 V14.6 H12.8 V7.2 Z",
                "pin" => "M8,1.4 C5.8,1.4 4.6,3 4.6,4.6 V8 L2.6,10.6 H7.2 V14.6 H8.8 V10.6 H13.4 L11.4,8 V4.6 C11.4,3 10.2,1.4 8,1.4 Z",
                "min" => "M2.4,7.1 H13.6 V8.9 H2.4 Z",
                "undo" => "M3.2,7.2 L7.4,3 L7.4,5.4 L12.6,5.4 L12.6,9.2 L7.4,9.2 L7.4,11.6 Z",
                _ => "M2,2 H14 V14 H2 Z"
            })
        };
        geo.Freeze();
        Cache[kind] = geo;
        return geo;
    }

    static Geometry Dots()
    {
        var g = new GeometryGroup();
        g.Children.Add(new EllipseGeometry(new Point(3.2, 8), 1.35, 1.35));
        g.Children.Add(new EllipseGeometry(new Point(8, 8), 1.35, 1.35));
        g.Children.Add(new EllipseGeometry(new Point(12.8, 8), 1.35, 1.35));
        return g;
    }

    static Geometry WindowGlyph()
    {
        var g = new GeometryGroup();
        g.Children.Add(new RectangleGeometry(new Rect(1.2, 2.4, 13.6, 11.2), 1.6, 1.6));
        g.Children.Add(new RectangleGeometry(new Rect(1.2, 2.4, 13.6, 2.4)));
        return g;
    }

    static Geometry Eye(bool slash)
    {
        var g = new GeometryGroup();
        g.Children.Add(Geometry.Parse("M1,8 C3.6,4.4 5.8,3.2 8,3.2 C10.2,3.2 12.4,4.4 15,8 C12.4,11.6 10.2,12.8 8,12.8 C5.8,12.8 3.6,11.6 1,8 Z"));
        g.Children.Add(new EllipseGeometry(new Point(8, 8), 1.7, 1.7));
        if (slash) g.Children.Add(Geometry.Parse("M2.2,3.2 L3.8,1.8 L13.8,12.6 L12.2,14 Z"));
        return g;
    }
}
