# Phase 0 memory baseline for BongHub.Next.
# Windows only. Does not start BongHub.exe, does not switch monitors, does not write data/ or %LOCALAPPDATA%\BongHub.
# A Next tray icon appears during tray_only and settings_hidden, then that process exits. Windows stay at (-30000,-30000).
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail([string]$Message) { throw $Message }

$running = @(Get-Process -Name 'BongHub.Next' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Fail "BongHub.Next is already running. Quit that Next process first. Do not stop BongHub.exe (the 3.4.x tray)."
}

Write-Host 'dotnet publish (Release, win-x64, framework-dependent ReadyToRun)'
& dotnet publish (Join-Path $root 'next\BongHub.Next\BongHub.Next.csproj') -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish exited $LASTEXITCODE" }

$pub = Join-Path $root 'next\BongHub.Next\bin\Release\net10.0-windows\win-x64\publish'
$exe = Join-Path $pub 'BongHub.Next.exe'
$rt = Join-Path $pub 'BongHub.Next.runtimeconfig.json'
if (-not (Test-Path $exe)) { Fail "missing $exe" }
if (Test-Path (Join-Path $pub 'coreclr.dll')) { Fail 'publish is self-contained (coreclr.dll is present)' }
$deps = [IO.File]::ReadAllText((Join-Path $pub 'BongHub.Next.deps.json'))
if ($deps -match 'System\.Windows\.Forms') { Fail 'deps.json still references System.Windows.Forms' }
if ($deps -notmatch 'H\.NotifyIcon') { Fail 'deps.json is missing H.NotifyIcon' }
$dll = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $pub 'BongHub.Next.dll')))
$rtr = "RTR$([char]0)"
if (($dll.IndexOf('ReadyToRun') -lt 0) -and ($dll.IndexOf($rtr) -lt 0)) { Fail 'BongHub.Next.dll has no ReadyToRun signature (RTR)' }
$rtText = [IO.File]::ReadAllText($rt)
if ($rtText -notmatch '"System\.GC\.Concurrent"') { Fail 'runtimeconfig is missing System.GC.Concurrent (csproj ConcurrentGarbageCollection)' }

$notesSrc = Join-Path $root 'data\notes'
$noteJson = @(Get-ChildItem -Path $notesSrc -Filter '*.json' | Where-Object { $_.Name -notlike '_*' })
if ($noteJson.Count -ne 7) { Fail "expected 7 note json files in data/notes, found $($noteJson.Count). Do not edit data/." }

$logDir = Join-Path $root 'next\test-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir 'baseline_memory.txt'
if (Test-Path $logPath) { Remove-Item -Force $logPath }
$utf8 = New-Object System.Text.UTF8Encoding $false
$sdk = (& dotnet --version).Trim()
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('BongHub.Next baseline')
$lines.Add("sdk=$sdk")
$lines.Add("publish=$pub")
$lines.Add('publish_self_contained=0')
$lines.Add('publish_ready_to_run=1')
$lines.Add('publish_winforms=0')
$lines.Add('publish_notifyicon=1')
$lines.Add('idle_seconds=20')
$lines.Add('runs_per_cell=3')
$lines.Add('gc_default=false')
$lines.Add('note=ConcurrentGarbageCollection stays false until these medians are reviewed. This script does not change the csproj.')

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

function Copy-Notes([string]$Dir) {
    $dest = Join-Path $Dir 'notes'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($j in $noteJson) {
        Copy-Item -Path $j.FullName -Destination (Join-Path $dest $j.Name)
        $rtf = [IO.Path]::ChangeExtension($j.FullName, '.rtf')
        if (Test-Path -LiteralPath $rtf) {
            Copy-Item -Path $rtf -Destination (Join-Path $dest ([IO.Path]::GetFileName($rtf)))
        }
    }
}

function Median($Values) {
    $sorted = @($Values | Sort-Object)
    return [int]$sorted[[int](($sorted.Count - 1) / 2)]
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
            if ($Text -match '(?m)^tray_visible_ms=') { Fail 'settings registered a tray icon' }
            if ($Text -notmatch '(?m)^main_window_created=1') { Fail 'settings main_window_created is not 1' }
            if ($Text -notmatch '(?m)^startup_notes_opened=0') { Fail 'settings startup_notes_opened is not 0' }
            if ($Text -match 'settings_hidden=1') { Fail 'settings set settings_hidden' }
        }
        'settings_hidden' {
            if ($Text -notmatch '(?m)^tray_visible_ms=\d+') { Fail 'settings_hidden missing tray_visible_ms' }
            if ($Text -notmatch '(?m)^first_frame_ms=\d+') { Fail 'settings_hidden missing first_frame_ms' }
            if ($Text -notmatch '(?m)^main_window_created=1') { Fail 'settings_hidden main_window_created is not 1' }
            if ($Text -notmatch '(?m)^startup_notes_opened=0') { Fail 'settings_hidden startup_notes_opened is not 0' }
            if ($Text -notmatch '(?m)^settings_hidden=1') { Fail 'settings_hidden missing settings_hidden=1' }
        }
        'notes7' {
            if ($Text -notmatch '(?m)^first_frame_ms=\d+') { Fail 'notes7 missing first_frame_ms' }
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
            for ($i = 1; $i -le 3; $i++) {
                $data = Join-Path $env:TEMP ("bhnext_mem_" + $PID + "_" + $sc.Name + "_" + $gcLabel + "_" + $i)
                $out = Join-Path $data 'out'
                if (Test-Path $data) { Remove-Item -Recurse -Force $data }
                New-Item -ItemType Directory -Force -Path $out | Out-Null
                Write-Config $data ([bool]$sc.OpenOnStartup)
                if ($sc.Notes) { Copy-Notes $data }
                $argList = @($sc.Args) + @('--data', $data, '--out', $out)
                Write-Host ("run {0} gc={1} #{2}" -f $sc.Name, $gcLabel, $i)
                $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru -WorkingDirectory $pub
                $startupPath = Join-Path $out 'next_startup.txt'
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
            $lines.Add(("median scenario={0} gc_concurrent={1} ws_mb={2} private_mb={3}" -f $sc.Name, $gcLabel, (Median @($ws)), (Median @($priv))))
        }
    }
}
finally {
    if ($null -ne $proc) {
        try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    }
    try { Set-Gc $false } catch { }
    Remove-Item Env:DOTNET_gcConcurrent -ErrorAction SilentlyContinue
}

[IO.File]::WriteAllText($logPath, (($lines -join "`r`n") + "`r`n"), $utf8)
Write-Host "wrote $logPath"
Write-Host 'Done. Leave ConcurrentGarbageCollection=false until the medians above are reviewed.'
