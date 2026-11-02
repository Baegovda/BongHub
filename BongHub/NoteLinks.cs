using System.Text.RegularExpressions;
using System.Windows;

namespace BongHub;

/// <summary>
/// Link shape matches 3.4.9 RichEdit: http(s), mailto, or www.
/// The underline and color are display-only. The saved RTF stays plain text.
/// </summary>
public static class NoteLinks
{
    public const string Tip = "Ctrl+클릭으로 열기";
    static readonly Regex Rx = new(@"(?i)(?:https?://|mailto:|www\.)[^\s<>]+", RegexOptions.CultureInvariant);

    public readonly record struct Hit(int Start, int Length, string Url);

    public static readonly DependencyProperty DisplayProperty = DependencyProperty.RegisterAttached(
        "Display", typeof(bool), typeof(NoteLinks), new FrameworkPropertyMetadata(false));

    public static void SetDisplay(DependencyObject element, bool value) => element.SetValue(DisplayProperty, value);
    public static bool GetDisplay(DependencyObject element) => element != null && (bool)element.GetValue(DisplayProperty);

    public static List<Hit> Find(string? text)
    {
        var list = new List<Hit>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (Match m in Rx.Matches(text))
        {
            int len = m.Length;
            while (len > 0 && IsTrail(m.Value[len - 1])) len--;
            if (len < 4) continue;
            string raw = m.Value[..len];
            string url = raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + raw : raw;
            list.Add(new Hit(m.Index, len, url));
        }
        return list;
    }

    public static bool TryHit(string text, int index, out string url)
    {
        url = "";
        if (string.IsNullOrEmpty(text) || index < 0) return false;
        foreach (var hit in Find(text))
        {
            if (index < hit.Start || index >= hit.Start + hit.Length) continue;
            url = hit.Url;
            return true;
        }
        return false;
    }

    static bool IsTrail(char c) => c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '"' or '\'';
}
