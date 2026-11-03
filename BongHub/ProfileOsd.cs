using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BongHub;

/// <summary>Big identify numbers. Constructed only when a real identify runs, never from tests.</summary>
public static class IdentifyFlash
{
    public static void Show(IReadOnlyList<ProfileMonitor> monitors)
    {
        foreach (var mon in monitors)
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                Width = Math.Max(80, mon.W),
                Height = Math.Max(80, mon.H),
                Left = mon.X,
                Top = mon.Y,
                IsHitTestVisible = false
            };
            w.Content = new TextBlock
            {
                Text = mon.Number.ToString(),
                FontSize = 160,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            w.SourceInitialized += (_, _) =>
            {
                var h = Win32.Handle(w);
                long ex = Win32.ExStyle(h);
                Win32.SetExStyle(h, ex | 0x08000020);
            };
            w.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            timer.Tick += (_, _) => { timer.Stop(); w.Close(); };
            timer.Start();
        }
    }
}

/// <summary>Profile-switch card. Off-screen and non-activating under <c>--off</c> and tests.</summary>
public static class ProfileOsd
{
    public static void Show(string name)
    {
        var card = new Border
        {
            Width = 360,
            Height = 108,
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 22, 0) };
        var mark = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0x4C, 0x8A, 0xFE), 40),
            Margin = new Thickness(0, 0, 14, 0)
        };
        mark.Child = new TextBlock { Text = "▣", FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(mark);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "프로필", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xF5, 0xF7, 0xFA)) });
        words.Children.Add(new TextBlock { Text = name.Length > 0 ? name : "프로필", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(words);
        card.Child = row;
        card.RenderTransform = new ScaleTransform(Fx.Enabled ? 0.94 : 1, Fx.Enabled ? 0.94 : 1);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
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
            IsHitTestVisible = false,
            Content = card,
            Tag = "profile-osd"
        };
        if (AppEnv.Off)
        {
            host.Left = -30000;
            host.Top = -30000;
            host.WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            host.WindowStartupLocation = WindowStartupLocation.Manual;
            host.Left = SystemParameters.VirtualScreenLeft + Math.Max(24, (SystemParameters.VirtualScreenWidth - 360) / 2);
            host.Top = SystemParameters.VirtualScreenTop + 72;
        }
        host.SourceInitialized += (_, _) =>
        {
            var h = Win32.Handle(host);
            long ex = Win32.ExStyle(h);
            Win32.SetExStyle(h, ex | 0x08000020);
        };
        host.Show();
        if (Fx.Enabled)
        {
            Fx.Appear(card);
            if (card.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Fx.Anim(0.94, 1, 280, Fx.OutQuint));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Fx.Anim(0.94, 1, 280, Fx.OutQuint));
            }
        }
        var life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Fx.Enabled ? 1500 : 400) };
        life.Tick += (_, _) =>
        {
            life.Stop();
            if (!Fx.Enabled) { host.Close(); return; }
            Fx.Disappear(card, () => { try { host.Close(); } catch { } });
        };
        life.Start();
    }
}
