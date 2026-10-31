$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$res = Join-Path $here 'res'
$src = Join-Path $here 'BongHub.cs'
# version comes from one place: AppInfo.Version in BongHub.cs (must match the assembly attributes)
$ver = [regex]::Match((Get-Content $src -Raw -Encoding UTF8), 'const string Version = "(\d+\.\d+\.\d+)"').Groups[1].Value
if (-not $ver) { throw "version not found" }
$asm = [regex]::Match((Get-Content $src -Raw -Encoding UTF8), 'AssemblyFileVersion\("([\d\.]+)"\)').Groups[1].Value
if ($asm -ne "$ver.0") { throw "version mismatch: AppInfo $ver vs AssemblyFileVersion $asm" }
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /codepage:65001 /optimize+ "/out:$here\BongHub.exe" `
  "/resource:$res\MultiMonitorTool.exe,MultiMonitorTool.exe" "/resource:$res\work_layout.cfg,work_layout.cfg" "/resource:$res\BongAd.exe,BongAd.exe" "/resource:$res\icon_blocks.ico,icon_blocks.ico" "/win32icon:$res\icon_blocks.ico" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.Security.dll /r:System.Data.dll /r:System.Xml.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll /r:System.Xaml.dll $src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$fv = (Get-Item "$here\BongHub.exe").VersionInfo.FileVersion
"BongHub $ver built (file version $fv)"
