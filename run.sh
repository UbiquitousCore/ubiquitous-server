#!/usr/bin/env bash
# Run the server with nothing but Docker (macOS, Linux, WSL). No .NET, just or
# Python needed. Developers: see setup.sh instead.
#   ./run.sh ingest   build the data package from the .ipa in incoming/ (once)
#   ./run.sh start    start the server and Postgres, wait until healthy
#   ./run.sh stop     stop them (saves are kept)
#   ./run.sh logs     follow the server log
#   ./run.sh account create <name> | password <name> | list   (server must be running)
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

command -v docker >/dev/null || { echo "Docker is not installed: https://docs.docker.com/get-docker/"; exit 1; }
docker info >/dev/null 2>&1 || { echo "Docker is installed but not running — start Docker Desktop / OrbStack and try again"; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is missing (docker compose)"; exit 1; }

bash scripts/setup/init-config.sh
export UBIQUITOUS_UID="$(id -u)" UBIQUITOUS_GID="$(id -g)"

compose=(docker compose)
if [ -f server.json ]; then compose+=(-f docker-compose.yml -f docker-compose.server-json.yml); fi

case "${1:-help}" in
  ingest)
    if ! find incoming -maxdepth 3 \( -iname '*.ipa' -o -iname tables.bin \) | grep -q .; then
      echo "Copy your client .ipa into incoming/ first, then run ./run.sh ingest again"; exit 2
    fi
    "${compose[@]}" run --rm --build ingest
    echo "Done. Start the server with: ./run.sh start (restart it if it was already running)"
    ;;
  start)
    "${compose[@]}" up --build -d --wait
    port="${UBIQUITOUS_WEBSOCKET_PORT:-8781}"
    echo "Server healthy: ws://127.0.0.1:${port}/"
    echo "Nobody can sign in until an account exists: ./run.sh account create <name>"
    ;;
  stop) "${compose[@]}" down ;;
  logs) "${compose[@]}" logs -f server ;;
  account)
    shift
    tty=(); if [ ! -t 0 ]; then tty=(-T); fi
    "${compose[@]}" exec ${tty[@]+"${tty[@]}"} server dotnet server.dll account "$@"
    ;;
  *) sed -n 2,8p "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
