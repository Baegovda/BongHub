using System.Text.RegularExpressions;

namespace BongHub.Next;

/// <summary>Same link shape as 3.4.9 RichEdit: http(s), mailto, or www. Clicking does not rewrite the RTF.</summary>
public static class NoteLinks
{
    static readonly Regex Rx = new(@"(?i)(?:https?://|mailto:|www\.)[^\s<>]+", RegexOptions.CultureInvariant);

    public static bool TryHit(string text, int index, out string url)
    {
        url = "";
        if (string.IsNullOrEmpty(text) || index < 0) return false;
        foreach (Match m in Rx.Matches(text))
        {
            if (index >= m.Index && index < m.Index + m.Length)
            {
                url = m.Value;
                if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
                return true;
            }
        }
        return false;
    }
}
