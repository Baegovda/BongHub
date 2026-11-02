using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace BongHub.Next.RichEditTests;

/// <summary>
/// Loads the 1×1 pngblip fixture with WPF <see cref="System.Windows.Controls.RichTextBox"/> on an STA thread.
/// No window is shown. Windows CI runs this.
/// </summary>
static class Program
{
    [STAThread]
    static int Main()
    {
        int fail = 0;
        var gate = new ManualResetEventSlim(false);
        Exception? error = null;
        var worker = new Thread(() =>
        {
            try { fail = Run(); }
            catch (Exception e) { error = e; fail = 1; }
            finally { gate.Set(); }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        gate.Wait();
        if (error != null) Console.WriteLine("FAIL exception — " + error);
        return fail;
    }

    static int Run()
    {
        int fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length == 0 ? "" : " — " + detail));
            if (!ok) fail++;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "1x1-pngblip.rtf");
        byte[] rtf = File.Exists(path) ? File.ReadAllBytes(path) : [];
        string text = Encoding.ASCII.GetString(rtf);
        Check("fixture is the 1x1 pngblip shape", rtf.Length > 0 && text.Contains(@"\pngblip\picw1\pich1\picwgoal15\pichgoal15") && text.Contains("89504e470d0a1a0a"), path + " bytes=" + rtf.Length);
        if (rtf.Length == 0)
        {
            Console.WriteLine("RESULT FAIL " + fail);
            return 1;
        }

        var box = NoteRtf.LoadBox(rtf);
        int windows = Application.Current?.Windows.Count ?? 0;
        Check("no window is shown", windows == 0, "windows=" + windows);
        string plain = NoteRtf.Plain(box);
        Check("plain text contains 그림", plain.Contains("그림"), Show(plain));
        int images = NoteRtf.Images(box);
        Check("png survives load as an image", images >= 1, "images=" + images);

        var kept = NoteRtf.Store(rtf, box, edited: false, out string? idle);
        Check("unedited store keeps the file bytes", kept.AsSpan().SequenceEqual(rtf) && idle == null, "warn=" + idle);

        box.Document.Blocks.Add(new Paragraph(new Run("리치")));
        byte[] wpf = NoteRtf.Save(box);
        int still = NoteRtf.Images(box);
        var chosen = NoteRtf.Choose(rtf, wpf, still, out string? warning);
        bool original = chosen.AsSpan().SequenceEqual(rtf);
        bool hasPng = Encoding.ASCII.GetString(chosen).Contains(@"\pngblip", StringComparison.Ordinal);
        bool kept = (warning == null && NoteRtf.Picts(chosen) >= still && still >= 1) || (warning != null && original && hasPng);
        Check("edited save keeps the picture or the original png", kept, "warn=" + (warning ?? "none") + " bytes=" + chosen.Length + " picts=" + NoteRtf.Picts(chosen) + " images=" + still);
        if (warning != null) Console.WriteLine(warning);
        Check("editor shows the edit", NoteRtf.Plain(box).Contains("리치"));

        Console.WriteLine(fail == 0 ? "RESULT PASS" : "RESULT FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }

    static string Show(string s)
    {
        s = s.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length <= 180 ? s : s.Substring(0, 180);
    }
}
