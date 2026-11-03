using System;
using System.Collections.Generic;
using System.Linq;

namespace BongHub;

public enum NoteBlockKind { Text, H1, H2, H3, Bullet, Number, Todo, Quote, Code, Divider }

public readonly record struct NoteCommand(string Id, string Title, string Hint, NoteBlockKind Kind, string[] Aliases);

public readonly record struct NoteShortcut(NoteBlockKind Kind, string Rest);

public readonly record struct NoteInline(int Start, int Length, string Text, string Style);

public readonly record struct EnterPlan(NoteBlockKind Next, bool Exit);

/// <summary>
/// Notion-style blocks for a sticky note. The note file stays RTF: a heading is a larger paragraph,
/// a list is a list, and the other kinds keep a short marker an older build still shows as text.
/// A paragraph that is not one of those shapes stays a plain paragraph.
/// </summary>
public static class NoteBlocks
{
    public static readonly NoteCommand[] Commands =
    {
        new("text", "텍스트", "일반 글", NoteBlockKind.Text, new[] { "text", "본문" }),
        new("h1", "제목 1", "큰 제목", NoteBlockKind.H1, new[] { "heading", "제목", "h1", "#" }),
        new("h2", "제목 2", "중간 제목", NoteBlockKind.H2, new[] { "h2", "##" }),
        new("h3", "제목 3", "작은 제목", NoteBlockKind.H3, new[] { "h3", "###" }),
        new("bullet", "글머리", "점 목록", NoteBlockKind.Bullet, new[] { "list", "목록", "bullet", "-" }),
        new("number", "번호", "번호 목록", NoteBlockKind.Number, new[] { "ol", "번호", "1" }),
        new("todo", "할 일", "체크할 수 있는 줄", NoteBlockKind.Todo, new[] { "todo", "체크", "[]" }),
        new("quote", "인용", "들여쓴 인용", NoteBlockKind.Quote, new[] { "quote", "인용", ">" }),
        new("code", "코드", "고정폭 글", NoteBlockKind.Code, new[] { "code", "코드" }),
        new("divider", "구분선", "줄을 나눠요", NoteBlockKind.Divider, new[] { "hr", "구분", "선", "---" })
    };

    public static bool TrySlash(string? text, out string query)
    {
        query = "";
        if (string.IsNullOrEmpty(text) || text[0] != '/') return false;
        if (text.Contains(' ') || text.Contains('\n') || text.Contains('\r')) return false;
        query = text[1..];
        return true;
    }

    public static List<NoteCommand> Match(string? query)
    {
        string q = (query ?? "").Trim();
        var list = new List<NoteCommand>();
        foreach (var command in Commands)
            if (q.Length == 0 || Hits(command, q)) list.Add(command);
        return list;
    }

    public static bool TryShortcut(string? text, out NoteShortcut hit)
    {
        hit = default;
        if (string.IsNullOrEmpty(text)) return false;
        if (text == "---") { hit = new NoteShortcut(NoteBlockKind.Divider, ""); return true; }
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            string rest = text[3..];
            if (rest.StartsWith(' ')) rest = rest[1..];
            hit = new NoteShortcut(NoteBlockKind.Code, rest);
            return true;
        }
        var marks = new (string Prefix, NoteBlockKind Kind)[]
        {
            ("### ", NoteBlockKind.H3), ("## ", NoteBlockKind.H2), ("# ", NoteBlockKind.H1),
            ("[ ] ", NoteBlockKind.Todo), ("[] ", NoteBlockKind.Todo),
            ("- ", NoteBlockKind.Bullet), ("* ", NoteBlockKind.Bullet),
            ("1. ", NoteBlockKind.Number),
            ("> ", NoteBlockKind.Quote)
        };
        foreach (var (prefix, kind) in marks)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            hit = new NoteShortcut(kind, text[prefix.Length..]);
            return true;
        }
        return false;
    }

    public static bool TryInline(string? text, out NoteInline hit)
    {
        hit = default;
        if (string.IsNullOrEmpty(text)) return false;
        if (TryWrap(text, "**", "bold", out hit)) return true;
        if (TryWrap(text, "~~", "strike", out hit)) return true;
        if (TryWrap(text, "`", "code", out hit)) return true;
        if (TryWrap(text, "*", "italic", out hit)) return true;
        return false;
    }

    /// <summary>A stored paragraph. Plain 11pt text stays <see cref="NoteBlockKind.Text"/>, including text that merely contains a slash.</summary>
    public static NoteBlockKind Classify(string? text, bool list, bool numbered, double fontPt, double bodyPt, bool mono)
    {
        string t = (text ?? "").TrimEnd('\r', '\n');
        if (t == "———" || t == "---") return NoteBlockKind.Divider;
        if (t.StartsWith("☐", StringComparison.Ordinal) || t.StartsWith("☑", StringComparison.Ordinal)) return NoteBlockKind.Todo;
        if (t.StartsWith("│", StringComparison.Ordinal)) return NoteBlockKind.Quote;
        if (mono) return NoteBlockKind.Code;
        if (list) return numbered ? NoteBlockKind.Number : NoteBlockKind.Bullet;
        _ = bodyPt;
        if (fontPt >= 22) return NoteBlockKind.H1;
        if (fontPt >= 18) return NoteBlockKind.H2;
        if (fontPt >= 14.5) return NoteBlockKind.H3;
        return NoteBlockKind.Text;
    }

    public static EnterPlan OnEnter(NoteBlockKind kind, string? text)
    {
        string t = (text ?? "").Trim();
        bool empty = t.Length == 0 || t is "☐" or "☑" or "│" or "•";
        if (empty && kind is NoteBlockKind.Bullet or NoteBlockKind.Number or NoteBlockKind.Todo or NoteBlockKind.Quote or NoteBlockKind.Code)
            return new EnterPlan(NoteBlockKind.Text, true);
        if (kind is NoteBlockKind.H1 or NoteBlockKind.H2 or NoteBlockKind.H3 or NoteBlockKind.Divider)
            return new EnterPlan(NoteBlockKind.Text, false);
        return new EnterPlan(kind, false);
    }

    public static string Story()
    {
        if (Classify("오늘 할 일", false, false, 11, 11, false) != NoteBlockKind.Text) return "plain note changed kind";
        if (Classify("see /docs later", false, false, 11, 11, false) != NoteBlockKind.Text) return "slash inside a sentence became a command";
        if (TrySlash("see /docs", out _)) return "slash matched past the first character";
        if (!TrySlash("/할", out string query) || query != "할") return "slash query " + query;
        var hits = Match(query);
        if (hits.Count != 1 || hits[0].Kind != NoteBlockKind.Todo) return "slash hits " + string.Join(",", hits.Select(h => h.Title));
        if (TryShortcut("#제목", out _)) return "hash without a space was a heading";
        if (!TryShortcut("## 안녕", out var h2) || h2.Kind != NoteBlockKind.H2 || h2.Rest != "안녕") return "h2 shortcut";
        if (!TryShortcut("# ", out var h1) || h1.Kind != NoteBlockKind.H1 || h1.Rest.Length != 0) return "empty h1";
        if (!TryShortcut("### 작은", out var h3) || h3.Kind != NoteBlockKind.H3 || h3.Rest != "작은") return "h3 shortcut";
        if (!TryShortcut("- 항목", out var bullet) || bullet.Kind != NoteBlockKind.Bullet || bullet.Rest != "항목") return "bullet shortcut";
        if (!TryShortcut("1. 첫째", out var number) || number.Kind != NoteBlockKind.Number || number.Rest != "첫째") return "number shortcut";
        if (!TryShortcut("[] 우유", out var todo) || todo.Kind != NoteBlockKind.Todo || todo.Rest != "우유") return "todo shortcut";
        if (!TryShortcut("> 말", out var quote) || quote.Kind != NoteBlockKind.Quote || quote.Rest != "말") return "quote shortcut";
        if (!TryShortcut("```", out var code) || code.Kind != NoteBlockKind.Code || code.Rest.Length != 0) return "code shortcut";
        if (!TryShortcut("---", out var line) || line.Kind != NoteBlockKind.Divider) return "divider shortcut";
        if (!TryInline("**굵게**", out var bold) || bold.Style != "bold" || bold.Text != "굵게" || bold.Start != 0) return "bold inline";
        if (!TryInline("안녕 **굵게**", out var mid) || mid.Style != "bold" || mid.Text != "굵게" || mid.Start != 3) return "bold inside a line";
        if (!TryInline("*기울임*", out var italic) || italic.Style != "italic" || italic.Text != "기울임") return "italic inline";
        if (!TryInline("~~지움~~", out var strike) || strike.Style != "strike" || strike.Text != "지움") return "strike inline";
        if (TryInline("**아직", out _)) return "unclosed marker was applied";
        var leave = OnEnter(NoteBlockKind.Bullet, "");
        if (!leave.Exit || leave.Next != NoteBlockKind.Text) return "empty bullet stayed a list";
        var keep = OnEnter(NoteBlockKind.Bullet, "항목");
        if (keep.Exit || keep.Next != NoteBlockKind.Bullet) return "filled bullet did not continue";
        var afterHead = OnEnter(NoteBlockKind.H1, "제목");
        if (afterHead.Exit || afterHead.Next != NoteBlockKind.Text) return "heading enter";
        if (Classify("본문", false, false, 24, 11, false) != NoteBlockKind.H1) return "stored heading was not read back";
        if (Classify("할 일", true, false, 11, 11, false) != NoteBlockKind.Bullet) return "stored list was not read back";
        if (Classify("☐ 우유", false, false, 11, 11, false) != NoteBlockKind.Todo) return "stored todo was not read back";
        if (Match("").Count != Commands.Length) return "empty slash hid a command";
        return "";
    }

    static bool Hits(NoteCommand command, string query)
    {
        if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (command.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var alias in command.Aliases)
            if (alias.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool TryWrap(string text, string mark, string style, out NoteInline hit)
    {
        hit = default;
        if (!text.EndsWith(mark, StringComparison.Ordinal) || text.Length <= mark.Length * 2) return false;
        int end = text.Length - mark.Length;
        if (mark == "*" && end > 0 && text[end - 1] == '*') return false;
        int open = text.LastIndexOf(mark, end - 1, end, StringComparison.Ordinal);
        if (open < 0 || open + mark.Length >= end) return false;
        if (mark == "*" && (open > 0 && text[open - 1] == '*' || text[open + 1] == '*')) return false;
        string body = text[(open + mark.Length)..end];
        if (body.Contains(mark, StringComparison.Ordinal) || body.Trim().Length == 0) return false;
        hit = new NoteInline(open, text.Length - open, body, style);
        return true;
    }
}
