using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BongHub;

public partial class MainWindow
{
    Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

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

    void BuildProfileEditor(MonitorProfile profile)
    {
        Header("이 프로필");
        var nameBox = new TextBox { Text = profile.Name, MinWidth = 180, FontSize = 16, FontWeight = FontWeights.SemiBold, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground, VerticalContentAlignment = VerticalAlignment.Center };
        var hotkeyBox = new TextBox { Text = profile.Hotkey, MinWidth = 120, FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground, VerticalContentAlignment = VerticalAlignment.Center };
        nameBox.LostFocus += (_, _) => { ProfileDoc.Rename(profile, nameBox.Text); SaveProfiles(); };
        hotkeyBox.LostFocus += (_, _) => { profile.Hotkey = hotkeyBox.Text.Trim(); SaveProfiles(); };
        var named = Group();
        FieldRow(named, "이름", Chip(nameBox));
        FieldRow(named, "단축키", Chip(hotkeyBox));

        Header("모니터 배치");
        DrawProfileCanvas(profile);

        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.FirstOrDefault();
        if (mon != null)
        {
            var monTools = new StackPanel { Orientation = Orientation.Horizontal };
            monTools.Children.Add(Small("주 모니터", () => { ProfileDoc.SetPrimary(profile, mon.Number); selectedMonitorNumber = mon.Number; SaveProfiles(); Render("presets", false); }));
            monTools.Children.Add(Small("모니터 추가", () => { AddMonitor(profile); SaveProfiles(); Render("presets", false); }));
            if (profile.Monitors.Count > 1)
                monTools.Children.Add(Small("이 모니터 삭제", () => { RemoveMonitor(profile, mon); SaveProfiles(); Render("presets", false); }));
            var monGroup = Group();
            Row(monGroup, "\uE7F4", mon.Primary ? "FF9500" : "4C8AFE", mon.Number + "번 · " + mon.W + "×" + mon.H, mon.Enabled ? "켜짐" : "꺼짐", monTools);
            Row(monGroup, "\uE7F4", "34C759", "이 모니터 사용", "주 모니터와 마지막 모니터는 끄지 않아요.", Switch(mon.Enabled, v =>
            {
                if (!ProfileDoc.SetEnabled(profile, mon.Number, v)) return;
                SaveProfiles();
                Render("presets", false);
            }, !AppEnv.ReadOnly));
        }

        Header("창");
        DrawWindowList(profile);
        Header("창 담기");
        var winGroup = Group();
        Button running = null!;
        running = Small("목록", () => ShowRunningMenu(profile, running));
        Row(winGroup, "\uE737", "5AC8FA", "실행 중인 창", "고르면 이 모니터에 담아요. 시험에서는 가짜 창만 나와요.", running);
        Row(winGroup, "\uE8B3", "4C8AFE", "조준", "십자선으로 창을 집어요.", Small("조준", () => { RequestCaptureWindow(); if (LastPick != null) AddPicked(profile, LastPick); }));
        var proc = PromptBox("notepad.exe");
        var title = PromptBox("제목 없음");
        var cls = PromptBox("Notepad");
        FieldRow(winGroup, "실행 파일", proc.Field);
        FieldRow(winGroup, "창 제목", title.Field);
        FieldRow(winGroup, "창 클래스", cls.Field);
        Row(winGroup, "\uE710", "34C759", "직접 담기", "위 세 칸으로 창을 추가해요.", Small("담기", () => AddPicked(profile, new WindowPick { Process = proc.Box.Text.Trim(), Title = title.Box.Text.Trim(), Class = cls.Box.Text.Trim(), W = 800, H = 600 })));

        Header("바탕화면 아이콘");
        var iconGroup = Group();
        string iconSub = profile.Icons.Count == 0 ? "아직 이 프로필에 담긴 아이콘이 없어요." : profile.Icons.Count + "개 · 지금 적용할 때 되돌려요.";
        Row(iconGroup, "\uE8A9", "34C759", "이 프로필의 배치", iconSub, Small("이 화면에 담기", () => CaptureProfileIcons(profile)));

        Header("아이콘을 언제 기억할까요");
        var pol = Group();
        DeskSwitch(pol, "\uE7F4", "FF9500", "적용 직전에 기억", "프로필을 바꾸기 전에 현재 아이콘 위치만 저장해요.", "SaveBeforeSwitch", ConfigStore.DeskSaveBeforeSwitch);
        DeskSwitch(pol, "\uE7F8", "AF52DE", "화면이 바뀌면 되돌리기", "저장된 배치가 있을 때만 아이콘을 옮겨요.", "RestoreOnChange", ConfigStore.DeskRestoreOnChange);
    }

    Border Chip(UIElement child) => new()
    {
        CornerRadius = new CornerRadius(8),
        BorderBrush = (Brush)FindResource("Separator"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 6, 10, 6),
        Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
        Child = child,
        MinWidth = 180,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    void FieldRow(StackPanel group, string label, UIElement editor)
        => Row(group, null, null, label, null, editor);

    (Border Field, TextBox Box) PromptBox(string placeholder)
    {
        var box = new TextBox { MinWidth = 180, FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground, VerticalContentAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = placeholder, Opacity = 0.4, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Foreground = (Brush)FindResource("Secondary") };
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid { MinWidth = 180 };
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return (Chip(grid), box);
    }

    void DrawWindowList(MonitorProfile profile)
    {
        var g = Group();
        if (profile.Apps.Count == 0)
            Row(g, "\uE737", "8E8E93", "담긴 창 없음", "모니터 안에서 끌어 놓고, 가장자리에서 크기를 바꿔요.", null);
        foreach (var app in profile.Apps.ToList())
        {
            var item = app;
            string sub = (item.Process.Length > 0 ? item.Process + " · " : "") + item.W + "×" + item.H + (item.State != "normal" ? " · " + item.State : "");
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(Small("삭제", () => { profile.Apps.Remove(item); if (selectedAppId == item.Id) selectedAppId = ""; SaveProfiles(); Render("presets", false); }));
            var row = Row(g, "\uE737", "5AC8FA", item.Name.Length > 0 ? item.Name : item.Title, sub, tools, () => { selectedAppId = item.Id; selectedMonitorNumber = item.Monitor; Render("presets", false); });
            if (item.Id == selectedAppId) row.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x5A, 0xC8, 0xFA));
        }
    }

    void DrawProfileCanvas(MonitorProfile profile)
    {
        const int cw = 560, pad = 16;
        var boxes = profile.Monitors.Select(m => m.Box).ToList();
        int ch = ProfileSnap.ContentHeight(boxes, cw, pad);
        var scene = ProfileSnap.Fit(boxes, cw, ch, pad);
        var stage = new Canvas { Width = cw, Height = ch, Margin = new Thickness(16, 0, 16, 4), VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true, UseLayoutRounding = true };
        var preview = new Border
        {
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0x8A, 0xFE)),
            CornerRadius = new CornerRadius(10),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        int Vx(double x) => scene.Bounds.X + (int)Math.Round((x - scene.OriginX) / scene.Scale);
        int Vy(double y) => scene.Bounds.Y + (int)Math.Round((y - scene.OriginY) / scene.Scale);
        ProfileMonitor? HostAt(int x, int y)
        {
            return profile.Monitors.FirstOrDefault(m => x >= m.X && y >= m.Y && x < m.X + m.W && y < m.Y + m.H) ?? profile.Monitors.FirstOrDefault();
        }
        foreach (var mon in profile.Monitors)
        {
            var box = ProfileSnap.ToCanvas(mon.Box, scene);
            var frame = new Border
            {
                Width = box.W,
                Height = box.H,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(mon.Number == selectedMonitorNumber ? 2 : 1),
                BorderBrush = (Brush)FindResource("Accent"),
                Background = new SolidColorBrush(Color.FromArgb(mon.Enabled ? (byte)40 : (byte)16, 0x4C, 0x8A, 0xFE)),
                Opacity = mon.Enabled ? 1 : 0.55,
                Cursor = Cursors.SizeAll
            };
            Canvas.SetLeft(frame, box.X);
            Canvas.SetTop(frame, box.Y);
            frame.Child = new TextBlock
            {
                Text = mon.Number + (mon.Primary ? " · 주" : "") + "\n" + mon.W + "×" + mon.H,
                Margin = new Thickness(10, 8, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Secondary"),
                IsHitTestVisible = false
            };
            var start = mon.Box;
            var origin = new Point(box.X, box.Y);
            Point grab = default;
            frame.MouseLeftButtonDown += (_, e) =>
            {
                selectedMonitorNumber = mon.Number;
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
            };
            frame.MouseLeftButtonUp += (_, e) =>
            {
                if (!frame.IsMouseCaptured) return;
                frame.ReleaseMouseCapture();
                var p = e.GetPosition(stage);
                int vdx = (int)Math.Round((p.X - grab.X) / scene.Scale);
                int vdy = (int)Math.Round((p.Y - grab.Y) / scene.Scale);
                var others = profile.Monitors.Where(m => m != mon).Select(m => m.Box).ToList();
                var snapped = ProfileSnap.SnapMonitor(new ProfileBox(start.X + vdx, start.Y + vdy, start.W, start.H), others, 48);
                mon.X = snapped.X;
                mon.Y = snapped.Y;
                SaveProfiles();
                Render("presets", false);
            };
            stage.Children.Add(frame);
        }
        foreach (var app in profile.Apps)
        {
            var home = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors[0];
            var abs = new ProfileBox(home.X + app.X, home.Y + app.Y, app.W, app.H);
            var view = ProfileSnap.ToCanvas(abs, scene);
            var tile = new Border
            {
                Width = Math.Max(72, view.W),
                Height = Math.Max(44, view.H),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("Card"),
                BorderBrush = app.Id == selectedAppId ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
                BorderThickness = new Thickness(app.Id == selectedAppId ? 2 : 1),
                Cursor = Cursors.SizeAll,
                Tag = app
            };
            Canvas.SetLeft(tile, view.X);
            Canvas.SetTop(tile, view.Y);
            var body = new StackPanel { Margin = new Thickness(8, 4, 8, 4), IsHitTestVisible = false };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = "\uE737", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 14, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(new TextBlock { Text = app.Name.Length > 0 ? app.Name : app.Title, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = app.W + "×" + app.H, Foreground = (Brush)FindResource("Secondary"), FontSize = 11 });
            tile.Child = body;
            var appStart = abs;
            string edge = "";
            Point grab = default;
            tile.MouseLeftButtonDown += (_, e) =>
            {
                selectedAppId = app.Id;
                selectedMonitorNumber = app.Monitor;
                tile.CaptureMouse();
                grab = e.GetPosition(tile);
                edge = EdgeAt(grab, tile.ActualWidth, tile.ActualHeight);
                tile.Cursor = edge.Length == 0 ? Cursors.SizeAll : Cursors.SizeNWSE;
                e.Handled = true;
            };
            tile.MouseMove += (_, e) =>
            {
                if (!tile.IsMouseCaptured) return;
                var p = e.GetPosition(stage);
                int px = Vx(p.X), py = Vy(p.Y);
                var host = HostAt(px, py);
                if (host == null) return;
                if (edge.Length == 0)
                {
                    var hit = ProfileSnap.PreviewAt(px, py, host.Box);
                    if (hit.Kind.Length > 0)
                    {
                        var c = ProfileSnap.ToCanvas(hit.Box, scene);
                        preview.Width = c.W;
                        preview.Height = c.H;
                        Canvas.SetLeft(preview, c.X);
                        Canvas.SetTop(preview, c.Y);
                        preview.Visibility = Visibility.Visible;
                        Canvas.SetZIndex(preview, 2);
                    }
                    else preview.Visibility = Visibility.Collapsed;
                }
                Canvas.SetLeft(tile, p.X - grab.X);
                Canvas.SetTop(tile, p.Y - grab.Y);
            };
            tile.MouseLeftButtonUp += (_, e) =>
            {
                if (!tile.IsMouseCaptured) return;
                tile.ReleaseMouseCapture();
                var p = e.GetPosition(stage);
                int px = Vx(p.X), py = Vy(p.Y);
                var host = HostAt(px, py) ?? home;
                int vdx = (int)Math.Round((p.X - (view.X + grab.X)) / scene.Scale);
                int vdy = (int)Math.Round((p.Y - (view.Y + grab.Y)) / scene.Scale);
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
                    if (hit.Kind.Length > 0)
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
                        var snapped = ProfileSnap.SnapWindow(new ProfileBox(landed.X + local.X, landed.Y + local.Y, local.W, local.H), landed.Box, siblings, ProfileSnap.Grid);
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
            Canvas.SetZIndex(tile, 1);
            stage.Children.Add(tile);
        }
        Canvas.SetZIndex(preview, 3);
        stage.Children.Add(preview);
        PageHost.Children.Add(stage);
    }

    static string EdgeAt(Point p, double w, double h)
    {
        bool l = p.X <= 10, r = p.X >= w - 10, t = p.Y <= 10, b = p.Y >= h - 10;
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

    void AddMonitor(MonitorProfile profile)
    {
        int x = profile.Monitors.Count == 0 ? 0 : profile.Monitors.Max(m => m.X + m.W);
        int n = profile.Monitors.Count == 0 ? 1 : profile.Monitors.Max(m => m.Number) + 1;
        profile.Monitors.Add(new ProfileMonitor { Id = "M" + n, Number = n, X = x, Y = 0, W = 1920, H = 1080, Enabled = true, Primary = profile.Monitors.Count == 0 });
        ProfileDoc.SyncLists(profile);
        selectedMonitorNumber = n;
    }

    void RemoveMonitor(MonitorProfile profile, ProfileMonitor mon)
    {
        if (profile.Monitors.Count <= 1) return;
        profile.Monitors.Remove(mon);
        foreach (var app in profile.Apps.Where(a => a.Monitor == mon.Number))
            app.Monitor = profile.Monitors[0].Number;
        if (!profile.Monitors.Any(m => m.Primary)) profile.Monitors[0].Primary = true;
        ProfileDoc.SyncLists(profile);
        selectedMonitorNumber = profile.Monitors[0].Number;
    }

    void AddPicked(MonitorProfile profile, WindowPick pick)
    {
        if (pick.Process.Length == 0 && pick.Title.Length == 0 && pick.Class.Length == 0) return;
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.First();
        var app = new ProfileApp
        {
            Id = ProfileDoc.NewId(),
            Process = pick.Process,
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
        SaveProfiles();
        Render("presets", false);
    }

    void ShowRunningMenu(MonitorProfile profile, FrameworkElement owner)
    {
        var menu = new ContextMenu { PlacementTarget = owner };
        foreach (var pick in ListCaptureCandidates())
        {
            var captured = pick;
            var item = new MenuItem { Header = (captured.Title.Length > 0 ? captured.Title : captured.Process) + " · " + captured.W + "×" + captured.H };
            item.Click += (_, _) => AddPicked(profile, captured);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "열린 창 없음", IsEnabled = false });
        menu.IsOpen = true;
    }

    void CaptureProfileIcons(MonitorProfile profile)
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
            profile.Icons = ProfileDoc.CaptureIcons(icons.Read());
            if (profile.Icons.Count > 0) profile.DeskLayout = "";
            SaveProfiles();
            Render("presets", false);
        }
        catch (Exception ex) { DesktopAction = ex.Message; }
    }
}
