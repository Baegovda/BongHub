@echo off
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BongAd.lnk" 2>nul
"%~dp0BongAd.exe" /restore
echo BongAd startup removed.
