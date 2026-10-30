@echo off
rem Build on Windows with MinGW-w64 gcc in PATH (e.g. winget install BrechtSanders.WinLibs.POSIX.UCRT)
cd /d "%~dp0"
gcc -Os -s -nostdlib -fno-builtin -fno-stack-protector -fno-asynchronous-unwind-tables -Wl,--subsystem,windows -Wl,-e,BongAdEntry -Wl,--gc-sections -o BongAd.exe BongAd.c -luser32 -lkernel32 -lgdi32
certutil -hashfile BongAd.exe SHA256
