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

    bool windowPickerOpen;

    void BuildProfileEditor(MonitorProfile profile)
    {
        var live = ScreenCatalog.Current();
        if (ScreenBind.Mismatch(profile, live))
        {
            var fix = Group();
            Row(fix, "\uE7BA", "FF9500", "화면이 저장된 구성과 달라요", "번호나 연결이 지금 PC와 맞지 않아요.", Small("현재 화면에 맞추기", () =>
            {
                ScreenBind.MatchCurrent(profile, live);
                SaveProfiles();
                Render("presets", false);
            }), wrap: true);
        }
        DrawProfileCanvas(profile);
        Header("담긴 창");
        var windowRows = Group();
        if (profile.Apps.Count == 0)
            Row(windowRows, "\uE737", "8E8E93", "아직 없어요", "+ 창 추가로 실행 중인 창을 담아요.", null, wrap: true);
        foreach (var listed in profile.Apps)
        {
            string listedName = listed.Name.Length > 0 ? listed.Name : (listed.Title.Length > 0 ? listed.Title : listed.Process);
            string listedId = listed.Id;
            int listedMonitor = listed.Monitor;
            Row(windowRows, "\uE737", "5AC8FA", listedName.Length > 0 ? listedName : "창", listed.W + "×" + listed.H, null, () =>
            {
                selectedAppId = listedId;
                selectedMonitorNumber = listedMonitor;
                Render("presets", false);
            }, wrap: true, icon: AppIcon.Lookup(listed.Process, listed.Path));
        }
        var add = Small(windowPickerOpen ? "닫기" : "+ 창 추가", () => { windowPickerOpen = !windowPickerOpen; Render("presets", false); });
        var bar = Group();
        Row(bar, "\uE710", "34C759", "창", "모니터 안에서 끌어 옮기고, 가장자리에서 크기를 바꿔요.", add, wrap: true);
        if (windowPickerOpen) DrawWindowPicker(profile);
        Header("선택");
        DrawDetail(profile);
        var icons = Group();
        string when = profile.Icons.Count == 0 ? "아직 이 프로필에 담긴 배치가 없어요." : profile.Icons.Count + "개 · " + (profile.IconsSaved.Length == 0 ? "저장됨" : profile.IconsSaved);
        Row(icons, "\uE8A9", "34C759", "아이콘 배치를 이 프로필에 저장", when, Small("지금 저장", () => CaptureProfileIcons(profile)), wrap: true);
        Row(icons, "\uE8A5", "5AC8FA", "적용할 때 아이콘 되돌리기", "지금 적용을 누르면 담긴 위치로 옮겨요.", Switch(profile.RestoreIcons, v => { profile.RestoreIcons = v; SaveProfiles(); }, !AppEnv.ReadOnly), wrap: true);
    }

    void DrawDetail(MonitorProfile profile)
    {
        var g = Group();
        var nameBox = new TextBox { Text = profile.Name, MinWidth = 180, FontSize = 16, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground };
        var hotkeyBox = new TextBox { Text = profile.Hotkey, MinWidth = 120, FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground };
        nameBox.LostFocus += (_, _) => { ProfileDoc.Rename(profile, nameBox.Text); SaveProfiles(); };
        hotkeyBox.LostFocus += (_, _) => { profile.Hotkey = hotkeyBox.Text.Trim(); SaveProfiles(); };
        FieldRow(g, "이름", Chip(nameBox));
        FieldRow(g, "단축키", Chip(hotkeyBox));
        var app = profile.Apps.FirstOrDefault(a => a.Id == selectedAppId);
        var mon = profile.Monitors.FirstOrDefault(m => m.Number == selectedMonitorNumber) ?? profile.Monitors.FirstOrDefault(m => m.Primary) ?? profile.Monitors.FirstOrDefault();
        if (app != null)
        {
            string label = app.Name.Length > 0 ? app.Name : app.Title;
            Row(g, "\uE737", "5AC8FA", label.Length > 0 ? label : "창", app.W + "×" + app.H + (app.Process.Length > 0 ? " · " + app.Process : ""), Small("창 삭제", () =>
            {
                profile.Apps.Remove(app);
                selectedAppId = "";
                SaveProfiles();
                Render("presets", false);
            }), wrap: true, icon: AppIcon.Lookup(app.Process, app.Path));
        }
        else if (mon != null)
        {
            string state = mon.Connected ? (mon.Enabled ? "이 프로필에서 사용" : "이 프로필에서 끔") : "연결 안 됨";
            UIElement? primary = mon.Connected ? Small(mon.Primary ? "주 모니터" : "주로 지정", () => { ProfileDoc.SetPrimary(profile, mon.Number); selectedMonitorNumber = mon.Number; SaveProfiles(); Render("presets", false); }) : null;
            Row(g, "\uE7F4", mon.Connected ? "4C8AFE" : "8E8E93", mon.Number + "번 · " + mon.W + "×" + mon.H, state, primary, wrap: true);
            if (mon.Connected)
                Row(g, "\uE7F4", "34C759", "이 프로필에서 사용", "끄면 적용할 때 이 화면을 빼요.", Switch(mon.Enabled, v =>
                {
                    if (!ProfileDoc.SetEnabled(profile, mon.Number, v)) return;
                    SaveProfiles();
                    Render("presets", false);
                }, !AppEnv.ReadOnly), wrap: true);
        }
    }

    void DrawWindowPicker(MonitorProfile profile)
    {
        var g = Group();
        Button running = null!;
        running = Small("목록", () => ShowRunningMenu(profile, running));
        Row(g, "\uE737", "5AC8FA", "실행 중인 창", "아이콘과 함께 고르면 담긴 창이 돼요. 시험에서는 가짜 창만 나와요.", running, wrap: true);
        Row(g, "\uE8B3", "4C8AFE", "조준", "십자선을 창 위에 놓아요.", Small("조준", () => { RequestCaptureWindow(); if (LastPick != null) AddPicked(profile, LastPick); }), wrap: true);
        var proc = PromptBox("notepad.exe");
        var title = PromptBox("메모장");
        var cls = PromptBox("Notepad");
        FieldRow(g, "실행 파일", proc.Field);
        FieldRow(g, "창 제목", title.Field);
        FieldRow(g, "창 클래스", cls.Field);
        Row(g, "\uE710", "34C759", "직접 추가", null, Small("담기", () => AddPicked(profile, new WindowPick { Process = proc.Box.Text.Trim(), Title = title.Box.Text.Trim(), Class = cls.Box.Text.Trim(), W = 800, H = 600 })));
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

    Button ProfileMenu(MonitorProfile item)
    {
        var b = new Button { Content = "⋯", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = "복제 또는 삭제" };
        var menu = new ContextMenu { PlacementTarget = b };
        var dup = new MenuItem { Header = "복제" };
        dup.Click += (_, _) => { var n = ProfileDoc.Duplicate(Profiles, item.Id); if (n != null) { SelectedProfileId = n.Id; SaveProfiles(); Render("presets", false); } };
        var del = new MenuItem { Header = "삭제" };
        del.Click += (_, _) => { if (ProfileDoc.Delete(Profiles, item.Id)) { SelectedProfileId = Profiles.FirstOrDefault()?.Id ?? ""; selectedAppId = ""; SaveProfiles(); Render("presets", false); } };
        menu.Items.Add(dup);
        menu.Items.Add(del);
        b.Click += (_, _) => menu.IsOpen = true;
        return b;
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
        var badges = new List<Border>();
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
                Opacity = !mon.Connected ? 0.35 : mon.Enabled ? 1 : 0.55,
                Cursor = Cursors.SizeAll
            };
            Canvas.SetLeft(frame, box.X);
            Canvas.SetTop(frame, box.Y);
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1C, 0x1C, 0x1E)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = mon.Number + (mon.Primary ? " · 주" : "") + "  " + mon.W + "×" + mon.H + (mon.Connected ? "" : "  연결 안 됨"),
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Canvas.SetLeft(badge, box.X + 8);
            Canvas.SetTop(badge, box.Y + 8);
            badges.Add(badge);
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
                Canvas.SetLeft(badge, origin.X + p.X - grab.X + 8);
                Canvas.SetTop(badge, origin.Y + p.Y - grab.Y + 8);
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
            stage.Children.Add(badge);
        }
        int tileZ = 10;
        var occupied = new List<Rect>();
        foreach (var app in profile.Apps)
        {
            var home = profile.Monitors.FirstOrDefault(m => m.Number == app.Monitor) ?? profile.Monitors[0];
            var abs = new ProfileBox(home.X + app.X, home.Y + app.Y, app.W, app.H);
            var view = ProfileSnap.ToCanvas(abs, scene);
            var homeBox = ProfileSnap.ToCanvas(home.Box, scene);
            int onMonitor = profile.Apps.Count(a => a.Monitor == home.Number);
            int steps = Math.Min(Math.Max(onMonitor - 1, 1), 4);
            double tileW = Math.Min(160, Math.Max(72, homeBox.W - 12 - 24 * steps));
            double tileH = Math.Min(52, Math.Max(36, Math.Min(homeBox.H - 36, 48)));
            int indexOnMonitor = profile.Apps.TakeWhile(a => !ReferenceEquals(a, app)).Count(a => a.Monitor == home.Number);
            double left = Math.Max(homeBox.X + 4, Math.Min(view.X, homeBox.X + homeBox.W - tileW - 4));
            double top = Math.Max(homeBox.Y + 28, Math.Min(view.Y, homeBox.Y + homeBox.H - tileH - 4));
            var spot = new Rect(left, top, tileW, tileH);
            bool heavy = occupied.Any(r => r.Width > 0 && r.IntersectsWith(spot) && Intersection(r, spot) > 0.35 * Math.Min(r.Width * r.Height, spot.Width * spot.Height));
            if (heavy)
            {
                double span = Math.Max(24, homeBox.W - tileW - 8);
                left = homeBox.X + 6 + (indexOnMonitor * 24) % span;
                top = homeBox.Y + 30 + (indexOnMonitor * 24 / Math.Max(24, span)) * 24;
                top = Math.Min(top, Math.Max(homeBox.Y + 28, homeBox.Y + homeBox.H - tileH - 4));
                spot = new Rect(left, top, tileW, tileH);
            }
            occupied.Add(spot);
            view = new ProfileBox((int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(tileW), (int)Math.Round(tileH));
            double font = Math.Clamp(Math.Min(tileW, tileH) / 7.0, 10, 13);
            string shown = app.Name.Length > 0 ? app.Name : (app.Title.Length > 0 ? app.Title : app.Process);
            var tile = new Border
            {
                Width = tileW,
                Height = tileH,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("Card"),
                BorderBrush = app.Id == selectedAppId ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
                BorderThickness = new Thickness(app.Id == selectedAppId ? 2 : 1),
                Cursor = Cursors.SizeAll,
                Tag = app,
                ToolTip = shown + "\n" + app.W + "×" + app.H + (app.Process.Length > 0 ? "\n" + app.Process : "")
            };
            Canvas.SetLeft(tile, view.X);
            Canvas.SetTop(tile, view.Y);
            Canvas.SetZIndex(tile, app.Id == selectedAppId ? 400 : 20 + indexOnMonitor);
            var body = new Grid { Margin = new Thickness(8, 6, 8, 6), IsHitTestVisible = false };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var icon = AppIcon.Lookup(app.Process, app.Path);
            UIElement mark = icon != null
                ? new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0) }
                : new TextBlock { Text = "\uE737", FontFamily = (FontFamily)FindResource("Icons"), FontSize = font, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0) };
            body.Children.Add(mark);
            var nameBlock = new TextBlock { Text = shown.Length > 0 ? shown : "창", Foreground = Brushes.White, FontSize = font, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(nameBlock, 1);
            body.Children.Add(nameBlock);
            var sizeBlock = new TextBlock { Text = app.W + "×" + app.H, Foreground = (Brush)FindResource("Secondary"), FontSize = Math.Max(10, font - 1), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(sizeBlock, 1);
            Grid.SetColumnSpan(sizeBlock, 2);
            body.Children.Add(sizeBlock);
            tile.Child = body;
            void Raise() { Canvas.SetZIndex(tile, ++tileZ); }
            tile.MouseEnter += (_, _) => Raise();
            var appStart = abs;
            string edge = "";
            Point grab = default;
            tile.MouseLeftButtonDown += (_, e) =>
            {
                selectedAppId = app.Id;
                selectedMonitorNumber = app.Monitor;
                Raise();
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
            stage.Children.Add(tile);
        }
        Canvas.SetZIndex(preview, 3);
        stage.Children.Add(preview);
        foreach (var badge in badges) Canvas.SetZIndex(badge, 100000);
        foreach (var mon in profile.Monitors)
        {
            int count = profile.Apps.Count(a => a.Monitor == mon.Number);
            if (count < 2) continue;
            var box = ProfileSnap.ToCanvas(mon.Box, scene);
            var countBadge = new Border
            {
                Width = 22,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource("Accent"),
                IsHitTestVisible = false,
                ToolTip = "이 모니터에 창 " + count + "개",
                Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Canvas.SetLeft(countBadge, box.X + box.W - 30);
            Canvas.SetTop(countBadge, box.Y + box.H - 26);
            Canvas.SetZIndex(countBadge, 100002);
            stage.Children.Add(countBadge);
        }
        PageHost.Children.Add(stage);
    }

    static double Intersection(Rect a, Rect b)
    {
        double w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
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
            var face = AppIcon.Lookup(captured.Process, captured.Path);
            if (face != null) item.Icon = new Image { Source = face, Width = 16, Height = 16 };
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
            profile.IconsSaved = DateTime.Now.ToString("M월 d일 HH:mm");
            if (profile.Icons.Count > 0) profile.DeskLayout = "";
            SaveProfiles();
            Render("presets", false);
        }
        catch (Exception ex) { DesktopAction = ex.Message; }
    }
}
