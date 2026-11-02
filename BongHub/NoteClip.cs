using System.Text;
using System.Text.Json.Nodes;

namespace BongHub;

public sealed class NoteClipPiece
{
    public string Text = "";
    public byte[]? Png;
}

/// <summary>
/// Text plus PNG pieces for a sticky note. The plain form never substitutes the word Image for a picture.
/// This type does not touch the system clipboard.
/// </summary>
public static class NoteClip
{
    public const string Format = "BongHub.Note";

    public static string Pack(IReadOnlyList<NoteClipPiece> pieces)
    {
        var arr = new JsonArray();
        foreach (var p in pieces)
        {
            var o = new JsonObject();
            if (!string.IsNullOrEmpty(p.Text)) o["t"] = p.Text;
            if (p.Png is { Length: > 0 }) o["p"] = Convert.ToBase64String(p.Png);
            if (o.Count > 0) arr.Add(o);
        }
        return arr.ToJsonString();
    }

    public static List<NoteClipPiece> Unpack(string? json)
    {
        var list = new List<NoteClipPiece>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return list;
            foreach (var node in arr.OfType<JsonObject>())
            {
                var piece = new NoteClipPiece { Text = node["t"]?.GetValue<string>() ?? "" };
                var b64 = node["p"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    try { piece.Png = Convert.FromBase64String(b64); } catch { piece.Png = null; }
                }
                if (piece.Text.Length > 0 || piece.Png is { Length: > 0 }) list.Add(piece);
            }
        }
        catch { }
        return list;
    }

    public static string Plain(IReadOnlyList<NoteClipPiece> pieces)
    {
        var sb = new StringBuilder();
        foreach (var p in pieces) sb.Append(p.Text);
        return sb.ToString();
    }

    public static string ToRtf(IReadOnlyList<NoteClipPiece> pieces)
    {
        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\deff0 ");
        foreach (var p in pieces)
        {
            if (p.Text.Length > 0) sb.Append(Escape(p.Text)).Append("\\par ");
            if (p.Png is { Length: > 0 })
            {
                sb.Append("{\\pict\\pngblip\\picw1\\pich1\\picwgoal15\\pichgoal15 ");
                foreach (var b in p.Png) sb.Append(b.ToString("x2"));
                sb.Append("}");
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par ");
            else if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }
}
