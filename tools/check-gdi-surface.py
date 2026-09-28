#!/usr/bin/env python3
"""The files ZkLobbyServer.Core compiles that still need System.Drawing.Common.

    python3 tools/check-gdi-surface.py

System.Drawing.Common is Windows-only on .NET 9 - `new Bitmap(4,4)` throws
TypeInitializationException on Linux. The lobby server still references the package, and
Shared/PlasmaShared/IMAGING-MIGRATION.md records why: three files wrap raw pixel buffers from the
native unitsync library in Bitmap, which is interop marshalling rather than a library swap and
cannot be tested without unitsync and real map files.

Everything else was removed from that project rather than ported, because it was code the server
compiles but cannot reach. This keeps that line where it is: the set of GDI+-using files must
match tools/gdi-surface.txt, and it fails IN EITHER DIRECTION - a new one needs a decision, and one
that disappears means the blocker got smaller and the baseline should say so.

**What this checks, precisely.** It names files that MENTION a GDI+-only type, in the compile
items MSBuild actually resolves. That is not the same claim as "fails to compile without the
package", and the difference matters: a single failed declaration stops Roslyn binding method
bodies across the whole compilation, so one build reports the files whose declarations break and
hides every file whose bodies do - which is exactly how SystemDrawingImageProcessor.cs contributed
zero of 84 errors while using Bitmap throughout. A build cannot be trusted to enumerate this set.
Reading the source can, at the cost of catching a mention in a comment.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(".").resolve()
PROJECT = Path("ZkLobbyServer.Core/ZkLobbyServer.Core.csproj")
BASELINE = Path("tools/gdi-surface.txt")

COMPILE = re.compile(r'<Compile\s+(?:Include|Update)\s*=\s*"([^"]+)"', re.IGNORECASE)
REMOVE = re.compile(r'<Compile\s+Remove\s*=\s*"([^"]+)"', re.IGNORECASE)
# The System.Drawing.Common types. Point/Size/Rectangle/Color are System.Drawing.Primitives,
# which is in the shared framework and cross-platform - naming them here would flag half the repo.
GDI = re.compile(r'\b(Bitmap|Graphics|ImageCodecInfo|EncoderParameters?|InterpolationMode|ImageFormat|PixelFormat|Pen|Brush|GraphicsUnit)\b')
COMMENT = re.compile(r'^\s*(//|///|\*|/\*)')


def tracked():
    out = subprocess.run(["git", "ls-files", "-z"], capture_output=True, text=True, check=True).stdout
    return [f for f in out.split("\0") if f]


def expand(base, raw, sources):
    candidate = base / raw.replace("\\", "/")
    try:
        rel = candidate.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return set()
    if "*" not in rel:
        return {rel} & sources
    pattern = re.escape(rel).replace(r"\*\*/", "(.*/)?").replace(r"\*\*", ".*").replace(r"\*", "[^/]*")
    matcher = re.compile("^" + pattern + "$")
    return {f for f in sources if matcher.match(f)}


def main():
    if not PROJECT.exists():
        print("%s is gone - this check names one project and that project is not there" % PROJECT)
        return 1

    sources = {f for f in tracked() if f.endswith(".cs") and "/obj/" not in f and "/bin/" not in f}
    text = PROJECT.read_text(encoding="utf-8-sig", errors="replace")
    base = PROJECT.parent

    compiled = set()
    for match in COMPILE.finditer(text):
        compiled |= expand(base, match.group(1), sources)
    for match in REMOVE.finditer(text):
        compiled -= expand(base, match.group(1), sources)

    if not compiled:
        print("resolved no source files for %s - the check would pass by seeing nothing" % PROJECT)
        return 1

    found = {}
    for path in sorted(compiled):
        hits = [line for line in Path(path).read_text(encoding="utf-8-sig", errors="replace").splitlines()
                if GDI.search(line) and not COMMENT.match(line)]
        if hits:
            found[path] = len(hits)

    expected = {}
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            name, _, note = line.partition("  ")
            expected[name.strip()] = note.strip()

    added = sorted(set(found) - set(expected))
    gone = sorted(set(expected) - set(found))

    for path in added:
        print("NEW GDI+ dependency in the .NET 9 lobby server: %s (%d lines)" % (path, found[path]))
    for path in gone:
        print("GONE, so the baseline is stale: %s  (%s)" % (path, expected[path]))

    if added or gone:
        print("\n%s compiles a different set of System.Drawing users than %s records." % (PROJECT, BASELINE))
        print("System.Drawing.Common is Windows-only on .NET 9: new use here does not fail any build,")
        print("it throws TypeInitializationException when the line finally runs on Linux.")
        return 1

    print("%d files in %s still need System.Drawing.Common, all recorded:" % (len(found), PROJECT.name))
    for path in sorted(found):
        print("  %-48s %s" % (path, expected[path]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
