#!/usr/bin/env bash
# The website and the lobby server, as two processes, against one database.
#
#     ./tools/stack.sh            bring both up, check they are talking, tear down
#     ./tools/stack.sh --serve    bring both up and leave them running
#
# **This is what Phase 1 was for.** The website used to start the lobby server inside its own
# IIS worker, so every deploy disconnected every player. Here they are separate containers:
# the ported site on .NET 9, the lobby server on mono, sharing the database and speaking over
# the HTTP+JSON transport in ZkLobbyServer/Api.
#
# The switch is one MiscVar. LobbyApiUrl set means the website talks to a server somewhere
# else; unset means it starts one of its own, which is what the live site still does.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

SITE_PORT=5200
API_PORT=8300
SECRET="local-dev-secret-not-a-real-one"
DB_NAME="${DB_NAME:-zk_test}"

DB_NAME="$DB_NAME" ./db/require-db.sh
CS="${ZK_CONNECTION_STRING:-$(DB_NAME="$DB_NAME" ./db/connection-string.sh)}"

echo "configuring the switch: LobbyApiUrl -> http://127.0.0.1:$API_PORT/"
./tools/lobby-config.sh set "$API_PORT"

echo "building both images..."
./tools/lobby-image.sh zk-lobby
docker build -q -t zk-site . >/dev/null

docker rm -f zk-lobby-up zk-site-up >/dev/null 2>&1 || true
harness_log="$(mktemp)"
cleanup() {
    docker rm -f zk-lobby-up zk-site-up >/dev/null 2>&1 || true
    docker run --rm --network host -e ZK_CONNECTION_STRING="$CS" \
        --entrypoint dotnet zk-site bin/ZeroKWeb.Host.dll --remove-planetwars-round >/dev/null 2>&1 || true
    rm -f "$harness_log"
    [ "${1:-}" = "keep-config" ] || ./tools/lobby-config.sh clear
}
trap 'cleanup' EXIT

# Before the lobby server, not after. PlanetWarsMatchMaker reads the default galaxy and the
# faction list in its CONSTRUCTOR and gives up on both if there is no galaxy yet, and MiscVar
# caches per process - so a round seeded after it starts is a round it never hears about, and
# the symptom is nothing rather than an error.
echo "seeding a PlanetWars round..."
docker run --rm --network host -e ZK_CONNECTION_STRING="$CS" \
    --entrypoint dotnet zk-site bin/ZeroKWeb.Host.dll --seed-planetwars-round | sed 's/^/   /'

echo "starting the lobby server..."
# No --rm: a container that removes itself takes its log with it the instant it exits, and
# this one exits precisely when something is worth reading. The cleanup trap below force-
# removes it either way.
docker run -d --name zk-lobby-up --network host -e ZK_CONNECTION_STRING="$CS" zk-lobby >/dev/null
for _ in $(seq 1 240); do
    docker logs zk-lobby-up 2>&1 | grep -q "lobby server running" && { lobby=1; break; }
    docker inspect -f '{{.State.Running}}' zk-lobby-up 2>/dev/null | grep -q true || break
    sleep 1
done

# Why, while the container is still there to ask. The cleanup trap removes it, so a caller that
# waits until the end - CI did - finds nothing to read, and the failure is a bare "the lobby
# server is up" FAIL. It takes this server about ninety seconds of WHR before it says anything
# interesting, which made a port collision read like a timeout for as long as nobody looked.
if [ "${lobby:-}" != "1" ]; then
    echo
    # Errors first, then the tail. This server logs one line per WHR iteration at startup -
    # hundreds of them - so a plain tail shows nothing but "Running WHR iteration N" while the
    # exception that killed it scrolled past long before.
    echo "the lobby server did not come up. Anything it called an error:"
    docker logs zk-lobby-up 2>&1 | grep -iE "error|exception|refusing|fatal|at [A-Z]" \
        | tail -25 | sed 's/^/   /' || true
    echo
    echo "and its last 10 lines:"
    docker logs zk-lobby-up 2>&1 | tail -10 | sed 's/^/   /' || true
    echo
fi

echo "starting the website..."
docker run -d --rm --name zk-site-up --network host \
    -e ZK_CONNECTION_STRING="$CS" -e ZK_HOST_URLS="http://0.0.0.0:$SITE_PORT" zk-site >/dev/null
for _ in $(seq 1 120); do
    curl -fsS -o /dev/null "http://127.0.0.1:$SITE_PORT/Home/NotLoggedIn" 2>/dev/null && { site=1; break; }
    sleep 1
done

failures=0
check() { if [ "$1" = "0" ]; then echo "   ok    $2"; else echo "   FAIL  $2"; failures=$((failures + 1)); fi; }

echo
echo "two processes, one database:"
check "$([ "${lobby:-}" = "1" ] && echo 0 || echo 1)" "the lobby server is up and listening on $API_PORT"
check "$([ "${site:-}" = "1" ] && echo 0 || echo 1)" "the website is up and serving on $SITE_PORT"

if [ "${lobby:-}" = "1" ] && [ "${site:-}" = "1" ]; then
    # HomeController.Index asks the lobby server for ConnectedUserCount and the battle stats, so
    # the front page is the one that cannot render without it.
    code=$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$SITE_PORT/" 2>/dev/null)
    check "$([ "$code" = "200" ] && echo 0 || echo 1)" "the front page, which asks the lobby server, answered ($code)"

    # The checks that need a lobby server, run by the site's own harness.
    #
    # The image's ENTRYPOINT is the harness binary and its CMD is --serve; with the CMD dropped it
    # runs the checks instead of serving. That is the whole trick: these are the SAME checks
    # tools/run-host.sh runs, and the only difference is that LobbyApiUrl is set here, so the ones
    # that skip themselves without a lobby server - Planetwars/PwMatchMaker.cshtml is the first -
    # run for real. It binds 127.0.0.1:5199 for its own requests, which is not $SITE_PORT, so the
    # container already serving above is left alone.
    echo "   ...  running the site's checks with a lobby server attached"
    if docker run --rm --network host -e ZK_CONNECTION_STRING="$CS" \
            --entrypoint dotnet zk-site bin/ZeroKWeb.Host.dll > "$harness_log" 2>&1; then
        harness=0
    else
        harness=1
    fi
    grep -E "MatchMaker rendered|options loop|faction beside|Join form|needs a lobby server|needs a seeded round" "$harness_log" | sed 's/^/     /' || true
    check "$harness" "the site's own checks pass with a lobby server attached"
    [ "$harness" = "0" ] || grep -E "^\s+(FAIL|note)" "$harness_log" | head -20

    # THE CONTROL, and the only reason the check above means anything: stop the lobby server and
    # the same page must stop working. Without this, a 200 proves the page rendered - not that it
    # ever asked. An earlier version of this script checked /Battles, which does not call the
    # lobby server at all and would have answered 200 with nothing running.
    #
    # The site is restarted because GetCurrentLobbyStats is MemCached for two minutes, so the
    # second request would otherwise be answered from the first one's result.
    echo "   ...  stopping the lobby server to check the page really depends on it"
    docker rm -f zk-lobby-up >/dev/null 2>&1 || true
    docker restart zk-site-up >/dev/null
    for _ in $(seq 1 120); do
        curl -sS -o /dev/null "http://127.0.0.1:$SITE_PORT/Home/NotLoggedIn" 2>/dev/null && break
        sleep 1
    done

    without=$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$SITE_PORT/" 2>/dev/null)
    check "$([ "$without" != "200" ] && echo 0 || echo 1)" \
        "and with the lobby server stopped it does NOT ($without) - so the two really were talking"
fi

if [ "${1:-}" = "--serve" ]; then
    trap 'cleanup keep-config' EXIT
    echo
    echo "left running: site http://127.0.0.1:$SITE_PORT, lobby API $API_PORT"
    echo "ctrl-c to stop; the MiscVar switch stays set until you run this again without --serve"
    docker attach zk-site-up
fi

echo
[ "$failures" = "0" ] && echo "all checks passed" || echo "$failures check(s) failed"
exit "$failures"
