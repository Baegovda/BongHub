using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace BongHub;

/// <summary>
/// Tray icon via H.NotifyIcon (no WinForms). Left click opens settings, right click opens the dark menu, 종료 is the only real exit.
/// Smoke and note tests pass registerShell: false so they never call Shell_NotifyIcon.
/// </summary>
public sealed class TrayService : IDisposable
{
    public const string BalloonTitle = "BongHub";
    readonly App app;
    readonly Action exit;
    readonly TaskbarIcon? icon;
    public ContextMenu Menu { get; }
    public bool ShellIconCreated => icon?.IsCreated == true;
    public int NativeBalloonCalls { get; private set; }
    public string? LastBalloonTitle { get; private set; }
    public string? LastBalloonMessage { get; private set; }
    public FrameworkElement? LastBalloonVisual { get; private set; }
    public TextBlock? LastBalloonSample { get; private set; }
    /// <summary>True when 종료 is the same App.RequestShutdown the process already uses.</summary>
    public bool ExitRequestsShutdown => exit.Target is App && exit.Method.DeclaringType == typeof(App) && exit.Method.Name == nameof(App.RequestShutdown);

    public TrayService(App app, bool registerShell)
    {
        this.app = app;
        exit = app.RequestShutdown;
        Menu = BuildMenu();
        if (!registerShell) return;

        icon = new TaskbarIcon
        {
            ToolTipText = BalloonTitle,
            ContextMenu = Menu,
            MenuActivation = PopupActivationMode.RightClick,
            PopupActivation = PopupActivationMode.None,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(OnLeftClick),
            Icon = LoadBlockIcon(),
            Visibility = Visibility.Visible,
        };
        // Loaded is not raised for an icon that is not in a tree. ForceCreate(false) keeps Efficiency Mode off so the baseline is not skewed.
        icon.ForceCreate(enablesEfficiencyMode: false);
        AppEnv.NoteTrayVisible();
    }

    public void OnLeftClick() => app.ShowSettings(null, user: true);
    public void OnNewNote() => NoteManager.NewNote();
    public void OnExit() => exit();

    public bool RaiseHeader(string header)
    {
        var item = Menu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == header);
        if (item == null) return false;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        return true;
    }

    /// <summary>Records the balloon. The native balloon is sent only after the shell icon exists.</summary>
    public void ShowBalloon(string message, string? title = null)
    {
        title ??= BalloonTitle;
        LastBalloonTitle = title;
        LastBalloonMessage = message;
        (LastBalloonVisual, LastBalloonSample) = BuildBalloon(title, message);
        if (icon is not { IsCreated: true }) return;
        NativeBalloonCalls++;
        icon.ShowNotification(title, message, NotificationIcon.Info, sound: false, respectQuietTime: true);
    }

    /// <summary>Same chrome as the ContextMenu style, so smoke can render it without opening a popup on the desktop.</summary>
    public (FrameworkElement Root, FrameworkElement Sample) CreateChromeVisual()
    {
        var panel = new StackPanel();
        FrameworkElement? sample = null;
        var textBrush = (Brush)Application.Current.FindResource("Text");
        foreach (var item in Menu.Items)
        {
            if (item is MenuItem mi)
            {
                var text = new TextBlock { Text = mi.Header as string ?? "", Foreground = textBrush };
                var copy = new MenuItem { Header = text, Foreground = textBrush, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
                sample ??= text;
                panel.Children.Add(copy);
            }
            else if (item is Separator)
                panel.Children.Add(new Separator { Style = (Style)Application.Current.FindResource(typeof(Separator)) });
        }
        var root = new Border
        {
            Background = (Brush)Application.Current.FindResource("MenuBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MenuStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            Width = 220,
            Child = panel,
        };
        return (root, sample ?? root);
    }

    public void Dispose()
    {
        if (icon == null) return;
        icon.Dispose();
    }

    readonly List<MenuItem> profileItems = new();
    Separator? profileSep;

    /// <summary>Puts the applied profile first in the menu with a check. Names are the profile names, not the check prefix.</summary>
    public void SetProfiles(IReadOnlyList<string> names, string active, Action<string>? apply = null)
    {
        foreach (var item in profileItems) Menu.Items.Remove(item);
        profileItems.Clear();
        if (profileSep != null) { Menu.Items.Remove(profileSep); profileSep = null; }
        int exit = -1;
        for (int i = 0; i < Menu.Items.Count; i++)
            if (Menu.Items[i] is MenuItem mi && (mi.Header as string) == "종료") exit = i;
        if (exit < 0 || names == null || names.Count == 0) return;
        int at = exit;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string label = string.Equals(name, active, StringComparison.Ordinal) ? "✓ " + name : name;
            string captured = name;
            var item = Item(label, () => apply?.Invoke(captured));
            profileItems.Add(item);
            Menu.Items.Insert(at++, item);
        }
        if (profileItems.Count == 0) return;
        profileSep = new Separator { Style = (Style)Application.Current.FindResource(typeof(Separator)) };
        Menu.Items.Insert(at, profileSep);
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)) };
        menu.Items.Add(Item("BongHub 열기", OnLeftClick));
        var newer = Item("새 스티커 메모", OnNewNote);
        string hk = NoteHotkey.Normalize(ConfigStore.Notes.Hotkey);
        if (hk.Length > 0) newer.InputGestureText = hk;
        menu.Items.Add(newer);
        menu.Items.Add(Item("스티커 메모 모두 보이기", () => NoteManager.ShowAll()));
        menu.Items.Add(Item("스티커 메모 모두 숨기기", () => NoteManager.HideAll()));
        menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource(typeof(Separator)) });
        menu.Items.Add(Item("종료", OnExit));
        return menu;
    }

    MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
        item.Click += (_, _) => app.Dispatcher.BeginInvoke(action);
        return item;
    }

    static (FrameworkElement Root, TextBlock Sample) BuildBalloon(string title, string message)
    {
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = (Brush)Application.Current.FindResource("Text") };
        var body = new TextBlock { Text = message, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = (Brush)Application.Current.FindResource("Secondary") };
        var panel = new StackPanel();
        panel.Children.Add(heading);
        panel.Children.Add(body);
        var root = new Border
        {
            Background = (Brush)Application.Current.FindResource("MenuBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MenuStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Width = 260,
            Child = panel,
        };
        return (root, heading);
    }

    static System.Drawing.Icon LoadBlockIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var extracted = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (extracted != null) return extracted;
        }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    sealed class RelayCommand : ICommand
    {
        readonly Action action;
        public RelayCommand(Action action) => this.action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
