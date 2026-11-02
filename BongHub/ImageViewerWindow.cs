using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BongHub;

/// <summary>Double-click viewer: wheel zoom, drag, fit / 100%, Esc. No title bar. Off-screen when <c>--off</c>.</summary>
public sealed class ImageViewerWindow : Window
{
    readonly Image image;
    readonly ScaleTransform scale = new(1, 1);
    readonly TranslateTransform pan = new();
    Point last;
    bool dragging;
    double fit = 1;
    public double Zoom { get; private set; } = 1;
    public bool Launched { get; private set; }

    public ImageViewerWindow(BitmapSource source)
    {
        Title = "이미지 보기";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = !AppEnv.Off;
        Width = 640;
        Height = 480;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x1A));
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (AppEnv.Off)
        {
            double s = Win32.SystemScale; if (s < 0.5) s = 1;
            Left = (AppEnv.OffsetX + 80) / s;
            Top = (AppEnv.OffsetY + 80) / s;
        }
        SourceInitialized += (_, _) =>
        {
            if (!AppEnv.Off) return;
            int x = AppEnv.OffsetX + 80, y = AppEnv.OffsetY + 80;
            Win32.RememberOffScreen(Win32.Handle(this), x, y);
            HwndSource.FromHwnd(Win32.Handle(this))?.AddHook(Win32.OffScreenHook);
            Win32.Place(this, x, y, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight));
        };
        Closed += (_, _) => Win32.ForgetOffScreen(Win32.Handle(this));
        var frozen = source;
        if (!frozen.IsFrozen) { var c = source.Clone(); c.Freeze(); frozen = c; }
        image = new Image { Source = frozen, Stretch = Stretch.Fill, Width = Math.Max(1, frozen.PixelWidth), Height = Math.Max(1, frozen.PixelHeight) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        var inner = new TransformGroup();
        inner.Children.Add(scale);
        inner.Children.Add(pan);
        image.RenderTransform = inner;
        var canvas = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
        canvas.Children.Add(image);
        var bar = new DockPanel { Height = 32, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x0E, 0x1A)), VerticalAlignment = VerticalAlignment.Top };
        var close = new Button { Content = "닫기", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 2, 6, 2) };
        close.Click += (_, _) => Close();
        var fitBtn = new Button { Content = "맞춤", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 2, 0, 2) };
        fitBtn.Click += (_, _) => Fit();
        var full = new Button { Content = "100%", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 2, 0, 2) };
        full.Click += (_, _) => SetZoom(1, animate: false);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(fitBtn);
        bar.Children.Add(full);
        var root = new Grid();
        root.Children.Add(canvas);
        root.Children.Add(bar);
        Content = root;
        canvas.MouseWheel += (_, e) => { SetZoom(Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), animate: false); e.Handled = true; };
        canvas.MouseLeftButtonDown += (_, e) => { dragging = true; last = e.GetPosition(canvas); canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(canvas);
            pan.X += p.X - last.X; pan.Y += p.Y - last.Y; last = p;
        };
        canvas.MouseLeftButtonUp += (_, _) => { dragging = false; canvas.ReleaseMouseCapture(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key is Key.OemPlus or Key.Add) { SetZoom(Zoom * 1.25, false); e.Handled = true; }
            else if (e.Key is Key.OemMinus or Key.Subtract) { SetZoom(Zoom / 1.25, false); e.Handled = true; }
            else if (e.Key is Key.D0 or Key.NumPad0) { Fit(); e.Handled = true; }
            else if (e.Key is Key.D1 or Key.NumPad1) { SetZoom(1, false); e.Handled = true; }
        };
        Loaded += (_, _) => Fit();
        Closed += (_, _) => { image.Source = null; };
    }

    public void Fit()
    {
        double w = Math.Max(1, ActualWidth > 1 ? ActualWidth - 16 : Width - 16);
        double h = Math.Max(1, ActualHeight > 1 ? ActualHeight - 48 : Height - 48);
        fit = Math.Min(1, Math.Min(w / image.Width, h / image.Height));
        pan.X = 0; pan.Y = 0;
        SetZoom(fit, false);
    }

    public void SetZoom(double z, bool animate)
    {
        Zoom = Math.Clamp(z, 0.02, 32);
        scale.ScaleX = scale.ScaleY = Zoom;
    }

    public static ImageViewerWindow Open(BitmapSource source)
    {
        var w = new ImageViewerWindow(source);
        w.Show();
        return w;
    }
}
