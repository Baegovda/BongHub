using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace BongHub.Next;

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
