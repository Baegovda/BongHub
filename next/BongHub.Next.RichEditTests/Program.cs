using System;
using System.IO;
using System.Text;

namespace BongHub.Next.RichEditTests;

/// <summary>
/// Headless proof that EM_STREAMIN of a 1×1 pngblip RTF does not CFG-fast-fail.
/// No window is shown. Windows CI runs this; it does not open the WPF app.
/// </summary>
static class Program
{
    static int Main()
    {
        int fail = 0;
        void Log(string s) => Console.WriteLine(s);
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length == 0 ? "" : " — " + detail));
            if (!ok) fail++;
        }

        Check("x64 EDITSTREAM is 24 bytes and the callback is at 16", RichEditRtf.LayoutOk, "size=" + RichEditRtf.EditStreamBytes + " off=" + RichEditRtf.CallbackOffset);
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "1x1-pngblip.rtf");
        byte[] rtf = File.Exists(path) ? File.ReadAllBytes(path) : [];
        string text = Encoding.ASCII.GetString(rtf);
        Check("fixture is the 1x1 pngblip shape", rtf.Length > 0 && text.Contains(@"\pngblip\picw1\pich1\picwgoal15\pichgoal15") && text.Contains("89504e470d0a1a0a"), path + " bytes=" + rtf.Length);

        IntPtr hwnd = RichEditRtf.CreateHidden(out string error);
        Check("hidden richedit created", hwnd != IntPtr.Zero, error);
        Check("richedit is not visible", hwnd != IntPtr.Zero && !RichEditRtf.IsVisible(hwnd));
        if (hwnd != IntPtr.Zero && rtf.Length > 0)
        {
            bool streamed = RichEditRtf.Stream(hwnd, inbound: true, rtf, out _, out string detail, Log);
            Check("EM_STREAMIN returned", streamed, detail);
            string plain = RichEditRtf.Plain(hwnd);
            Check("plain text contains 그림", plain.Contains("그림"), Show(plain));
            bool saved = RichEditRtf.TrySave(hwnd, out byte[] back, Log);
            Check("EM_STREAMOUT returned", saved, "bytes=" + back.Length);
            Check("stream out keeps the png blip", saved && RichEditRtf.KeepsBlips(rtf, back));
            RichEditRtf.Destroy(hwnd);

            IntPtr again = RichEditRtf.CreateHidden(out error);
            Check("second hidden richedit", again != IntPtr.Zero, error);
            if (again != IntPtr.Zero)
            {
                bool loaded = RichEditRtf.TryLoad(again, rtf, Log, out string how);
                Check("TryLoad", loaded, how);
                string plain2 = RichEditRtf.Plain(again);
                Check("TryLoad plain contains 그림", plain2.Contains("그림"), Show(plain2));
                bool saved2 = RichEditRtf.TrySave(again, out byte[] back2, Log);
                Check("TryLoad save keeps the png blip", saved2 && RichEditRtf.KeepsBlips(rtf, back2), "bytes=" + back2.Length);
                Check("still not visible", !RichEditRtf.IsVisible(again));
                RichEditRtf.Destroy(again);
            }
        }

        Console.WriteLine(fail == 0 ? "RESULT PASS" : "RESULT FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }

    static string Show(string s)
    {
        s = s.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length <= 180 ? s : s.Substring(0, 180);
    }
}
