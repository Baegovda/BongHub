using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BongHub;

public partial class MainWindow
{
    public int AirCount => ConfigStore.AirItems.Count;
    public IReadOnlyList<string> AirNames => ConfigStore.AirItems.Select(item => item.Name).ToList();

    public bool AddAirProgram(string? path)
    {
        if (!AirList.Add(ConfigStore.AirItems, path)) return false;
        ConfigStore.SaveAir();
        return true;
    }

    public bool MoveAirProgram(int from, int to)
    {
        if (!AirList.Move(ConfigStore.AirItems, from, to)) return false;
        ConfigStore.SaveAir();
        return true;
    }

    public bool RemoveAirProgram(int index)
    {
        if (!AirList.RemoveAt(ConfigStore.AirItems, index)) return false;
        ConfigStore.SaveAir();
        return true;
    }

    void PageAirDock()
    {
        Footer("단축키를 누르고 있는 동안 커서 옆에 독이 떠요. 항목을 누르거나, 그 위에 둔 채로 키를 떼면 실행해요. 다른 곳에서 떼거나 Esc면 닫혀요.");
        var g = Group();
        Row(g, "\uE7C3", "5AC8FA", "사용", "끄면 단축키를 눌러도 독이 나오지 않아요.", Switch(ConfigStore.AirEnabled, on =>
        {
            ConfigStore.AirEnabled = on;
            ConfigStore.SaveAir();
            AirDockHotkey.Bind();
        }, !AppEnv.ReadOnly), tight: true);
        var hotkey = new HotkeyField();
        hotkey.SetValue(ConfigStore.AirHotkey);
        hotkey.Gesture += gesture =>
        {
            if (AppEnv.ReadOnly) return;
            var taken = Profiles.Select(p => p.Hotkey).Append(ConfigStore.Notes.Hotkey);
            var (value, error) = HotkeyEdit.Apply(ConfigStore.AirHotkey, gesture, taken, ConfigStore.Notes.Hotkey);
            if (error.Length > 0) { hotkey.Shake(); return; }
            ConfigStore.AirHotkey = value;
            ConfigStore.SaveAir();
            AirDockHotkey.Bind();
            hotkey.SetValue(value);
            if (value.Length > 0 && gesture is not "back") hotkey.Confirm();
        };
        Row(g, null, null, "단축키", "클릭한 뒤 누르세요. Esc는 취소, Backspace는 지워요.", hotkey, tight: true);
        int size = AirDockLayout.ClampIcon(ConfigStore.AirIcon);
        var sizeLabel = Value(size.ToString());
        var slider = new Slider
        {
            Style = (Style)FindResource(typeof(Slider)),
            Minimum = 40,
            Maximum = 88,
            Width = 160,
            Value = size,
            IsEnabled = !AppEnv.ReadOnly,
            IsSnapToTickEnabled = true,
            TickFrequency = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        int guard = 0;
        slider.ValueChanged += (_, _) =>
        {
            if (guard > 0) return;
            int next = AirDockLayout.ClampIcon((int)Math.Round(slider.Value));
            sizeLabel.Text = next.ToString();
            if (next == ConfigStore.AirIcon) return;
            ConfigStore.AirIcon = next;
            ConfigStore.SaveAir();
        };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        sizeRow.Children.Add(slider);
        sizeRow.Children.Add(sizeLabel);
        Row(g, null, null, "아이콘 크기", "40에서 88. 모니터 배율에 맞춰 커져요.", sizeRow, tight: true);

        Header("프로그램");
        var list = Group();
        list.AllowDrop = !AppEnv.ReadOnly;
        list.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        list.Drop += (_, e) =>
        {
            if (AppEnv.ReadOnly || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            bool any = false;
            foreach (var file in files) if (AddAirProgram(file)) any = true;
            if (any) Render("airdock", false);
        };
        if (ConfigStore.AirItems.Count == 0)
            Row(list, null, null, "등록한 프로그램 없음", "실행 파일이나 바로 가기를 놓거나 찾아보세요.", null, tight: true);
        var rows = new List<Border>();
        int drag = -1;
        for (int i = 0; i < ConfigStore.AirItems.Count; i++)
        {
            int index = i;
            var program = ConfigStore.AirItems[i];
            var remove = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = "\uE74D",
                Width = 28,
                Height = 28,
                ToolTip = "빼기"
            };
            remove.Click += (_, _) => { if (RemoveAirProgram(index)) Render("airdock", false); };
            string sub = program.Path.Length > 48 ? "…" + program.Path[^42..] : program.Path;
            var row = Row(list, "\uE7C3", "8E8E93", program.Name, sub, remove, tight: true);
            row.Tag = "air-row";
            row.Cursor = Cursors.SizeAll;
            row.ToolTip = program.Path;
            row.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsInside(source, remove)) return;
                drag = index;
                Mouse.Capture(row);
            };
            row.Unloaded += (_, _) =>
            {
                if (drag == index) drag = -1;
                if (Mouse.Captured == row) Mouse.Capture(null);
            };
            row.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (Mouse.Captured == row) Mouse.Capture(null);
                if (drag < 0) return;
                int from = drag;
                drag = -1;
                var point = e.GetPosition(list);
                int to = from;
                for (int r = 0; r < rows.Count; r++)
                {
                    var top = rows[r].TranslatePoint(new Point(0, 0), list).Y;
                    if (point.Y >= top && point.Y < top + rows[r].ActualHeight) { to = r; break; }
                }
                if (from != to && MoveAirProgram(from, to)) Render("airdock", false);
            };
            rows.Add(row);
        }
        var browse = Small("찾아보기", () =>
        {
            if (AppEnv.ReadOnly || AppEnv.Off) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "프로그램|*.exe;*.lnk",
                CheckFileExists = true,
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            bool any = false;
            foreach (var file in dialog.FileNames) if (AddAirProgram(file)) any = true;
            if (any) Render("airdock", false);
        });
        Row(list, null, null, "추가", "exe와 lnk만 받아요. 끌어다 놓아도 돼요.", browse, tight: true);
    }

    static bool IsInside(DependencyObject? node, DependencyObject root)
    {
        while (node != null)
        {
            if (node == root) return true;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
}
