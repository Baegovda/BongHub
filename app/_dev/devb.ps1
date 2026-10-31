$ErrorActionPreference = 'Continue'
$d = 'C:\Users\Revaptor_FX\BongHub'
Set-Location "$d\_dev"
$res = "$d\res"
$o = & C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001 /optimize+ /out:BongHub.exe "/resource:$res\MultiMonitorTool.exe,MultiMonitorTool.exe" "/resource:$res\work_layout.cfg,work_layout.cfg" "/resource:$res\BongAd.exe,BongAd.exe" "/resource:$res\icon_blocks.ico,icon_blocks.ico" "/win32icon:$res\icon_blocks.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.Security.dll /r:System.Data.dll /r:System.Xml.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll /r:System.Xaml.dll BongHub.cs 2>&1
$o | Select-String "error" | Select-Object -First 40 | Out-String -Width 400
"build exit $LASTEXITCODE"
