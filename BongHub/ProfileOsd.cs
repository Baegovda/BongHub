using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

/// <summary>Profile-switch card. Off-screen under <c>--off</c>. Otherwise pinned on the primary monitor in physical pixels.</summary>
public static class ProfileOsd
{
    public static void Show(string name, IReadOnlyList<ProfileMonitor>? monitors = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 22, 0) };
        var mark = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0x4C, 0x8A, 0xFE), 40),
            Margin = new Thickness(0, 0, 14, 0)
        };
        mark.Child = new TextBlock { Text = "▣", FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(mark);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "프로필", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xF5, 0xF7, 0xFA)) });
        words.Children.Add(new TextBlock { Text = name.Length > 0 ? name : "프로필", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
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
            var host = new Window
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
            return;
        }
        var home = IdentifyLayout.HostOf(monitors);
        ScreenCard place = home != null
            ? IdentifyLayout.Center(home, 360, 108)
            : new ScreenCard(0, false, -30000, -30000, 360, 108);
        var live = IdentifyFlash.NewCard(shell, place, home?.Scale ?? 100, "profile-osd");
        var life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fx.Enabled ? 1600 : 400) };
        life.Tick += (_, _) =>
        {
            life.Stop();
            if (live.Content is UIElement body && Fx.Enabled)
                Fx.Disappear(body, () => { try { live.Close(); } catch { } });
            else
                try { live.Close(); } catch { }
        };
        life.Start();
    }
}
