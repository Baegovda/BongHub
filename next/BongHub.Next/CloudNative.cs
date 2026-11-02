using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BongHub.Next;

/// <summary>
/// git.exe transport. Constructing it demands the gate, so tests never reach a fetch or a push.
/// gh.exe is not started here. Tokens are not read or written.
/// </summary>
public sealed class GitCloudTransport : ICloudTransport
{
    readonly string repo;
    readonly string branch;
    readonly string device;
    readonly string work;

    public GitCloudTransport(string repo, string branch, string device)
    {
        CloudSystem.Touch();
        this.repo = repo ?? "";
        this.branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
        this.device = string.IsNullOrWhiteSpace(device) ? "device" : device;
        work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "cloud", "repo");
    }

    public bool TryFetch(out Dictionary<string, CloudFile> files, out string error)
    {
        files = new Dictionary<string, CloudFile>(StringComparer.Ordinal);
        CloudSystem.NoteGit();
        if (repo.Contains("github.com", StringComparison.OrdinalIgnoreCase)) CloudSystem.NoteNetwork();
        string root = Path.GetDirectoryName(work) ?? work;
        Directory.CreateDirectory(root);
        if (!Directory.Exists(Path.Combine(work, ".git")))
        {
            if (Directory.Exists(work)) try { Directory.Delete(work, true); } catch { }
            int clone = Git("clone --quiet " + Q(repo) + " repo", root, 120000, out error);
            if (clone != 0) return false;
        }
        Git("config user.name BongHub", work, 30000, out _);
        Git("config user.email bonghub@" + device.ToLowerInvariant() + ".local", work, 30000, out _);
        Git("config core.autocrlf false", work, 30000, out _);
        Git("remote set-url origin " + Q(repo), work, 30000, out _);
        int fetch = Git("fetch --quiet origin", work, 90000, out error);
        if (fetch != 0) return false;
        int has = Git("rev-parse --verify --quiet origin/" + branch, work, 30000, out _);
        if (has == 0)
        {
            Git("checkout --quiet -B " + branch + " origin/" + branch, work, 30000, out _);
            Git("reset --quiet --hard origin/" + branch, work, 30000, out _);
            Git("clean -fdq", work, 30000, out _);
        }
        else
        {
            Git("checkout --quiet --orphan " + branch, work, 30000, out _);
            Git("rm -rfq --cached .", work, 30000, out _);
        }
        ReadTree(files);
        error = "";
        return true;
    }

    public bool TryPush(Dictionary<string, CloudFile> files, string message, out string error)
    {
        CloudSystem.NoteGit();
        if (repo.Contains("github.com", StringComparison.OrdinalIgnoreCase)) CloudSystem.NoteNetwork();
        WriteTree(files);
        Git("add -A", work, 30000, out _);
        int quiet = Git("diff --cached --quiet", work, 30000, out _);
        if (quiet == 0) { error = ""; return true; }
        int commit = Git("commit --quiet -m " + Q(message), work, 30000, out error);
        if (commit != 0) return false;
        int push = Git("push --quiet origin " + branch, work, 90000, out error);
        return push == 0;
    }

    static void ReadTree(Dictionary<string, CloudFile> files)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "cloud", "repo");
        void Add(string full, string rel)
        {
            if (!File.Exists(full)) return;
            files[rel.Replace('\\', '/')] = new CloudFile { Bytes = File.ReadAllBytes(full), MtimeUtc = File.GetLastWriteTimeUtc(full) };
        }
        string readme = Path.Combine(root, "README.md");
        Add(readme, "README.md");
        string data = Path.Combine(root, "data");
        if (!Directory.Exists(data)) return;
        foreach (var file in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
        {
            string rel = "data/" + Path.GetRelativePath(data, file).Replace('\\', '/');
            Add(file, rel);
        }
    }

    static void WriteTree(Dictionary<string, CloudFile> files)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BongHub", "cloud", "repo");
        string data = Path.Combine(root, "data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        foreach (var kv in files)
        {
            if (kv.Key.Contains("..", StringComparison.Ordinal)) continue;
            string full = Path.Combine(root, kv.Key.Replace('/', Path.DirectorySeparatorChar));
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, kv.Value.Bytes);
            if (kv.Value.MtimeUtc > DateTime.MinValue)
                try { File.SetLastWriteTimeUtc(full, DateTime.SpecifyKind(kv.Value.MtimeUtc, DateTimeKind.Utc)); } catch { }
        }
    }

    static int Git(string args, string cwd, int timeout, out string output)
    {
        string exe = File.Exists(@"C:\Program Files\Git\cmd\git.exe") ? @"C:\Program Files\Git\cmd\git.exe" : "git";
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        try
        {
            using var p = Process.Start(psi);
            if (p == null) { output = "git을 시작하지 못했어요"; return -2; }
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout)) { try { p.Kill(entireProcessTree: false); } catch { } output = "시간 초과"; return -1; }
            output = (o.Result + e.Result).Trim();
            return p.ExitCode;
        }
        catch (Exception ex) { output = ex.Message; return -2; }
    }

    static string Q(string s) => "\"" + (s ?? "").Replace("\"", "") + "\"";
}
