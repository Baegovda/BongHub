using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BongHub;

/// <summary>Turns a clipboard object into note pieces. Does not open the system clipboard itself.</summary>
public static class NotePaste
{
    public const int MaxBytes = 32 * 1024 * 1024;

    public static List<NoteClipPiece> Read(IDataObject? data)
    {
        var empty = new List<NoteClipPiece>();
        if (data == null) return empty;
        var own = ReadPack(data);
        if (own.Count > 0) return own;
        var files = ReadFiles(data);
        if (files.Count > 0) return files;
        var png = ReadPng(data);
        if (png.Count > 0) return png;
        var html = ReadHtml(data);
        if (html.Count > 0) return html;
        var rtf = ReadRtf(data);
        if (rtf.Count > 0) return rtf;
        var bitmap = ReadBitmap(data);
        if (bitmap.Count > 0) return bitmap;
        return ReadText(data);
    }

    static List<NoteClipPiece> ReadPack(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(NoteClip.Format) && data.GetData(NoteClip.Format) is string pack)
                return NoteClip.Unpack(pack);
        }
        catch (Exception ex) { AppEnv.Log("paste pack: " + ex.Message); }
        return new List<NoteClipPiece>();
    }

    static List<NoteClipPiece> ReadFiles(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files) return list;
            var names = new List<string>();
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (NoteImages.IsImagePath(file) && new FileInfo(file).Exists)
                {
                    var bytes = File.ReadAllBytes(file);
                    if (bytes.Length > 0 && bytes.Length <= MaxBytes) list.Add(new NoteClipPiece { Png = bytes });
                    else AppEnv.Log("paste file skipped, size " + bytes.Length);
                }
                else names.Add(file);
            }
            if (list.Count == 0 && names.Count > 0)
                list.Add(new NoteClipPiece { Text = string.Join("\n", names) });
        }
        catch (Exception ex) { AppEnv.Log("paste files: " + ex.Message); }
        return list;
    }

    static List<NoteClipPiece> ReadPng(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            if (!data.GetDataPresent("PNG")) return list;
            var raw = data.GetData("PNG");
            byte[]? bytes = raw as byte[];
            if (bytes == null && raw is Stream stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            if (bytes is { Length: > 8 } && bytes.Length <= MaxBytes && bytes[0] == 0x89)
                list.Add(new NoteClipPiece { Png = bytes });
            else if (bytes is { Length: > MaxBytes }) AppEnv.Log("paste png skipped, size " + bytes.Length);
        }
        catch (Exception ex) { AppEnv.Log("paste png: " + ex.Message); }
        return list;
    }

    static List<NoteClipPiece> ReadHtml(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            if (!data.GetDataPresent(DataFormats.Html) || data.GetData(DataFormats.Html) is not string html) return list;
            foreach (Match img in Regex.Matches(html, "data:image/(?:png|jpeg|jpg);base64,([A-Za-z0-9+/=]+)", RegexOptions.IgnoreCase))
            {
                try
                {
                    var bytes = Convert.FromBase64String(img.Groups[1].Value);
                    if (bytes.Length > 0 && bytes.Length <= MaxBytes) list.Add(new NoteClipPiece { Png = bytes });
                }
                catch { }
            }
            string text = Regex.Replace(html, "(?is)<script.*?>.*?</script>|<style.*?>.*?</style>", "");
            text = Regex.Replace(text, "(?is)<br\\s*/?>", "\n");
            text = Regex.Replace(text, "(?is)</p>", "\n");
            text = Regex.Replace(text, "(?is)<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text).Trim();
            if (text.Length > 0) list.Insert(0, new NoteClipPiece { Text = text });
        }
        catch (Exception ex) { AppEnv.Log("paste html: " + ex.Message); }
        return list;
    }

    static List<NoteClipPiece> ReadRtf(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            if (!data.GetDataPresent(DataFormats.Rtf) || data.GetData(DataFormats.Rtf) is not string rtf || rtf.Length == 0) return list;
            var bytes = Encoding.GetEncoding(28591).GetBytes(rtf);
            foreach (var pict in RtfPict.Parse(bytes))
            {
                if (pict.Bytes.Length > 0 && pict.Bytes.Length <= MaxBytes)
                    list.Add(new NoteClipPiece { Png = pict.Bytes });
            }
            string text = Regex.Replace(rtf, @"\{\\\*?\\[^{}]+\}", "");
            text = Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", " ");
            text = Regex.Replace(text, @"\\par\b", "\n");
            text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", "");
            text = text.Replace("{", "").Replace("}", "").Trim();
            if (text.Length > 0) list.Insert(0, new NoteClipPiece { Text = text });
        }
        catch (Exception ex) { AppEnv.Log("paste rtf: " + ex.Message); }
        return list;
    }

    static List<NoteClipPiece> ReadBitmap(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            if (!data.GetDataPresent(DataFormats.Bitmap) || data.GetData(DataFormats.Bitmap) is not BitmapSource source) return list;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var bytes = ms.ToArray();
            if (bytes.Length > 0 && bytes.Length <= MaxBytes) list.Add(new NoteClipPiece { Png = bytes });
            else AppEnv.Log("paste bitmap skipped, size " + bytes.Length);
        }
        catch (Exception ex) { AppEnv.Log("paste bitmap: " + ex.Message); }
        return list;
    }

    static List<NoteClipPiece> ReadText(IDataObject data)
    {
        var list = new List<NoteClipPiece>();
        try
        {
            string? text = null;
            if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
            else if (data.GetDataPresent(DataFormats.Text)) text = data.GetData(DataFormats.Text) as string;
            if (!string.IsNullOrEmpty(text)) list.Add(new NoteClipPiece { Text = text });
        }
        catch (Exception ex) { AppEnv.Log("paste text: " + ex.Message); }
        return list;
    }
}
