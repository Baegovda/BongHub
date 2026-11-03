using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BongHub;

/// <summary>
/// One topmost card per monitor, pinned with <see cref="Win32.PlaceTopmost"/> in physical pixels.
/// Tests never call <see cref="Show"/>; they only compute <see cref="IdentifyLayout"/>.
/// </summary>
public static class IdentifyFlash
{
    public static void Show(IReadOnlyList<ProfileMonitor> monitors)
    {
        if (monitors == null || !DisplayGate.RealAllowed) return;
        foreach (var mon in monitors)
        {
            if (mon.W < 8 || mon.H < 8) continue;
            var card = IdentifyLayout.Of(mon);
            var host = NewCard(CardBody(mon.Number.ToString(), 72), card, mon.Scale, "identify-card");
            var life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fx.Enabled ? 2500 : 2500) };
            life.Tick += (_, _) =>
            {
                life.Stop();
                Close(host);
            };
            life.Start();
        }
    }

    internal static Window NewCard(UIElement body, ScreenCard place, int scalePercent, string tag)
    {
        int scale = scalePercent < 50 ? 100 : scalePercent;
        double dip = scale / 100.0;
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Width = Math.Max(40, place.W / dip),
            Height = Math.Max(40, place.H / dip),
            IsHitTestVisible = false,
            Content = body,
            Tag = tag
        };
        void Pin()
        {
            var h = Win32.Handle(host);
            if (h == IntPtr.Zero) return;
            long ex = Win32.ExStyle(h);
            Win32.SetExStyle(h, ex | 0x080000A0);
            Win32.PlaceTopmost(h, place.X, place.Y, place.W, place.H);
        }
        host.SourceInitialized += (_, _) =>
        {
            var h = Win32.Handle(host);
            HwndSource.FromHwnd(h)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == Win32.WM_MOUSEACTIVATE)
                {
                    handled = true;
                    return new IntPtr(Win32.MA_NOACTIVATE);
                }
                if (msg == Win32.WM_WINDOWPOSCHANGING)
                    Win32.ForcePhysical(lp, place.X, place.Y, place.W, place.H);
                return IntPtr.Zero;
            });
            Pin();
        };
        host.DpiChanged += (_, _) => Pin();
        host.ContentRendered += (_, _) => Pin();
        host.Show();
        Pin();
        if (Fx.Enabled && body is FrameworkElement fe)
        {
            fe.Opacity = 0;
            Fx.Appear(fe, 0.96, 220);
        }
        return host;
    }

    static void Close(Window host)
    {
        if (host.Content is UIElement body && Fx.Enabled)
            Fx.Disappear(body, () => { try { host.Close(); } catch { } });
        else
            try { host.Close(); } catch { }
    }

    public static Border CardBody(string text, double font)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = font,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x14, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = row
        };
    }
}

/// <summary>Words on the profile-switch card. The window only paints them.</summary>
public static class OsdCopy
{
    public static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "초";
    }

    public static string Line(string? phase, double seconds, string? error)
    {
        if (phase == "fail") return string.IsNullOrWhiteSpace(error) ? "실패" : error!.Trim();
        if (phase == "done") return "완료 · " + Clock(seconds);
        return Clock(seconds);
    }
}

/// <summary>Profile-switch card. Off-screen under <c>--off</c>. Otherwise pinned on the primary monitor in physical pixels.</summary>
public static class ProfileOsd
{
    static Window? host;
    static TextBlock? nameText;
    static TextBlock? statusText;
    static Ellipse? ring;
    static TextBlock? mark;
    static DispatcherTimer? clock;
    static readonly System.Diagnostics.Stopwatch Watch = new();
    public static string Phase { get; private set; } = "";
    public static string Status { get; private set; } = "";

    public static void Show(string name, IReadOnlyList<ProfileMonitor>? monitors = null)
    {
        CloseNow();
        Phase = "run";
        Status = OsdCopy.Line("run", 0, null);
        var row = new Grid { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        ring = new Ellipse
        {
            Width = 28, Height = 28,
            Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0xC8, 0xFA)),
            StrokeThickness = 3,
            StrokeDashArray = new DoubleCollection { 2.2, 1.5 },
            StrokeDashCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            IsHitTestVisible = false,
            Tag = "osd-spin"
        };
        mark = new TextBlock
        {
            Text = "",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Tag = "osd-mark"
        };
        badge.Children.Add(ring);
        badge.Children.Add(mark);
        row.Children.Add(badge);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameText = new TextBlock
        {
            Text = name.Length > 0 ? name : "프로필",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = 250,
            Tag = "osd-name"
        };
        statusText = new TextBlock
        {
            Text = Status,
            FontSize = 13,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xF5, 0xF7, 0xFA)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = 250,
            Tag = "osd-status"
        };
        words.Children.Add(nameText);
        words.Children.Add(statusText);
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var shell = new Border
        {
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = row
        };
        if (AppEnv.Off)
        {
            host = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                Width = 360,
                Height = 108,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                IsHitTestVisible = false,
                Content = shell,
                Tag = "profile-osd"
            };
            host.Show();
        }
        else
        {
            var home = IdentifyLayout.HostOf(monitors);
            ScreenCard place = home != null
                ? IdentifyLayout.Center(home, 360, 108)
                : new ScreenCard(0, false, -30000, -30000, 360, 108);
            host = IdentifyFlash.NewCard(shell, place, home?.Scale ?? 100, "profile-osd");
        }
        Spin(true);
        Watch.Restart();
        clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        clock.Tick += (_, _) => Paint(OsdCopy.Line("run", Watch.Elapsed.TotalSeconds, null));
        clock.Start();
    }

    /// <summary>Test hook. Sets the running clock without waiting.</summary>
    public static void Advance(double seconds)
    {
        if (Phase != "run") return;
        Paint(OsdCopy.Line("run", seconds, null));
    }

    public static void Succeed(double? seconds = null) => Finish("done", seconds ?? Watch.Elapsed.TotalSeconds, null);
    public static void Fail(string? error, double? seconds = null) => Finish("fail", seconds ?? Watch.Elapsed.TotalSeconds, error);

    static void Finish(string phase, double seconds, string? error)
    {
        Phase = phase;
        clock?.Stop();
        Watch.Stop();
        Spin(false);
        if (mark != null)
        {
            mark.Text = phase == "done" ? "✓" : "!";
            mark.Foreground = new SolidColorBrush(phase == "done" ? Color.FromRgb(0x34, 0xC7, 0x59) : Color.FromRgb(0xFF, 0x9F, 0x0A));
            mark.Visibility = Visibility.Visible;
        }
        Paint(OsdCopy.Line(phase, seconds, error));
        var life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fx.Enabled ? 900 : 40) };
        life.Tick += (_, _) =>
        {
            life.Stop();
            Fade();
        };
        life.Start();
    }

    static void Paint(string line)
    {
        Status = line;
        if (statusText != null) statusText.Text = line;
    }

    static void Spin(bool on)
    {
        if (ring?.RenderTransform is not RotateTransform spin) return;
        spin.BeginAnimation(RotateTransform.AngleProperty, null);
        if (!on) { ring.Visibility = Visibility.Collapsed; return; }
        ring.Visibility = Visibility.Visible;
        if (mark != null) mark.Visibility = Visibility.Collapsed;
        if (!Fx.Enabled) return;
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever };
        spin.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    static void Fade()
    {
        var window = host;
        if (window == null) return;
        if (window.Content is UIElement body && Fx.Enabled)
            Fx.Disappear(body, () => { try { window.Close(); } catch { } if (ReferenceEquals(host, window)) host = null; });
        else
        {
            try { window.Close(); } catch { }
            if (ReferenceEquals(host, window)) host = null;
        }
    }

    static void CloseNow()
    {
        clock?.Stop();
        clock = null;
        var window = host;
        host = null;
        if (window == null) return;
        try { window.Close(); } catch { }
    }
}
