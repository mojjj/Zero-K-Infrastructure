#!/usr/bin/env bash
# Every check in tools/ fails when it can see nothing.
#
#     ./tools/check-the-checks.sh
#
# A check that examines an empty list reports success. That is not a hypothetical: moving CI
# onto tools/mono-buildable.proj pointed the item guard in tools/build-website.sh at a file
# naming no items, so it went from 314 items to 0 and stayed green for a commit. Nothing failed,
# which is precisely the problem - a guard that has stopped guarding looks exactly like a guard
# with nothing to report.
#
# Most checks here find their subjects with `git ls-files <pattern>`. A directory rename, a
# project move, or a glob that stops matching empties that list without any error. So this runs
# each one with a `git` on PATH that returns nothing for `ls-files` and forwards everything else
# to the real git, and requires a non-zero exit. A traceback does not count: a crash is not a
# diagnosis, and the message is what tells the next person what actually broke.
#
# A check that does not find its subjects through git ls-files says so in itself:
#
#     blinding-exempt: <why blinding it proves nothing>
#
# which is the same shape as `// not-in-a-project:` in check-orphaned-sources.py.
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHIM="$(mktemp -d)"
trap 'rm -rf "$SHIM"' EXIT

cat > "$SHIM/git" <<'SHIMEOF'
#!/usr/bin/env bash
for argument in "$@"; do [ "$argument" = "ls-files" ] && exit 0; done
exec git "$@"
SHIMEOF
chmod +x "$SHIM/git"

blinded=0
exempt=0
failures=0

for check in $(git -C "$REPO" ls-files 'tools/check-*'); do
    [ "$check" = "tools/check-the-checks.sh" ] && continue

    reason="$(grep -oP 'blinding-exempt:\s*\K.*' "$REPO/$check" | head -1)"
    if [ -n "$reason" ]; then
        printf '   exempt  %-30s %s\n' "$(basename "$check")" "$reason"
        exempt=$((exempt + 1))
        continue
    fi

    case "$check" in
        *.py) run=(python3 "$REPO/$check") ;;
        *.js) run=(node "$REPO/$check") ;;
        *)    run=("$REPO/$check") ;;
    esac

    output="$(cd "$REPO" && PATH="$SHIM:$PATH" "${run[@]}" 2>&1)"
    status=$?

    if [ "$status" -eq 0 ]; then
        printf '   FAIL    %-30s passed while seeing nothing\n' "$(basename "$check")"
        failures=$((failures + 1))
    elif grep -q 'Traceback (most recent call last)' <<<"$output"; then
        printf '   FAIL    %-30s crashed instead of reporting it\n' "$(basename "$check")"
        failures=$((failures + 1))
    else
        printf '   ok      %-30s %s\n' "$(basename "$check")" "$(tail -1 <<<"$output" | cut -c1-70)"
        blinded=$((blinded + 1))
    fi
done

echo

# This script is a check, so it answers to its own rule.
if [ "$((blinded + exempt))" -eq 0 ]; then
    echo "found no checks to blind - this check would pass by seeing nothing" >&2
    exit 2
fi

if [ "$failures" -ne 0 ]; then
    echo "$failures check(s) do not fail usefully when they can see nothing." >&2
    echo "Give it a guard that exits non-zero when its subject list is empty, or write" >&2
    echo "    blinding-exempt: <why blinding it proves nothing>" >&2
    echo "in the file if it does not find its subjects through git ls-files." >&2
    exit 1
fi

echo "$blinded check(s) fail when blinded, $exempt exempt"
