#!/usr/bin/env bash
# Does the .NET 9 lobby server actually START, and does it open the port players connect to?
#
#     ./tools/lobby-core-start.sh
#
# ZkLobbyServer.Core proves the chain compiles. That is not the same claim. This runs the program
# against a real database and checks three things a build cannot see:
#
#   1. it refuses when no standalone server is configured - the website would start its own
#   2. it refuses when configured but handed no site directory, naming the GeoIP file it wants
#   3. configured and handed the site directory, it comes up and LISTENS ON BOTH PORTS
#
# (3) is here because of a defect that only running found. TcpTransportServerListener.Bind
# P/Invoked SetHandleInformation out of kernel32; off Windows that throws DllNotFoundException,
# Bind caught it, retried 120 times and gave up - and the server came up with its API listening
# and no player port at all. Nothing was wrong in any log at a glance. Checking that the process
# is alive, or that the API answers, would both have passed. Only the player port shows it.
#
# It costs a couple of minutes: a Whole History Rating pass runs before the listener opens.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

DB_NAME="${DB_NAME:-zk_test}"
export ZK_CONNECTION_STRING="${ZK_CONNECTION_STRING:-$(DB_NAME="$DB_NAME" ./db/connection-string.sh)}"

# The API port must not be lobby-config.sh's default of 8200: that is GlobalConst.LobbyServerPort
# in Local mode, so the API would take the port the player listener is about to ask for and the
# thing under test would fail for a reason of our own making.
API_PORT=8300
PLAYER_PORT=8200
LOG="$(mktemp)"
CONTAINER=zk-lobby-core
BOOT_TIMEOUT="${ZK_LOBBY_BOOT_TIMEOUT:-360}"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; ./tools/lobby-config.sh clear >/dev/null 2>&1 || true; rm -f "$LOG"; }
trap cleanup EXIT

failures=0
check() { if [ "$1" = "0" ]; then echo "   ok    $2"; else echo "   FAIL  $2"; failures=$((failures+1)); fi; }

echo "the .NET 9 lobby server, starting:"

./tools/lobby-config.sh clear >/dev/null 2>&1 || true
out="$(./tools/dotnet.sh run --project ZkLobbyServer.Standalone.Core 2>&1 || true)"
grep -q "LobbyApiUrl MiscVar is not set" <<<"$out" \
    && check 0 "refuses when no standalone server is configured" \
    || check 1 "refuses when no standalone server is configured"

./tools/lobby-config.sh set "$API_PORT" >/dev/null 2>&1
out="$(./tools/dotnet.sh run --project ZkLobbyServer.Standalone.Core 2>&1 || true)"
grep -q "GeoLite2-Country.mmdb" <<<"$out" \
    && check 0 "gets past configuration and asks for the GeoIP database" \
    || check 1 "gets past configuration and asks for the GeoIP database"

# The real start. Built first, so the build's own minutes do not eat the boot timeout.
./tools/dotnet.sh build ZkLobbyServer.Standalone.Core -v q --nologo >/dev/null
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
ZK_DOTNET_NAME="$CONTAINER" ./tools/dotnet.sh run --project ZkLobbyServer.Standalone.Core --no-build -- /repo/Zero-K.info >"$LOG" 2>&1 &

listening=1
for _ in $(seq 1 "$BOOT_TIMEOUT"); do
    if grep -q "Listening at port $PLAYER_PORT" "$LOG" 2>/dev/null; then listening=0; break; fi
    if ! docker inspect "$CONTAINER" >/dev/null 2>&1 && [ -s "$LOG" ] && ! grep -q Listening "$LOG"; then break; fi
    sleep 1
done
check "$listening" "starts, and says it is listening on the player port"

# Opening a TCP connection rather than reading a socket table: it is the claim that matters
# ("a client can reach it"), and it does not need iproute2 to be installed on the runner.
connects() { timeout 5 bash -c "exec 3<>/dev/tcp/127.0.0.1/$1" 2>/dev/null; }
connects "$PLAYER_PORT" \
    && check 0 "a client can connect to the player port ($PLAYER_PORT)" \
    || check 1 "a client can connect to the player port ($PLAYER_PORT)"
connects "$API_PORT" \
    && check 0 "a client can connect to the API port ($API_PORT)" \
    || check 1 "a client can connect to the API port ($API_PORT)"
grep -qiE 'kernel32|DllNotFoundException' "$LOG" \
    && check 1 "no Windows-only P/Invoke on the way up" \
    || check 0 "no Windows-only P/Invoke on the way up"

if [ "$listening" != "0" ]; then echo; echo "--- last 30 lines ---"; tail -30 "$LOG"; fi

echo
if [ "$failures" -ne 0 ]; then echo "$failures check(s) failed"; exit 1; fi
echo "the lobby server runs on .NET 9, on Linux, with players able to reach it"
