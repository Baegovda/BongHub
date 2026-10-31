# runs preview modes off-screen on the user's desktop (-30000,-30000, no activation, no taskbar) and
# proves it: every 100 ms lists visible windows of the test process that intersect any monitor.
param([string[]]$Modes, [int]$Sec = 180)
Add-Type -TypeDefinition @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class OW {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rr, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public static List<string> OnScreen(int pid, int vx, int vy, int vw, int vh) {
    var res = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { R r; GetWindowRect(h, out r);
      if (r.Rr > vx && r.L < vx + vw && r.B > vy && r.T < vy + vh && r.Rr - r.L > 1) res.Add(h + " " + r.L + "," + r.T + "," + r.Rr + "," + r.B); } return true; }, IntPtr.Zero);
    return res;
  }
}
"@ -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Windows.Forms
$v = [System.Windows.Forms.SystemInformation]::VirtualScreen
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($m in $Modes) {
  $fg0 = [OW]::GetForegroundWindow(); $seen = @{}; $fgChanged = 0
  $p = Start-Process "$dir\BongHub.exe" -ArgumentList $m -PassThru -WorkingDirectory $dir
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $Sec) {
    foreach ($w in [OW]::OnScreen($p.Id, $v.X, $v.Y, $v.Width, $v.Height)) { $seen[$w] = 1 }
    $fg = [OW]::GetForegroundWindow(); $fp = 0; [OW]::GetWindowThreadProcessId($fg, [ref]$fp) | Out-Null; if ($fp -eq $p.Id) { $fgChanged++ }
    Start-Sleep -Milliseconds 100
  }
  if (-not $p.HasExited) { $p.Kill(); "TIMEOUT $m" } else { "$m -> $($p.ExitCode)  on-screen windows: $($seen.Count)  focus taken: $fgChanged" }
  foreach ($k in $seen.Keys) { "   visible: $k" }
}
