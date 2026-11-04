using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace BongHub;

/// <summary>Large profile editor. Same profiles as the settings page, in the app's dark window.</summary>
public sealed class ProfileEditorWindow : Window
{
    readonly MainWindow owner;
    readonly StackPanel host = new() { Margin = new Thickness(28, 8, 32, 28) };
    DispatcherTimer? settle;

    public ProfileEditorWindow(MainWindow owner)
    {
        this.owner = owner;
        Title = "프로필 편집";
        Width = 1180;
        Height = 820;
        MinWidth = 760;
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
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var root = new Grid();
        root.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0xD0, 0x0D, 0x10, 0x18),
                Color.FromArgb(0xD0, 0x14, 0x12, 0x24),
                new Point(0, 0), new Point(1, 1))
        });
        var dock = new DockPanel();
        var caption = new Grid { Height = 52 };
        caption.Children.Add(new TextBlock
        {
            Text = "프로필 편집",
            FontFamily = (FontFamily)FindResource("DisplayFont"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(28, 0, 0, 0)
        });
        var close = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "\uE8BB",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            ToolTip = "닫기"
        };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        caption.Children.Add(close);
        DockPanel.SetDock(caption, Dock.Top);
        dock.Children.Add(caption);
        dock.Children.Add(new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Padding = new Thickness(4, 0, 8, 12)
        });
        root.Children.Add(dock);
        Content = root;
        WindowFx.Attach(this, root, wantBackdrop: true, solid: Color.FromRgb(0x0B, 0x0D, 0x12));
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) =>
        {
            if (AppEnv.Off || AppEnv.Smoke || AppEnv.NoteTest) return;
            settle ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            settle.Stop();
            settle.Tick -= OnSettled;
            settle.Tick += OnSettled;
            settle.Start();
        };
    }

    void OnSettled(object? sender, EventArgs e)
    {
        settle?.Stop();
        Redraw();
    }

    public void Redraw()
    {
        host.Children.Clear();
        owner.FillProfileEditor(host);
    }
}
