using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BongHub.Next;

/// <summary>
/// Splices one <c>Notes.&lt;key&gt;</c> value into a 3.4.x <c>config.json</c>.
/// Characters outside that value are copied unchanged, so a cloud sync does not see a full-file rewrite.
/// </summary>
public static class NotesConfigPatch
{
    public static bool TryApply(string text, string key, JsonNode? value, out string updated)
        => TryApply(text, "Notes", key, value, out updated);

    /// <summary>Splices one value under a top-level object such as <c>Notes</c> or <c>General</c>. Other bytes stay.</summary>
    public static bool TryApply(string text, string section, string key, JsonNode? value, out string updated)
    {
        updated = text;
        if (string.IsNullOrEmpty(text) || value is null || !IsKey(section) || !IsKey(key)) return false;
        var r = new R(text);
        r.SkipWs();
        if (r.Eof || r.C != '{') return false;
        int rootOpen = r.I;
        if (!r.ReadObject(out int rootClose, out int rootMembers, out int rootLast, out var notes, wantChild: section)) return false;
        if (notes.IsObject)
        {
            var inner = new R(text) { I = notes.ValueStart };
            if (!inner.ReadObject(out int notesClose, out int notesMembers, out int notesLast, out var hit, wantChild: key)) return false;
            if (hit.Found && !hit.IsObject)
            {
                string existing = text.Substring(hit.ValueStart, hit.ValueEnd - hit.ValueStart);
                string formatted = Format(value);
                if (existing == formatted || Same(existing, value)) { updated = text; return true; }
                updated = text.Substring(0, hit.ValueStart) + formatted + text.Substring(hit.ValueEnd);
                return true;
            }
            if (hit.Found && hit.IsObject) return false;
            return InsertMember(text, notes.ValueStart, notesClose, notesMembers, notesLast, "\"" + key + "\": " + Format(value), out updated);
        }
        if (notes.Found) return false;

        string nl = Newline(text);
        bool rootPretty = nl.Length > 0 && text.IndexOf('\n', rootOpen, rootClose - rootOpen) >= 0;
        string memberIndent = MemberIndent(CloseIndent(text, rootClose));
        string innerText = "\"" + key + "\": " + Format(value);
        string notesJson = rootPretty
            ? "{" + nl + memberIndent + ExtraIndent(memberIndent) + innerText + nl + memberIndent + "}"
            : "{" + innerText + "}";
        return InsertMember(text, rootOpen, rootClose, rootMembers, rootLast, "\"" + section + "\": " + notesJson, out updated);
    }

    /// <summary>Minimal config when the file does not exist yet. 3.4.x pretty-print uses CRLF and two-space indents.</summary>
    public static string CreateNew(string key, JsonNode value)
    {
        if (!IsKey(key) || value is null) throw new ArgumentException("key");
        return "{\r\n  \"Version\": 2,\r\n  \"Notes\": {\r\n    \"" + key + "\": " + Format(value) + "\r\n  }\r\n}";
    }

    public static string CreateSection(string section, string key, JsonNode value)
    {
        if (!IsKey(section) || !IsKey(key) || value is null) throw new ArgumentException("key");
        return "{\r\n  \"Version\": 2,\r\n  \"" + section + "\": {\r\n    \"" + key + "\": " + Format(value) + "\r\n  }\r\n}";
    }

    /// <summary>
    /// Patch <paramref name="path"/> in place. Returns false when the file cannot be patched safely
    /// (it is left untouched). <paramref name="changed"/> is false when the value was already there.
    /// </summary>
    public static bool TryWriteFile(string path, string key, JsonNode? value, out bool changed)
        => TryWriteFile(path, "Notes", key, value, out changed);

    public static bool TryWriteFile(string path, string section, string key, JsonNode? value, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(path) || value is null || !IsKey(section) || !IsKey(key)) return false;
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (!File.Exists(path))
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WriteAtomic(path, enc.GetBytes(section == "Notes" ? CreateNew(key, value) : CreateSection(section, key, value)), bom: false);
            changed = true;
            return true;
        }
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try { text = enc.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException) { return false; }
        if (!TryApply(text, section, key, value, out string updated)) return false;
        if (updated == text) return true;
        WriteAtomic(path, enc.GetBytes(updated), bom);
        changed = true;
        return true;
    }

    public static string Format(JsonNode value)
    {
        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out bool b)) return b ? "true" : "false";
            if (jv.TryGetValue<string>(out string? s)) return Quote(s ?? "");
            if (TryNumber(jv, out string num)) return num;
        }
        return value.ToJsonString();
    }

    static bool TryNumber(JsonValue jv, out string num)
    {
        num = "";
        string raw = jv.ToJsonString();
        if (raw.Length == 0 || raw[0] == '"') return false;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
        if (double.IsNaN(d) || double.IsInfinity(d)) return false;
        if (d >= long.MinValue && d <= long.MaxValue && d == Math.Truncate(d))
        {
            num = ((long)d).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        num = d.ToString("G15", CultureInfo.InvariantCulture);
        return true;
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static bool Same(string existingJson, JsonNode value)
    {
        try { return JsonNode.DeepEquals(JsonNode.Parse(existingJson), value); }
        catch { return false; }
    }

    static bool IsKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (char c in key)
            if (!(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        return true;
    }

    static string Newline(string text) => text.Contains("\r\n") ? "\r\n" : (text.Contains('\n') ? "\n" : "");

    static bool InsertMember(string text, int openBrace, int closeBrace, int memberCount, int lastValueEnd, string memberText, out string updated)
    {
        updated = text;
        if (openBrace < 0 || closeBrace <= openBrace || closeBrace >= text.Length || text[closeBrace] != '}') return false;
        bool pretty = text.IndexOf('\n', openBrace, closeBrace - openBrace) >= 0;
        if (!pretty)
        {
            if (memberCount > 0 && !HasComma(text, lastValueEnd, closeBrace))
            {
                text = text.Insert(lastValueEnd, ",");
                closeBrace++;
            }
            updated = text.Insert(closeBrace, memberText);
            return true;
        }
        string nl = Newline(text);
        if (nl.Length == 0) nl = "\n";
        if (memberCount > 0 && !HasComma(text, lastValueEnd, closeBrace))
        {
            text = text.Insert(lastValueEnd, ",");
            closeBrace++;
        }
        int lineStart = LineStart(text, closeBrace);
        string indent = MemberIndent(CloseIndent(text, closeBrace));
        updated = text.Insert(lineStart, indent + memberText + nl);
        return true;
    }

    static int LineStart(string text, int index)
    {
        int i = index - 1;
        while (i >= 0 && text[i] != '\n') i--;
        return i + 1;
    }

    static string CloseIndent(string text, int closeBrace)
    {
        int start = LineStart(text, closeBrace);
        int j = start;
        while (j < closeBrace && text[j] is ' ' or '\t') j++;
        if (j != closeBrace) return "";
        return text.Substring(start, closeBrace - start);
    }

    static string MemberIndent(string closeIndent)
    {
        if (closeIndent.Length > 0 && closeIndent.IndexOf(' ') < 0) return closeIndent + "\t";
        return closeIndent + "  ";
    }

    static string ExtraIndent(string memberIndent) => memberIndent.Length > 0 && memberIndent.IndexOf(' ') < 0 ? "\t" : "  ";

    static bool HasComma(string text, int from, int until)
    {
        for (int i = from; i < until && i < text.Length; i++)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n') continue;
            return c == ',';
        }
        return false;
    }

    static void WriteAtomic(string path, byte[] payload, bool bom)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
            fs.Write(payload, 0, payload.Length);
        }
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    struct Hit
    {
        public bool Found;
        public bool IsObject;
        public int ValueStart;
        public int ValueEnd;
    }

    sealed class R
    {
        public readonly string S;
        public int I;
        public R(string s) { S = s; }
        public bool Eof => I >= S.Length;
        public char C => S[I];
        public void SkipWs() { while (!Eof && C is ' ' or '\t' or '\r' or '\n') I++; }
        public bool Eat(char c) { if (Eof || C != c) return false; I++; return true; }

        /// <summary>I at '{'. On success I is just after the matching '}'. <paramref name="child"/> is the first member named <paramref name="wantChild"/>.</summary>
        public bool ReadObject(out int close, out int members, out int lastValueEnd, out Hit child, string? wantChild)
        {
            close = -1; members = 0; lastValueEnd = -1; child = default;
            if (!Eat('{')) return false;
            SkipWs();
            if (Eat('}')) { close = I - 1; return true; }
            while (true)
            {
                SkipWs();
                if (!TryString(out string name)) return false;
                SkipWs();
                if (!Eat(':')) return false;
                SkipWs();
                int vs = I;
                bool isObject = !Eof && C == '{';
                if (!SkipValue()) return false;
                int ve = I;
                members++;
                lastValueEnd = ve;
                if (wantChild != null && name == wantChild && !child.Found)
                    child = new Hit { Found = true, IsObject = isObject, ValueStart = vs, ValueEnd = ve };
                SkipWs();
                if (Eat(','))
                {
                    SkipWs();
                    if (!Eof && C == '}') { close = I; I++; return true; }
                    continue;
                }
                if (Eat('}')) { close = I - 1; return true; }
                return false;
            }
        }

        public bool SkipValue()
        {
            if (Eof) return false;
            if (C == '"') return TryString(out _);
            if (C == '{') return ReadObject(out _, out _, out _, out _, null);
            if (C == '[') return SkipArray();
            if (C == 't') return Lit("true");
            if (C == 'f') return Lit("false");
            if (C == 'n') return Lit("null");
            if (C == '-' || (C >= '0' && C <= '9')) return SkipNumber();
            return false;
        }

        bool SkipArray()
        {
            if (!Eat('[')) return false;
            SkipWs();
            if (Eat(']')) return true;
            while (true)
            {
                SkipWs();
                if (!SkipValue()) return false;
                SkipWs();
                if (Eat(','))
                {
                    SkipWs();
                    if (!Eof && C == ']') { I++; return true; }
                    continue;
                }
                return Eat(']');
            }
        }

        bool Lit(string lit)
        {
            if (I + lit.Length > S.Length) return false;
            if (string.CompareOrdinal(S, I, lit, 0, lit.Length) != 0) return false;
            int end = I + lit.Length;
            if (end < S.Length && (char.IsLetterOrDigit(S[end]) || S[end] == '_')) return false;
            I = end;
            return true;
        }

        bool SkipNumber()
        {
            if (Eat('-') && (Eof || C < '0' || C > '9')) return false;
            if (Eof || C < '0' || C > '9') return false;
            if (C == '0') I++;
            else while (!Eof && C >= '0' && C <= '9') I++;
            if (!Eof && C == '.')
            {
                I++;
                if (Eof || C < '0' || C > '9') return false;
                while (!Eof && C >= '0' && C <= '9') I++;
            }
            if (!Eof && (C == 'e' || C == 'E'))
            {
                I++;
                if (!Eof && (C == '+' || C == '-')) I++;
                if (Eof || C < '0' || C > '9') return false;
                while (!Eof && C >= '0' && C <= '9') I++;
            }
            return true;
        }

        public bool TryString(out string decoded)
        {
            decoded = "";
            if (!Eat('"')) return false;
            var sb = new StringBuilder();
            while (!Eof)
            {
                char c = C; I++;
                if (c == '"') { decoded = sb.ToString(); return true; }
                if (c == '\\')
                {
                    if (Eof) return false;
                    char e = C; I++;
                    switch (e)
                    {
                        case '"': case '\\': case '/': sb.Append(e); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (I + 4 > S.Length) return false;
                            if (!int.TryParse(S.Substring(I, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)) return false;
                            sb.Append((char)cp);
                            I += 4;
                            break;
                        default: return false;
                    }
                }
                else if (c < 0x20) return false;
                else sb.Append(c);
            }
            return false;
        }
    }
}
