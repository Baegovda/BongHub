using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BongHub;

/// <summary>Large profile editor. It edits the same profiles as the settings page.</summary>
public sealed class ProfileEditorWindow : Window
{
    readonly MainWindow owner;
    readonly StackPanel host = new();
    DispatcherTimer? settle;

    public ProfileEditorWindow(MainWindow owner)
    {
        this.owner = owner;
        Title = "프로필 편집";
        Width = 1180;
        Height = 820;
        MinWidth = 760;
        MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12));
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 8, 12, 16)
        };
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
