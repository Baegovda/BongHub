using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BongHub;

/// <summary>Dark translucent dock at the cursor. Tests build it off-screen and never start a program.</summary>
public sealed class AirDockWindow : Window
{
    static AirDockWindow? current;
    readonly AirProgram[] items;
    readonly AirCells cells;
    int hover = -1;
    bool taken;
    readonly Border[] cellsVisual;

    AirDockWindow(AirProgram[] items, AirCells cells)
    {
        this.items = items;
        this.cells = cells;
        cellsVisual = new Border[items.Length];
        ThemeMark.SetOn(this, true);
        WithoutStylus(this);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = Math.Max(40, cells.Width);
        Height = Math.Max(40, cells.Height);
        Content = Build();
        Closed += (_, _) =>
        {
            try { ReleaseMouseCapture(); } catch { }
            try { Mouse.Capture(null); } catch { }
            if (ReferenceEquals(current, this)) current = null;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            taken = true;
            CloseCurrent();
        };
    }

    public static AirDockWindow? Current => current;

    public static void OpenAtCursor()
    {
        if (!AirDockHotkey.NativeAllowed || ConfigStore.AirItems.Count == 0) return;
        var cursor = Win32.Cursor();
        int x = 0, y = 0, w = 1920, h = 1080, dpi = 100;
        bool found = false;
        try
        {
            foreach (var screen in Win32.DisplayIdentities())
            {
                if (cursor.X < screen.X || cursor.Y < screen.Y || cursor.X >= screen.X + screen.W || cursor.Y >= screen.Y + screen.H) continue;
                x = screen.X; y = screen.Y; w = screen.W; h = screen.H; dpi = screen.Scale <= 0 ? 100 : screen.Scale;
                found = true;
                break;
            }
        }
        catch (Exception ex) { AppEnv.Log("airdock: " + ex.Message); }
        if (!found)
        {
            foreach (var mon in Win32.Monitors())
            {
                if (cursor.X < mon.X || cursor.Y < mon.Y || cursor.X >= mon.X + mon.W || cursor.Y >= mon.Y + mon.H) continue;
                x = mon.X; y = mon.Y; w = mon.W; h = mon.H;
                found = true;
                break;
            }
        }
        if (!found) return;
        var items = ConfigStore.AirItems.ToArray();
        var cells = AirDockLayout.Cells(ConfigStore.AirIcon, items.Length);
        var frame = AirDockLayout.Place(cursor.X, cursor.Y, x, y, w, h, dpi, ConfigStore.AirIcon, items.Length);
        CloseCurrent();
        var dock = new AirDockWindow(items, cells);
        current = dock;
        dock.Left = -32000;
        dock.Top = -32000;
        WithoutStylus(dock);
        dock.Show();
        double scale = Math.Max(96, dpi) / 100.0;
        var hwnd = Win32.Handle(dock);
        Win32.PlaceTopmost(hwnd, frame.X, frame.Y, Math.Max(1, (int)Math.Round(cells.Width * scale)), Math.Max(1, (int)Math.Round(cells.Height * scale)));
        if (dock.Content is UIElement root) Fx.Appear(root, 0.92, 280);
    }

    public static AirDockWindow Present(IReadOnlyList<AirProgram> programs, int iconDip)
    {
        var list = (programs ?? Array.Empty<AirProgram>()).ToArray();
        var cells = AirDockLayout.Cells(iconDip, list.Length);
        var dock = new AirDockWindow(list, cells);
        current = dock;
        dock.WindowStartupLocation = WindowStartupLocation.Manual;
        dock.Left = -32000;
        dock.Top = -32000;
        WithoutStylus(dock);
        dock.Show();
        return dock;
    }

    /// <summary>Close every dock and drop mouse capture. Call this before the dispatcher shuts down.</summary>
    public static void ShutdownForExit()
    {
        try { Mouse.Capture(null); } catch { }
        AirDockWindow[] open;
        try { open = Application.Current?.Windows.OfType<AirDockWindow>().ToArray() ?? Array.Empty<AirDockWindow>(); }
        catch { open = current == null ? Array.Empty<AirDockWindow>() : new[] { current }; }
        current = null;
        foreach (var dock in open)
        {
            try { dock.ReleaseMouseCapture(); } catch { }
            try { dock.Close(); } catch { }
        }
        try { Mouse.Capture(null); } catch { }
    }

    static void WithoutStylus(Window window)
    {
        try
        {
            Stylus.SetIsFlicksEnabled(window, false);
            Stylus.SetIsPressAndHoldEnabled(window, false);
            Stylus.SetIsTapFeedbackEnabled(window, false);
            Stylus.SetIsTouchFeedbackEnabled(window, false);
        }
        catch { }
    }

    public static string? ReleasePath()
    {
        var dock = current;
        if (dock == null || dock.taken || dock.hover < 0 || dock.hover >= dock.items.Length) return null;
        dock.taken = true;
        return dock.items[dock.hover].Path;
    }

    public static void CloseCurrent()
    {
        var dock = current;
        current = null;
        if (dock == null) return;
        try { dock.Close(); } catch { }
    }

    UIElement Build()
    {
        var shell = new Border
        {
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.45, Color = Colors.Black }
        };
        var canvas = new Canvas { Width = cells.Width, Height = cells.Height, IsHitTestVisible = true };
        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var icon = cells.Icons[i];
            var label = cells.Labels[i];
            var tile = new Border
            {
                Width = icon.W,
                Height = icon.H,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                ClipToBounds = true,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = Face(items[i], icon.W)
            };
            Canvas.SetLeft(tile, icon.X);
            Canvas.SetTop(tile, icon.Y);
            var caption = new TextBlock
            {
                Text = items[i].Name,
                Width = label.W,
                Height = label.H,
                FontSize = 12,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            Canvas.SetLeft(caption, label.X);
            Canvas.SetTop(caption, label.Y);
            cellsVisual[i] = tile;
            canvas.Children.Add(tile);
            canvas.Children.Add(caption);
            tile.MouseEnter += (_, _) => SetHover(index);
            caption.MouseEnter += (_, _) => SetHover(index);
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Activate(index); };
            caption.MouseLeftButtonUp += (_, e) => { e.Handled = true; Activate(index); };
        }
        canvas.MouseLeave += (_, _) => SetHover(-1);
        shell.Child = canvas;
        return shell;
    }

    UIElement Face(AirProgram program, int size)
    {
        var image = AppIcon.Lookup(program.Name, program.Path);
        if (image != null) return new Image { Source = image, Width = size - 16, Height = size - 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        string letter = program.Name.Length > 0 ? program.Name[..1] : "•";
        return new TextBlock
        {
            Text = letter,
            FontSize = Math.Max(16, size / 3),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    void SetHover(int index)
    {
        if (hover == index) return;
        if (hover >= 0 && hover < cellsVisual.Length) Spring(cellsVisual[hover], 1);
        hover = index;
        if (hover >= 0 && hover < cellsVisual.Length) Spring(cellsVisual[hover], 1.12);
    }

    static void Spring(Border tile, double to)
    {
        if (tile.RenderTransform is not ScaleTransform scale) return;
        if (!Fx.Enabled) { scale.ScaleX = to; scale.ScaleY = to; return; }
        var anim = Fx.Anim(to, 220, Fx.Spring);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    void Activate(int index)
    {
        if (taken || index < 0 || index >= items.Length) return;
        taken = true;
        string path = items[index].Path;
        CloseCurrent();
        AirDockLaunch.Run(path);
    }
}
