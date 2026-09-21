#!/usr/bin/env bash
# Compiles Zero-K.info's Razor views against ASP.NET Core on .NET 9 and classifies what
# stops each one.
#
#     ./tools/view-port-report.sh            print the inventory
#     ./tools/view-port-report.sh --check    fail if it no longer matches the committed one
#     ./tools/view-port-report.sh --update   re-record it
#
# The views have never been compiled by anything in this repository: mono ships no
# aspnet_compiler.exe, so tools/build-website.sh covers C# only. This is the first thing
# that looks at them, and it exists to turn "117 views, condition unknown" into a number.
#
# It measures compilation, NOT rendering. A view that compiles can still throw on the first
# request, and none of these have been rendered by anything.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
MODE="${1:-show}"
EXPECTED=Zero-K.info/view-port-inventory.txt
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Always the container, never whatever SDK the machine happens to have - including on a
# runner that has one. The Razor compiler ships inside the SDK, so its diagnostics are an
# SDK-version artefact: the CI runner carries SDK 10.0.12 next to the 9.0.318 its workflow
# installs and, with no global.json, picks the higher one. The inventory would then differ
# from the committed one for a reason that has nothing to do with the views. One SDK, named
# in tools/dotnet.sh, everywhere.
./tools/dotnet.sh build ZeroKWeb.Core/ZeroKWeb.Core.csproj -v q --nologo --no-incremental \
    > "$WORK/build.txt" 2>&1 && rc=0 || rc=$?

# A build that failed for a reason this report cannot see - no SDK, a restore failure, a
# broken csproj - yields zero view diagnostics, and zero diagnostics reads as a clean bill
# of health. Refuse to report one.
if [ "${rc:-0}" -ne 0 ] && ! grep -qE 'Zero-K\.info/Views/[^(]+\([0-9]+,[0-9]+\): error ' "$WORK/build.txt"; then
    echo "the build failed for a reason this report cannot classify:" >&2
    tail -20 "$WORK/build.txt" >&2
    exit 2
fi

python3 - "$WORK/build.txt" > "$WORK/report.txt" <<'PY'
import re, sys, pathlib, collections

text = pathlib.Path(sys.argv[1]).read_text()
errors = collections.defaultdict(list)
# The path prefix differs between the container (/repo/...) and a CI runner's checkout,
# so match from Zero-K.info onwards and ignore whatever sits in front of it.
for m in re.finditer(r'[^\s(]*?(Zero-K\.info/Views/[^(]+)\((\d+),\d+\): error ([A-Z]+\d+): ([^[]+)', text):
    errors[m.group(1)].append((m.group(3), m.group(4).strip()))

views = sorted(str(p) for p in pathlib.Path("Zero-K.info/Views").rglob("*.cshtml"))

# Razor stopped accepting these outright; they need rewriting whatever happens elsewhere.
RAZOR_LANGUAGE = {"RZ1002", "RZ1031"}
# Types that live in the web project itself, which is not ported. Not view problems - these
# views are waiting on their controllers and will resolve when those move.
UNPORTED = ("ZeroKWeb", "Controller", "AwardCalculator", "PwLadder")

def classify(view):
    found = errors.get(view, [])
    if not found:
        return "compiles"
    codes = {c for c, _ in found}
    if codes & RAZOR_LANGUAGE:
        return "razor-language"
    if codes <= {"CS0246"} and all(any(u in msg for u in UNPORTED) for _, msg in found):
        return "waiting-on-controllers"
    return "other"

buckets = collections.defaultdict(list)
for view in views:
    buckets[classify(view)].append(view)

order = ["compiles", "waiting-on-controllers", "razor-language", "other"]
print("# Razor views compiled against ASP.NET Core on .NET 9.")
print("# GENERATED - run tools/view-port-report.sh --update.")
print("#")
print("# compiles               nothing stops this view today")
print("# waiting-on-controllers only missing types from the unported web project")
print("# razor-language         Razor itself rejects it; needs rewriting regardless")
print("# other                  a package reference or an MVC 5 API")
print()
print("total %d" % len(views))
for name in order:
    print("%-24s %d" % (name, len(buckets[name])))
print()
for name in order:
    if name == "compiles":
        continue
    for view in buckets[name]:
        detail = sorted({c for c, _ in errors[view]})
        print("%-24s %s  [%s]" % (name, view.replace("Zero-K.info/Views/", ""), " ".join(detail)))
PY

case "$MODE" in
  --update)
    cp "$WORK/report.txt" "$EXPECTED"
    echo "recorded $EXPECTED"
    sed -n '/^total/,/^$/p' "$EXPECTED"
    ;;
  --check)
    if diff -q "$EXPECTED" "$WORK/report.txt" >/dev/null 2>&1; then
      echo "the view inventory is unchanged"
      sed -n '/^total/,/^$/p' "$EXPECTED"
    else
      echo "the view inventory has changed:"
      diff "$EXPECTED" "$WORK/report.txt" || true
      echo
      echo "if this is deliberate, re-record it with ./tools/view-port-report.sh --update"
      exit 1
    fi
    ;;
  *)
    cat "$WORK/report.txt"
    ;;
esac
