using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BongHub;

/// <summary>One custom-color control: a + swatch opens an HSV pad, and a single themed hex field edits both stops.</summary>
public sealed class CustomColorBar : Grid
{
    readonly TextBox hex;
    readonly TextBlock hint;
    readonly Border chip;
    readonly Popup picker;
    Grid pad = null!;
    Ellipse knob = null!;
    Slider hue = null!;
    Border startPick = null!;
    Border endPick = null!;
    double ah, asat = 1, av = 1, bh, bsat = 1, bv = 1;
    bool editingEnd, mute;
    public event Action<string>? Preview;
    public event Action<string>? Commit;
    public string HexText => hex.Text ?? "";

    public CustomColorBar()
    {
        picker = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var plus = new Button { Content = "\uE710", ToolTip = "맞춤 색", Width = 30, Height = 30 };
        plus.SetResourceReference(StyleProperty, "IconButton");
        plus.Click += (_, _) => picker.IsOpen = !picker.IsOpen;
        Children.Add(plus);

        chip = new Border
        {
            Width = 36,
            Height = 22,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "미리보기",
        };
        chip.MouseLeftButtonUp += (_, _) => picker.IsOpen = !picker.IsOpen;
        Grid.SetColumn(chip, 1);
        Children.Add(chip);

        var field = new Grid { MinWidth = 148, VerticalAlignment = VerticalAlignment.Center };
        hex = new TextBox { MaxLength = 28, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "시작과 끝 RRGGBB" };
        hint = new TextBlock
        {
            Text = "RRGGBB · RRGGBB",
            Opacity = 0.42,
            IsHitTestVisible = false,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        hex.TextChanged += (_, _) =>
        {
            hint.Visibility = hex.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!mute) PushFromHex(commit: false);
        };
        hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (!PushFromHex(commit: true)) CommitStops();
            e.Handled = true;
        };
        field.Children.Add(hex);
        field.Children.Add(hint);
        Grid.SetColumn(field, 2);
        Children.Add(field);

        picker.PlacementTarget = plus;
        picker.Child = BuildPicker();
        Children.Add(picker);
        Load("g:blocks");
    }

    public void Load(string? key)
    {
        NoteColors.Raw(string.IsNullOrEmpty(key) ? "g:blocks" : key!, out var a, out var b);
        (ah, asat, av) = ToHsv(a);
        (bh, bsat, bv) = ToHsv(b);
        WriteHex();
        Paint();
    }

    public void OpenPicker() => picker.IsOpen = true;
    public void ClosePicker() => picker.IsOpen = false;

    UIElement BuildPicker()
    {
        var root = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 6, 0, 0),
            Width = 196,
        };
        root.SetResourceReference(Border.BackgroundProperty, "MenuBg");
        root.SetResourceReference(Border.BorderBrushProperty, "MenuStroke");
        var stack = new StackPanel();
        var stops = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var start = StopChip("시작");
        var end = StopChip("끝");
        startPick = start;
        endPick = end;
        start.MouseLeftButtonUp += (_, _) => Select(false);
        end.MouseLeftButtonUp += (_, _) => Select(true);
        stops.Children.Add(start);
        stops.Children.Add(end);
        stack.Children.Add(stops);

        var host = new Border { Width = 168, Height = 104, CornerRadius = new CornerRadius(8), ClipToBounds = true, Cursor = Cursors.Cross };
        pad = new Grid();
        pad.Children.Add(new Border());
        var shade = new Rectangle { IsHitTestVisible = false };
        shade.Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1));
        pad.Children.Add(shade);
        host.Child = pad;
        host.MouseLeftButtonDown += (_, e) => { host.CaptureMouse(); MovePad(e.GetPosition(host)); };
        host.MouseMove += (_, e) => { if (host.IsMouseCaptured) MovePad(e.GetPosition(host)); };
        host.MouseLeftButtonUp += (_, _) => host.ReleaseMouseCapture();
        var canvas = new Canvas { Width = 168, Height = 104 };
        canvas.Children.Add(host);
        knob = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
        canvas.Children.Add(knob);
        stack.Children.Add(canvas);

        hue = new Slider { Minimum = 0, Maximum = 360, Margin = new Thickness(0, 10, 0, 0), IsMoveToPointEnabled = true };
        hue.ValueChanged += (_, _) =>
        {
            if (mute) return;
            if (editingEnd) bh = hue.Value; else ah = hue.Value;
            Publish(commit: false);
        };
        stack.Children.Add(hue);

        var apply = new Button { Content = "적용", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
        apply.Click += (_, _) => CommitStops();
        stack.Children.Add(apply);
        root.Child = stack;
        return root;
    }

    static Border StopChip(string label)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, FontSize = 12 },
        };
    }

    void Select(bool end)
    {
        editingEnd = end;
        Paint();
    }

    void MovePad(Point p)
    {
        double s = Math.Clamp(p.X / 168.0, 0, 1);
        double v = Math.Clamp(1 - p.Y / 104.0, 0, 1);
        if (editingEnd) { bsat = s; bv = v; } else { asat = s; av = v; }
        Publish(commit: false);
    }

    void CommitStops()
    {
        WriteHex();
        picker.IsOpen = false;
        Commit?.Invoke(CurrentKey());
    }

    bool PushFromHex(bool commit)
    {
        if (!TryPair(hex.Text, out var a, out var b)) return false;
        (ah, asat, av) = ToHsv(a);
        (bh, bsat, bv) = ToHsv(b);
        Paint();
        var key = NoteColors.Custom(a, b);
        if (commit) { picker.IsOpen = false; Commit?.Invoke(key); }
        else Preview?.Invoke(key);
        return true;
    }

    void Publish(bool commit)
    {
        WriteHex();
        Paint();
        if (commit) Commit?.Invoke(CurrentKey());
        else Preview?.Invoke(CurrentKey());
    }

    string CurrentKey() => NoteColors.Custom(FromHsv(ah, asat, av), FromHsv(bh, bsat, bv));

    void WriteHex()
    {
        mute = true;
        hex.Text = Hex(FromHsv(ah, asat, av)) + " · " + Hex(FromHsv(bh, bsat, bv));
        mute = false;
        hint.Visibility = Visibility.Collapsed;
    }

    void Paint()
    {
        var a = FromHsv(ah, asat, av);
        var b = FromHsv(bh, bsat, bv);
        chip.Background = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        if (pad.Children[0] is Border hueLayer)
        {
            var pure = FromHsv(editingEnd ? bh : ah, 1, 1);
            hueLayer.Background = new LinearGradientBrush(Colors.White, pure, new Point(0, 0), new Point(1, 0));
        }
        double s = editingEnd ? bsat : asat;
        double v = editingEnd ? bv : av;
        Canvas.SetLeft(knob, s * 168 - 6);
        Canvas.SetTop(knob, (1 - v) * 104 - 6);
        mute = true;
        hue.Value = editingEnd ? bh : ah;
        mute = false;
        startPick.Background = new SolidColorBrush(a);
        endPick.Background = new SolidColorBrush(b);
        var ring = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        var idle = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        startPick.BorderBrush = editingEnd ? idle : ring;
        endPick.BorderBrush = editingEnd ? ring : idle;
        if (startPick.Child is TextBlock st) st.Foreground = BrushFor(a);
        if (endPick.Child is TextBlock et) et.Foreground = BrushFor(b);
    }

    static Brush BrushFor(Color c) => NoteColors.Lum(c) > 0.45 ? Brushes.Black : Brushes.White;

    static bool TryPair(string text, out Color a, out Color b)
    {
        a = default;
        b = default;
        var hex = new string((text ?? "").Where(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F').ToArray());
        if (hex.Length < 12) return false;
        try
        {
            a = HexColor(hex[..6]);
            b = HexColor(hex.Substring(6, 6));
            return true;
        }
        catch { return false; }
    }

    static string Hex(Color c) => c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    static Color HexColor(string h) { int v = Convert.ToInt32(h, 16); return Color.FromRgb((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255)); }

    static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0.0001)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    static Color FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
