using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BongHub;

/// <summary>Set by every app theme style. Smoke walks real windows and flags a control that missed it.</summary>
public static class ThemeMark
{
    public static readonly DependencyProperty OnProperty = DependencyProperty.RegisterAttached(
        "On", typeof(bool), typeof(ThemeMark), new FrameworkPropertyMetadata(false));

    public static void SetOn(DependencyObject element, bool value) => element.SetValue(OnProperty, value);
    public static bool GetOn(DependencyObject element) => (bool)element.GetValue(OnProperty);
}

public static class ThemeAudit
{
    static readonly HashSet<Type> Watched = new()
    {
        typeof(TextBox), typeof(RichTextBox), typeof(PasswordBox), typeof(ComboBox), typeof(ComboBoxItem),
        typeof(Button), typeof(ToggleButton), typeof(RepeatButton), typeof(CheckBox), typeof(RadioButton),
        typeof(Slider), typeof(ScrollBar), typeof(ScrollViewer), typeof(ContextMenu), typeof(MenuItem), typeof(Menu),
        typeof(ToolTip), typeof(ListBox), typeof(ListBoxItem), typeof(ListView), typeof(ListViewItem),
        typeof(ProgressBar), typeof(Expander), typeof(TabControl), typeof(TabItem), typeof(Separator),
        typeof(FlowDocumentScrollViewer), typeof(GridSplitter), typeof(StatusBar), typeof(ToolBar),
        typeof(DatePicker), typeof(Calendar), typeof(GroupBox), typeof(TreeView), typeof(TreeViewItem),
        typeof(Thumb),
    };

    public static List<string> Bare(params DependencyObject[] extra)
    {
        var seen = new HashSet<DependencyObject>();
        var bad = new List<string>();
        void Walk(DependencyObject? node)
        {
            if (node == null || !seen.Add(node)) return;
            if (node is FrameworkElement fe) { try { fe.ApplyTemplate(); } catch { } }
            if (Missed(node)) bad.Add(Describe(node));
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        if (Application.Current != null)
            foreach (Window w in Application.Current.Windows) Walk(w);
        foreach (var root in extra) Walk(root);
        return bad;
    }

    static bool Missed(DependencyObject node)
    {
        bool watch = Watched.Contains(node.GetType()) || (node is Window && node.GetType().Namespace == "BongHub");
        if (!watch) return false;
        if (!ThemeMark.GetOn(node)) return true;
        return node is Control c && Washed(c.Background);
    }

    static bool Washed(Brush? brush) => brush is SolidColorBrush sb && sb.Color.A > 240 && NoteColors.Lum(sb.Color) > 0.85;

    static string Describe(DependencyObject node)
    {
        string name = node is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name) ? "#" + fe.Name : "";
        string wash = node is Control c && Washed(c.Background) ? " white" : "";
        return node.GetType().Name + name + wash;
    }
}
