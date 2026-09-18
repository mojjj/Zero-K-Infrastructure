#!/usr/bin/env bash
# Compile-checks the website on Linux, with no .NET Framework and no Visual Studio.
#
#   ./tools/build-website.sh
#
# Builds Zero-K.info with msbuild under mono in Docker. It is the only way to know whether
# a change to the website compiles without a Windows machine, and it is what every
# modernization change in this repository has been checked with.
#
# Works on a copy of the tracked files, so the repository never collects root-owned obj/
# and packages/ directories from the container.
#
# What it CANNOT do, and why the Windows build still matters:
#   * Razor views are not compiled. mono ships no aspnet_compiler.exe (MSB6004), so a
#     mistake in a .cshtml file gets through this check untouched.
#   * It does not run anything. Running the site needs Windows, IIS Express and a database.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${ZK_BUILD_DIR:-$HOME/.cache/zk-website-build}"
CACHE="${NUGET_CACHE:-$HOME/.cache/zk-mono-nuget}"
PROJECT="${1:-Zero-K.info/asp.net.csproj}"
mkdir -p "$CACHE"

# The container writes obj/ as root, so a plain rm -rf fails with permission denied.
if [ -d "$WORK" ]; then
    docker run --rm -v "$(dirname "$WORK")":/w alpine rm -rf "/w/$(basename "$WORK")"
fi
mkdir -p "$WORK"
git -C "$REPO" ls-files -z | tar -C "$REPO" --null -T - -cf - | tar -C "$WORK" -xf -

# One workaround, applied to the copy only. ZkData.MissionUpdater.UpdateMission uses
# ZipFile.Open, which trips a System.IO.Compression facade version conflict under mono -
# reproducible on unmodified master, so it is an artefact of the container rather than of
# any change. Both overloads are stubbed; their signatures are kept so callers still
# resolve.
python3 - "$WORK" <<'PYEOF'
import sys, os, re
path = os.path.join(sys.argv[1], "ZkData/MissionService/MissionUpdater.cs")
source = open(path, encoding="utf-8-sig").read()
position, stubbed = 0, 0
while True:
    match = re.compile(r"public void UpdateMission\([^)]*\)\s*\{").search(source, position)
    if not match:
        break
    start, depth, index = match.end() - 1, 0, match.end() - 1
    while index < len(source):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                break
        index += 1
    head = source[match.start():start]
    body = '{ throw new System.NotImplementedException("stubbed for the mono build"); }'
    source = source[:match.start()] + head + body + source[index + 1:]
    position = match.start() + len(head) + len(body)
    stubbed += 1
open(path, "w", encoding="utf-8").write(source)
print("stubbed %d UpdateMission overload(s)" % stubbed)
PYEOF

# Platform=x64 is required: asp.net.csproj defines only Debug|x64 and Release|x64, and
# without it msbuild fails with "the OutputPath property is not set".
# DebugType=none because mono cannot write Windows PDBs.
docker run --rm -v "$WORK":/src -w /src -v "$CACHE":/root/.nuget mono:6.12 bash -c "
    nuget restore Zero-K.sln >/dev/null 2>&1 || nuget restore Zero-K.sln
    msbuild '$PROJECT' /p:Configuration=Debug /p:Platform=x64 /p:DebugType=none /v:minimal /nologo
"
