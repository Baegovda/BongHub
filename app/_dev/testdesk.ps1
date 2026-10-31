# runs BongHub test modes on a separate hidden Windows desktop: nothing can appear on the user's screen
param([string[]]$Modes, [int]$Sec = 180)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class HD {
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr CreateDesktop(string n, IntPtr d, IntPtr m, int f, uint a, IntPtr s);
  [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SI { public int cb; public string r, desk, title; public int x,y,w,h,xc,yc,fa,flags; public short show, r2; public IntPtr r3, i, o, e; }
  [StructLayout(LayoutKind.Sequential)] public struct PI { public IntPtr hp, ht; public int pid, tid; }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inh, uint f, IntPtr env, string dir, ref SI si, out PI pi);
  [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
  [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out uint c);
  [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr h, uint c);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
"@ -ErrorAction SilentlyContinue
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$desk = [HD]::CreateDesktop("BongHubTest", [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
if ($desk -eq [IntPtr]::Zero) { throw "CreateDesktop failed" }
try {
  foreach ($m in $Modes) {
    $si = New-Object HD+SI; $si.cb = [Runtime.InteropServices.Marshal]::SizeOf($si); $si.desk = "BongHubTest"
    $pi = New-Object HD+PI
    if (-not [HD]::CreateProcess("$dir\BongHub.exe", "`"$dir\BongHub.exe`" $m", [IntPtr]::Zero, [IntPtr]::Zero, $false, 0, [IntPtr]::Zero, $dir, [ref]$si, [ref]$pi)) { "$m -> start failed"; continue }
    $w = [HD]::WaitForSingleObject($pi.hp, $Sec * 1000); $code = 0
    if ($w -ne 0) { [HD]::TerminateProcess($pi.hp, 99) | Out-Null; "TIMEOUT $m" } else { [HD]::GetExitCodeProcess($pi.hp, [ref]$code) | Out-Null; "$m -> $code" }
    [HD]::CloseHandle($pi.hp) | Out-Null; [HD]::CloseHandle($pi.ht) | Out-Null
  }
} finally { [HD]::CloseDesktop($desk) | Out-Null }
