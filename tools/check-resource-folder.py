#!/usr/bin/env python3
"""The site's resource directory is named in one place.

    ./tools/check-resource-folder.py

`GlobalConst.ResourceFolder` is the directory published resource files live in - metadata,
torrents, minimaps - and the URL segment they are served under. It exists because the name used
to be written out by hand in four files and two of them spelled it differently:

    PlasmaServer.StoreMetadata   MapPath("~/Resources")          the writer
    MetaDataCache                Path.Combine(.., "resources")   the lobby server's disk read
    MissionUpdater               SiteDiskPath + @"\\resources\\"   and it CREATED that directory
    Fixer                        SiteDiskPath + @"\\Resources"

On NTFS those are one directory, so the spread was invisible for years. Off Windows they are
three, and neither half reports it: the lobby server's disk lookup simply misses and falls back to
HTTP, and a mission upload writes into a directory the site does not serve.

**A test cannot catch this.** By the time code runs, a literal has already become a path; what has
to be checked is that nobody wrote the name down again. So this reads the source.

Exempt: the constant's own declaration, and prose - comments, XML docs and this file - because
naming the directory while explaining it is the point.
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DECLARATION = "Shared/PlasmaShared/GlobalConst.Portable.cs"

# A path or URL segment, not the word. "~/Resources", "\resources\", "/Resources/", "resources"
# as an argument to Path.Combine - but never db.Resources, the DbSet, which is the same word
# doing an entirely different job and appears everywhere.
PATTERNS = [
    re.compile(r'"~/[Rr]esources'),
    re.compile(r'"/[Rr]esources/'),
    re.compile(r'@?"\\+[Rr]esources'),
    re.compile(r'Path\.Combine\([^)]*"[Rr]esources"'),
]

COMMENT = re.compile(r'^\s*(///|//|\*|/\*)')


def tracked_sources():
    out = subprocess.run(["git", "-C", str(ROOT), "ls-files", "*.cs", "*.cshtml"],
                         capture_output=True, text=True, check=True).stdout
    return [line for line in out.splitlines() if line]


def main():
    offenders = []
    for name in tracked_sources():
        if name == DECLARATION or name.startswith("tools/"):
            continue
        path = ROOT / name
        try:
            text = path.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for number, line in enumerate(text.splitlines(), 1):
            if COMMENT.match(line):
                continue
            if any(pattern.search(line) for pattern in PATTERNS):
                offenders.append("%s:%d: %s" % (name, number, line.strip()))

    if offenders:
        print("The resource directory is named outside GlobalConst.ResourceFolder:")
        print()
        for line in offenders:
            print("  " + line)
        print()
        print("Use GlobalConst.ResourceFolder. On Windows a second spelling is the same directory;")
        print("off Windows it is a different one, and nothing fails - the file is just never found.")
        return 1

    print("the resource directory is named once, in GlobalConst.ResourceFolder")
    return 0


if __name__ == "__main__":
    sys.exit(main())
