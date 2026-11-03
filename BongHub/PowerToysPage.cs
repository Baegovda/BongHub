using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BongHub;

public partial class MainWindow
{
    void DrawPowerToys()
    {
        var module = PtCatalog.All.FirstOrDefault(m => m.Key == openPowerToy);
        if (module.Key == null)
        {
            DrawPowerToyDashboard();
            return;
        }
        DrawPowerToyPage(module);
    }

    void DrawPowerToyDashboard()
    {
        var intro = new TextBlock
        {
            Text = "값은 저장만 해요. 후킹과 단축키 등록은 하지 않아요.",
            Margin = new Thickness(16, 0, 16, 10),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("Secondary"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        FirstSecondary ??= intro;
        PageHost.Children.Add(intro);
        var wrap = new WrapPanel();
        foreach (var module in PtCatalog.All)
        {
            var toy = ConfigStore.PowerToys.FirstOrDefault(t => t.Name == module.Key);
            bool on = toy?.Enabled ?? false;
            var card = new Border
            {
                Width = 280,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 12, 12, 12),
                CornerRadius = new CornerRadius(16),
                Background = (Brush)FindResource("Card"),
                BorderBrush = (Brush)FindResource("CardStroke"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = "pt-module"
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Tile(module.Glyph, module.Hex, 28, 8, 14));
            title.Children.Add(new TextBlock
            {
                Text = module.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            grid.Children.Add(title);
            var summary = new TextBlock
            {
                Text = module.Summary,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("Secondary"),
                Margin = new Thickness(0, 8, 8, 0),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(summary, 1);
            grid.Children.Add(summary);
            string key = module.Key;
            var toggle = Switch(on, v => { if (!ConfigStore.SetPt(key, "Enabled", JsonValue.Create(v))) Render("powertoys", false); }, !AppEnv.ReadOnly);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 1);
            Grid.SetRowSpan(toggle, 2);
            grid.Children.Add(toggle);
            card.Child = grid;
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is CheckBox) return;
                openPowerToy = key;
                Render("powertoys", false);
            };
            wrap.Children.Add(card);
        }
        PageHost.Children.Add(wrap);
    }

    void DrawPowerToyPage(PtCatalog.Module module)
    {
        var toy = ConfigStore.PowerToys.FirstOrDefault(t => t.Name == module.Key) ?? new ToySnapshot { Name = module.Key };
        var head = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = Small("모듈", () => { openPowerToy = ""; Render("powertoys", false); });
        back.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(back);
        var words = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = module.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var line = new TextBlock { Text = module.Summary, FontSize = 12.5, Foreground = (Brush)FindResource("Secondary"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        FirstSecondary ??= line;
        words.Children.Add(line);
        Grid.SetColumn(words, 1);
        head.Children.Add(words);
        var enabled = Switch(toy.Enabled, v => { if (!ConfigStore.SetPt(module.Key, "Enabled", JsonValue.Create(v))) Render("powertoys", false); }, !AppEnv.ReadOnly);
        enabled.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(enabled, 2);
        head.Children.Add(enabled);
        PageHost.Children.Add(head);
        PageHost.Children.Add(new TextBlock
        {
            Text = module.How,
            Margin = new Thickness(16, 0, 16, 12),
            FontSize = 13,
            Foreground = (Brush)FindResource("Secondary"),
            TextWrapping = TextWrapping.Wrap
        });
        var g = Group();
        string key = module.Key;
        switch (key)
        {
            case "Zones":
                Flag(g, toy, key, "Shift로 끌기", "Shift를 누를 때만 구역에 붙어요.", "ShiftDrag", true);
                Flag(g, toy, key, "끄는 동안 표시", "놓을 구역을 미리 보여요.", "ShowOnDrag", false);
                Flag(g, toy, key, "여러 모니터", "구역이 모니터를 넘어요.", "Span", true);
                Flag(g, toy, key, "화살표 키", "Win과 화살표로 옮겨요.", "WinArrows", false);
                Flag(g, toy, key, "크기 복원", "빼면 원래 크기로 돌아와요.", "RestoreSize", true);
                Flag(g, toy, key, "모든 모니터", "같은 배치를 모든 화면에 써요.", "AllMonitors", false);
                Flag(g, toy, key, "번호", "구역에 번호를 표시해요.", "Numbers", true);
                Number(g, toy, key, "투명도", "0–100", "Opacity", 55, 0, 100);
                break;
            case "Grab":
                Choices(g, toy, key, "수정키", "이 키를 누른 채 창을 끌어요.", "Modifier", toy.Text("Modifier").Length > 0 ? toy.Text("Modifier") : "Alt", "Alt", "Ctrl", "Shift", "Win");
                Flag(g, toy, key, "가장자리에서 크기", "변을 끌면 크기가 바뀌어요.", "Resize", true);
                Flag(g, toy, key, "전체 화면 건너뛰기", "전체 화면 창은 잡지 않아요.", "SkipFullscreen", true);
                break;
            case "Work":
                Flag(g, toy, key, "없는 앱 실행", "배치에 있는데 닫혀 있으면 켜요.", "LaunchMissing", true);
                Flag(g, toy, key, "열린 창 이동", "이미 열린 창을 저장된 자리로 옮겨요.", "MoveExisting", true);
                if (ConfigStore.Workspaces.Count == 0)
                    Row(g, null, null, "저장된 묶음 없음", "config의 작업 영역이 비어 있어요.", null, tight: true);
                foreach (var ws in ConfigStore.Workspaces)
                    Row(g, null, null, ws.Name.Length > 0 ? ws.Name : "작업 영역", "창 " + ws.Apps.Count + "개", null, tight: true);
                break;
            case "Palette":
                Hotkey(g, toy, key, "단축키", "Hotkey");
                Flag(g, toy, key, "앱", "설치된 앱을 찾아요.", "Apps", true);
                Flag(g, toy, key, "계산", "식을 계산해요.", "Calc", true);
                Flag(g, toy, key, "웹", "검색 주소를 열어요.", "Web", true);
                Flag(g, toy, key, "단위", "단위를 바꿔요.", "Units", true);
                Flag(g, toy, key, "시간", "시간을 더해요.", "Time", true);
                Flag(g, toy, key, "창", "열린 창으로 가요.", "Windows", true);
                Flag(g, toy, key, "파일", "파일을 찾아요.", "Files", true);
                Flag(g, toy, key, "셸", "명령을 실행해요.", "Shell", true);
                Flag(g, toy, key, "터미널", "터미널을 열어요.", "Terminal", true);
                Flag(g, toy, key, "시스템", "시스템 명령을 찾아요.", "System", true);
                Flag(g, toy, key, "설정", "Windows 설정을 찾아요.", "Settings", true);
                break;
            case "Paste":
                Hotkey(g, toy, key, "변환", "Hotkey");
                Hotkey(g, toy, key, "일반 텍스트", "PlainHotkey");
                Hotkey(g, toy, key, "JSON", "JsonHotkey");
                Hotkey(g, toy, key, "마크다운", "MarkdownHotkey");
                break;
            case "Peek":
                Hotkey(g, toy, key, "단축키", "Hotkey");
                Flag(g, toy, key, "포커스를 잃으면 닫기", "다른 곳을 누르면 미리보기가 사라져요.", "CloseOnFocusLoss", true);
                Flag(g, toy, key, "HTML 표시", "HTML 파일을 읽어서 보여요.", "RenderHtml", true);
                break;
            case "Resizer":
                Flag(g, toy, key, "탐색기 메뉴", "그림 파일의 메뉴에 넣어요.", "ContextMenu", false);
                Choices(g, toy, key, "맞춤", "그림을 칸에 어떻게 넣을지예요.", "Fit", toy.Text("Fit").Length > 0 ? toy.Text("Fit") : "fit", "fit", "fill", "stretch");
                Flag(g, toy, key, "줄이기만", "이미 작으면 키우지 않아요.", "ShrinkOnly", true);
                Flag(g, toy, key, "원본 교체", "새 파일 대신 원본을 덮어요.", "Replace", false);
                Flag(g, toy, key, "방향 무시", "세로 그림도 가로 크기를 써요.", "IgnoreOrientation", true);
                Flag(g, toy, key, "날짜 유지", "파일 날짜를 그대로 둬요.", "KeepDate", false);
                Flag(g, toy, key, "메타데이터 제거", "위치 같은 부가 정보를 빼요.", "RemoveMeta", false);
                Number(g, toy, key, "품질", "1–100", "Quality", 90, 1, 100);
                Text(g, toy, key, "이름 형식", "%1은 원래 이름, %2는 크기 이름.", "NameFormat", "%1 (%2)");
                Choices(g, toy, key, "대체 형식", "맞출 수 없을 때 저장하는 형식.", "Fallback", toy.Text("Fallback").Length > 0 ? toy.Text("Fallback") : "png", "png", "jpg");
                break;
        }
    }

    void Flag(StackPanel g, ToySnapshot toy, string module, string title, string hint, string field, bool fallback)
        => Row(g, null, null, title, hint, Switch(toy.Flag(field, fallback), v => { if (!ConfigStore.SetPt(module, field, JsonValue.Create(v))) Render("powertoys", false); }, !AppEnv.ReadOnly), tight: true);

    void Number(StackPanel g, ToySnapshot toy, string module, string title, string hint, string field, int fallback, int min, int max)
    {
        int cur = Math.Clamp(toy.Number(field, fallback), min, max);
        var label = Value(cur.ToString());
        label.Margin = new Thickness(8, 0, 0, 0);
        var slider = new Slider { Minimum = min, Maximum = max, Width = 140, Value = cur, IsEnabled = !AppEnv.ReadOnly, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        int guard = 0;
        slider.ValueChanged += (_, _) =>
        {
            if (guard > 0) return;
            int v = (int)Math.Round(slider.Value);
            label.Text = v.ToString();
            if (v == toy.Number(field, fallback)) return;
            if (!ConfigStore.SetPt(module, field, JsonValue.Create(v)))
            {
                guard++;
                slider.Value = toy.Number(field, fallback);
                guard--;
            }
        };
        var trail = new StackPanel { Orientation = Orientation.Horizontal };
        trail.Children.Add(slider);
        trail.Children.Add(label);
        Row(g, null, null, title, hint, trail, tight: true);
    }

    void Text(StackPanel g, ToySnapshot toy, string module, string title, string hint, string field, string fallback)
    {
        string cur = toy.Text(field);
        if (cur.Length == 0) cur = fallback;
        var box = new TextBox { Text = cur, MinWidth = 140, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Foreground };
        box.LostFocus += (_, _) => { if (box.Text != toy.Text(field)) ConfigStore.SetPt(module, field, JsonValue.Create(box.Text)); };
        Row(g, null, null, title, hint, Chip(box), tight: true);
    }

    void Choices(StackPanel g, ToySnapshot toy, string module, string title, string hint, string field, string current, params string[] options)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var opt in options)
        {
            bool on = string.Equals(current, opt, StringComparison.OrdinalIgnoreCase);
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = on ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)),
                Child = new TextBlock { Text = opt, FontSize = 12, Foreground = Brushes.White }
            };
            string pick = opt;
            pill.MouseLeftButtonUp += (_, _) => { if (ConfigStore.SetPt(module, field, JsonValue.Create(pick))) Render("powertoys", false); };
            row.Children.Add(pill);
        }
        Row(g, null, null, title, hint, row, tight: true);
    }

    void Hotkey(StackPanel g, ToySnapshot toy, string module, string title, string field)
    {
        var box = new HotkeyField();
        box.SetValue(toy.Text(field));
        box.Gesture += gesture =>
        {
            if (AppEnv.ReadOnly) return;
            if (gesture is "escape") { box.Cancel(); return; }
            var taken = ConfigStore.PowerToys.Where(t => t.Name != module).Select(t => t.Hotkey);
            var (value, error) = HotkeyEdit.Apply(toy.Text(field), gesture, taken, ConfigStore.Notes.Hotkey);
            if (error.Length > 0) { box.Shake(); return; }
            if (!ConfigStore.SetPt(module, field, JsonValue.Create(value))) { box.Shake(); return; }
            box.SetValue(value);
            if (value.Length > 0 && gesture is not "back") box.Confirm();
        };
        Row(g, null, null, title, "Esc는 취소, Backspace는 지워요.", box, tight: true);
    }
}
