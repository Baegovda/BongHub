@echo off
rem usage: install-startup.bat          (ad hide + compact layout)
rem        install-startup.bat /dark    (+ dark overlay, ~3MB)
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BongAd.lnk"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:LNK); $s.TargetPath='%~dp0BongAd.exe'; $s.Arguments='%*'; $s.WindowStyle=1; $s.WorkingDirectory='%~dp0'; $s.Save()"
"%~dp0BongAd.exe" /restore
start "" "%~dp0BongAd.exe" %*
echo BongAd startup registered: %LNK%
