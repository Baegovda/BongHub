using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BongHub;

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
                string formatted = FormatLike(existing, value);
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

    /// <summary>Replaces one field inside the <paramref name="index"/>-th object of a top-level array. Other bytes stay.</summary>
    public static bool TryApplyArrayField(string text, string arrayKey, int index, string field, JsonNode? value, out string updated)
    {
        updated = text;
        if (string.IsNullOrEmpty(text) || value is null || index < 0 || !IsKey(arrayKey) || !IsKey(field)) return false;
        var r = new R(text);
        r.SkipWs();
        if (r.Eof || r.C != '{') return false;
        if (!r.ReadObject(out _, out _, out _, out var arr, wantChild: arrayKey)) return false;
        if (!arr.Found || arr.IsObject) return false;
        var inner = new R(text) { I = arr.ValueStart };
        if (!inner.Eat('[')) return false;
        inner.SkipWs();
        if (!inner.Eof && inner.C == ']') return false;
        int seen = 0;
        while (true)
        {
            inner.SkipWs();
            if (inner.Eof || inner.C != '{') return false;
            if (!inner.ReadObject(out _, out _, out _, out var hit, wantChild: field)) return false;
            if (seen == index)
            {
                if (!hit.Found || hit.IsObject) return false;
                string existing = text.Substring(hit.ValueStart, hit.ValueEnd - hit.ValueStart);
                string formatted = FormatLike(existing, value);
                if (existing == formatted || Same(existing, value)) return true;
                updated = text.Substring(0, hit.ValueStart) + formatted + text.Substring(hit.ValueEnd);
                return true;
            }
            seen++;
            inner.SkipWs();
            if (inner.Eat(',')) { inner.SkipWs(); if (!inner.Eof && inner.C == ']') return false; continue; }
            return false;
        }
    }

    /// <summary>Replaces one field inside a nested object such as <c>Pt.Zones.Enabled</c>. Other bytes stay.</summary>
    public static bool TryApplyNested(string text, string section, string child, string field, JsonNode? value, out string updated)
    {
        updated = text;
        if (string.IsNullOrEmpty(text) || value is null || !IsKey(section) || !IsKey(child) || !IsKey(field)) return false;
        var r = new R(text);
        r.SkipWs();
        if (r.Eof || r.C != '{') return false;
        if (!r.ReadObject(out _, out _, out _, out var sec, wantChild: section)) return false;
        if (!sec.Found || !sec.IsObject) return false;
        var mid = new R(text) { I = sec.ValueStart };
        if (!mid.ReadObject(out _, out _, out _, out var obj, wantChild: child)) return false;
        if (!obj.Found || !obj.IsObject) return false;
        var inner = new R(text) { I = obj.ValueStart };
        if (!inner.ReadObject(out _, out _, out _, out var hit, wantChild: field)) return false;
        if (!hit.Found || hit.IsObject) return false;
        string existing = text.Substring(hit.ValueStart, hit.ValueEnd - hit.ValueStart);
        string formatted = FormatLike(existing, value);
        if (existing == formatted || Same(existing, value)) return true;
        updated = text.Substring(0, hit.ValueStart) + formatted + text.Substring(hit.ValueEnd);
        return true;
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

    public static bool TryWriteArrayField(string path, string arrayKey, int index, string field, JsonNode? value, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(path) || value is null || index < 0 || !IsKey(arrayKey) || !IsKey(field)) return false;
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (!File.Exists(path)) return false;
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try { text = enc.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException) { return false; }
        if (!TryApplyArrayField(text, arrayKey, index, field, value, out string updated)) return false;
        if (updated == text) return true;
        WriteAtomic(path, enc.GetBytes(updated), bom);
        changed = true;
        return true;
    }

    public static bool TryWriteNested(string path, string section, string child, string field, JsonNode? value, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(path) || value is null || !IsKey(section) || !IsKey(child) || !IsKey(field)) return false;
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (!File.Exists(path)) return false;
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try { text = enc.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException) { return false; }
        if (!TryApplyNested(text, section, child, field, value, out string updated)) return false;
        if (updated == text) return true;
        WriteAtomic(path, enc.GetBytes(updated), bom);
        changed = true;
        return true;
    }

    /// <summary>The single middle span where <paramref name="after"/> differs from <paramref name="before"/>.</summary>
    public static void ChangedSpan(string before, string after, out string removed, out string inserted)
    {
        before ??= "";
        after ??= "";
        int pre = 0;
        int n = Math.Min(before.Length, after.Length);
        while (pre < n && before[pre] == after[pre]) pre++;
        int b = before.Length, a = after.Length;
        while (b > pre && a > pre && before[b - 1] == after[a - 1]) { b--; a--; }
        removed = before.Substring(pre, b - pre);
        inserted = after.Substring(pre, a - pre);
    }

    /// <summary>
    /// True when that span only inserts <c>TabOrder</c>, replaces its array, or reorders the names inside it.
    /// Presets, Pt, Desk, and sibling General keys must stay outside the span.
    /// </summary>
    public static bool TabOrderSpanOnly(string removed, string inserted)
    {
        if (string.IsNullOrEmpty(inserted)) return false;
        if (ContainsForeign(removed) || ContainsForeign(inserted)) return false;
        if (inserted.Contains("\"TabOrder\"", StringComparison.Ordinal)) return true;
        if (StartsJsonArray(removed) && StartsJsonArray(inserted)) return true;
        // A reorder leaves the brackets in place, so the span is only quoted names, commas, and the original indent.
        return IsArrayInterior(removed) && IsArrayInterior(inserted);
    }

    /// <summary>
    /// Replace an existing value. A string array keeps its brackets, commas, newlines, and indent;
    /// only the element text moves. Anything else uses <see cref="Format"/>.
    /// </summary>
    static string FormatLike(string existing, JsonNode value)
    {
        if (value is JsonArray arr && TryRewriteStringArray(existing, arr, out string rewritten)) return rewritten;
        return Format(value);
    }

    static bool TryRewriteStringArray(string existing, JsonArray incoming, out string rewritten)
    {
        rewritten = "";
        if (!TrySplitStringArray(existing, out var values, out var gaps)) return false;
        var next = new List<string>();
        foreach (var node in incoming)
        {
            if (node is not JsonValue jv || !jv.TryGetValue<string>(out string? s)) return false;
            next.Add(s ?? "");
        }
        if (next.Count == values.Count)
        {
            var sb = new StringBuilder(existing.Length + 16);
            for (int i = 0; i < next.Count; i++)
            {
                sb.Append(gaps[i]);
                sb.Append(Quote(next[i]));
            }
            sb.Append(gaps[next.Count]);
            rewritten = sb.ToString();
            return true;
        }
        if (values.Count == 0) return false;
        string separator = values.Count >= 2 ? gaps[1] : GuessSeparator(gaps[0]);
        var built = new StringBuilder(existing.Length + next.Count * 16);
        for (int i = 0; i < next.Count; i++)
        {
            built.Append(i == 0 ? gaps[0] : separator);
            built.Append(Quote(next[i]));
        }
        built.Append(gaps[values.Count]);
        rewritten = built.ToString();
        return true;
    }

    /// <summary>Gaps are the raw slices around each quoted element, so commas and indentation stay in the gaps.</summary>
    static bool TrySplitStringArray(string text, out List<string> values, out List<string> gaps)
    {
        values = new List<string>();
        gaps = new List<string>();
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n') i++;
        if (i >= text.Length || text[i] != '[') return false;
        int gapStart = 0;
        i++;
        bool wantValue = true;
        bool any = false;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n') { i++; continue; }
            if (c == ']')
            {
                if (wantValue && any) return false;
                gaps.Add(text.Substring(gapStart));
                int j = i + 1;
                while (j < text.Length && text[j] is ' ' or '\t' or '\r' or '\n') j++;
                return j == text.Length && gaps.Count == values.Count + 1;
            }
            if (!wantValue)
            {
                if (c != ',') return false;
                i++;
                wantValue = true;
                continue;
            }
            if (c != '"') return false;
            gaps.Add(text.Substring(gapStart, i - gapStart));
            if (!ReadJsonString(text, ref i, out string value)) return false;
            values.Add(value);
            gapStart = i;
            wantValue = false;
            any = true;
        }
        return false;
    }

    static bool ReadJsonString(string text, ref int i, out string value)
    {
        value = "";
        if (i >= text.Length || text[i] != '"') return false;
        i++;
        var sb = new StringBuilder();
        while (i < text.Length)
        {
            char c = text[i++];
            if (c == '"') { value = sb.ToString(); return true; }
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= text.Length) return false;
            char e = text[i++];
            switch (e)
            {
                case '"': case '\\': case '/': sb.Append(e); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > text.Length) return false;
                    if (!int.TryParse(text.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)) return false;
                    sb.Append((char)cp);
                    i += 4;
                    break;
                default: return false;
            }
        }
        return false;
    }

    static string GuessSeparator(string prefix)
    {
        int nl = prefix.LastIndexOf('\n');
        if (nl < 0) return prefix.EndsWith(' ') ? ", " : ",";
        string indent = prefix.Substring(nl + 1);
        string nlText = prefix.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return "," + nlText + indent;
    }

    static bool IsArrayInterior(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('{') >= 0 || s.IndexOf('}') >= 0 || s.IndexOf('[') >= 0 || s.IndexOf(']') >= 0) return false;
        return s.IndexOf('"') >= 0;
    }

    static bool StartsJsonArray(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i < s.Length && s[i] == '[';
    }

    static bool ContainsForeign(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Contains("PRESET_KEEP", StringComparison.Ordinal)
            || s.Contains("PT_KEEP", StringComparison.Ordinal)
            || s.Contains("DESK_KEEP", StringComparison.Ordinal)
            || s.Contains("OverlayOpacity", StringComparison.Ordinal)
            || s.Contains("IdleEnabled", StringComparison.Ordinal)
            || s.Contains("\"Presets\"", StringComparison.Ordinal)
            || s.Contains("\"Pt\"", StringComparison.Ordinal)
            || s.Contains("\"Desk\"", StringComparison.Ordinal)
            || s.Contains("\"Kakao\"", StringComparison.Ordinal);
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
