using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BongHub;

/// <summary>
/// WPF RichTextBox RTF. Load and save go through <see cref="TextRange"/>. No msftedit.
/// An edited save that would drop a picture still on screen keeps the original file bytes.
/// </summary>
static class NoteRtf
{
    public static RichTextBox LoadBox(byte[] rtf)
    {
        var box = new RichTextBox();
        using var ms = new MemoryStream(rtf ?? []);
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Load(ms, DataFormats.Rtf);
        return box;
    }

    public static byte[] Save(RichTextBox box)
    {
        using var ms = new MemoryStream();
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Save(ms, DataFormats.Rtf);
        return ms.ToArray();
    }

    public static string Plain(RichTextBox box)
        => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    public static int Images(RichTextBox box)
    {
        int n = 0;
        foreach (var block in box.Document.Blocks) n += Count(block);
        return n;
    }

    public static int Picts(byte[] rtf)
    {
        if (rtf == null || rtf.Length == 0) return 0;
        var s = Encoding.ASCII.GetString(rtf);
        int n = 0;
        for (int i = s.IndexOf("\\pict", StringComparison.Ordinal); i >= 0; i = s.IndexOf("\\pict", i + 5, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Unedited notes return <paramref name="original"/> without writing. Edited notes return the WPF RTF, or the original when that RTF dropped an on-screen picture.</summary>
    public static byte[] Store(byte[] original, RichTextBox box, bool edited, out string? warning)
    {
        warning = null;
        if (!edited) return original ?? [];
        return Choose(original ?? [], Save(box), Images(box), 0, out warning);
    }

    public static byte[] Choose(byte[] original, byte[] candidate, int imagesOnScreen, out string? warning)
        => Choose(original, candidate, imagesOnScreen, 0, out warning);

    /// <summary>True when a <c>\cf</c> still points at an automatic ink.</summary>
    public static bool AppliesAutomaticInk(byte[] rtf)
    {
        if (rtf == null || rtf.Length == 0) return false;
        string s = RtfPict.Latin1.GetString(rtf);
        const string open = "{\\colortbl";
        int start = s.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return false;
        int end = s.IndexOf('}', start);
        if (end < 0) return false;
        var parts = s.Substring(start + open.Length, end - (start + open.Length)).Split(';');
        var auto = new HashSet<int>();
        for (int i = 0; i < parts.Length; i++)
            if (TryColor(parts[i], out byte r, out byte g, out byte b) && NoteColors.IsAutomaticInk(Color.FromRgb(r, g, b)))
                auto.Add(i);
        if (auto.Count == 0) return false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 3 >= s.Length || s[i + 1] != 'c' || s[i + 2] != 'f' || s[i + 3] is < '0' or > '9') continue;
            int j = i + 3, n = 0;
            while (j < s.Length && s[j] is >= '0' and <= '9') { n = n * 10 + (s[j] - '0'); j++; }
            if (auto.Contains(n)) return true;
        }
        return false;
    }

    /// <summary>Drops <c>\cf</c> uses of the automatic inks and renumbers the color table. Chosen colors stay.</summary>
    public static byte[] StripAutomaticInk(byte[] rtf)
    {
        if (rtf == null || rtf.Length == 0) return rtf ?? [];
        string s = RtfPict.Latin1.GetString(rtf);
        const string open = "{\\colortbl";
        int start = s.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return rtf;
        int end = s.IndexOf('}', start);
        if (end < 0) return rtf;
        string table = s.Substring(start + open.Length, end - (start + open.Length));
        var parts = table.Split(';');
        var keep = new List<string>();
        var map = new Dictionary<int, int>();
        bool dropped = false;
        for (int i = 0; i < parts.Length; i++)
        {
            if (TryColor(parts[i], out byte r, out byte g, out byte b) && NoteColors.IsAutomaticInk(Color.FromRgb(r, g, b)))
            {
                dropped = true;
                continue;
            }
            map[i] = keep.Count;
            keep.Add(parts[i]);
        }
        if (!dropped) return rtf;
        var sb = new StringBuilder(s.Length);
        sb.Append(s, 0, start);
        sb.Append(open);
        sb.Append(string.Join(';', keep));
        sb.Append(s, end, s.Length - end);
        return RtfPict.Latin1.GetBytes(RewriteCf(sb.ToString(), map));
    }

    static bool TryColor(string part, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var red = RegexColor(part, "red");
        var green = RegexColor(part, "green");
        var blue = RegexColor(part, "blue");
        if (red < 0 || green < 0 || blue < 0) return false;
        r = (byte)red; g = (byte)green; b = (byte)blue;
        return true;
    }

    static int RegexColor(string part, string name)
    {
        int i = part.IndexOf("\\" + name, StringComparison.Ordinal);
        if (i < 0) return -1;
        i += name.Length + 1;
        int n = 0; bool any = false;
        while (i < part.Length && part[i] is >= '0' and <= '9') { any = true; n = n * 10 + (part[i] - '0'); i++; }
        return any ? n : -1;
    }

    static string RewriteCf(string s, Dictionary<int, int> map)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'c' && s[i + 2] == 'f' && s[i + 3] is >= '0' and <= '9')
            {
                int j = i + 3, n = 0;
                while (j < s.Length && s[j] is >= '0' and <= '9') { n = n * 10 + (s[j] - '0'); j++; }
                if (!map.TryGetValue(n, out int neu))
                {
                    if (j < s.Length && s[j] == ' ') j++;
                    i = j - 1;
                    continue;
                }
                sb.Append("\\cf").Append(neu);
                if (j < s.Length && s[j] == ' ') sb.Append(' ');
                i = j - 1;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <param name="groupsHeld">Original pict groups still owned by the note. A save with fewer picts keeps the file.</param>
    public static byte[] Choose(byte[] original, byte[] candidate, int imagesOnScreen, int groupsHeld, out string? warning)
    {
        int picts = Picts(candidate);
        if ((imagesOnScreen > 0 && picts < imagesOnScreen) || (groupsHeld > 0 && picts < groupsHeld))
        {
            warning = $"wpf rtf dropped images screen={imagesOnScreen} groups={groupsHeld} picts={picts}; kept original bytes";
            return original ?? [];
        }
        warning = null;
        return candidate ?? original ?? [];
    }

    static int CountInlines(InlineCollection inlines)
    {
        int n = 0;
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer { Child: Image }) n++;
            else if (inline is Span span) n += CountInlines(span.Inlines);
        }
        return n;
    }

    static int Count(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                return CountInlines(p.Inlines);
            case BlockUIContainer { Child: System.Windows.Controls.Image }:
                return 1;
            case List list:
                int c = 0;
                foreach (var item in list.ListItems)
                    foreach (var b in item.Blocks) c += Count(b);
                return c;
            case Section section:
                int s = 0;
                foreach (var b in section.Blocks) s += Count(b);
                return s;
            default:
                return 0;
        }
    }
}
