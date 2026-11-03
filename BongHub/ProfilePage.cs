using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

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
    public FrameworkElement? ProfileCanvasHost { get; private set; }
    readonly Dictionary<int, Border> monitorFrames = new();
    double profileSceneScale = 0.1;
    public int HoveredMonitor { get; private set; }
    public string IconSaveNote { get; private set; } = "";
    public FrameworkElement? ProfileWindowList { get; private set; }
    public bool PreviewIcons { get; private set; }

    FrameworkElement VectorButton(string kind, string tip, Action click, string? keep = null, double box = 26)
    {
        var icon = ProfileMarks.Icon(kind, box >= 24 ? 14 : 11);
        var b = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = box,
            Height = box,
            ToolTip = tip,
            Tag = keep ?? "path-icon",
            Padding = new Thickness(0),
            FontFamily = (FontFamily)FindResource("UiFont"),
            Background = Brushes.Transparent
        };
        b.Click += (_, _) => click();
        // The Path has to be a real visual child of the canvas. Button.Content stays inside the template
        // until a layout pass, so a walk right after Render would miss it.
        var host = new Grid { Width = box, Height = box, Margin = new Thickness(2, 0, 0, 0) };
        host.Children.Add(icon);
        host.Children.Add(b);
        return host;
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
                var primary = live.FirstOrDefault(x => x.Primary) ?? live[0];
                int originX = live.Min(s => s.X), originY = live.Min(s => s.Y);
                int scale = primary.Scale <= 0 ? 100 : primary.Scale;
                var bin = IconMap.ToClient(left.X + 40, left.Y + 36, originX, originY, scale);
                var hub = IconMap.ToClient(left.X + 40, left.Y + 120, originX, originY, scale);
                var doc = IconMap.ToClient(primary.X + 48, primary.Y + 36, originX, originY, scale);
                profile0.Icons.Add(new ProfileIcon { Name = "휴지통", X = bin.X, Y = bin.Y, Show = true });
                profile0.Icons.Add(new ProfileIcon { Name = "BongHub", X = hub.X, Y = hub.Y, Show = true });
                profile0.Icons.Add(new ProfileIcon { Name = "문서", X = doc.X, Y = doc.Y, Show = false });
            }
            SelectedProfileId = profile0.Id;
            ProfileLayoutNote = string.Join(",", profile0.Monitors.Where(m => m.Connected).OrderBy(m => m.X).ThenBy(m => m.Y).Select(m => m.Number.ToString()));
            Render("presets", false);
        }
        finally { ScreenCatalog.Source = null; }
    }

    bool windowPickerOpen;
    string hotkeyMessage = "";
    string hotkeyCue = "";

    public void OpenProfileEditor()
    {
        if (profileEditor != null)
        {
            if (AppEnv.Off) profileEditor.Redraw();
            else profileEditor.Activate();
            profileEditor.Redraw();
            return;
        }
        profileEditor = new ProfileEditorWindow(this);
        profileEditor.Closed += (_, _) => profileEditor = null;
        if (AppEnv.Off)
        {
            profileEditor.WindowStartupLocation = WindowStartupLocation.Manual;
            profileEditor.ShowActivated = false;
            profileEditor.Left = -28000;
            profileEditor.Top = -28000;
        }
        profileEditor.Show();
    }

    public void FillProfileEditor(Panel sink)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        if (profile == null) return;
        BuildProfileEditor(profile, sink);
    }

    void BuildProfileEditor(MonitorProfile profile, Panel sink)
    {
        ProfileMismatchBanner = false;
        bool main = ReferenceEquals(sink, PageHost);
        var live = ScreenCatalog.Current();
        string summary = ProfileDiff.Summary(profile, live);
        if (summary.Length > 0)
            sink.Children.Add(new TextBlock
            {
                Text = summary,
                Margin = new Thickness(12, 0, 12, 4),
                FontSize = 12,
                Foreground = (Brush)FindResource("Secondary"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = summary
            });
        if (ProfileApplyNote.Length > 0)
            sink.Children.Add(new TextBlock { Text = ProfileApplyNote, Margin = new Thickness(12, 0, 12, 4), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), TextWrapping = TextWrapping.Wrap });
        DrawProfileCanvas(profile, sink, main);
        DrawWindowStrip(profile, sink, main);
        Header(sink, "프로필");
        DrawProfileSettings(profile, sink);
        var app = profile.Apps.FirstOrDefault(a => a.Id == selectedAppId);
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors.FirstOrDefault();
        if (app != null && mon != null)
        {
            Header(sink, "선택한 창");
            var g = Group(sink);
            DrawAppDetail(g, profile, app, mon);
        }
        else if (mon != null)
        {
            Header(sink, "선택한 모니터");
            var g = Group(sink);
            DrawMonitorDetail(g, profile, mon);
        }
        Header(sink, "아이콘");
        DrawIcons(profile, sink);
    }

    void DrawIcons(MonitorProfile profile, Panel sink)
    {
        var icons = Group(sink);
        int shown = profile.Icons.Count(i => i.Show);
        int hidden = profile.Icons.Count - shown;
        string when = profile.Icons.Count == 0
            ? "아직 이 프로필에 담긴 배치가 없어요."
            : "담긴 " + profile.Icons.Count + "개 · 보이는 " + shown + "개" + (hidden > 0 ? " · 숨김 " + hidden + "개" : "")
              + (profile.IconsSaved.Length == 0 ? "" : " · " + profile.IconsSaved);
        var save = Small("배치 저장", () => CaptureProfileIcons(profile));
        Row(icons, "\uE8A9", "34C759", "아이콘 배치", when, save, tight: true);
        if (IconSaveNote.Length > 0)
            Row(icons, "\uE8A5", "34C759", IconSaveNote, "저장한 개수와 시각이에요.", null, tight: true);
        string key = ScreenMap.Key(profile.Monitors.Where(m => m.Connected).Select(m => new MonitorSpec(m.W, m.H, m.X, m.Y, m.Primary)));
        string auto = ScreenMap.Describe(key);
        if (DesktopGate.RealAllowed)
        {
            var found = LayoutStore.Default.Load().FirstOrDefault(a => a.Auto && a.Key == key);
            auto = found == null ? auto + " · 자동 저장은 아직 없어요." : auto + " · 자동 저장이 있어요.";
        }
        else auto += " · 화면 구성마다 자동 저장은 일반 실행에서 보여요.";
        Row(icons, "\uE8A5", "5AC8FA", "이 화면 구성", auto, null, tight: true);
        Row(icons, "\uE8A5", "5AC8FA", "적용할 때 아이콘 되돌리기", "보이는 아이콘만 옮겨요.", Switch(profile.RestoreIcons, v => { profile.RestoreIcons = v; SaveProfiles(); }, !AppEnv.ReadOnly), tight: true);
        Row(icons, "\uE8A5", "34C759", "모니터맵 위 아이콘 점", "켜면 모니터맵에 작은 점으로 보여요.", Switch(PreviewIcons, v => { PreviewIcons = v; Render("presets", false); }, true), tight: true);
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
        Row(icons, "\uE8A5", "34C759", "고른 아이콘만 되돌리기", "눈 표시가 켜진 아이콘만 옮겨요.", Small("고른 것만", () => RestoreSelectedIcons(profile, picked)), tight: true);
    }

    void DrawGhosts(MonitorProfile profile, Canvas stage, ProfileScene scene)
    {
        IReadOnlyList<TrackedWindow> open;
        if (GhostWindows.Source != null) open = GhostWindows.Source();
        else if (WindowGate.RealAllowed)
        {
            var probes = LiveWindowSurface.ReadProbes();
            open = probes.Where(WindowPicker.Accept).Select(p => p.Window).Where(w => w != null).Cast<TrackedWindow>().ToList();
        }
        else return;
        foreach (var win in GhostWindows.Unregistered(open, profile.Apps))
        {
            var home = profile.Monitors.FirstOrDefault(m => win.X >= m.X && win.Y >= m.Y && win.X < m.X + m.W && win.Y < m.Y + m.H) ?? profile.Monitors.FirstOrDefault();
            if (home == null) continue;
            var homeView = ProfileSnap.ToCanvas(home.Box, scene, 8, 8);
            var chip = ProfileChrome.WindowChip(win.X - home.X, win.Y - home.Y, win.W, win.H, home.W, home.H, homeView.X, homeView.Y, homeView.W, homeView.H);
            if (chip.W < 8 || chip.H < 8) continue;
            int argb = GhostWindows.Argb(win.Process + "|" + win.Class);
            var ghost = new Grid { Width = chip.W, Height = chip.H, Opacity = 0.72, IsHitTestVisible = false, Tag = "ghost-chip", ToolTip = (win.Title.Length > 0 ? win.Title : win.Process) + "\n아직 프로필에 없음" };
            ghost.Children.Add(new Rectangle
            {
                RadiusX = 8, RadiusY = 8,
                Fill = new SolidColorBrush(Color.FromArgb((byte)((argb >> 24) & 255), (byte)((argb >> 16) & 255), (byte)((argb >> 8) & 255), (byte)(argb & 255))),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            });
            var reserved = new List<DipRect> { ProfileChrome.NumberPill(homeView.X, homeView.Y, home.Primary) };
            var slot = CanvasInk.Line(chip, reserved, chip.W < 70 ? 12 : 14, 3);
            if (slot.W >= 10)
            {
                ghost.Children.Add(new TextBlock
                {
                    Text = win.Title.Length > 0 ? win.Title : win.Process,
                    Foreground = Brushes.White,
                    FontSize = chip.W < 70 ? 10 : 11,
                    Margin = new Thickness(Math.Max(0, slot.X - chip.X), Math.Max(0, slot.Y - chip.Y), 0, 0),
                    MaxWidth = slot.W,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = "ghost-label"
                });
            }
            Canvas.SetLeft(ghost, chip.X);
            Canvas.SetTop(ghost, chip.Y);
            Canvas.SetZIndex(ghost, 15);
            stage.Children.Add(ghost);
        }
    }

    void DrawWindowStrip(MonitorProfile profile, Panel sink, bool mainList)
    {
        var stack = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        bool any = false;
        foreach (var app in profile.Apps)
        {
            any = true;
            var item = app;
            var mon = profile.Monitors.FirstOrDefault(m => m.Number == item.Monitor);
            string name = item.Name.Length > 0 ? item.Name : (item.Title.Length > 0 ? item.Title : item.Process);
            var chip = new Border
            {
                Width = 168,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 6, 6, 6),
                CornerRadius = new CornerRadius(12),
                Background = item.Id == selectedAppId ? new SolidColorBrush(Color.FromArgb(0x28, 0x4C, 0x8A, 0xFE)) : new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)),
                Cursor = Cursors.Hand
            };
            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var face = AppFace(item.Process, item.Path);
            if (face is FrameworkElement faceEl)
            {
                faceEl.Width = 16;
                faceEl.Height = 16;
                faceEl.Margin = new Thickness(0, 0, 6, 0);
                faceEl.VerticalAlignment = VerticalAlignment.Center;
            }
            titleRow.Children.Add(face);
            titleRow.Children.Add(new TextBlock
            {
                Text = (mon != null ? mon.Number + "  " : "") + (name.Length > 0 ? name : "창"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 110
            });
            inner.Children.Add(titleRow);
            var foot = new DockPanel { Margin = new Thickness(0, 3, 0, 0), LastChildFill = true };
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            tools.Children.Add(VectorButton("pencil", "수치로 고치기", () => { selectedAppId = item.Id; selectedMonitorNumber = item.Monitor; Render("presets", false); }, null, 18));
            tools.Children.Add(VectorButton("trash", "창 빼기", () => RemoveApp(profile, item), "keep:trash:" + item.Id, 18));
            DockPanel.SetDock(tools, Dock.Right);
            foot.Children.Add(tools);
            foot.Children.Add(new TextBlock
            {
                Text = item.W + "×" + item.H,
                FontSize = 11,
                Foreground = (Brush)FindResource("Secondary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetRow(foot, 1);
            inner.Children.Add(foot);
            chip.Child = inner;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is Button) return;
                selectedAppId = item.Id;
                selectedMonitorNumber = item.Monitor;
                Render("presets", false);
            };
            wrap.Children.Add(chip);
        }
        if (!any)
            stack.Children.Add(new TextBlock { Text = "담긴 창이 없어요.", Margin = new Thickness(8, 6, 8, 6), Foreground = (Brush)FindResource("Secondary"), FontSize = 12.5 });
        else stack.Children.Add(wrap);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 8, 4) };
        bar.Children.Add(Small(windowPickerOpen ? "닫기" : "창 추가", () => { windowPickerOpen = !windowPickerOpen; Render("presets", false); }));
        bar.Children.Add(Small("식별", () => RequestIdentify()));
        stack.Children.Add(bar);
        if (windowPickerOpen) stack.Children.Add(PickerBody(profile));
        var list = new Border { Style = (Style)FindResource("Group"), Child = stack, Margin = new Thickness(0, 8, 0, 0) };
        list.Tag = "keep:window-list";
        list.Focusable = true;
        if (mainList) ProfileWindowList = list;
        sink.Children.Add(list);
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

    void DrawProfileSettings(MonitorProfile profile, Panel sink)
    {
        var g = Group(sink);
        var nameBox = new TextBox { Text = profile.Name, MinWidth = 140, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground };
        nameBox.LostFocus += (_, _) => { ProfileDoc.Rename(profile, nameBox.Text); SaveProfiles(); ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey); };
        Row(g, null, null, "이름", null, Chip(nameBox), tight: true);
        var hotkeyBox = new HotkeyField();
        hotkeyBox.SetValue(profile.Hotkey);
        string cue = hotkeyCue;
        hotkeyCue = "";
        hotkeyBox.Loaded += (_, _) =>
        {
            if (cue == "ok") hotkeyBox.Confirm();
            else if (cue == "bad") hotkeyBox.Shake();
        };
        hotkeyBox.Gesture += gesture =>
        {
            if (AppEnv.ReadOnly) return;
            var taken = Profiles.Where(p => p.Id != profile.Id).Select(p => p.Hotkey);
            var (value, error) = HotkeyEdit.Apply(profile.Hotkey, gesture, taken, ConfigStore.Notes.Hotkey);
            hotkeyMessage = error;
            if (error.Length > 0)
            {
                hotkeyCue = "bad";
                Render("presets", false);
                return;
            }
            bool changed = value != profile.Hotkey;
            if (changed)
            {
                profile.Hotkey = value;
                SaveProfiles();
                ProfileHotkey.Bind(Profiles, ConfigStore.Notes.Hotkey);
                if (ProfileHotkey.LastError.Length > 0) hotkeyMessage = ProfileHotkey.LastError;
            }
            hotkeyCue = changed && value.Length > 0 && gesture is not "escape" and not "back" ? "ok" : "";
            if (hotkeyMessage.Length > 0) hotkeyCue = "bad";
            Render("presets", false);
        };
        Row(g, null, null, "단축키", null, hotkeyBox, tight: true);
        string remembered = profile.SessionSaved.Length == 0 ? "아직 남긴 배치가 없어요." : "마지막 기록 " + profile.SessionSaved + " · 창 " + profile.Session.Count + "개";
        Row(g, "\uE8A5", "5AC8FA", "떠날 때 창 배치 기억", remembered, Switch(profile.RememberWindows, v => { profile.RememberWindows = v; SaveProfiles(); }, !AppEnv.ReadOnly), tight: true);
        if (hotkeyMessage.Length > 0)
            Row(g, "\uE7BA", "FF9500", hotkeyMessage, "Esc는 취소, Backspace는 지워요.", null, tight: true);
    }

    void DrawAppDetail(StackPanel g, MonitorProfile profile, ProfileApp app, ProfileMonitor mon)
    {
        string label = app.Name.Length > 0 ? app.Name : app.Title;
        Row(g, "\uE737", "5AC8FA", label.Length > 0 ? label : "창", app.W + "×" + app.H + (app.Process.Length > 0 ? " · " + app.Process : ""), null, icon: AppIcon.Lookup(app.Process, app.Path), tight: true);
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
            var box = ProfileSnap.Resize(new ProfileBox(mon.X + nx, mon.Y + ny, nw, nh), mon.Work, "", 0, 0, 120);
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
        Row(g, null, null, "X  Y  너비  높이", null, nums, tight: true);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal };
        toggles.Children.Add(IconToggle("pin", "항상 위", "적용할 때 이 창을 맨 위에 둬요.", app.TopMost, v => { app.TopMost = v; SaveProfiles(); }, !AppEnv.ReadOnly));
        toggles.Children.Add(IconToggle("min", "최소화", "적용할 때 최소화해요.", app.State == "min", v => { app.State = v ? "min" : "normal"; SaveProfiles(); Render("presets", false); }, !AppEnv.ReadOnly));
        toggles.Children.Add(IconToggle("app", "없으면 실행", "경로가 있고 창이 없을 때만 일반 실행에서 켜요.", app.Launch, v => { app.Launch = v; SaveProfiles(); }, !AppEnv.ReadOnly && app.Path.Length > 0));
        Row(g, null, null, "창 옵션", null, toggles, tight: true);
    }

    void DrawMonitorDetail(StackPanel g, MonitorProfile profile, ProfileMonitor mon)
    {
        string orient = mon.Orientation == 0 ? "" : " · " + mon.Orientation + "°";
        string state = mon.Connected
            ? (mon.Enabled ? "적용하면 켜 둠" : "적용하면 이 화면을 꺼요") + " · " + (mon.Scale <= 0 ? 100 : mon.Scale) + "%" + orient
            : "연결 안 됨";
        UIElement? primary = mon.Connected ? Small(mon.Primary ? "주 모니터" : "주로 지정", () => { ProfileDoc.SetPrimary(profile, mon.Number); selectedMonitorNumber = mon.Number; SaveProfiles(); Render("presets", false); }) : null;
        Row(g, "\uE7F4", mon.Connected ? "4C8AFE" : "8E8E93", mon.Number + "번 · " + mon.W + "×" + mon.H, state, primary, tight: true);
        if (!mon.Connected) return;
        Row(g, "\uE7F4", "34C759", "이 모니터 켜기", mon.Primary ? "주 모니터라 이 프로필에서도 항상 켜져 있어요." : "끄면 이 프로필을 적용할 때 이 화면을 꺼요. 창 배치와는 별개예요.", Switch(mon.Enabled, v =>
        {
            if (!ProfileDoc.SetEnabled(profile, mon.Number, v)) { Render("presets", false); return; }
            SaveProfiles();
            Render("presets", false);
        }, !AppEnv.ReadOnly && !mon.Primary), tight: true);
        int bright = profile.Brightness.FirstOrDefault(b => string.Equals(b.Monitor, mon.Id, StringComparison.OrdinalIgnoreCase))?.Value ?? -1;
        bool brightOn = bright >= 0;
        Row(g, "\uE706", "FFCC00", "밝기", brightOn ? bright + "%" : "적용할 때 밝기는 그대로 둬요.", Switch(brightOn, v =>
        {
            SetBrightness(profile, mon.Id, v ? (bright >= 0 ? bright : 70) : -1);
            SaveProfiles();
            Render("presets", false);
        }, !AppEnv.ReadOnly), tight: true);
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
        Row(g, "\uE706", "FFCC00", "밝기 값", "0–100. 지금 적용할 때만 바꿔요.", trail, tight: true);
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

    FrameworkElement IconToggle(string kind, string title, string tip, bool on, Action<bool> changed, bool enabled)
    {
        var b = new Button
        {
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(0),
            ToolTip = title + "\n" + tip,
            IsEnabled = enabled,
            Background = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
            BorderBrush = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)),
            BorderThickness = new Thickness(on ? 0 : 1),
            Content = ProfileMarks.Icon(kind, 13),
            Tag = "path-icon"
        };
        b.Click += (_, _) => changed(!on);
        return b;
    }

    void DrawProfileCanvas(MonitorProfile profile, Panel sink, bool main)
    {
        double availW = main ? PageHost.ActualWidth : sink.ActualWidth;
        if (availW < 280)
        {
            double view = main ? Scroller.ViewportWidth : 0;
            availW = view > 320 ? view - 4 : (main ? 680 : 1040);
        }
        double availH = main && Scroller.ViewportHeight > 240 ? Scroller.ViewportHeight : (main ? 560 : 640);
        var (cw, fitH) = ProfileChrome.Stage(availW, availH);
        if (!main) fitH = Math.Min(420, Math.Max(fitH, 280));
        const int pad = 16;
        var boxes = profile.Monitors.Select(m => m.Box).ToList();
        var scene = ProfileSnap.Fit(boxes, cw, fitH, pad);
        if (main) profileSceneScale = scene.Scale < 0.02 || double.IsNaN(scene.Scale) ? 0.02 : scene.Scale;
        int ch = ProfileChrome.PaintedHeight(scene.Bounds.H, scene.Scale, pad);
        var stage = new Canvas { Width = cw, Height = ch, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true, UseLayoutRounding = true, Background = Brushes.Transparent };
        if (main)
        {
            ProfileStage = stage;
            profileCanvasWidth = cw;
            ProfileCanvasHost = null;
        }
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
        var monitorGhost = new Border
        {
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0x8A, 0xFE)),
            CornerRadius = new CornerRadius(12),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Opacity = Fx.Enabled ? 0 : 0.9
        };
        var guideLine = new Line { Stroke = (Brush)FindResource("Accent"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var guideLine2 = new Line { Stroke = (Brush)FindResource("Accent"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        void Reveal(UIElement el)
        {
            if (el.Visibility == Visibility.Visible) return;
            el.Visibility = Visibility.Visible;
            if (el is not UIElement u) return;
            if (!Fx.Enabled) { u.Opacity = 0.92; return; }
            u.Opacity = 0;
            u.BeginAnimation(UIElement.OpacityProperty, Fx.Anim(0, 0.92, 140));
        }
        int Vx(double x) => scene.Bounds.X + (int)Math.Round((x - scene.OriginX) / scene.Scale);
        int Vy(double y) => scene.Bounds.Y + (int)Math.Round((y - scene.OriginY) / scene.Scale);
        void ShowEdge(Line line, bool on, int x1, int y1, int x2, int y2)
        {
            if (!on) { line.Visibility = Visibility.Collapsed; return; }
            var a = ProfileSnap.ToPoint(x1, y1, scene);
            var b = ProfileSnap.ToPoint(x2, y2, scene);
            line.X1 = a.X; line.Y1 = a.Y; line.X2 = b.X; line.Y2 = b.Y;
            Reveal(line);
        }
        void ShowCue(SnapCue cue)
        {
            var c = ProfileSnap.ToCanvas(cue.Box, scene, 2, 2);
            preview.Width = Math.Max(2, c.W);
            preview.Height = Math.Max(2, c.H);
            Canvas.SetLeft(preview, c.X);
            Canvas.SetTop(preview, c.Y);
            previewLabel.Text = cue.Kind.Length > 0 ? ZoneName(cue.Kind) : "";
            Reveal(preview);
            bool used = false;
            if (cue.Left || cue.Right)
            {
                int x = cue.Left ? cue.Box.X : cue.Box.Right;
                ShowEdge(guideLine, true, x, cue.Box.Y, x, cue.Box.Bottom);
                used = true;
            }
            else guideLine.Visibility = Visibility.Collapsed;
            if (cue.Top || cue.Bottom)
            {
                int y = cue.Top ? cue.Box.Y : cue.Box.Bottom;
                ShowEdge(used ? guideLine2 : guideLine, true, cue.Box.X, y, cue.Box.Right, y);
            }
            else if (!used) guideLine.Visibility = Visibility.Collapsed;
            else guideLine2.Visibility = Visibility.Collapsed;
        }
        void ShowGuide(SnapGuide guide)
        {
            if (!guide.Active) { guideLine.Visibility = Visibility.Collapsed; guideLine2.Visibility = Visibility.Collapsed; return; }
            var a = ProfileSnap.ToPoint(guide.X1, guide.Y1, scene);
            var b = ProfileSnap.ToPoint(guide.X2, guide.Y2, scene);
            guideLine.X1 = a.X; guideLine.Y1 = a.Y; guideLine.X2 = b.X; guideLine.Y2 = b.Y;
            Reveal(guideLine);
            if (!guide.Corner) { guideLine2.Visibility = Visibility.Collapsed; return; }
            var c = ProfileSnap.ToPoint(guide.X3, guide.Y3, scene);
            var d = ProfileSnap.ToPoint(guide.X4, guide.Y4, scene);
            guideLine2.X1 = c.X; guideLine2.Y1 = c.Y; guideLine2.X2 = d.X; guideLine2.Y2 = d.Y;
            Reveal(guideLine2);
        }
        ProfileMonitor? HostAt(int x, int y)
            => profile.Monitors.FirstOrDefault(m => m.Connected && x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H)
               ?? profile.Monitors.FirstOrDefault(m => x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H)
               ?? profile.Monitors.FirstOrDefault();
        if (main) monitorFrames.Clear();
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
                Opacity = !mon.Connected ? 0.38 : mon.Enabled ? 1 : 0.4,
                Cursor = AppSettings.MonitorLocked ? Cursors.Hand : Cursors.SizeAll
            };
            if (mon.Primary)
            {
                frame.BorderBrush = (Brush)FindResource("Accent");
                frame.BorderThickness = new Thickness(selected ? 2.5 : 2);
            }
            if (!mon.Enabled)
                frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x8E, 0x8E, 0x93));
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
            var tileBody = new Grid();
            tileBody.Children.Add(badge);
            if (!mon.Enabled)
            {
                var hatch = new Canvas { IsHitTestVisible = false, Opacity = 0.55 };
                var hatchBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xC7, 0xC7, 0xCC));
                for (int i = -2; i < 8; i++)
                {
                    double o = i * 22;
                    hatch.Children.Add(new Line { X1 = o, Y1 = 0, X2 = o + box.H, Y2 = box.H, Stroke = hatchBrush, StrokeThickness = 1.2 });
                }
                tileBody.Children.Add(hatch);
                var off = ProfileMarks.Icon("power", 13);
                off.HorizontalAlignment = HorizontalAlignment.Right;
                off.VerticalAlignment = VerticalAlignment.Top;
                off.Margin = new Thickness(0, 6, 8, 0);
                tileBody.Children.Add(off);
            }
            frame.Child = tileBody;
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
                var liveSnap = ProfileSnap.SnapMonitorWithGuide(new ProfileBox(start.X + vdx, start.Y + vdy, start.W, start.H), others, reach);
                ShowGuide(liveSnap.Guide);
                var ghost = ProfileSnap.ToCanvas(liveSnap.Box, scene, 8, 8);
                monitorGhost.Width = ghost.W;
                monitorGhost.Height = ghost.H;
                Canvas.SetLeft(monitorGhost, ghost.X);
                Canvas.SetTop(monitorGhost, ghost.Y);
                Reveal(monitorGhost);
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
            if (main) monitorFrames[mon.Number] = frame;
            Canvas.SetZIndex(frame, 1);
            stage.Children.Add(frame);
            foreach (var strip in ProfileSnap.TaskbarStrips(mon))
            {
                var band = ProfileSnap.ToCanvas(strip, scene, 1, 1);
                if (band.W < 1 || band.H < 1) continue;
                var shade = new System.Windows.Shapes.Rectangle
                {
                    Width = band.W,
                    Height = band.H,
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x3A, 0x3D, 0x48)),
                    IsHitTestVisible = false,
                    Tag = "taskbar-strip"
                };
                Canvas.SetLeft(shade, band.X);
                Canvas.SetTop(shade, band.Y);
                Canvas.SetZIndex(shade, 2);
                stage.Children.Add(shade);
            }
        }
        int tileZ = 10;
        StackPanel? fallbackTools = null;
        bool drewSelected = false;
        if (AppSettings.ChipsVisible)
        foreach (var app in profile.Apps)
        {
            var home = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors[0];
            var homeView = ProfileSnap.ToCanvas(home.Box, scene, 8, 8);
            var chip = ProfileChrome.WindowChip(app.X, app.Y, app.W, app.H, home.W, home.H, homeView.X, homeView.Y, homeView.W, homeView.H);
            bool homeDetail = ProfileChrome.Detail(homeView.W, homeView.H, home.W, home.H, home.Scale, home.Orientation, home.Primary, home.Connected).Length > 0;
            var contentInset = ProfileChrome.ChipContent(chip, homeView.X, homeView.Y, homeView.W, ProfileChrome.BadgeBand(homeView.W, homeView.H, homeDetail), home.Primary);
            string shownName = app.Name.Length > 0 ? app.Name : (app.Title.Length > 0 ? app.Title : app.Process);
            string chipText = shownName.Length > 0 ? shownName : "창";
            bool countOnly = false;
            bool parked = false;
            if (chip.W < 2 || chip.H < 2) continue;
            if (!contentInset.Show)
            {
                // The real rectangle sits under the identify number. Keep a compact chip beside it so the
                // edit buttons stay on the canvas, without covering the number or the resolution line.
                var parkedBox = ProfileChrome.ParkedChip(homeView.X, homeView.Y, homeView.W, homeView.H, home.Primary);
                if (parkedBox.W < 2 || parkedBox.H < 2) continue;
                chip = parkedBox;
                contentInset = new ProfileChrome.ChipInk(true, 2, 1, 13);
                parked = true;
            }
            // A parked chip shares the number's row and does not cover the info line, so it must not dim that line.
            if (!parked && chipSpots.TryGetValue(home.Number, out var spots)) spots.Add(chip);
            bool selectedTile = app.Id == selectedAppId;
            var tile = new Border
            {
                Width = chip.W,
                Height = chip.H,
                CornerRadius = new CornerRadius(parked ? 8 : 10),
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1C, 0x1C, 0x1E)),
                BorderBrush = selectedTile ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
                BorderThickness = new Thickness(selectedTile ? 2 : 1),
                Cursor = Cursors.SizeAll,
                ToolTip = (app.Name.Length > 0 ? app.Name : app.Title) + "\n" + app.W + "×" + app.H,
                Tag = "window-chip"
            };
            Canvas.SetLeft(tile, chip.X);
            Canvas.SetTop(tile, chip.Y);
            Canvas.SetZIndex(tile, selectedTile ? 40 : 20);
            double nameFont = chip.W < 70 || chip.H < 34 ? 10 : 12;
            bool roomForSize = chip.H >= 56 && chip.W >= 72;
            double toolBox = chip.W >= 120 && chip.H >= 64 ? 16 : 12;
            double toolBand = toolBox * 2 + 8;
            // Buttons own the bottom-right. The name keeps the rest of the width, and a short chip hides the buttons.
            bool cornerRoom = chip.H >= nameFont + toolBox + 16 && chip.W >= toolBand + 48;
            double insetL = contentInset.Left > 0.5 ? contentInset.Left : 0;
            double insetT = contentInset.Top > 0.5 ? contentInset.Top : 0;
            double iconW = !countOnly && contentInset.Icon >= 10 ? Math.Min(contentInset.Icon, 14) + 6 : 0;
            double nameRoom = chip.W - 12 - insetL - iconW;
            // The button column is reserved even when the buttons sit at the bottom, so the name cannot run through them.
            bool buttonsFit = cornerRoom && nameRoom - toolBand >= 28;
            if (!buttonsFit) cornerRoom = false;
            bool showIcon = iconW > 0 && nameRoom >= 36;
            double nameMax = Math.Max(20, cornerRoom ? nameRoom - toolBand : nameRoom);
            var nameBlock = new TextBlock
            {
                Text = chipText,
                Foreground = Brushes.White,
                FontSize = nameFont,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = nameMax,
                IsHitTestVisible = false
            };
            var body = new Grid { Margin = new Thickness(4 + insetL, 2 + insetT, 2, 2) };
            var lines = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            if (showIcon)
            {
                var mark = AppFace(app.Process, app.Path);
                if (mark is FrameworkElement face)
                {
                    face.Width = Math.Min(contentInset.Icon, 14);
                    face.Height = Math.Min(contentInset.Icon, 14);
                    face.Margin = new Thickness(0, 1, 4, 0);
                    face.VerticalAlignment = VerticalAlignment.Top;
                }
                lines.Children.Add(mark);
            }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            words.Children.Add(nameBlock);
            if (roomForSize)
            {
                words.Children.Add(new TextBlock
                {
                    Text = app.W + "×" + app.H,
                    Foreground = (Brush)FindResource("Secondary"),
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = nameMax,
                    IsHitTestVisible = false
                });
            }
            lines.Children.Add(words);
            body.Children.Add(lines);
            bool showTools = cornerRoom && (parked || selectedTile || fallbackTools == null);
            var chipTools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = showTools ? Visibility.Visible : Visibility.Hidden,
                IsHitTestVisible = showTools,
                Tag = "chip-tools"
            };
            chipTools.Children.Add(VectorButton("pencil", "수치로 고치기", () => { selectedAppId = app.Id; selectedMonitorNumber = app.Monitor; Render("presets", false); }, "keep:edit:" + app.Id, toolBox));
            chipTools.Children.Add(VectorButton("trash", "창 빼기", () => RemoveApp(profile, app), "keep:trash:" + app.Id, toolBox));
            body.Children.Add(chipTools);
            tile.ClipToBounds = true;
            tile.Child = body;
            if (selectedTile) drewSelected = true;
            else if (cornerRoom) fallbackTools ??= chipTools;
            tile.MouseEnter += (_, _) =>
            {
                if (!cornerRoom) return;
                chipTools.Visibility = Visibility.Visible;
                chipTools.IsHitTestVisible = true;
                Canvas.SetZIndex(tile, 50);
            };
            tile.MouseLeave += (_, _) => { if (cornerRoom && app.Id != selectedAppId) { chipTools.Visibility = Visibility.Hidden; chipTools.IsHitTestVisible = false; } };
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
                startedKind = ProfileSnap.PreviewAt(sx, sy, startHost.Work).Kind;
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
                    var resized = ProfileSnap.Resize(appStart, host.Work, edge, vdxLive, vdyLive, 120);
                    ShowCue(new SnapCue(resized, resized.X == host.Work.X, resized.Right == host.Work.Right, resized.Y == host.Work.Y, resized.Bottom == host.Work.Bottom, ""));
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
                    var hit = ProfileSnap.PreviewAt(px, py, host.Work);
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
                    var movedBox = new ProfileBox(appStart.X + vdxLive, appStart.Y + vdyLive, appStart.W, appStart.H);
                    var siblings = profile.Apps.Where(a => a != app && a.Monitor == host.Number).Select(a => new ProfileBox(host.X + a.X, host.Y + a.Y, a.W, a.H)).ToList();
                    string zone = arm && hit.Kind.Length > 0 ? hit.Kind : "";
                    ShowCue(SnapCueMath.Window(movedBox, host.Work, siblings, 1, ProfileSnap.EdgeSlop(scene.Scale), zone, hit.Box));
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
                    var resized = ProfileSnap.Resize(appStart, host.Work, edge, vdx, vdy, 120);
                    app.Monitor = host.Number;
                    app.X = resized.X - host.X;
                    app.Y = resized.Y - host.Y;
                    app.W = resized.W;
                    app.H = resized.H;
                    app.State = "normal";
                }
                else
                {
                    var hit = ProfileSnap.PreviewAt(px, py, host.Work);
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
                        var snapped = ProfileSnap.SnapWindow(new ProfileBox(landed.X + local.X, landed.Y + local.Y, local.W, local.H), landed.Work, siblings, 1, ProfileSnap.EdgeSlop(scene.Scale));
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
        {
            var iconFrame = IconMap.Frame(profile.Monitors);
            foreach (var icon in profile.Icons)
            {
                var screen = IconMap.ToScreen(icon.X, icon.Y, iconFrame.Left, iconFrame.Top, iconFrame.Scale);
                var home = profile.Monitors.FirstOrDefault(m => screen.X >= m.X && screen.Y >= m.Y && screen.X < m.X + m.W && screen.Y < m.Y + m.H);
                if (home == null) continue;
                var homeView = ProfileSnap.ToCanvas(home.Box, scene, 8, 8);
                var dot = ProfileChrome.IconDot(screen.X, screen.Y, home.X, home.Y, home.W, home.H, homeView.X, homeView.Y, homeView.W, homeView.H);
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
        }
        DrawGhosts(profile, stage, scene);
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
                Tag = "monitor-number"
            };
            var numberRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            numberRow.Children.Add(new TextBlock
            {
                Text = mon.Number.ToString(),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                Tag = "monitor-label",
                VerticalAlignment = VerticalAlignment.Center
            });
            if (mon.Primary)
            {
                var star = ProfileMarks.Icon("star", 11);
                star.Margin = new Thickness(4, 0, 0, 0);
                star.VerticalAlignment = VerticalAlignment.Center;
                numberRow.Children.Add(star);
            }
            number.Child = numberRow;
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
        var expand = VectorButton("expand", "모니터맵을 크게", () => OpenProfileEditor(), box: 26);
        Canvas.SetLeft(expand, Math.Max(4, cw - 36));
        Canvas.SetTop(expand, 4);
        Canvas.SetZIndex(expand, 80);
        stage.Children.Add(expand);
        var card = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        var bar = new Grid { Height = 32, Margin = new Thickness(2, 0, 2, 2) };
        var pills = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pills.Children.Add(StatePill(AppSettings.ChipsVisible, "eye", "eyeoff", "창 칩", "칩 숨김", "저장된 창 칩을 모니터맵에 보여요.", "chip-visibility", () =>
        {
            AppSettings.SetChipsVisible(!AppSettings.ChipsVisible);
            Render("presets", false);
        }));
        pills.Children.Add(StatePill(AppSettings.MonitorLocked, "lock", "unlock", "위치 잠금", "잠금 해제", "켜 두면 창을 옮기는 동안 모니터가 따라 움직이지 않아요.", "monitor-lock", () =>
        {
            AppSettings.SetMonitorLocked(!AppSettings.MonitorLocked);
            Render("presets", false);
        }));
        bar.Children.Add(pills);
        DockPanel.SetDock(bar, Dock.Top);
        card.Children.Add(bar);
        stage.Margin = new Thickness(0);
        card.Children.Add(stage);
        if (main) ProfileCanvasHost = card;
        sink.Children.Add(card);
    }

    Border StatePill(bool on, string onIcon, string offIcon, string onText, string offText, string tip, string tag, Action click)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1.5),
            Tag = tag,
            ToolTip = tip,
            Background = new SolidColorBrush(on ? Color.FromRgb(0x4C, 0x8A, 0xFE) : Color.FromArgb(0x14, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x4C, 0x8A, 0xFE) : Color.FromArgb(0x66, 0xA3, 0xAB, 0xBC))
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ProfileMarks.Icon(on ? onIcon : offIcon, 14));
        row.Children.Add(new TextBlock
        {
            Text = on ? onText : offText,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = on ? Brushes.White : (Brush)FindResource("Secondary")
        });
        pill.Child = row;
        pill.MouseLeftButtonUp += (_, _) =>
        {
            if (!Fx.Enabled) { click(); return; }
            var bg = (SolidColorBrush)pill.Background;
            var next = !on;
            bg.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(next ? Color.FromRgb(0x4C, 0x8A, 0xFE) : Color.FromArgb(0x14, 255, 255, 255), TimeSpan.FromMilliseconds(160)));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            timer.Tick += (_, _) => { timer.Stop(); click(); };
            timer.Start();
        };
        return pill;
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
        if (pick.W > 0 && pick.H > 0)
        {
            var spot = AimPlace.Of(pick.X, pick.Y, pick.W, pick.H, profile.Monitors);
            app.Monitor = spot.Monitor;
            app.X = spot.X;
            app.Y = spot.Y;
            app.W = spot.W;
            app.H = spot.H;
        }
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
        var resized = ProfileSnap.Resize(new ProfileBox(mon.X + app.X, mon.Y + app.Y, app.W, app.H), mon.Work, edge, dx, dy, 120);
        app.X = resized.X - mon.X;
        app.Y = resized.Y - mon.Y;
        app.W = resized.W;
        app.H = resized.H;
        Render("presets", false);
        SyncLiveFields(app);
        return true;
    }

    public void HoverChip(int monitorNumber) => PaintMonitorHover(monitorNumber);

    public string IdentifyNote { get; private set; } = "";

    public void RequestIdentify()
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
        if (profile == null) return;
        bool real = DisplayGate.RealAllowed;
        IdentifyNote = MonitorIdentify.Plan(profile.Monitors, real);
        if (real) IdentifyFlash.Show(profile.Monitors);
    }

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
                "power" => "M8,3.4 A4.4,4.4 0 1 0 8.02,3.4 Z M7.15,1.2 H8.85 V6.4 H7.15 Z",
                "star" => "M8,1.6 L9.5,5.6 L13.8,5.8 L10.5,8.6 L11.6,12.8 L8,10.6 L4.4,12.8 L5.5,8.6 L2.2,5.8 L6.5,5.6 Z",
                "expand" => "M2,6 V2 H6 M10,2 H14 V6 M14,10 V14 H10 M6,14 H2 V10",
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
