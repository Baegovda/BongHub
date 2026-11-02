# Phase 0 memory baseline for BongHub.
# Windows only. Does not start BongHub.exe, does not switch monitors, does not read or write data/ or %LOCALAPPDATA%\BongHub.
# The notes7 scenario writes its own 7 notes into the temp data copy. It does not use data/notes.
# A tray icon appears during tray_only and settings_hidden, then that process exits. Windows stay at (-30000,-30000).
# Judge gc_concurrent=true medians of WorkingSet64 AND PrivateMemorySize64 against the same ceilings.
# render_mode=software. Trimming the working set does not lower private bytes.
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Fail([string]$Message) { throw $Message }

$running = @(Get-Process -Name 'BongHub' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Fail "BongHub is already running. Quit that process first."
}

Write-Host 'dotnet publish (Release, win-x64, framework-dependent ReadyToRun)'
& dotnet publish (Join-Path $root 'BongHub\BongHub.csproj') -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish exited $LASTEXITCODE" }

$pub = Join-Path $root 'BongHub\bin\Release\net10.0-windows\win-x64\publish'
$exe = Join-Path $pub 'BongHub.exe'
$rt = Join-Path $pub 'BongHub.runtimeconfig.json'
if (-not (Test-Path $exe)) { Fail "missing $exe" }
if (Test-Path (Join-Path $pub 'coreclr.dll')) { Fail 'publish is self-contained (coreclr.dll is present)' }
$deps = [IO.File]::ReadAllText((Join-Path $pub 'BongHub.deps.json'))
if ($deps -match 'System\.Windows\.Forms') { Fail 'deps.json still references System.Windows.Forms' }
if ($deps -notmatch 'H\.NotifyIcon') { Fail 'deps.json is missing H.NotifyIcon' }
$dll = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $pub 'BongHub.dll')))
$rtr = "RTR$([char]0)"
if (($dll.IndexOf('ReadyToRun') -lt 0) -and ($dll.IndexOf($rtr) -lt 0)) { Fail 'BongHub.dll has no ReadyToRun signature (RTR)' }
$rtText = [IO.File]::ReadAllText($rt)
if ($rtText -notmatch '"System\.GC\.Concurrent"') { Fail 'runtimeconfig is missing System.GC.Concurrent (csproj ConcurrentGarbageCollection)' }

$logDir = Join-Path $root 'test-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir 'baseline_memory.txt'
if (Test-Path $logPath) { Remove-Item -Force $logPath }
$utf8 = New-Object System.Text.UTF8Encoding $false
$sdk = (& dotnet --version).Trim()
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('BongHub baseline')
$lines.Add("sdk=$sdk")
$lines.Add("publish=$pub")
$lines.Add('publish_self_contained=0')
$lines.Add('publish_ready_to_run=1')
$lines.Add('publish_winforms=0')
$lines.Add('publish_notifyicon=1')
$lines.Add('idle_seconds=20')
$lines.Add('runs_per_cell=3')
$lines.Add('gc_default=true')
$lines.Add('note=0.4.0 csproj default is ConcurrentGarbageCollection=true. This script still measures both and restores true.')
$lines.Add('notes7_source=generated count=7 image_notes=1')

function Set-Gc([bool]$On) {
    $raw = [IO.File]::ReadAllText($rt)
    $val = $(if ($On) { 'true' } else { 'false' })
    $next = [regex]::Replace($raw, '"System\.GC\.Concurrent"\s*:\s*(true|false)', ('"System.GC.Concurrent": ' + $val))
    [IO.File]::WriteAllText($rt, $next, $utf8)
}

function Write-Config([string]$Dir, [bool]$OpenOnStartup) {
    $flag = $(if ($OpenOnStartup) { 'true' } else { 'false' })
    $json = @"
{
  "Version": 2,
  "Notes": {
    "DefaultColor": "g:blocks",
    "Gradient": true,
    "FontSize": 11,
    "Opacity": 100,
    "OpenOnStartup": $flag,
    "Hotkey": "Ctrl+Alt+N"
  }
}
"@
    [IO.File]::WriteAllText((Join-Path $Dir 'config.json'), $json.Trim(), $utf8)
}

# 1x1 transparent PNG, same bytes as the RichEdit fixture. One note only, so notes7 does not decode the synced photos.
$script:ImageRtf = @'
{\rtf1\ansi\ansicpg949\deff0{\fonttbl{\f0\fnil\fcharset129 Malgun Gothic;}}
\viewkind4\uc1\pard\f0\fs24 baseline image \u-21000?\u-17988?{\pict\pngblip\picw1\pich1\picwgoal15\pichgoal15
89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000a49444154789c63000100000500010d0a2db40000000049454e44ae426082}\par}
'@

function New-TextRtf([string]$Plain) {
    return ('{{\rtf1\ansi\deff0{{\fonttbl{{\f0\fnil Arial;}}}}\pard\f0\fs22 {0}\par}}' -f $Plain)
}

function Write-BaselineNotes([string]$Dir) {
    $full = [IO.Path]::GetFullPath($Dir)
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (-not $full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { Fail "notes dir is not under TEMP: $full" }
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $root 'data')).TrimEnd('\') + '\'
    if ($full.StartsWith($dataRoot, [StringComparison]::OrdinalIgnoreCase)) { Fail "refusing to write under data/: $full" }
    $dest = Join-Path $full 'notes'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding $false
    for ($i = 1; $i -le 7; $i++) {
        $id = 'b1base{0:d6}' -f $i
        $text = "baseline note $i"
        $rtf = $(if ($i -eq 7) { $script:ImageRtf } else { New-TextRtf $text })
        $y = 80 + ($i * 24)
        $meta = '{"Id":"ID","Color":"g:blocks","X":120,"Y":YY,"W":330,"H":330,"TopMost":false,"Open":true,"Zoom":1.0,"Text":"TEXT","Modified":"\/Date(1700000000000)\/"}'
        $meta = $meta.Replace('ID', $id).Replace('YY', [string]$y).Replace('TEXT', $text)
        [IO.File]::WriteAllText((Join-Path $dest ($id + '.json')), $meta, $utf8)
        [IO.File]::WriteAllText((Join-Path $dest ($id + '.rtf')), $rtf.Trim(), $utf8)
    }
    Assert-GeneratedNotes $dest
}

function Assert-GeneratedNotes([string]$Dest) {
    $json = @(Get-ChildItem -LiteralPath $Dest -Filter '*.json' | Where-Object { $_.Name -notlike '_*' })
    $rtf = @(Get-ChildItem -LiteralPath $Dest -Filter '*.rtf')
    if ($json.Count -ne 7) { Fail "generated note json count is $($json.Count), expected 7" }
    if ($rtf.Count -ne 7) { Fail "generated note rtf count is $($rtf.Count), expected 7" }
    $images = 0
    foreach ($file in $rtf) {
        if ($file.Length -le 0) { Fail "generated rtf is empty: $($file.Name)" }
        $body = [IO.File]::ReadAllText($file.FullName)
        if ([string]::IsNullOrWhiteSpace($body)) { Fail "generated rtf is blank: $($file.Name)" }
        if ($body -match '\\pngblip') { $images++ }
    }
    if ($images -ne 1) { Fail "expected exactly one image note, found $images" }
    foreach ($file in $json) {
        $meta = [IO.File]::ReadAllText($file.FullName)
        if ($meta -notmatch '"Open"\s*:\s*true') { Fail "generated note is not Open: $($file.Name)" }
        $id = [IO.Path]::GetFileNameWithoutExtension($file.Name)
        if (-not (Test-Path -LiteralPath (Join-Path $Dest ($id + '.rtf')))) { Fail "generated note has no rtf: $id" }
    }
}

function Median($Values) {
    $sorted = @($Values | Sort-Object)
    return [int]$sorted[[int](($sorted.Count - 1) / 2)]
}

function Read-Metric([string]$Text, [string]$Name) {
    $m = [regex]::Match($Text, ('(?m)^' + [regex]::Escape($Name) + '=(\d+)'))
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return $null
}

function Read-Text([string]$Path) {
    for ($n = 0; $n -lt 20; $n++) {
        try { return [IO.File]::ReadAllText($Path) } catch { Start-Sleep -Milliseconds 50 }
    }
    Fail "could not read $Path"
}

function Stop-Measured([System.Diagnostics.Process]$Proc) {
    if ($null -eq $Proc) { return }
    try { $Proc.Refresh() } catch { return }
    if ($Proc.HasExited) { return }
    $quit = Start-Process -FilePath $exe -ArgumentList '--quit' -PassThru -WindowStyle Hidden
    try { if ($Proc.WaitForExit(15000)) { return } } catch { }
    Stop-Process -Id $Proc.Id -Force -ErrorAction SilentlyContinue
    Fail "measurement process $($Proc.Id) did not exit after --quit"
}

function Assert-Startup([string]$Text, [string]$Scenario) {
    if ($Text -notmatch '(?m)^render_mode=software') { Fail "$Scenario render_mode is not software" }
    switch ($Scenario) {
        'tray_only' {
            if ($Text -notmatch '(?m)^tray_visible_ms=\d+') { Fail 'tray_only missing tray_visible_ms' }
            if ($Text -match '(?m)^first_frame_ms=') { Fail 'tray_only created a window (first_frame_ms)' }
            if ($Text -notmatch '(?m)^main_window_created=0') { Fail 'tray_only main_window_created is not 0' }
            if ($Text -notmatch '(?m)^startup_notes_opened=0') { Fail 'tray_only startup_notes_opened is not 0' }
            if ($Text -match 'settings_hidden=1') { Fail 'tray_only hid a settings window' }
        }
        'settings' {
            if ($Text -notmatch '(?m)^first_frame_ms=\d+') { Fail 'settings missing first_frame_ms' }
            if ($Text -notmatch '(?m)^hwnd_render_mode=SoftwareOnly') { Fail 'settings hwnd_render_mode is not SoftwareOnly' }
            if ($Text -match '(?m)^tray_visible_ms=') { Fail 'settings registered a tray icon' }
            if ($Text -notmatch '(?m)^main_window_created=1') { Fail 'settings main_window_created is not 1' }
            if ($Text -notmatch '(?m)^startup_notes_opened=0') { Fail 'settings startup_notes_opened is not 0' }
            if ($Text -match 'settings_hidden=1') { Fail 'settings set settings_hidden' }
        }
        'settings_hidden' {
            if ($Text -notmatch '(?m)^tray_visible_ms=\d+') { Fail 'settings_hidden missing tray_visible_ms' }
            if ($Text -notmatch '(?m)^first_frame_ms=\d+') { Fail 'settings_hidden missing first_frame_ms' }
            if ($Text -notmatch '(?m)^hwnd_render_mode=SoftwareOnly') { Fail 'settings_hidden hwnd_render_mode is not SoftwareOnly' }
            if ($Text -notmatch '(?m)^main_window_created=1') { Fail 'settings_hidden main_window_created is not 1' }
            if ($Text -notmatch '(?m)^startup_notes_opened=0') { Fail 'settings_hidden startup_notes_opened is not 0' }
            if ($Text -notmatch '(?m)^settings_hidden=1') { Fail 'settings_hidden missing settings_hidden=1' }
        }
        'notes7' {
            if ($Text -notmatch '(?m)^first_frame_ms=\d+') { Fail 'notes7 missing first_frame_ms' }
            if ($Text -notmatch '(?m)^hwnd_render_mode=SoftwareOnly') { Fail 'notes7 hwnd_render_mode is not SoftwareOnly' }
            if ($Text -match '(?m)^tray_visible_ms=') { Fail 'notes7 registered a tray icon' }
            if ($Text -notmatch '(?m)^main_window_created=1') { Fail 'notes7 main_window_created is not 1' }
            if ($Text -notmatch '(?m)^startup_notes_opened=7') { Fail 'notes7 startup_notes_opened is not 7' }
            if ($Text -match 'settings_hidden=1') { Fail 'notes7 set settings_hidden' }
        }
        default { Fail "unknown scenario $Scenario" }
    }
}

$scenarios = @(
    @{ Name = 'tray_only'; Args = @('--tray', '--off'); Notes = $false; OpenOnStartup = $false },
    @{ Name = 'settings'; Args = @('--off'); Notes = $false; OpenOnStartup = $false },
    @{ Name = 'settings_hidden'; Args = @('--tray', '--off', '--idle-hidden'); Notes = $false; OpenOnStartup = $false },
    @{ Name = 'notes7'; Args = @('--off'); Notes = $true; OpenOnStartup = $true }
)

$proc = $null
try {
    foreach ($gcOn in @($false, $true)) {
        Set-Gc $gcOn
        $env:DOTNET_gcConcurrent = $(if ($gcOn) { '1' } else { '0' })
        $gcLabel = $(if ($gcOn) { 'true' } else { 'false' })
        $lines.Add('')
        $lines.Add("gc_concurrent=$gcLabel")
        foreach ($sc in $scenarios) {
            $ws = New-Object System.Collections.Generic.List[int]
            $priv = New-Object System.Collections.Generic.List[int]
            $frames = New-Object System.Collections.Generic.List[int]
            $trays = New-Object System.Collections.Generic.List[int]
            for ($i = 1; $i -le 3; $i++) {
                $data = Join-Path $env:TEMP ("bhnext_mem_" + $PID + "_" + $sc.Name + "_" + $gcLabel + "_" + $i)
                $out = Join-Path $data 'out'
                if (Test-Path $data) { Remove-Item -Recurse -Force $data }
                New-Item -ItemType Directory -Force -Path $out | Out-Null
                Write-Config $data ([bool]$sc.OpenOnStartup)
                if ($sc.Notes) { Write-BaselineNotes $data }
                $argList = @($sc.Args) + @('--data', $data, '--out', $out)
                Write-Host ("run {0} gc={1} #{2}" -f $sc.Name, $gcLabel, $i)
                $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru -WorkingDirectory $pub
                $startupPath = Join-Path $out 'startup.txt'
                $ready = $false
                $lastReadyError = ''
                $deadline = (Get-Date).AddSeconds(60)
                $startupText = ''
                while ((Get-Date) -lt $deadline) {
                    Start-Sleep -Milliseconds 400
                    $proc.Refresh()
                    if ($proc.HasExited) { Fail "$($sc.Name) exited before it was ready (code $($proc.ExitCode))" }
                    if (Test-Path -LiteralPath $startupPath) {
                        $startupText = Read-Text $startupPath
                        try { Assert-Startup $startupText $sc.Name; $ready = $true; break }
                        catch { $lastReadyError = $_.Exception.Message }
                    }
                }
                if (-not $ready) { Fail "$($sc.Name) was not ready within 60s. $lastReadyError`r`n$startupText" }
                Start-Sleep -Seconds 20
                $proc.Refresh()
                if ($proc.HasExited) { Fail "$($sc.Name) exited during the 20s idle" }
                $wsMb = [int]($proc.WorkingSet64 / 1MB)
                $privMb = [int]($proc.PrivateMemorySize64 / 1MB)
                $ws.Add($wsMb)
                $priv.Add($privMb)
                $frameMs = Read-Metric $startupText 'first_frame_ms'
                if ($null -ne $frameMs) { $frames.Add($frameMs) }
                $trayMs = Read-Metric $startupText 'tray_visible_ms'
                if ($null -ne $trayMs) { $trays.Add($trayMs) }
                $startupText = Read-Text $startupPath
                Assert-Startup $startupText $sc.Name
                $lines.Add(("run={0} scenario={1} gc_concurrent={2} ws_mb={3} private_mb={4}" -f $i, $sc.Name, $gcLabel, $wsMb, $privMb))
                $lines.Add('startup<<')
                $lines.Add($startupText.TrimEnd())
                $lines.Add('startup>>')
                Stop-Measured $proc
                $proc = $null
                Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
            }
            $medianLine = "median scenario={0} gc_concurrent={1} ws_mb={2} private_mb={3}" -f $sc.Name, $gcLabel, (Median @($ws)), (Median @($priv))
            if ($frames.Count -gt 0) { $medianLine += " first_frame_ms=$(Median @($frames))" }
            if ($trays.Count -gt 0) { $medianLine += " tray_visible_ms=$(Median @($trays))" }
            $lines.Add($medianLine)
        }
    }
}
finally {
    if ($null -ne $proc) {
        try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    }
    try { Set-Gc $true } catch { }
    Remove-Item Env:DOTNET_gcConcurrent -ErrorAction SilentlyContinue
}

$lines.Add('')
$lines.Add('targets gc_concurrent=true medians: tray_only ws<=120 and private<=120, tray_visible_ms<=500; settings ws<=150 and private<=150, first_frame_ms<=700; notes7 ws<=180 and private<=180')
$lines.Add('render_mode=software and hwnd_render_mode=SoftwareOnly are required. idle_trim=1 in the startup text only means the trim ran. Private bytes are the committed number; trimming the working set does not lower private.')
[IO.File]::WriteAllText($logPath, (($lines -join "`r`n") + "`r`n"), $utf8)
Write-Host "wrote $logPath"
Write-Host 'Done. Judge gc_concurrent=true medians. Working set AND private must meet the same ceilings. render_mode=software.'
