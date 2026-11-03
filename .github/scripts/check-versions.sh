#!/usr/bin/env bash
# Fail when the BongHub version strings drift apart.
# csproj Version, csproj FileVersion, AppEnv.Version, AppInfo.Version.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
fail() { echo "FAIL: $1" >&2; exit 1; }

grab() { grep -oE "$1" "$2" | head -1 | sed -E 's/.*"([0-9.]+)".*/\1/'; }

csproj="$root/BongHub/BongHub.csproj"
nver=$(grep -oE '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>' "$csproj" | head -1 | sed -E 's/<\/?Version>//g')
nfv=$(grep -oE '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+</FileVersion>' "$csproj" | head -1 | sed -E 's/<\/?FileVersion>//g')
env=$(grab 'const string Version = "[0-9]+\.[0-9]+\.[0-9]+"' "$root/BongHub/AppEnv.cs")
info=$(grab 'const string Version = "[0-9]+\.[0-9]+\.[0-9]+"' "$root/BongHub/AppInfo.cs")
[[ -n "$nver" ]] || fail "BongHub <Version> not found"
[[ "$nver" == "1.5.0" ]] || fail "BongHub <Version> is '$nver', expected 1.5.0"
[[ "$nfv" == "$nver.0" ]] || fail "FileVersion is '$nfv', expected '$nver.0'"
[[ "$env" == "$nver" ]] || fail "AppEnv.Version is '$env', expected '$nver'"
[[ "$info" == "$nver" ]] || fail "AppInfo.Version is '$info', expected '$nver'"

echo "OK BongHub $nver"
