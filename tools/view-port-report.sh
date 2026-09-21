#!/usr/bin/env bash
# Compiles Zero-K.info's Razor views against ASP.NET Core on .NET 9 and classifies what
# stops each one.
#
#     ./tools/view-port-report.sh            print the inventory
#     ./tools/view-port-report.sh --check    fail if it no longer matches the committed one
#     ./tools/view-port-report.sh --update   re-record it
#
# The views had never been compiled by anything in this repository: mono ships no
# aspnet_compiler.exe, so tools/build-website.sh covers C# only. This turns "117 views,
# condition unknown" into a number.
#
# It measures compilation, NOT rendering. A view that compiles can still throw on the first
# request, and none of these has been rendered by anything.
#
# WHY IT COMPILES MORE THAN ONCE. Roslyn does not bind method bodies at all if the
# compilation has a declaration-level error. One build of all 116 views has 66 of them - a
# view whose @model type is missing is a broken field declaration - so NO view's body is ever
# bound, and every body-level mistake in the other 50 goes unreported. A report reads that
# silence as "compiles". The first version of this script did exactly that, and a view
# rewritten to call ThisTypeDoesNotExistAnywhere sat in its "37 clean" without a murmur.
#
# It is not an error-budget effect, which is what this comment used to claim. Two views are
# enough: _ViewStart.cshtml alone reports its two CS1061s, and adding one view with a missing
# @model type makes both disappear.
#
# So: one build of everything, which reliably yields the Razor diagnostics and the
# declaration-level ones, then a second pass over only the views that came back clean - a set
# with no declaration errors in it by construction, so bodies are bound and body errors are
# reported. Batches keep the blast radius small if that assumption ever fails: a batch that
# turns out to contain a declaration error is re-run one view at a time.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
MODE="${1:-show}"
EXPECTED=Zero-K.info/view-port-inventory.txt
BATCH=15
WORK="$(mktemp -d)"
PROPS=ZeroKWeb.Core/view-batch.props
trap 'rm -rf "$WORK"; rm -f "$PROPS"' EXIT

# Always the container, never whatever SDK the machine happens to have - including on a
# runner that has one. The Razor compiler ships inside the SDK, so its diagnostics are an
# SDK-version artefact: the CI runner carries SDK 10.0.12 beside the 9.0.318 its workflow
# installs and, with no global.json, picks the higher one.
#
# $1: newline-separated views to KEEP, or empty for all of them. The subset arrives as a
# generated props file rather than a -p: property because an MSBuild command-line property
# cannot carry semicolons, and a list of paths is nothing but semicolons.
build() {
    local keep="$1" out="$2"
    rm -f "$PROPS"
    if [ -n "$keep" ]; then
        {
            echo "<Project><ItemGroup>"
            comm -23 <(find Zero-K.info/Views -name '*.cshtml' | sort) <(printf '%s\n' "$keep" | sort) \
              | sed 's|/|\\|g; s|^|    <Content Remove="..\\|; s|$|" />|'
            echo "</ItemGroup></Project>"
        } > "$PROPS"
    fi
    ./tools/dotnet.sh build ZeroKWeb.Core/ZeroKWeb.Core.csproj -v q --nologo --no-incremental \
        > "$out" 2>&1 && return 0 || return $?
}

build "" "$WORK/pass1.txt" && rc=0 || rc=$?

# A build that failed for a reason this report cannot see - no SDK, a restore failure, a
# broken csproj - yields zero view diagnostics, and zero diagnostics reads as a clean bill
# of health. Refuse to report one.
if [ "${rc:-0}" -ne 0 ] && ! grep -qE 'Zero-K\.info/Views/[^(]+\([0-9]+,[0-9]+\): error ' "$WORK/pass1.txt"; then
    echo "the build failed for a reason this report cannot classify:" >&2
    tail -20 "$WORK/pass1.txt" >&2
    exit 2
fi

python3 - "$WORK/pass1.txt" > "$WORK/clean-after-pass1.txt" <<'PYEOF'
import re, sys, pathlib
text = pathlib.Path(sys.argv[1]).read_text()
blamed = set(re.findall(r'[^\s(]*?(Zero-K\.info/Views/[^(]+)\(\d+,\d+\): error ', text))
for view in sorted(str(p) for p in pathlib.Path("Zero-K.info/Views").rglob("*.cshtml")):
    if view not in blamed:
        print(view)
PYEOF

: > "$WORK/pass2.txt"
mapfile -t CANDIDATES < "$WORK/clean-after-pass1.txt"
total=${#CANDIDATES[@]}
for ((i = 0; i < total; i += BATCH)); do
    keep_arr=("${CANDIDATES[@]:i:BATCH}")
    keep=$(printf '%s\n' "${keep_arr[@]}")
    build "$keep" "$WORK/batch.txt" || true
    # A batch that compiled nothing tells us nothing, and its silence would be read as every
    # view in it being clean. That is the same mistake twice, so it stops here.
    if ! grep -qE 'CSC : |Build succeeded|Zero-K\.info/Views/' "$WORK/batch.txt"; then
        echo "a batch build produced no compilation at all:" >&2
        tail -10 "$WORK/batch.txt" >&2
        exit 2
    fi

    # A declaration error anywhere in the batch means no body in it was bound, so every other
    # view's silence is worthless. Should not happen - pass 1 reports declaration errors and
    # these views had none - but the whole point of this rewrite is not to trust silence.
    if grep -qE 'Zero-K\.info/Views/[^(]+\([0-9]+,[0-9]+\): error CS(0246|0234)' "$WORK/batch.txt"; then
        echo "   batch has a declaration error; re-running its views one at a time" >&2
        for view in "${keep_arr[@]}"; do
            build "$view" "$WORK/single.txt" || true
            cat "$WORK/single.txt" >> "$WORK/pass2.txt"
        done
    else
        cat "$WORK/batch.txt" >> "$WORK/pass2.txt"
    fi
done
rm -f "$PROPS"

cat "$WORK/pass1.txt" "$WORK/pass2.txt" > "$WORK/all.txt"

python3 - "$WORK/all.txt" "$WORK/clean-after-pass1.txt" > "$WORK/report.txt" <<'PYEOF'
import re, sys, pathlib, collections

text = pathlib.Path(sys.argv[1]).read_text()
rebatched = set(pathlib.Path(sys.argv[2]).read_text().split())

errors = collections.defaultdict(list)
for m in re.finditer(r'[^\s(]*?(Zero-K\.info/Views/[^(]+)\((\d+),\d+\): error ([A-Z]+\d+): ([^[]+)', text):
    errors[m.group(1)].append((m.group(3), m.group(4).strip()))

views = sorted(str(p) for p in pathlib.Path("Zero-K.info/Views").rglob("*.cshtml"))

RAZOR_LANGUAGE = {"RZ1002", "RZ1031"}
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

# A view called clean without having been recompiled in a batch was only ever judged by the
# pass the error limit can silence. There should be none; say so loudly if there are.
unverified = [v for v in buckets["compiles"] if v not in rebatched]

order = ["compiles", "waiting-on-controllers", "razor-language", "other"]
print("# Razor views compiled against ASP.NET Core on .NET 9.")
print("# GENERATED - run tools/view-port-report.sh --update.")
print("#")
print("# compiles               nothing stops this view today, verified in a small batch")
print("# waiting-on-controllers only missing types from the unported web project")
print("# razor-language         Razor itself rejects it; needs rewriting regardless")
print("# other                  a package reference or an MVC 5 API")
print()
print("total %d" % len(views))
for name in order:
    print("%-24s %d" % (name, len(buckets[name])))
if unverified:
    print("UNVERIFIED               %d" % len(unverified))
print()
for name in order:
    if name == "compiles":
        continue
    for view in buckets[name]:
        detail = sorted({c for c, _ in errors[view]})
        print("%-24s %s  [%s]" % (name, view.replace("Zero-K.info/Views/", ""), " ".join(detail)))
PYEOF

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
