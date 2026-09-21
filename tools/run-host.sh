#!/usr/bin/env bash
# Serves a real Zero-K view over HTTP on .NET 9 and checks the response.
#
#     ./tools/run-host.sh            request it once and check
#     ./tools/run-host.sh --serve    leave it running on http://localhost:5199
#
# Only the views the inventory says compile are included - a project that does not build
# serves nothing - so the set is generated here from the committed inventory.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
INVENTORY=Zero-K.info/view-port-inventory.txt
PROPS=ZeroKWeb.Host/view-set.props
trap 'rm -f "$PROPS"' EXIT

[ -f "$INVENTORY" ] || { echo "missing $INVENTORY - run tools/view-port-report.sh --update" >&2; exit 2; }

{
    echo "<Project><ItemGroup>"
    grep -oE '[A-Za-z0-9_/.]+\.cshtml' "$INVENTORY" | sort -u \
      | sed 's|/|\\|g; s|^|    <Content Remove="..\\Zero-K.info\\Views\\|; s|$|" />|'
    echo "</ItemGroup></Project>"
} > "$PROPS"

ZK_CONNECTION_STRING="${ZK_CONNECTION_STRING:-$(DB_NAME="${DB_NAME:-zk_test}" ./db/connection-string.sh)}" \
    ./tools/dotnet.sh run --project ZeroKWeb.Host -- "$@"
