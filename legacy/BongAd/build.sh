#!/bin/sh
# Cross-build BongAd.exe on Linux/macOS. Option A: mingw-w64
if command -v x86_64-w64-mingw32-gcc >/dev/null; then
  exec x86_64-w64-mingw32-gcc -Os -s -nostdlib -fno-builtin -fno-stack-protector -fno-asynchronous-unwind-tables \
    -Wl,--subsystem,windows -Wl,-e,BongAdEntry -Wl,--gc-sections -o BongAd.exe BongAd.c -luser32 -lkernel32 -lgdi32
fi
# Option B: zig (pip install ziglang) - this is how the committed BongAd.exe was built
set -e; Z="python3 -m ziglang"; ZD=$(python3 -c "import ziglang,os;print(os.path.dirname(ziglang.__file__))"); T=$(mktemp -d)
$Z cc -target x86_64-windows-gnu -c -Os -fno-builtin -fno-stack-protector -fno-asynchronous-unwind-tables \
  -I"$ZD/lib/libc/include/any-windows-any" -o "$T/b.obj" BongAd.c
for l in user32 kernel32 gdi32; do
  D="$ZD/lib/libc/mingw/lib-common/$l.def"
  if [ -f "$D.in" ]; then $Z cc -E -P -x c -DDEF_X64 -I"$ZD/lib/libc/mingw/def-include" "$D.in" -o "$T/$l.def" 2>/dev/null; else cp "$D" "$T/$l.def"; fi
  $Z dlltool -m i386:x86-64 -d "$T/$l.def" -l "$T/lib$l.a"
done
$Z lld-link /nologo /entry:BongAdEntry /subsystem:windows /nodefaultlib /opt:ref /Brepro /out:BongAd.exe "$T/b.obj" "$T/libuser32.a" "$T/libkernel32.a" "$T/libgdi32.a"
sha256sum BongAd.exe 2>/dev/null || shasum -a 256 BongAd.exe
