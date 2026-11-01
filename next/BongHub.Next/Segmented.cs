using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BongHub.Next;

/// <summary>iOS segmented control: a pill that springs to the selected segment.</summary>
public sealed class Segmented : Border
{
    readonly Grid grid = new();
    readonly Border pill;
    readonly TranslateTransform pillT = new();
    readonly List<TextBlock> labels = new();
    public double SegmentWidth { get; }
    public int SelectedIndex { get; private set; }
    public event Action<int>? Changed;

    public Segmented(IEnumerable<string> items, int selected, double segmentWidth = 64)
    {
        SegmentWidth = segmentWidth;
        CornerRadius = new CornerRadius(10); Background = new SolidColorBrush(Color.FromArgb(0x33, 0x76, 0x76, 0x80)); Padding = new Thickness(2);
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Center;
        pill = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0x72)), Width = segmentWidth, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = pillT,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.25, Direction = 270 } };
        grid.Children.Add(pill);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        int i = 0;
        foreach (var it in items)
        {
            int idx = i++;
            var tb = new TextBlock { Text = it, Width = segmentWidth, Height = 28, Padding = new Thickness(0, 5, 0, 0), TextAlignment = TextAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            tb.MouseLeftButtonUp += (_, _) => Select(idx, true);
            labels.Add(tb); row.Children.Add(tb);
        }
        grid.Children.Add(row);
        Child = grid;
        Select(selected, false);
    }
    public bool Select(int idx, bool notify)
    {
        if (idx < 0 || idx >= labels.Count || !IsEnabled) return false;
        bool changed = idx != SelectedIndex;
        SelectedIndex = idx;
        if (notify) Fx.MoveX(pillT, idx * SegmentWidth, 300); else { pillT.BeginAnimation(TranslateTransform.XProperty, null); pillT.X = idx * SegmentWidth; }
        for (int i = 0; i < labels.Count; i++) labels[i].Opacity = i == idx ? 1 : 0.7;
        if (notify && changed) Changed?.Invoke(idx);
        return true;
    }
}
