using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace BongHub;

/// <summary>
/// Physical monitor canvas. Sizes default to EDID millimetres. A typed size or a drag is saved in settings.
/// The caller passes the monitors. This window does not read a display.
/// </summary>
public sealed class PhysicalLayoutWindow : Window
{
    readonly List<EdgeMonitor> panels = new();
    readonly Canvas stage = new() { Height = 380, ClipToBounds = true };
    readonly StackPanel fields = new() { Margin = new Thickness(0, 16, 0, 0) };
    readonly TextBlock note = new()
    {
        Text = "비워 두면 EDID예요. EDID가 없으면 그 변은 옮기지 않아요. 네모를 끌면 맞닿은 위치가 mm로 움직여요.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 13,
        Margin = new Thickness(0, 0, 0, 14),
        Tag = "physical-note"
    };
    string selected = "";
    bool guard;
    EdgeMonitor? dragPanel;
    Point dragStart;
    double dragShiftX, dragShiftY;
    double scale = 2;

    public PhysicalLayoutWindow()
    {
        Title = "실물 배치";
        Width = 920;
        Height = 700;
        MinWidth = 680;
        MinHeight = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemeMark.SetOn(this, true);
        FontFamily = (FontFamily)FindResource("UiFont");
        Foreground = (Brush)FindResource("Text");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 52,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            UseAeroCaptionButtons = false
        });
        var root = new Grid();
        root.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(0xD0, 0x0D, 0x10, 0x18), Color.FromArgb(0xD0, 0x14, 0x12, 0x24), new Point(0, 0), new Point(1, 1))
        });
        var dock = new DockPanel { Margin = new Thickness(28, 0, 28, 24) };
        var caption = new Grid { Height = 52 };
        caption.Children.Add(new TextBlock
        {
            Text = "실물 배치",
            FontFamily = (FontFamily)FindResource("DisplayFont"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "\uE8BB",
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "닫기"
        };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        caption.Children.Add(close);
        DockPanel.SetDock(caption, Dock.Top);
        dock.Children.Add(caption);
        var body = new StackPanel();
        note.Foreground = (Brush)FindResource("Secondary");
        body.Children.Add(note);
        body.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
            Padding = new Thickness(12),
            Child = stage
        });
        body.Children.Add(fields);
        dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(dock);
        Content = root;
        WindowFx.Attach(this, root, wantBackdrop: true, solid: Color.FromRgb(0x0B, 0x0D, 0x12));
    }

    public void LoadPanels(IReadOnlyList<EdgeMonitor> source)
    {
        panels.Clear();
        foreach (var s in source)
        {
            var copy = new EdgeMonitor
            {
                Id = s.Id, X = s.X, Y = s.Y, W = s.W, H = s.H,
                WidthMm = s.WidthMm, HeightMm = s.HeightMm, Orientation = s.Orientation,
                OverrideWmm = s.OverrideWmm, OverrideHmm = s.OverrideHmm,
                ShiftXmm = s.ShiftXmm, ShiftYmm = s.ShiftYmm
            };
            var tune = AppSettings.FindPhysical(copy.Id);
            if (tune != null)
            {
                copy.OverrideWmm = tune.WidthMm;
                copy.OverrideHmm = tune.HeightMm;
                copy.ShiftXmm = tune.ShiftXmm;
                copy.ShiftYmm = tune.ShiftYmm;
            }
            panels.Add(copy);
        }
        if (panels.All(p => p.Id != selected)) selected = panels.FirstOrDefault()?.Id ?? "";
        Redraw();
    }

    /// <summary>Test hook. Writes the override and does not read a monitor.</summary>
    public bool SetMillimetres(string id, int widthMm, int heightMm)
    {
        var panel = panels.FirstOrDefault(p => p.Id == id);
        if (panel == null) return false;
        panel.OverrideWmm = widthMm;
        panel.OverrideHmm = heightMm;
        Persist();
        Redraw();
        return true;
    }

    void Redraw()
    {
        DrawCanvas();
        DrawFields();
    }

    void DrawCanvas()
    {
        stage.Children.Clear();
        var laid = CursorCross.Arrange(panels);
        if (laid.Count == 0) return;
        double minX = laid.Min(b => b.X), minY = laid.Min(b => b.Y);
        double maxX = laid.Max(b => b.X + b.W), maxY = laid.Max(b => b.Y + b.H);
        double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
        scale = Math.Min(820 / spanX, 340 / spanY);
        if (scale <= 0 || double.IsInfinity(scale)) scale = 1;
        foreach (var box in laid)
        {
            var panel = panels.First(p => p.Id == box.Id);
            bool on = panel.Id == selected;
            var face = new Border
            {
                Width = Math.Max(36, box.W * scale),
                Height = Math.Max(28, box.H * scale),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(on ? Color.FromArgb(0x55, 0x4C, 0x8A, 0xFE) : Color.FromArgb(0x22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x4C, 0x8A, 0xFE) : Color.FromArgb(0x55, 255, 255, 255)),
                BorderThickness = new Thickness(on ? 2 : 1),
                Cursor = Cursors.SizeAll,
                Tag = panel.Id,
                Child = new TextBlock
                {
                    Text = panel.Id + "\n" + (int)Math.Round(box.W) + "×" + (int)Math.Round(box.H) + " mm",
                    Margin = new Thickness(8, 6, 8, 0),
                    FontSize = 12,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Canvas.SetLeft(face, (box.X - minX) * scale + 8);
            Canvas.SetTop(face, (box.Y - minY) * scale + 8);
            double originLeft = Canvas.GetLeft(face);
            double originTop = Canvas.GetTop(face);
            face.MouseLeftButtonDown += (_, e) =>
            {
                selected = panel.Id;
                dragPanel = panel;
                dragStart = e.GetPosition(stage);
                dragShiftX = panel.ShiftXmm;
                dragShiftY = panel.ShiftYmm;
                originLeft = Canvas.GetLeft(face);
                originTop = Canvas.GetTop(face);
                face.CaptureMouse();
                DrawFields();
                e.Handled = true;
            };
            face.MouseMove += (_, e) =>
            {
                if (dragPanel != panel || e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(stage);
                panel.ShiftXmm = dragShiftX + (now.X - dragStart.X) / scale;
                panel.ShiftYmm = dragShiftY + (now.Y - dragStart.Y) / scale;
                Canvas.SetLeft(face, originLeft + (now.X - dragStart.X));
                Canvas.SetTop(face, originTop + (now.Y - dragStart.Y));
            };
            face.MouseLeftButtonUp += (_, _) =>
            {
                if (dragPanel != panel) return;
                face.ReleaseMouseCapture();
                dragPanel = null;
                Persist();
                Redraw();
            };
            stage.Children.Add(face);
        }
    }

    void DrawFields()
    {
        fields.Children.Clear();
        var panel = panels.FirstOrDefault(p => p.Id == selected);
        if (panel == null) return;
        var box = CursorCross.Arrange(panels).First(b => b.Id == panel.Id);
        var title = new TextBlock { Text = panel.Id + " · " + panel.W + "×" + panel.H, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        fields.Children.Add(title);
        string edid = panel.WidthMm > 0 && panel.HeightMm > 0 ? "EDID " + panel.WidthMm + "×" + panel.HeightMm + " mm" : "EDID 없음";
        fields.Children.Add(new TextBlock { Text = edid, Foreground = (Brush)FindResource("Secondary"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Field("가로 mm", ((int)Math.Round(box.W)).ToString(), text =>
        {
            if (!int.TryParse(text, out int n) || n <= 0) { panel.OverrideWmm = 0; return; }
            panel.OverrideWmm = n;
        }));
        row.Children.Add(Field("세로 mm", ((int)Math.Round(box.H)).ToString(), text =>
        {
            if (!int.TryParse(text, out int n) || n <= 0) { panel.OverrideHmm = 0; return; }
            panel.OverrideHmm = n;
        }));
        double inches = CursorCross.DiagonalInches(box.W, box.H);
        row.Children.Add(Field("대각선 in", inches <= 0 ? "" : inches.ToString("0.0"), text =>
        {
            if (!double.TryParse(text, out double n) || n <= 0) return;
            var mm = CursorCross.MmFromDiagonal(n, panel.W, panel.H);
            panel.OverrideWmm = mm.W;
            panel.OverrideHmm = mm.H;
        }));
        fields.Children.Add(row);
        var shift = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        shift.Children.Add(Field("가로 오프셋", panel.ShiftXmm.ToString("0.#"), text =>
        {
            panel.ShiftXmm = double.TryParse(text, out double n) ? n : 0;
        }));
        shift.Children.Add(Field("세로 오프셋", panel.ShiftYmm.ToString("0.#"), text =>
        {
            panel.ShiftYmm = double.TryParse(text, out double n) ? n : 0;
        }));
        var reset = new Button { Content = "EDID로", Padding = new Thickness(14, 8, 14, 8), VerticalAlignment = VerticalAlignment.Bottom };
        reset.Click += (_, _) =>
        {
            panel.OverrideWmm = 0;
            panel.OverrideHmm = 0;
            panel.ShiftXmm = 0;
            panel.ShiftYmm = 0;
            Persist();
            Redraw();
        };
        shift.Children.Add(reset);
        fields.Children.Add(shift);
    }

    StackPanel Field(string caption, string value, Action<string> edited)
    {
        var box = new TextBox { Text = value, Width = 96, FontSize = 13, Margin = new Thickness(0, 2, 0, 0) };
        box.LostFocus += (_, _) =>
        {
            if (guard) return;
            guard = true;
            edited(box.Text.Trim());
            Persist();
            guard = false;
            DrawCanvas();
        };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        stack.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = (Brush)FindResource("Secondary") });
        stack.Children.Add(box);
        return stack;
    }

    void Persist()
    {
        AppSettings.SetPhysical(panels.Select(p => new PhysicalTune
        {
            Id = p.Id,
            WidthMm = p.OverrideWmm,
            HeightMm = p.OverrideHmm,
            ShiftXmm = p.ShiftXmm,
            ShiftYmm = p.ShiftYmm
        }).ToList());
    }
}
