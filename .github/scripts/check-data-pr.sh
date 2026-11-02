#!/usr/bin/env bash
# On a pull request, a commit that touches data/ must be an app sync commit
# (subject starts with "sync:"). People do not edit the cloud payload in PRs.
set -euo pipefail
base=${1:-}
[[ -n "$base" ]] || { echo "FAIL: base sha required" >&2; exit 1; }
git rev-parse --verify --quiet "$base^{commit}" >/dev/null || { echo "FAIL: base sha not in this clone: $base" >&2; exit 1; }

fail=0
while read -r c; do
  [[ -n "$c" ]] || continue
  subject=$(git log -1 --format=%s "$c")
  files=$(git diff-tree --no-commit-id --name-only -r "$c" || true)
  data=$(printf '%s\n' "$files" | grep '^data/' || true)
  if [[ -n "$data" && "$subject" != sync:* ]]; then
    echo "FAIL: $c touches data/ but is not a sync commit"
    echo "  subject: $subject"
    printf '%s\n' "$data" | sed 's/^/  /'
    fail=1
  fi
done < <(git rev-list --no-merges "$base"..HEAD)

if [[ "$fail" -ne 0 ]]; then
  echo "data/ is the cloud payload. Sync commits (subject 'sync: …') may update it. Other commits may not." >&2
  exit 1
fi
echo "OK no human commit under data/ in $base..HEAD"
