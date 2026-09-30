#!/usr/bin/env python3
r"""Every file a .resx points at exists, spelled the way it is on disk.

    ./tools/check-resx-paths.py

A .resx entry names a file by a relative path:

    <value>Resources\Ranks\0_0.png;System.Drawing.Bitmap, System.Drawing, ...</value>

msbuild reads that path at build time. On Windows the case does not have to match what is on
disk, so a designer that wrote `resources\ranks\0_0.png` produced a project that builds forever
and a path that is wrong. ZeroKLobby had 67 of them - the whole of Ranks.resx, both licences and
one sound - against directories actually named `Resources\Ranks`, `Resources\License` and
`Resources\Sounds`.

Off Windows the same entries stop the build outright:

    Ranks.resx(123,5): error MSB3103: Invalid Resx file. Could not find a part of the path
    "/src/ZeroKLobby/resources/ranks/0_0.png"

That is a better failure than most in this repository - it is loud, and it names the file - but
nothing here builds ZeroKLobby off Windows, so it was reached only by someone trying. This check
is the part that does not need a build: it compares every entry against the filesystem, which
answers the same question in a second and on any machine.

Paths are resolved relative to the .resx file's own directory, which is how msbuild resolves them.
Entries without a path separator are inline data, not files, and are skipped.
"""
import io
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# <value>some\relative\path.png;Type, Assembly, ...</value>
ENTRY = re.compile(r"<value>([^<;]+\\[^<;]+);")


def main():
    listed = subprocess.run(["git", "-C", str(ROOT), "ls-files", "*.resx"],
                            capture_output=True, text=True, check=True).stdout
    files = [name for name in listed.splitlines() if name]

    missing = []
    checked = 0
    for name in files:
        resx = ROOT / name
        base = resx.parent
        try:
            text = io.open(resx, encoding="utf-8-sig", errors="replace").read()
        except OSError:
            continue

        for match in ENTRY.finditer(text):
            written = match.group(1)
            checked += 1
            if (base / written.replace("\\", os.sep)).exists():
                continue

            # Say what it should have been, when only the case is wrong - which it always has
            # been so far, and which is the hard part to work out by hand.
            current, resolved, ok = base, [], True
            for part in written.replace("\\", os.sep).split(os.sep):
                try:
                    found = next((e for e in os.listdir(current) if e.lower() == part.lower()), None)
                except OSError:
                    found = None
                if found is None:
                    ok = False
                    break
                resolved.append(found)
                current = current / found

            missing.append((name, written, "\\".join(resolved) if ok else None))

    if missing:
        print("%d .resx entr%s a file that is not there:"
              % (len(missing), "y names" if len(missing) == 1 else "ies name"))
        print()
        for name, written, fix in missing:
            print("  %s" % name)
            print("      %s" % written)
            print("      %s" % ("should be " + fix if fix else "nothing on disk matches, even ignoring case"))
        print()
        print("On Windows these build anyway; off it they are MSB3103 and the project does not.")
        return 1

    print("%d .resx path entries in %d files, all present as written" % (checked, len(files)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
