using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BongHub;

/// <summary>
/// Finds text that is cut off without an ellipsis. Measurement uses the element's own DPI,
/// so the same tree passes at 96 DPI and at 120 DPI.
/// </summary>
public static class UiOverflow
{
    public static string First(DependencyObject? root)
    {
        if (root == null) return "";
        string hit = "";
        void Walk(DependencyObject node)
        {
            if (hit.Length > 0) return;
            if (node is TextBlock tb && tb.IsVisible && !string.IsNullOrEmpty(tb.Text) && tb.TextTrimming == TextTrimming.None && tb.ActualWidth > 2 && tb.ActualHeight > 2)
            {
                double dpi = 1;
                try { dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip; } catch { dpi = 1; }
                if (dpi < 0.5 || double.IsNaN(dpi)) dpi = 1;
                var formatted = new FormattedText(
                    tb.Text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                    tb.FontSize,
                    Brushes.Black,
                    dpi);
                if (tb.TextWrapping != TextWrapping.NoWrap)
                    formatted.MaxTextWidth = Math.Max(1, tb.ActualWidth);
                bool wide = tb.TextWrapping == TextWrapping.NoWrap && formatted.Width > tb.ActualWidth + 3;
                bool tall = formatted.Height > tb.ActualHeight + 8;
                if (wide || tall)
                    hit = (wide ? "wide " : "tall ") + tb.Text.Replace("\r", " ").Replace("\n", " ");
            }
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
            for (int i = 0; i < count && hit.Length == 0; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return hit;
    }

    /// <summary>A profile action button whose content is still a font glyph (including the ellipsis character).</summary>
    public static int FontIconButtons(DependencyObject? root)
    {
        if (root == null) return 0;
        int n = 0;
        void Walk(DependencyObject node)
        {
            if (node is Button button && button.Content is string text && text.Length > 0 && text.Length <= 3 && (text[0] >= '\uE000' || text is "⋯" or "..." or "…"))
                n++;
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return n;
    }

    public static int Paths(DependencyObject? root)
    {
        if (root == null) return 0;
        int n = 0;
        void Walk(DependencyObject node)
        {
            if (node is System.Windows.Shapes.Path) n++;
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return n;
    }

    public static int Images(DependencyObject? root)
    {
        if (root == null) return 0;
        int n = 0;
        void Walk(DependencyObject node)
        {
            if (node is Image) n++;
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return n;
    }
}
