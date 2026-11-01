# WebP decoder resources

- Upstream: Google / WebM Project libwebp 1.6.0.
- Source: https://developers.google.com/speed/webp/docs/precompiled
- Package: https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0-windows-x64.zip
- Included files: bin/dwebp.exe and bin/webpmux.exe, embedded by build.ps1 and extracted on first WebP preview.
- License: WebP-LICENSE.txt (upstream COPYING at tag v1.6.0).
- No codec installation or registry registration is performed. Decoding runs in a hidden child process.
- Animated WebP is shown as its first frame; the mux tool extracts that frame before decoding.
