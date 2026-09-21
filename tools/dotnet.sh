#!/usr/bin/env bash
# Runs the .NET 9 SDK from a container, because nothing here installs it on the host.
#
#     ./tools/dotnet.sh build ZkData.Core/ZkData.Core.csproj
#     ZK_CONNECTION_STRING=... ./tools/dotnet.sh run --project ZkData.Core -- read
#
# The workload notice is silenced because it goes to STDOUT, and some of these commands
# have stdout that is data - `-- rate` prints a table meant to be diffed.
#
# --network host so the harness can reach the SQL Server on 127.0.0.1:14330 that
# db/docker-compose.yml starts; the NuGet cache is kept outside the repo so restores do
# not repeat and do not land in git status.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CACHE="${ZK_NUGET_CACHE:-$HOME/.nuget/packages}"
mkdir -p "$CACHE"

exec docker run --rm -i \
    --network host \
    -u "$(id -u):$(id -g)" \
    -v "$REPO:/repo" \
    -v "$CACHE:/nuget" \
    -e HOME=/tmp \
    -e NUGET_PACKAGES=/nuget \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e DOTNET_NOLOGO=1 \
    -e DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1 \
    -e ZK_CONNECTION_STRING="${ZK_CONNECTION_STRING:-}" \
    -w /repo \
    mcr.microsoft.com/dotnet/sdk:9.0 \
    dotnet "$@"
