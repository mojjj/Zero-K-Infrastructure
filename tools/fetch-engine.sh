#!/usr/bin/env bash
# Downloads a Spring engine so the unitsync P/Invokes can be called for real.
#
#     ./tools/fetch-engine.sh              # prints the directory to use
#     ZK_DOTNET_MOUNT=$(./tools/fetch-engine.sh) ./tools/dotnet.sh test Tests.Portable/...
#
# The engine is NOT downloaded into the repository, and that is not tidiness: this checkout is
# synced by Synology Drive, which replicates anything in the tree and restores it after deletion.
# It goes under the user cache instead, and is kept once fetched.
#
# Same URL the product itself uses - EngineDownload.cs builds
# {BaseSiteUrl}/engine/{platform}/{name}.zip - so this is the ordinary distribution channel, not a
# scrape. linux64 because that is what SpringPaths picks on a 64-bit Unix.
set -euo pipefail

VERSION="${ZK_ENGINE_VERSION:-105.1.1-2457-g8095d30}"
DEST="${ZK_ENGINE_DIR:-$HOME/.cache/zk-engine/$VERSION}"
URL="https://zero-k.info/engine/linux64/$VERSION.zip"

if [ ! -f "$DEST/libunitsync.so" ]; then
    echo "fetching $VERSION (~42MB) from $URL" >&2
    mkdir -p "$DEST"
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    curl -fsSL --max-time 600 -o "$tmp/engine.zip" "$URL"
    unzip -o -q "$tmp/engine.zip" -d "$DEST"
    [ -f "$DEST/libunitsync.so" ] || { echo "no libunitsync.so in $VERSION - wrong platform or layout" >&2; exit 1; }
    echo "unpacked into $DEST" >&2
fi

echo "$DEST"
