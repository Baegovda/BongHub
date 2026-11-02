#!/usr/bin/env bash
# Fail when the tray or Next version strings drift apart.
# Tray: AppInfo.Version, AssemblyVersion, AssemblyFileVersion, AssemblyInformationalVersion, app/version.txt, app/_dev/version.txt.
# Next: csproj Version, csproj FileVersion, AppEnv.Version.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cs="$root/app/BongHub.cs"
fail() { echo "FAIL: $1" >&2; exit 1; }

grab() { grep -oE "$1" "$2" | head -1 | sed -E 's/.*"([0-9.]+)".*/\1/'; }

ver=$(grab 'const string Version = "[0-9]+\.[0-9]+\.[0-9]+"' "$cs")
filever=$(grab 'AssemblyFileVersion\("[0-9.]+"\)' "$cs")
asmver=$(grab 'AssemblyVersion\("[0-9.]+"\)' "$cs")
info=$(grab 'AssemblyInformationalVersion\("[0-9.]+"\)' "$cs")
txt=$(head -n 1 "$root/app/version.txt" | tr -d '\r')
devtxt=$(head -n 1 "$root/app/_dev/version.txt" | tr -d '\r')

[[ -n "$ver" ]] || fail "AppInfo.Version not found in app/BongHub.cs"
[[ "$filever" == "$ver.0" ]] || fail "AssemblyFileVersion is '$filever', expected '$ver.0'"
[[ "$asmver" == "$ver.0" ]] || fail "AssemblyVersion is '$asmver', expected '$ver.0'"
[[ "$info" == "$ver" ]] || fail "AssemblyInformationalVersion is '$info', expected '$ver'"
[[ "$txt" == "$ver" ]] || fail "app/version.txt is '$txt', expected '$ver'"
[[ "$devtxt" == "$ver" ]] || fail "app/_dev/version.txt is '$devtxt', expected '$ver'"

csproj="$root/next/BongHub.Next/BongHub.Next.csproj"
nver=$(grep -oE '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>' "$csproj" | head -1 | sed -E 's/<\/?Version>//g')
nfv=$(grep -oE '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+</FileVersion>' "$csproj" | head -1 | sed -E 's/<\/?FileVersion>//g')
env=$(grab 'const string Version = "[0-9]+\.[0-9]+\.[0-9]+"' "$root/next/BongHub.Next/AppEnv.cs")
[[ -n "$nver" ]] || fail "Next <Version> not found"
[[ "$nfv" == "$nver.0" ]] || fail "Next FileVersion is '$nfv', expected '$nver.0'"
[[ "$env" == "$nver" ]] || fail "AppEnv.Version is '$env', expected '$nver'"

echo "OK tray $ver  next $nver"
