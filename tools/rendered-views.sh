#!/usr/bin/env bash
# Which Razor views actually RENDER when the host harness runs?
#
#     ./tools/rendered-views.sh            the count, and what never renders
#
# 122 views compile, and tools/view-port-report.sh checks that. This asks the other question, and
# they are not the same one: News/Index.cshtml compiled for weeks and threw on its first request.
#
# The host records every view Razor executes when ZK_RENDERED_VIEWS names a file - see
# RenderedViewRecorder in ZeroKWeb.Host/Program.cs. This runs the harness with it set and diffs the
# result against the tracked .cshtml files.
#
# It measures the harness, which runs with NO lobby server attached - deliberately, because the
# site has to render without one. Planetwars/PwMatchMaker.cshtml cannot be reached that way at
# all: its action asks Global.LobbyApi before choosing a view. tools/stack.sh renders it, against
# a real lobby server in another container, and says so.
#
# **A view in the "never rendered" list is not broken.** Most render perfectly well when something
# asks for them; /Home, /Forum and /Clans did, which is how they came to be in the harness at all.
# The list is a coverage measurement, not a defect report.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

LOG=rendered-views.tmp          # under the repo because that is what the container can see
trap 'rm -f "$LOG"' EXIT
rm -f "$LOG"

ZK_RENDERED_VIEWS="/repo/$LOG" ./tools/run-host.sh >/dev/null 2>&1 || {
    echo "the harness did not finish - run ./tools/run-host.sh to see why" >&2; exit 1; }

sort -u "$LOG" | sed 's|^/Views/|Zero-K.info/Views/|' > "$LOG.rendered"
git ls-files 'Zero-K.info/Views/*.cshtml' | grep -vE '_ViewStart|_ViewImports' | sort > "$LOG.all"
rendered=$(wc -l < "$LOG.rendered")
all=$(wc -l < "$LOG.all")

echo "$rendered of $all views render when the harness runs."
echo
echo "never rendered by it ($((all - rendered))):"
comm -13 "$LOG.rendered" "$LOG.all" | sed 's|Zero-K.info/Views/|  |'
rm -f "$LOG.rendered" "$LOG.all"
